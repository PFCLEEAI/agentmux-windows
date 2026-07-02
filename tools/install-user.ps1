[CmdletBinding()]
param(
  [string]$PackagePath = "",
  [string]$InstallRoot = "",
  [switch]$SkipPathUpdate
)

$ErrorActionPreference = "Stop"

function Write-Step {
  param([string]$Message)
  Write-Host "[agentmux-install] $Message"
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

function Get-DefaultInstallRoot {
  $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
  if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = $env:LOCALAPPDATA
  }

  if ([string]::IsNullOrWhiteSpace($localAppData)) {
    throw "LOCALAPPDATA is not available. Pass -InstallRoot explicitly."
  }

  return (Join-Path $localAppData "Programs\AgentMux")
}

function Resolve-PackageRoot {
  param([string]$InputPath)

  $resolvedInput = Resolve-FullPath $InputPath
  if (-not [string]::IsNullOrWhiteSpace($resolvedInput)) {
    if (-not (Test-Path -LiteralPath $resolvedInput -PathType Container)) {
      throw "PackagePath must be an extracted package directory: $resolvedInput"
    }

    return $resolvedInput
  }

  $scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    [System.IO.Path]::GetFullPath((Get-Location).Path)
  } else {
    [System.IO.Path]::GetFullPath($PSScriptRoot)
  }

  $candidate = Split-Path -Parent $scriptRoot
  if (-not (Test-Path -LiteralPath (Join-Path $candidate "AgentMux.exe") -PathType Leaf)) {
    throw "Could not infer package root from script location. Pass -PackagePath."
  }

  return $candidate
}

function Assert-RequiredPackageFiles {
  param([string]$Root)

  foreach ($requiredFile in @(
    "AgentMux.exe",
    "AgentMux.dll",
    "AgentMux.runtimeconfig.json",
    "cli\agentmux.exe",
    "cli\agentmux.dll",
    "PACKAGE.json",
    "EVIDENCE.json",
    "SHA256SUMS.txt",
    "tools\install-user.ps1",
    "tools\manual-desktop-smoke.ps1",
    "tools\verify-manual-desktop-evidence.ps1"
  )) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $requiredFile) -PathType Leaf)) {
      throw "Required package file missing: $requiredFile"
    }
  }
}

function Test-Sha256Sums {
  param([string]$Root)

  $checksumsPath = Join-Path $Root "SHA256SUMS.txt"
  if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw "SHA256SUMS.txt not found in package root."
  }

  $rootFull = Normalize-PathForCompare $Root
  $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar
  $listed = @{}

  foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ([string]::IsNullOrWhiteSpace($line)) {
      continue
    }

    if ($line -notmatch "^([0-9A-Fa-f]{64})  (.+)$") {
      throw "Invalid checksum line: $line"
    }

    $expectedHash = $Matches[1].ToLowerInvariant()
    $relativeRaw = $Matches[2]
    if ([string]::IsNullOrWhiteSpace($relativeRaw) -or
        [System.IO.Path]::IsPathRooted($relativeRaw) -or
        $relativeRaw.Contains("\") -or
        $relativeRaw.Split("/") -contains "..") {
      throw "Invalid checksum path: $relativeRaw"
    }

    $relative = $relativeRaw.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $path = [System.IO.Path]::GetFullPath((Join-Path $Root $relative))
    if (-not $path.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Checksum path escapes package root: $relativeRaw"
    }

    $relativeKey = $relativeRaw.ToLowerInvariant()
    if ($listed.ContainsKey($relativeKey)) {
      throw "Duplicate checksum entry: $relativeRaw"
    }

    $listed[$relativeKey] = $true

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      throw "Checksum entry is missing from package: $relativeRaw"
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expectedHash) {
      throw "Checksum mismatch for $relativeRaw"
    }
  }

  Get-ChildItem -LiteralPath $Root -Recurse -File -Force | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace("\", "/")
    if ($relative -ne "SHA256SUMS.txt") {
      if (-not $listed.ContainsKey($relative.ToLowerInvariant())) {
        throw "Package file is not covered by SHA256SUMS.txt: $relative"
      }
    }
  }
}

function Normalize-PathForCompare {
  param([string]$Path)
  return [System.IO.Path]::GetFullPath($Path).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
}

function Assert-SafeInstallRoot {
  param(
    [string]$PackageRoot,
    [string]$TargetRoot
  )

  $target = Normalize-PathForCompare $TargetRoot
  $package = Normalize-PathForCompare $PackageRoot
  $driveRoot = [System.IO.Path]::GetPathRoot($target).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
  $forbiddenExact = @(
    $env:USERPROFILE,
    $env:LOCALAPPDATA,
    $env:APPDATA,
    $env:TEMP,
    $env:TMP
  )
  $forbiddenTrees = @(
    $env:SystemRoot,
    [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles),
    ${env:ProgramFiles(x86)}
  )

  if ([string]::Equals($target, $driveRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install into a drive root: $target"
  }

  if ($target.StartsWith("\\", [System.StringComparison]::Ordinal)) {
    throw "Refusing to install into a UNC/network path: $target"
  }

  foreach ($forbidden in $forbiddenExact) {
    if ([string]::IsNullOrWhiteSpace($forbidden)) {
      continue
    }

    $forbiddenPath = Normalize-PathForCompare $forbidden
    if ([string]::Equals($target, $forbiddenPath, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to install into broad user/system directory: $target"
    }
  }

  foreach ($forbidden in $forbiddenTrees) {
    if ([string]::IsNullOrWhiteSpace($forbidden)) {
      continue
    }

    $forbiddenPath = Normalize-PathForCompare $forbidden
    $forbiddenPrefix = $forbiddenPath + [System.IO.Path]::DirectorySeparatorChar
    if ([string]::Equals($target, $forbiddenPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $target.StartsWith($forbiddenPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to install into system or machine-wide location: $target"
    }
  }

  if ([string]::Equals($target, $package, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallRoot must be different from the package root."
  }

  $targetPrefix = $target + [System.IO.Path]::DirectorySeparatorChar
  $packagePrefix = $package + [System.IO.Path]::DirectorySeparatorChar

  if ($package.StartsWith($targetPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallRoot cannot contain the package root because install replaces the target directory."
  }

  if ($target.StartsWith($packagePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallRoot cannot be inside the package root because package copy would recurse."
  }
}

function Test-AgentMuxInstallRoot {
  param([string]$Root)

  if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    return $false
  }

  if (Test-Path -LiteralPath (Join-Path $Root "INSTALL.json") -PathType Leaf) {
    try {
      $installInfo = Get-Content -LiteralPath (Join-Path $Root "INSTALL.json") -Raw | ConvertFrom-Json
      if (-not [string]::IsNullOrWhiteSpace($installInfo.installRoot) -and
          -not [string]::IsNullOrWhiteSpace($installInfo.app) -and
          -not [string]::IsNullOrWhiteSpace($installInfo.cli)) {
        return $true
      }
    } catch {
      return $false
    }
  }

  return (
    (Test-Path -LiteralPath (Join-Path $Root "AgentMux.exe") -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $Root "cli\agentmux.exe") -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $Root "PACKAGE.json") -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $Root "tools\install-user.ps1") -PathType Leaf)
  )
}

function Assert-ReplaceableInstallRoot {
  param([string]$Root)

  if (-not (Test-Path -LiteralPath $Root)) {
    return
  }

  if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    throw "InstallRoot exists but is not a directory: $Root"
  }

  $children = @(Get-ChildItem -LiteralPath $Root -Force)
  if ($children.Count -eq 0) {
    return
  }

  if (-not (Test-AgentMuxInstallRoot -Root $Root)) {
    throw "Refusing to replace a non-AgentMux install directory: $Root"
  }
}

function Copy-PackageToInstallRoot {
  param(
    [string]$PackageRoot,
    [string]$TargetRoot
  )

  $targetParent = Split-Path -Parent $TargetRoot
  if ([string]::IsNullOrWhiteSpace($targetParent)) {
    throw "InstallRoot must have a parent directory."
  }

  New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
  $stagingRoot = Join-Path $targetParent ("AgentMux.installing." + [Guid]::NewGuid().ToString("N"))
  $backupRoot = ""

  try {
    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

    Get-ChildItem -LiteralPath $PackageRoot -Force | ForEach-Object {
      Copy-Item -LiteralPath $_.FullName -Destination $stagingRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $TargetRoot) {
      $backupRoot = Join-Path $targetParent ("AgentMux.backup." + [Guid]::NewGuid().ToString("N"))
      Move-Item -LiteralPath $TargetRoot -Destination $backupRoot
    }

    Move-Item -LiteralPath $stagingRoot -Destination $TargetRoot

    if (-not [string]::IsNullOrWhiteSpace($backupRoot) -and (Test-Path -LiteralPath $backupRoot)) {
      Remove-Item -LiteralPath $backupRoot -Recurse -Force
    }
  } catch {
    if (-not [string]::IsNullOrWhiteSpace($backupRoot) -and
        (Test-Path -LiteralPath $backupRoot) -and
        -not (Test-Path -LiteralPath $TargetRoot)) {
      Move-Item -LiteralPath $backupRoot -Destination $TargetRoot
    }

    throw
  } finally {
    if (Test-Path -LiteralPath $stagingRoot) {
      Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
  }
}

function Write-Shims {
  param([string]$TargetRoot)

  $binRoot = Join-Path $TargetRoot "bin"
  New-Item -ItemType Directory -Force -Path $binRoot | Out-Null

  $cliShim = @"
@echo off
setlocal
set "AGENTMUX_INSTALL=%~dp0.."
"%AGENTMUX_INSTALL%\cli\agentmux.exe" %*
"@

  $appShim = @"
@echo off
setlocal
set "AGENTMUX_INSTALL=%~dp0.."
"%AGENTMUX_INSTALL%\AgentMux.exe" %*
"@

  [System.IO.File]::WriteAllText((Join-Path $binRoot "agentmux.cmd"), $cliShim, [System.Text.Encoding]::ASCII)
  [System.IO.File]::WriteAllText((Join-Path $binRoot "agentmux-app.cmd"), $appShim, [System.Text.Encoding]::ASCII)

  return $binRoot
}

function Test-PathListContains {
  param(
    [string]$PathList,
    [string]$Needle
  )

  $normalizedNeedle = Normalize-PathForCompare $Needle
  foreach ($entry in ($PathList -split ";")) {
    if ([string]::IsNullOrWhiteSpace($entry)) {
      continue
    }

    try {
      $normalizedEntry = Normalize-PathForCompare $entry
      if ([string]::Equals($normalizedEntry, $normalizedNeedle, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
      }
    } catch {
      continue
    }
  }

  return $false
}

function Add-ToUserPath {
  param([string]$BinRoot)

  $userPath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::User)
  if (Test-PathListContains -PathList $userPath -Needle $BinRoot) {
    return $false
  }

  $separator = if ([string]::IsNullOrWhiteSpace($userPath) -or $userPath.EndsWith(";")) { "" } else { ";" }
  [Environment]::SetEnvironmentVariable("Path", "$userPath$separator$BinRoot", [EnvironmentVariableTarget]::User)

  if (-not (Test-PathListContains -PathList $env:Path -Needle $BinRoot)) {
    $env:Path = "$env:Path;$BinRoot"
  }

  return $true
}

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $runningOnWindows) {
  throw "install-user.ps1 must be run on Windows."
}

$packageRoot = Resolve-PackageRoot -InputPath $PackagePath
$installTarget = if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
  Get-DefaultInstallRoot
} else {
  Resolve-FullPath $InstallRoot
}

Assert-RequiredPackageFiles -Root $packageRoot
Assert-SafeInstallRoot -PackageRoot $packageRoot -TargetRoot $installTarget
Assert-ReplaceableInstallRoot -Root $installTarget

Write-Step "Package root: $packageRoot"
Write-Step "Install root: $installTarget"
Write-Step "Verifying package checksums."
Test-Sha256Sums -Root $packageRoot

Write-Step "Copying package files."
Copy-PackageToInstallRoot -PackageRoot $packageRoot -TargetRoot $installTarget

Write-Step "Creating command shims."
$binRoot = Write-Shims -TargetRoot $installTarget

$pathUpdated = $false
if ($SkipPathUpdate) {
  Write-Step "Skipping user PATH update."
} else {
  $pathUpdated = Add-ToUserPath -BinRoot $binRoot
  if ($pathUpdated) {
    Write-Step "Added shim directory to the current user's PATH. Open a new shell to use it everywhere."
  } else {
    Write-Step "Shim directory is already on the current user's PATH."
  }
}

$installedCli = Join-Path $installTarget "cli\agentmux.exe"
$cliHelp = & $installedCli --help
if ($LASTEXITCODE -ne 0 -or ($cliHelp -join "`n") -notmatch "agentmux - CLI") {
  throw "Installed CLI smoke failed."
}

$installedCliShim = Join-Path $installTarget "bin\agentmux.cmd"
$shimHelp = & $installedCliShim --help
if ($LASTEXITCODE -ne 0 -or ($shimHelp -join "`n") -notmatch "agentmux - CLI") {
  throw "Installed CLI shim smoke failed."
}

$installInfo = [ordered]@{
  installedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  packageRoot = $packageRoot
  installRoot = $installTarget
  app = Join-Path $installTarget "AgentMux.exe"
  cli = $installedCli
  shimRoot = $binRoot
  cliShim = Join-Path $binRoot "agentmux.cmd"
  appShim = Join-Path $binRoot "agentmux-app.cmd"
  userPathUpdated = [bool]$pathUpdated
  skippedPathUpdate = [bool]$SkipPathUpdate
}
$installInfo | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $installTarget "INSTALL.json")

Write-Step "Installed AgentMux Windows."
Write-Host "App: $($installInfo.app)"
Write-Host "CLI: $($installInfo.cli)"
Write-Host "CLI shim: $($installInfo.cliShim)"
Write-Host "App shim: $($installInfo.appShim)"
if ($SkipPathUpdate) {
  Write-Host "PATH was not changed. Add this directory to PATH if wanted: $binRoot"
} elseif ($pathUpdated) {
  Write-Host "Open a new terminal before using 'agentmux' from PATH."
}
