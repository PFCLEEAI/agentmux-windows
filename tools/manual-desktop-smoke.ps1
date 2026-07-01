[CmdletBinding()]
param(
  [string]$PackagePath = "",
  [string]$AppPath = "",
  [string]$CliPath = "",
  [string]$EvidenceRoot = "",
  [switch]$SkipLaunch,
  [switch]$RunRpcChecks
)

$ErrorActionPreference = "Stop"

function Write-Step {
  param([string]$Message)
  Write-Host "[agentmux-smoke] $Message"
}

function Add-Failure {
  param([string]$Message)
  $script:Failures.Add($Message) | Out-Null
  Write-Warning $Message
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

function Resolve-PackageEntryPath {
  param(
    [string]$Root,
    [string]$RelativePath
  )

  if ([string]::IsNullOrWhiteSpace($Root) -or [string]::IsNullOrWhiteSpace($RelativePath)) {
    return ""
  }

  if ([System.IO.Path]::IsPathRooted($RelativePath)) {
    throw "Package manifest path must be relative: $RelativePath"
  }

  $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
  $full = [System.IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
  $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar

  if (-not $full.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Package manifest path escapes package root: $RelativePath"
  }

  return $full
}

function Write-TextFile {
  param(
    [string]$Path,
    [string]$Content
  )

  [System.IO.File]::WriteAllText($Path, $Content, [System.Text.Encoding]::UTF8)
}

function Invoke-Captured {
  param(
    [string]$FilePath,
    [string[]]$Arguments,
    [string]$OutputPath,
    [switch]$AllowFailure
  )

  $stdout = & $FilePath @Arguments 2>&1
  $exitCode = $LASTEXITCODE
  Write-TextFile -Path $OutputPath -Content (($stdout | Out-String) + "`nexitCode=$exitCode`n")

  if (-not $AllowFailure -and $exitCode -ne 0) {
    throw "$FilePath $($Arguments -join ' ') failed with exit code $exitCode."
  }

  return $exitCode
}

function Test-Sha256Sums {
  param(
    [string]$PackageRoot,
    [string]$OutputPath
  )

  $checksumsPath = Join-Path $PackageRoot "SHA256SUMS.txt"
  if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    Write-TextFile -Path $OutputPath -Content "SHA256SUMS.txt not found; package checksum verification skipped.`n"
    return $true
  }

  $lines = Get-Content -LiteralPath $checksumsPath
  $results = New-Object System.Collections.Generic.List[string]
  $ok = $true

  foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
      continue
    }

    $parts = $line -split "  ", 2
    if ($parts.Length -ne 2) {
      $ok = $false
      $results.Add("INVALID LINE: $line") | Out-Null
      continue
    }

    $relative = $parts[1].Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $path = Join-Path $PackageRoot $relative

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      $ok = $false
      $results.Add("MISSING: $($parts[1])") | Out-Null
      continue
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $parts[0].ToLowerInvariant()) {
      $ok = $false
      $results.Add("MISMATCH: $($parts[1]) expected=$($parts[0]) actual=$actual") | Out-Null
      continue
    }

    $results.Add("OK: $($parts[1])") | Out-Null
  }

  Write-TextFile -Path $OutputPath -Content (($results -join "`n") + "`n")
  return $ok
}

function Get-WebView2RuntimeInfo {
  $keys = @(
    "HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9C7F0C5}",
    "HKLM:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9C7F0C5}",
    "HKLM:\Software\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9C7F0C5}"
  )

  foreach ($key in $keys) {
    try {
      if (Test-Path $key) {
        $value = Get-ItemProperty -Path $key
        return [ordered]@{
          found = $true
          registryKey = $key
          productVersion = $value.pv
          name = $value.name
        }
      }
    } catch {
      return [ordered]@{
        found = $false
        registryKey = $key
        error = $_.Exception.Message
      }
    }
  }

  return [ordered]@{
    found = $false
    registryKey = $null
    productVersion = $null
    name = $null
  }
}

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $runningOnWindows) {
  throw "manual-desktop-smoke.ps1 must be run from an interactive Windows desktop session."
}

$script:Failures = New-Object System.Collections.Generic.List[string]
$scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
  [System.IO.Path]::GetFullPath((Get-Location).Path)
} else {
  [System.IO.Path]::GetFullPath($PSScriptRoot)
}

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
  $EvidenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "agentmux-desktop-smoke-evidence"
}

$EvidenceRoot = Resolve-FullPath $EvidenceRoot
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDir = Join-Path $EvidenceRoot $timestamp
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $evidenceDir "screenshots") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $evidenceDir "logs") | Out-Null
Write-Step "Evidence directory: $evidenceDir"

$packageRoot = ""
$packageInput = Resolve-FullPath $PackagePath
$candidateFromScript = Split-Path -Parent $scriptRoot
$candidateFromLocation = [System.IO.Path]::GetFullPath((Get-Location).Path)

if (-not [string]::IsNullOrWhiteSpace($packageInput)) {
  if (-not (Test-Path -LiteralPath $packageInput)) {
    throw "PackagePath does not exist: $packageInput"
  }

  if (Test-Path -LiteralPath $packageInput -PathType Leaf) {
    $extension = [System.IO.Path]::GetExtension($packageInput)
    if ($extension -ne ".zip") {
      throw "PackagePath file must be a .zip file: $packageInput"
    }

    $outerChecksum = "$packageInput.sha256"
    if (Test-Path -LiteralPath $outerChecksum -PathType Leaf) {
      $checksumText = [System.IO.File]::ReadAllText($outerChecksum, [System.Text.Encoding]::ASCII)
      if ($checksumText.Contains("`r") -or $checksumText -notmatch "^[0-9A-Fa-f]{64}  .+`n$") {
        throw "Release ZIP sibling .sha256 must be one LF-only '<sha256>  <filename>' line."
      }

      $checksumParts = $checksumText.TrimEnd("`n") -split "  ", 2
      if ($checksumParts.Length -ne 2 -or $checksumParts[1] -ne (Split-Path -Leaf $packageInput)) {
        throw "Release ZIP sibling .sha256 filename does not match package ZIP."
      }

      $expectedHash = $checksumParts[0].ToLowerInvariant()
      $actualHash = (Get-FileHash -LiteralPath $packageInput -Algorithm SHA256).Hash.ToLowerInvariant()
      Write-TextFile -Path (Join-Path $evidenceDir "zip-checksum.txt") -Content "expected=$expectedHash`nactual=$actualHash`n"
      if ($actualHash -ne $expectedHash) {
        throw "Release ZIP checksum mismatch."
      }
    } else {
      Write-TextFile -Path (Join-Path $evidenceDir "zip-checksum.txt") -Content "Sibling .sha256 not found; outer ZIP checksum verification failed.`n"
      throw "Release ZIP sibling .sha256 not found: $outerChecksum"
    }

    $extractRoot = Join-Path $evidenceDir "package-extract"
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -LiteralPath $packageInput -DestinationPath $extractRoot -Force
    $packageRoot = $extractRoot
  } else {
    $packageRoot = $packageInput
  }
} elseif (Test-Path -LiteralPath (Join-Path $candidateFromScript "AgentMux.exe") -PathType Leaf) {
  $packageRoot = $candidateFromScript
} elseif (Test-Path -LiteralPath (Join-Path $candidateFromLocation "AgentMux.exe") -PathType Leaf) {
  $packageRoot = $candidateFromLocation
}

$packageManifest = $null
$evidenceManifest = $null
if (-not [string]::IsNullOrWhiteSpace($packageRoot)) {
  $manifestPath = Join-Path $packageRoot "PACKAGE.json"
  if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    try {
      $packageManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    } catch {
      Add-Failure "PACKAGE.json is invalid: $($_.Exception.Message)"
    }
  } else {
    Add-Failure "PACKAGE.json not found in package root: $packageRoot"
  }

  $evidencePath = Join-Path $packageRoot "EVIDENCE.json"
  if (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
    try {
      $evidenceManifest = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    } catch {
      Add-Failure "EVIDENCE.json is invalid: $($_.Exception.Message)"
    }
  } else {
    Add-Failure "EVIDENCE.json not found in package root: $packageRoot"
  }
}

$resolvedAppPath = Resolve-FullPath $AppPath
$resolvedCliPath = Resolve-FullPath $CliPath

if ([string]::IsNullOrWhiteSpace($resolvedAppPath) -and $null -ne $packageManifest -and -not [string]::IsNullOrWhiteSpace($packageManifest.app)) {
  try {
    $resolvedAppPath = Resolve-PackageEntryPath -Root $packageRoot -RelativePath $packageManifest.app
  } catch {
    Add-Failure $_.Exception.Message
  }
}

if ([string]::IsNullOrWhiteSpace($resolvedCliPath) -and $null -ne $packageManifest -and -not [string]::IsNullOrWhiteSpace($packageManifest.cli)) {
  try {
    $resolvedCliPath = Resolve-PackageEntryPath -Root $packageRoot -RelativePath $packageManifest.cli
  } catch {
    Add-Failure $_.Exception.Message
  }
}

if ([string]::IsNullOrWhiteSpace($resolvedAppPath) -and -not [string]::IsNullOrWhiteSpace($packageRoot)) {
  $resolvedAppPath = Join-Path $packageRoot "AgentMux.exe"
}

if ([string]::IsNullOrWhiteSpace($resolvedCliPath) -and -not [string]::IsNullOrWhiteSpace($packageRoot)) {
  $resolvedCliPath = Join-Path $packageRoot "cli\agentmux.exe"
}

if ([string]::IsNullOrWhiteSpace($resolvedAppPath) -or -not (Test-Path -LiteralPath $resolvedAppPath -PathType Leaf)) {
  Add-Failure "AgentMux.exe not found. Pass -PackagePath or -AppPath."
}

if ([string]::IsNullOrWhiteSpace($resolvedCliPath) -or -not (Test-Path -LiteralPath $resolvedCliPath -PathType Leaf)) {
  Add-Failure "agentmux.exe CLI not found. Pass -PackagePath or -CliPath."
}

if (-not [string]::IsNullOrWhiteSpace($packageRoot)) {
  foreach ($requiredPackageFile in @(
    "AgentMux.exe",
    "cli\agentmux.exe",
    "PACKAGE.json",
    "EVIDENCE.json",
    "SHA256SUMS.txt",
    "docs\manual-windows-desktop-smoke.md",
    "tools\install-user.ps1",
    "tools\manual-desktop-smoke.ps1"
  )) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $requiredPackageFile) -PathType Leaf)) {
      Add-Failure "Required package file missing: $requiredPackageFile"
    }
  }
}

$dotnetRuntimes = @()
try {
  $dotnetRuntimes = & dotnet --list-runtimes 2>&1
} catch {
  $dotnetRuntimes = @("dotnet --list-runtimes failed: $($_.Exception.Message)")
}

$osInfo = $null
try {
  $osInfo = Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture
} catch {
  $osInfo = [ordered]@{ error = $_.Exception.Message }
}

$environment = [ordered]@{
  capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  powershell = [ordered]@{
    version = $PSVersionTable.PSVersion.ToString()
    edition = $PSVersionTable.PSEdition
  }
  os = $osInfo
  dotnetRuntimes = @($dotnetRuntimes)
  webView2Runtime = Get-WebView2RuntimeInfo
  packageRoot = $packageRoot
  appPath = $resolvedAppPath
  cliPath = $resolvedCliPath
  skipLaunch = [bool]$SkipLaunch
  runRpcChecks = [bool]$RunRpcChecks
}
$environment | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $evidenceDir "environment.json")

$packageMetadata = [ordered]@{
  packageRoot = $packageRoot
  appPath = $resolvedAppPath
  cliPath = $resolvedCliPath
  manifest = $packageManifest
  evidence = $evidenceManifest
}
$packageMetadata | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $evidenceDir "package-metadata.json")

$browserSmokePath = Join-Path $evidenceDir "browser-smoke.html"
$browserSmokeHtml = @"
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>AgentMux Desktop Smoke</title>
</head>
<body>
  <h1 id="ready">AgentMux Desktop Smoke</h1>
  <input id="prompt" value="">
  <button id="submit" onclick="document.getElementById('status').textContent = document.getElementById('prompt').value">Submit</button>
  <p id="status">waiting</p>
  <script>console.log("agentmux desktop smoke page ready");</script>
</body>
</html>
"@
Write-TextFile -Path $browserSmokePath -Content $browserSmokeHtml
$browserSmokeUri = ([System.Uri]$browserSmokePath).AbsoluteUri

if (-not [string]::IsNullOrWhiteSpace($packageRoot)) {
  $checksumOk = Test-Sha256Sums -PackageRoot $packageRoot -OutputPath (Join-Path $evidenceDir "package-checksums.txt")
  if (-not $checksumOk) {
    Add-Failure "Package SHA256SUMS.txt verification failed."
  }
}

if (-not [string]::IsNullOrWhiteSpace($resolvedCliPath) -and (Test-Path -LiteralPath $resolvedCliPath -PathType Leaf)) {
  try {
    Invoke-Captured -FilePath $resolvedCliPath -Arguments @("--help") -OutputPath (Join-Path $evidenceDir "cli-help.txt") | Out-Null
  } catch {
    Add-Failure "CLI --help failed: $($_.Exception.Message)"
  }
}

$launchInfo = [ordered]@{
  launched = $false
  processId = $null
  appPath = $resolvedAppPath
  workingDirectory = $null
  error = $null
}

if (-not $SkipLaunch -and $script:Failures.Count -eq 0) {
  try {
    $workingDirectory = Split-Path -Parent $resolvedAppPath
    $process = Start-Process -FilePath $resolvedAppPath -WorkingDirectory $workingDirectory -PassThru
    $launchInfo.launched = $true
    $launchInfo.processId = $process.Id
    $launchInfo.workingDirectory = $workingDirectory
    Write-Step "Launched AgentMux.exe with PID $($process.Id)."
    Start-Sleep -Seconds 3
  } catch {
    $launchInfo.error = $_.Exception.Message
    Add-Failure "Failed to launch AgentMux.exe: $($_.Exception.Message)"
  }
}

$launchInfo | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $evidenceDir "launch.json")

if ($RunRpcChecks -and -not [string]::IsNullOrWhiteSpace($resolvedCliPath) -and (Test-Path -LiteralPath $resolvedCliPath -PathType Leaf)) {
  foreach ($rpcCheck in @("ping", "status")) {
    try {
      Invoke-Captured -FilePath $resolvedCliPath -Arguments @($rpcCheck) -OutputPath (Join-Path $evidenceDir "rpc-$rpcCheck.txt") | Out-Null
    } catch {
      Add-Failure "RPC check '$rpcCheck' failed: $($_.Exception.Message)"
    }
  }
}

$checklist = @"
# AgentMux Windows Manual Desktop Smoke Checklist

Evidence folder: $evidenceDir
Captured: $((Get-Date).ToString("o"))

Do not paste tokens, passwords, SSH keys, API keys, customer data, private prompts, browser cookies, or browser trace files from private pages into this checklist.

## Preflight

- [ ] environment.json exists.
- [ ] package-metadata.json exists and paths point to the intended package.
- [ ] package-checksums.txt is all OK, or checksum skip is explained.
- [ ] cli-help.txt exists and shows agentmux - CLI.
- [ ] launch.json records the app launch or -SkipLaunch was intentional.
- [ ] tools\install-user.ps1 exists if the smoke is using a packaged build.

## Manual Evidence

- [ ] 01-app-launch.png: visible WPF window, workspace sidebar, terminal pane.
- [ ] 02-terminal-typed-output.png: physical keyboard typed echo AGENTMUX_DESKTOP_SMOKE and output is visible.
- [ ] 03-split-focus.png: split layout visible and focus moved with keyboard shortcuts.
- [ ] 04-zoom.png: zoom toggle visible.
- [ ] 05-close-pane.png: close-pane behavior visible.
- [ ] 06-browser-pane.png and manual-browser.png: browser pane loaded and screenshot command worked.
- [ ] 07-notification-terminal.png: OSC notification captured, raw escape text not visible.
- [ ] 07-native-toast.png or note: native Windows toast preview visible, or Windows settings/elevation recorded if no toast appeared.
- [ ] 08-session-restore.png: layout restored after relaunch.

Browser smoke file URI:

$browserSmokeUri

## CLI/RPC Results

- [ ] agentmux ping passed while the app was running.
- [ ] agentmux status passed while the app was running.
- [ ] agentmux workspace list returned the visible workspace state.
- [ ] agentmux focus right matched visible focus behavior when a right neighbor existed.
- [ ] agentmux pane resize --cols 100 --rows 30 completed against an active terminal pane.
- [ ] agentmux read-screen --lines 50 included the terminal smoke output.
- [ ] Browser automation commands returned expected output.
- [ ] Notification list/jump-latest/clear returned expected output.

## Notes

Result: PASS / FAIL

Tester:

Windows version:

Package commit or run id:

Failures or deviations:

Redactions performed before sharing evidence:

"@
Write-TextFile -Path (Join-Path $evidenceDir "manual-checklist.md") -Content $checklist

Write-Step "Manual checklist: $(Join-Path $evidenceDir "manual-checklist.md")"
Write-Step "Next: follow docs/manual-windows-desktop-smoke.md and add screenshots to the evidence folder."

if ($script:Failures.Count -gt 0) {
  Write-TextFile -Path (Join-Path $evidenceDir "failures.txt") -Content (($script:Failures -join "`n") + "`n")
  Write-Error "Manual desktop smoke preflight failed. See failures.txt in $evidenceDir."
  exit 1
}

Write-Step "Preflight complete. Manual desktop checks are still required before this smoke can pass."
