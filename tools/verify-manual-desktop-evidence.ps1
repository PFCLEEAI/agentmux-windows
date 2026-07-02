[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$EvidencePath,

  [string]$PackagePath = "",
  [string]$ReportPath = "",
  [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"

$script:Errors = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]
$script:Info = New-Object System.Collections.Generic.List[string]
$script:CheckedFiles = New-Object System.Collections.Generic.List[object]
$script:TempRoots = New-Object System.Collections.Generic.List[string]
$script:PackageIntegrityFailed = $false
$script:PackageIntegrityChecked = $false
$script:ManualGateStillRequired = $false
$script:ReleaseReadinessBlocked = $false

function Add-ErrorMessage {
  param([string]$Message)
  $script:Errors.Add($Message) | Out-Null
  Write-Warning $Message
}

function Add-WarningMessage {
  param([string]$Message)
  $script:Warnings.Add($Message) | Out-Null
  Write-Host "[agentmux-evidence] warning: $Message"
}

function Add-PackageErrorMessage {
  param([string]$Message)
  $script:PackageIntegrityFailed = $true
  Add-ErrorMessage $Message
}

function Add-InfoMessage {
  param([string]$Message)
  $script:Info.Add($Message) | Out-Null
  Write-Host "[agentmux-evidence] $Message"
}

function Resolve-FullPath {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return ""
  }

  if ([System.IO.Path]::IsPathRooted($Path)) {
    return [System.IO.Path]::GetFullPath($Path)
  }

  return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Normalize-RelativePath {
  param([string]$RelativePath)

  return $RelativePath.Replace("/", [System.IO.Path]::DirectorySeparatorChar).Replace("\", [System.IO.Path]::DirectorySeparatorChar)
}

function Test-IsUnderRoot {
  param(
    [string]$Root,
    [string]$Path
  )

  $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
  $pathFull = [System.IO.Path]::GetFullPath($Path)
  $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar
  return $pathFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Join-ContainedPath {
  param(
    [string]$Root,
    [string]$RelativePath
  )

  if ([string]::IsNullOrWhiteSpace($RelativePath)) {
    throw "Relative path is empty."
  }

  if ([System.IO.Path]::IsPathRooted($RelativePath)) {
    throw "Path must be relative: $RelativePath"
  }

  $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
  $full = [System.IO.Path]::GetFullPath((Join-Path $rootFull (Normalize-RelativePath $RelativePath)))

  if (-not (Test-IsUnderRoot -Root $rootFull -Path $full)) {
    throw "Path escapes root: $RelativePath"
  }

  return $full
}

function Add-CheckedFile {
  param(
    [string]$Category,
    [string]$Path,
    [bool]$Exists
  )

  $script:CheckedFiles.Add([ordered]@{
    category = $Category
    path = $Path
    exists = $Exists
  }) | Out-Null
}

function Require-File {
  param(
    [string]$Root,
    [string]$RelativePath,
    [string]$Category = "required"
  )

  try {
    $path = Join-ContainedPath -Root $Root -RelativePath $RelativePath
  } catch {
    Add-ErrorMessage $_.Exception.Message
    Add-CheckedFile -Category $Category -Path $RelativePath -Exists $false
    return $false
  }

  $exists = Test-Path -LiteralPath $path -PathType Leaf
  Add-CheckedFile -Category $Category -Path $path -Exists $exists
  if (-not $exists) {
    Add-ErrorMessage "Required file missing: $RelativePath"
    return $false
  }

  return $true
}

function Read-JsonFile {
  param(
    [string]$Path,
    [string]$Name
  )

  try {
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
  } catch {
    Add-ErrorMessage "$Name is invalid JSON: $($_.Exception.Message)"
    return $null
  }
}

function Resolve-EvidenceDirectory {
  param([string]$Path)

  $resolved = Resolve-FullPath $Path
  if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
    throw "EvidencePath does not exist or is not a directory: $resolved"
  }

  if ((Test-Path -LiteralPath (Join-Path $resolved "manual-checklist.md") -PathType Leaf) -or
      (Test-Path -LiteralPath (Join-Path $resolved "environment.json") -PathType Leaf)) {
    return $resolved
  }

  $candidates = Get-ChildItem -LiteralPath $resolved -Directory |
    Where-Object {
      (Test-Path -LiteralPath (Join-Path $_.FullName "manual-checklist.md") -PathType Leaf) -or
      (Test-Path -LiteralPath (Join-Path $_.FullName "environment.json") -PathType Leaf)
    } |
    Sort-Object LastWriteTimeUtc -Descending

  if ($candidates.Count -eq 0) {
    throw "EvidencePath has no helper evidence directory: $resolved"
  }

  if ($candidates.Count -gt 1) {
    Add-WarningMessage "EvidencePath contains multiple helper output directories; using latest: $($candidates[0].FullName)"
  }

  return [System.IO.Path]::GetFullPath($candidates[0].FullName)
}

function Resolve-PackageRoot {
  param(
    [string]$Path,
    [string]$WorkRoot
  )

  $resolved = Resolve-FullPath $Path
  if ([string]::IsNullOrWhiteSpace($resolved)) {
    return ""
  }

  if (-not (Test-Path -LiteralPath $resolved)) {
    Add-PackageErrorMessage "PackagePath does not exist: $resolved"
    return ""
  }

  if (Test-Path -LiteralPath $resolved -PathType Container) {
    return $resolved
  }

  if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
    Add-PackageErrorMessage "PackagePath is neither a file nor a directory: $resolved"
    return ""
  }

  if ([System.IO.Path]::GetExtension($resolved) -ne ".zip") {
    Add-PackageErrorMessage "PackagePath file must be a .zip file: $resolved"
    return ""
  }

  $outerChecksum = "$resolved.sha256"
  if (-not (Test-Path -LiteralPath $outerChecksum -PathType Leaf)) {
    Add-PackageErrorMessage "Release ZIP sibling .sha256 not found: $outerChecksum"
  } else {
    $checksumText = [System.IO.File]::ReadAllText($outerChecksum, [System.Text.Encoding]::ASCII)
    if ($checksumText.Contains("`r") -or $checksumText -notmatch "^[0-9A-Fa-f]{64}  .+`n$") {
      Add-PackageErrorMessage "Release ZIP sibling .sha256 must be one LF-only '<sha256>  <filename>' line."
    } else {
      $checksumParts = $checksumText.TrimEnd("`n") -split "  ", 2
      $expectedName = Split-Path -Leaf $resolved
      $actualHash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
      if ($checksumParts.Length -ne 2 -or $checksumParts[1] -ne $expectedName) {
        Add-PackageErrorMessage "Release ZIP sibling .sha256 filename does not match package ZIP."
      } elseif ($actualHash -ne $checksumParts[0].ToLowerInvariant()) {
        Add-PackageErrorMessage "Release ZIP checksum mismatch."
      } else {
        Add-InfoMessage "Release ZIP outer checksum OK."
      }
    }
  }

  $extractRoot = Join-Path $WorkRoot "verification-package-extract"
  if (Test-Path -LiteralPath $extractRoot) {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
  Expand-Archive -LiteralPath $resolved -DestinationPath $extractRoot -Force
  $script:TempRoots.Add($extractRoot) | Out-Null
  return $extractRoot
}

function Test-Sha256SumsStrict {
  param([string]$PackageRoot)

  $checksumsPath = Join-Path $PackageRoot "SHA256SUMS.txt"
  if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    Add-PackageErrorMessage "SHA256SUMS.txt not found in package root."
    return
  }

  $checksumText = [System.IO.File]::ReadAllText($checksumsPath, [System.Text.Encoding]::ASCII)
  if ($checksumText.Contains("`r")) {
    Add-PackageErrorMessage "SHA256SUMS.txt must be LF-only."
  }

  $entries = @{}
  $lines = $checksumText -split "`n"
  foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
      continue
    }

    if ($line -notmatch "^([0-9A-Fa-f]{64})  (.+)$") {
      Add-PackageErrorMessage "Invalid checksum line: $line"
      continue
    }

    $expectedHash = $Matches[1].ToLowerInvariant()
    $relative = $Matches[2]
    if ([string]::IsNullOrWhiteSpace($relative) -or [System.IO.Path]::IsPathRooted($relative)) {
      Add-PackageErrorMessage "Checksum entry path must be relative: $relative"
      continue
    }

    if ($relative.Replace("\", "/") -eq "SHA256SUMS.txt") {
      Add-PackageErrorMessage "SHA256SUMS.txt must not contain a checksum entry for itself."
      continue
    }

    try {
      $path = Join-ContainedPath -Root $PackageRoot -RelativePath $relative
    } catch {
      Add-PackageErrorMessage "Checksum entry escapes package root: $relative"
      continue
    }

    $key = $relative.Replace("\", "/").ToLowerInvariant()
    if ($entries.ContainsKey($key)) {
      Add-PackageErrorMessage "Duplicate checksum entry: $relative"
      continue
    }

    $entries[$key] = $true

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      Add-PackageErrorMessage "Checksum entry missing from package: $relative"
      continue
    }

    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
      Add-PackageErrorMessage "Checksum mismatch for ${relative}: expected=$expectedHash actual=$actualHash"
    }
  }

  $files = Get-ChildItem -LiteralPath $PackageRoot -Recurse -File
  foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($PackageRoot, $file.FullName).Replace("\", "/")
    if ($relative -eq "SHA256SUMS.txt") {
      continue
    }

    if (-not $entries.ContainsKey($relative.ToLowerInvariant())) {
      Add-PackageErrorMessage "Package file is not covered by SHA256SUMS.txt: $relative"
    }
  }
}

function Test-Package {
  param([string]$PackageRoot)

  foreach ($requiredPackageFile in @(
    "AgentMux.exe",
    "cli\agentmux.exe",
    "PACKAGE.json",
    "EVIDENCE.json",
    "SHA256SUMS.txt",
    "docs\manual-windows-desktop-smoke.md",
    "tools\install-user.ps1",
    "tools\manual-desktop-smoke.ps1",
    "tools\verify-manual-desktop-evidence.ps1"
  )) {
    if (-not (Require-File -Root $PackageRoot -RelativePath $requiredPackageFile -Category "package")) {
      $script:PackageIntegrityFailed = $true
    }
  }

  $manifestPath = Join-Path $PackageRoot "PACKAGE.json"
  $evidencePath = Join-Path $PackageRoot "EVIDENCE.json"
  $manifest = if (Test-Path -LiteralPath $manifestPath -PathType Leaf) { Read-JsonFile -Path $manifestPath -Name "PACKAGE.json" } else { $null }
  $evidence = if (Test-Path -LiteralPath $evidencePath -PathType Leaf) { Read-JsonFile -Path $evidencePath -Name "EVIDENCE.json" } else { $null }
  if ((Test-Path -LiteralPath $manifestPath -PathType Leaf) -and $null -eq $manifest) {
    $script:PackageIntegrityFailed = $true
  }
  if ((Test-Path -LiteralPath $evidencePath -PathType Leaf) -and $null -eq $evidence) {
    $script:PackageIntegrityFailed = $true
  }

  if ($null -ne $manifest) {
    foreach ($requiredManifestFile in @("EVIDENCE.json", "tools\manual-desktop-smoke.ps1", "tools\verify-manual-desktop-evidence.ps1")) {
      if ($manifest.requiredFiles -notcontains $requiredManifestFile) {
        Add-PackageErrorMessage "PACKAGE.json requiredFiles does not include $requiredManifestFile."
      }
    }
  }

  if ($null -ne $evidence) {
    if ($evidence.manualGate.status -ne "required") {
      Add-PackageErrorMessage "EVIDENCE.json manualGate.status should remain required."
    } else {
      $script:ManualGateStillRequired = $true
    }

    if ($evidence.releaseReadiness.status -ne "blocked") {
      Add-PackageErrorMessage "EVIDENCE.json releaseReadiness.status should remain blocked until real desktop smoke passes."
    } else {
      $script:ReleaseReadinessBlocked = $true
    }

    if ($evidence.integrity.checksumFile -ne "SHA256SUMS.txt") {
      Add-PackageErrorMessage "EVIDENCE.json integrity.checksumFile should be SHA256SUMS.txt."
    }
  }

  Test-Sha256SumsStrict -PackageRoot $PackageRoot
  $script:PackageIntegrityChecked = $true
}

function Test-EvidenceScreenshot {
  param(
    [string]$EvidenceDir,
    [string]$Name,
    [switch]$AllowNote
  )

  $rootPath = Join-Path $EvidenceDir $Name
  $screenshotsPath = Join-Path (Join-Path $EvidenceDir "screenshots") $Name
  if ((Test-Path -LiteralPath $rootPath -PathType Leaf) -or (Test-Path -LiteralPath $screenshotsPath -PathType Leaf)) {
    Add-CheckedFile -Category "manual-evidence" -Path $Name -Exists $true
    return $true
  }

  if ($AllowNote) {
    $noteName = [System.IO.Path]::ChangeExtension($Name, ".txt")
    $rootNote = Join-Path $EvidenceDir $noteName
    $screenshotsNote = Join-Path (Join-Path $EvidenceDir "screenshots") $noteName
    if ((Test-Path -LiteralPath $rootNote -PathType Leaf) -or (Test-Path -LiteralPath $screenshotsNote -PathType Leaf)) {
      Add-CheckedFile -Category "manual-evidence" -Path $noteName -Exists $true
      return $true
    }
  }

  Add-CheckedFile -Category "manual-evidence" -Path $Name -Exists $false
  Add-ErrorMessage "Manual evidence file missing: $Name"
  return $false
}

function Test-FullManualEvidence {
  param(
    [string]$EvidenceDir,
    [string]$ChecklistText
  )

  if ($ChecklistText -notmatch "(?im)^Result:\s*PASS\s*$") {
    Add-ErrorMessage "manual-checklist.md must contain an explicit 'Result: PASS' line for full verification."
  }

  foreach ($field in @("Tester", "Windows version", "Package commit or run id")) {
    if ($ChecklistText -notmatch "(?im)^$([regex]::Escape($field)):\s*\S+") {
      Add-ErrorMessage "manual-checklist.md must include a non-empty '$field' field."
    }
  }

  foreach ($screenshot in @(
    "01-app-launch.png",
    "02-terminal-typed-output.png",
    "03-split-focus.png",
    "04-zoom.png",
    "05-close-pane.png",
    "06-browser-pane.png",
    "manual-browser.png",
    "07-notification-terminal.png",
    "08-session-restore.png"
  )) {
    Test-EvidenceScreenshot -EvidenceDir $EvidenceDir -Name $screenshot | Out-Null
  }

  Test-EvidenceScreenshot -EvidenceDir $EvidenceDir -Name "07-native-toast.png" -AllowNote | Out-Null
}

$evidenceDir = ""
$packageRoot = ""
try {
  $evidenceDir = Resolve-EvidenceDirectory -Path $EvidencePath
  Add-InfoMessage "Evidence directory: $evidenceDir"

  if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $evidenceDir "MANUAL-EVIDENCE-VERIFICATION.json"
  } else {
    $ReportPath = Resolve-FullPath $ReportPath
  }

  foreach ($requiredEvidenceFile in @(
    "environment.json",
    "package-metadata.json",
    "browser-smoke.html",
    "manual-checklist.md",
    "launch.json",
    "cli-help.txt"
  )) {
    Require-File -Root $evidenceDir -RelativePath $requiredEvidenceFile -Category "evidence" | Out-Null
  }

  $environmentPath = Join-Path $evidenceDir "environment.json"
  $metadataPath = Join-Path $evidenceDir "package-metadata.json"
  $checklistPath = Join-Path $evidenceDir "manual-checklist.md"
  $environment = if (Test-Path -LiteralPath $environmentPath -PathType Leaf) { Read-JsonFile -Path $environmentPath -Name "environment.json" } else { $null }
  $metadata = if (Test-Path -LiteralPath $metadataPath -PathType Leaf) { Read-JsonFile -Path $metadataPath -Name "package-metadata.json" } else { $null }
  $checklistText = if (Test-Path -LiteralPath $checklistPath -PathType Leaf) { Get-Content -LiteralPath $checklistPath -Raw } else { "" }

  if ($null -ne $environment) {
    if (-not [string]::IsNullOrWhiteSpace($environment.packageRoot)) {
      Require-File -Root $evidenceDir -RelativePath "package-checksums.txt" -Category "evidence" | Out-Null
    }

    if (-not $PreflightOnly -and [bool]$environment.skipLaunch) {
      Add-WarningMessage "Evidence was produced with -SkipLaunch; verify manual launch context before accepting the desktop gate."
    }
  }

  $resolvedPackagePath = Resolve-FullPath $PackagePath
  if (-not [string]::IsNullOrWhiteSpace($resolvedPackagePath)) {
    $packageRoot = Resolve-PackageRoot -Path $resolvedPackagePath -WorkRoot $evidenceDir
  } elseif ($null -ne $metadata -and -not [string]::IsNullOrWhiteSpace($metadata.packageRoot) -and (Test-Path -LiteralPath $metadata.packageRoot -PathType Container)) {
    $packageRoot = [System.IO.Path]::GetFullPath($metadata.packageRoot)
  }

  if (-not [string]::IsNullOrWhiteSpace($packageRoot)) {
    Test-Package -PackageRoot $packageRoot
  } elseif (-not $PreflightOnly) {
    Add-WarningMessage "No package root was available; full verification cannot recheck PACKAGE.json, EVIDENCE.json, or SHA256SUMS.txt."
  }

  if ($PreflightOnly) {
    Add-InfoMessage "PreflightOnly selected; manual screenshots, PASS marker, and physical-keyboard evidence remain unchecked."
  } else {
    Test-FullManualEvidence -EvidenceDir $evidenceDir -ChecklistText $checklistText
  }
} catch {
  Add-ErrorMessage $_.Exception.Message
}

$proofBoundaries = @(
  "This verifier checks evidence packet shape and package integrity only.",
  "A passing verifier report is not physical keyboard proof by itself.",
  "A human reviewer must inspect real visible Windows desktop screenshots or clips before the manual gate can pass.",
  "Hosted CI and -PreflightOnly output remain preflight evidence only.",
  "The verifier does not redact screenshots, terminal output, CLI stdout, browser text, URLs, or notification text."
)

$missingEvidenceFiles = @($script:CheckedFiles | Where-Object { $_.category -eq "evidence" -and -not $_.exists })
$packageIntegrityVerified = [bool]($script:PackageIntegrityChecked -and -not $script:PackageIntegrityFailed)
$helperEvidencePacketVerified = [bool]($missingEvidenceFiles.Count -eq 0)

Add-InfoMessage "Preparing verification report."

$report = [ordered]@{
  verificationVersion = 1
  checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  mode = if ($PreflightOnly) { "preflight" } else { "manual" }
  status = if ($script:Errors.Count -eq 0) { "passed" } else { "failed" }
  packageIntegrityVerified = $packageIntegrityVerified
  helperEvidencePacketVerified = $helperEvidencePacketVerified
  manualGateStillRequired = [bool]$script:ManualGateStillRequired
  releaseReadinessStillBlocked = [bool]$script:ReleaseReadinessBlocked
  evidencePath = $evidenceDir
  packagePath = Resolve-FullPath $PackagePath
  packageRoot = $packageRoot
  errors = @($script:Errors)
  warnings = @($script:Warnings)
  info = @($script:Info)
  checkedFiles = @($script:CheckedFiles)
  proofBoundaries = $proofBoundaries
}

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
  New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Add-InfoMessage "Verification report: $ReportPath"

if ($script:Errors.Count -gt 0) {
  Write-Error "Manual desktop evidence verification failed with $($script:Errors.Count) error(s)."
  exit 1
}

Add-InfoMessage "Manual desktop evidence verification passed in $($report.mode) mode."
