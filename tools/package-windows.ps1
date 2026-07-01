[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$OutputPath,

  [string]$Configuration = "Release",
  [string]$Commit = $env:GITHUB_SHA,
  [string]$RunId = $env:GITHUB_RUN_ID,
  [string]$RunNumber = $env:GITHUB_RUN_NUMBER,
  [string]$RunAttempt = $env:GITHUB_RUN_ATTEMPT,
  [string]$Repository = $env:GITHUB_REPOSITORY,
  [string]$Ref = $env:GITHUB_REF,
  [string]$RefName = $env:GITHUB_REF_NAME,
  [string]$RefType = $env:GITHUB_REF_TYPE,
  [string]$EventName = $env:GITHUB_EVENT_NAME,
  [string]$Workflow = $env:GITHUB_WORKFLOW,
  [string]$ReleaseTag = "",
  [string]$AssetBase = "agentmux-windows-package",
  [string]$PackageLayout = "zip-root",

  [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$package = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
  [System.IO.Path]::GetFullPath($OutputPath)
} else {
  [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
}

function Invoke-DotNet {
  param([string[]]$Arguments)

  & dotnet @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
  }
}

function Get-ProvenanceValue {
  param(
    [string]$Value,
    [string]$Fallback
  )

  if ([string]::IsNullOrWhiteSpace($Value)) {
    return $Fallback
  }

  return $Value
}

Push-Location $repoRoot
try {
  if (Test-Path $package) {
    Remove-Item -Recurse -Force $package
  }

  New-Item -ItemType Directory -Force -Path $package | Out-Null
  $cliPackage = Join-Path $package "cli"
  New-Item -ItemType Directory -Force -Path $cliPackage | Out-Null

  $appPublishArgs = @(
    "publish",
    "src/AgentMux.Win.App/AgentMux.Win.App.csproj",
    "-c",
    $Configuration,
    "-o",
    $package
  )
  $cliPublishArgs = @(
    "publish",
    "src/AgentMux.Cli/AgentMux.Cli.csproj",
    "-c",
    $Configuration,
    "-o",
    $cliPackage
  )

  if ($NoBuild) {
    $appPublishArgs += "--no-build"
    $cliPublishArgs += "--no-build"
  }

  Invoke-DotNet $appPublishArgs
  Invoke-DotNet $cliPublishArgs

  Get-ChildItem $package -Recurse -Include *.pdb,*.xml | Remove-Item -Force
  New-Item -ItemType Directory -Force -Path (Join-Path $package "docs") | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $package "tools") | Out-Null
  Copy-Item README.md $package
  Copy-Item LICENSE $package
  Copy-Item docs/manual-windows-desktop-smoke.md (Join-Path $package "docs")
  Copy-Item tools/install-user.ps1 (Join-Path $package "tools")
  Copy-Item tools/manual-desktop-smoke.ps1 (Join-Path $package "tools")

  $requiredFiles = @(
    "AgentMux.exe",
    "AgentMux.dll",
    "AgentMux.runtimeconfig.json",
    "AgentMux.deps.json",
    "cli\agentmux.exe",
    "cli\agentmux.dll",
    "cli\agentmux.runtimeconfig.json",
    "cli\agentmux.deps.json",
    "AgentMux.Core.dll",
    "AgentMux.Win.Pty.dll",
    "Assets\Terminal\terminal.html",
    "Assets\Terminal\vendor\xterm\xterm.css",
    "Assets\Terminal\vendor\xterm\xterm.js",
    "Microsoft.Web.WebView2.Core.dll",
    "Microsoft.Web.WebView2.Wpf.dll",
    "Microsoft.Windows.SDK.NET.dll",
    "WinRT.Runtime.dll",
    "runtimes\win-x64\native\WebView2Loader.dll",
    "README.md",
    "LICENSE",
    "docs\manual-windows-desktop-smoke.md",
    "tools\install-user.ps1",
    "tools\manual-desktop-smoke.ps1",
    "PACKAGE.json",
    "SHA256SUMS.txt"
  )

  $manifest = [ordered]@{
    name = "AgentMux Windows"
    packageKind = "framework-dependent"
    packageLayout = $PackageLayout
    assetBase = $AssetBase
    repository = Get-ProvenanceValue $Repository "local"
    ref = Get-ProvenanceValue $Ref "local"
    refName = Get-ProvenanceValue $RefName "local"
    refType = Get-ProvenanceValue $RefType "local"
    eventName = Get-ProvenanceValue $EventName "local"
    workflow = Get-ProvenanceValue $Workflow "local"
    releaseTag = if ([string]::IsNullOrWhiteSpace($ReleaseTag)) { $null } else { $ReleaseTag }
    commit = Get-ProvenanceValue $Commit "local"
    runId = Get-ProvenanceValue $RunId "local"
    runNumber = Get-ProvenanceValue $RunNumber "local"
    runAttempt = Get-ProvenanceValue $RunAttempt "local"
    builtAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    app = "AgentMux.exe"
    cli = "cli\agentmux.exe"
    requires = @(".NET 9 Desktop Runtime", "WebView2 Runtime")
    requiredFiles = $requiredFiles
  }
  $manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $package "PACKAGE.json")

  $hashes = Get-ChildItem $package -Recurse -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object FullName |
    ForEach-Object {
      $relative = [System.IO.Path]::GetRelativePath($package, $_.FullName).Replace("\", "/")
      $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      "$hash  $relative"
    }
  [System.IO.File]::WriteAllText(
    (Join-Path $package "SHA256SUMS.txt"),
    (($hashes -join "`n") + "`n"),
    [System.Text.Encoding]::ASCII)

  foreach ($file in $requiredFiles) {
    $path = Join-Path $package $file
    if (-not (Test-Path $path)) {
      throw "Missing package file: $file"
    }
  }

  $cliHelp = & (Join-Path $package "cli\agentmux.exe") --help
  if ($LASTEXITCODE -ne 0 -or ($cliHelp -join "`n") -notmatch "agentmux - CLI") {
    throw "Packaged CLI smoke failed."
  }

  Write-Host "Created AgentMux Windows package at $package"
  Get-ChildItem $package | Sort-Object Name | Select-Object Name,Length
} finally {
  Pop-Location
}
