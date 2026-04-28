<#
.SYNOPSIS
    Publishes winGup and builds the Inno Setup installer.
.DESCRIPTION
    1. Ensures Inno Setup 6 is installed (installs via winget if missing).
    2. Runs dotnet publish (self-contained, single-file, win-x64).
    3. Compiles installer/winGup.iss → dist/winGup-<version>-setup.exe
.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.2.0
#>
param(
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot  = $PSScriptRoot
$IssFile   = Join-Path $RepoRoot "installer\winGup.iss"
$PublishDir = Join-Path $RepoRoot "publish"
$DistDir   = Join-Path $RepoRoot "dist"
$Project   = Join-Path $RepoRoot "src\WinGup\WinGup.csproj"

# ── 1. Inno Setup ────────────────────────────────────────────────────────────

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Installing Inno Setup 6 via winget..." -ForegroundColor Cyan
    winget install --id JRSoftware.InnoSetup --silent --accept-source-agreements --accept-package-agreements
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        Write-Error "Inno Setup not found after install. Restart your terminal and re-run."
        exit 1
    }
}

# ── 2. Publish ────────────────────────────────────────────────────────────────

Write-Host "Publishing winGup $Version (win-x64, self-contained)..." -ForegroundColor Cyan

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish $Project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed."; exit 1 }

$exe = Join-Path $PublishDir "winGup.exe"
if (-not (Test-Path $exe)) { Write-Error "Expected $exe not found after publish."; exit 1 }

# ── 3. Build installer ────────────────────────────────────────────────────────

Write-Host "Building installer..." -ForegroundColor Cyan

if (-not (Test-Path $DistDir)) { New-Item $DistDir -ItemType Directory | Out-Null }

& $iscc $IssFile /DAppVersion=$Version
if ($LASTEXITCODE -ne 0) { Write-Error "ISCC failed."; exit 1 }

$installer = Get-ChildItem $DistDir -Filter "winGup-*-setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host "Done: $($installer.FullName)" -ForegroundColor Green
