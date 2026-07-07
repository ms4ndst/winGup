<#
.SYNOPSIS
    Publishes winGup and builds a signed MSIX package.
.DESCRIPTION
    1. dotnet publish  — self-contained, single-file, win-x64
    2. Generates logo assets (Catppuccin Mauve) if not already present
    3. Locates Windows SDK tools (makeappx, signtool, makepri)
    4. Creates/reuses a self-signed cert (CN=winGup Dev), valid 5 years
    5. Generates resources.pri from the package layout
    6. Packs  → dist/winGup-<version>.msix
    7. Signs  the package with the cert
    8. Installs cert to LocalMachine\TrustedPeople so Windows accepts the package
.NOTES
    Step 8 requires an elevated prompt on first run.
    Subsequent builds reuse the existing cert and skip the elevation step.

    Windows SDK (makeappx/signtool/makepri) must be installed:
      winget install Microsoft.WindowsSDK.10.0.22621

    Service mode is NOT included in the MSIX package — use the raw exe for that:
      sc create WinGup binPath="<path>\winGup.exe --service"
.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.2.0
#>
param(
    [string]$Version = "1.0.5"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot    = $PSScriptRoot
$PublishDir  = Join-Path $RepoRoot "publish"
$PackageDir  = Join-Path $RepoRoot "package"
$DistDir     = Join-Path $RepoRoot "dist"
$Project     = Join-Path $RepoRoot "src\WinGup\WinGup.csproj"
$ManifestSrc = Join-Path $RepoRoot "installer\Package.appxmanifest"
$AssetsDir   = Join-Path $RepoRoot "installer\Assets"
$MsixVersion = "$Version.0"      # MSIX requires 4-part version (X.Y.Z.0)
$CertSubject = "CN=winGup Dev"
$MsixOut     = Join-Path $DistDir "winGup-$Version.msix"

# ── Helper: locate a Windows SDK tool ────────────────────────────────────────

function Find-SdkTool([string]$Name) {
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Filter $Name -Recurse -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -match '\\x64\\' } |
               Sort-Object FullName -Descending |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    return $cmd?.Source
}

# ── 1. Publish ────────────────────────────────────────────────────────────────

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

$ExePath = Join-Path $PublishDir "winGup.exe"
if (-not (Test-Path $ExePath)) { Write-Error "Expected $ExePath not found after publish."; exit 1 }

# ── 2. Locate Windows SDK tools ───────────────────────────────────────────────

Write-Host "Locating Windows SDK tools..." -ForegroundColor Cyan

$makeappx = Find-SdkTool "makeappx.exe"
$signtool  = Find-SdkTool "signtool.exe"
$makepri   = Find-SdkTool "makepri.exe"

foreach ($pair in @(@("makeappx.exe", $makeappx), @("signtool.exe", $signtool), @("makepri.exe", $makepri))) {
    if (-not $pair[1]) {
        Write-Error "$($pair[0]) not found. Install the Windows SDK:`n  winget install Microsoft.WindowsSDK.10.0.22621"
        exit 1
    }
    Write-Host "  $($pair[0]): $($pair[1])"
}

# ── 3. Generate logo assets ───────────────────────────────────────────────────

Write-Host "Generating logo assets..." -ForegroundColor Cyan

if (-not (Test-Path $AssetsDir)) { New-Item $AssetsDir -ItemType Directory | Out-Null }

Add-Type -AssemblyName System.Drawing

function New-Logo {
    param([string]$Path, [int]$W, [int]$H, [string]$Label = "")
    if (Test-Path $Path) { return }

    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # Catppuccin Mauve background
    $g.Clear([System.Drawing.Color]::FromArgb(0xcb, 0xa6, 0xf7))

    if ($Label -ne "") {
        $fontSize = [Math]::Max(8.0, $H * 0.28)
        $font  = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0x11, 0x11, 0x1b))
        $sz    = $g.MeasureString($Label, $font)
        $g.DrawString($Label, $font, $brush, ($W - $sz.Width) / 2, ($H - $sz.Height) / 2)
        $font.Dispose()
        $brush.Dispose()
    }

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "  Created $(Split-Path $Path -Leaf)"
}

New-Logo -Path (Join-Path $AssetsDir "Square44x44Logo.png")   -W  44 -H  44 -Label "wG"
New-Logo -Path (Join-Path $AssetsDir "Square150x150Logo.png") -W 150 -H 150 -Label "wG"
New-Logo -Path (Join-Path $AssetsDir "Wide310x150Logo.png")   -W 310 -H 150 -Label "winGup"
New-Logo -Path (Join-Path $AssetsDir "StoreLogo.png")         -W  50 -H  50 -Label "wG"

# ── 4. Build package layout ───────────────────────────────────────────────────

Write-Host "Building package layout..." -ForegroundColor Cyan

if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }
New-Item $PackageDir -ItemType Directory | Out-Null

Copy-Item $ExePath $PackageDir
Copy-Item $AssetsDir (Join-Path $PackageDir "Assets") -Recurse

# Substitute version into manifest
$manifest = (Get-Content $ManifestSrc -Raw) -replace 'VERSION_PLACEHOLDER', $MsixVersion
Set-Content (Join-Path $PackageDir "AppxManifest.xml") -Value $manifest -Encoding UTF8

# ── 5. Generate resources.pri ─────────────────────────────────────────────────

Write-Host "Generating resources.pri..." -ForegroundColor Cyan

# Use package directory for config to avoid temp path issues
$PriConfig = Join-Path $PackageDir "priconfig.xml"

& $makepri createconfig /cf $PriConfig /dq en-US /pv 10.0.0 /o
if ($LASTEXITCODE -ne 0) { Write-Error "makepri createconfig failed."; exit 1 }

# Verify config file was created
if (-not (Test-Path $PriConfig)) { 
    Write-Error "PRI config file not found at $PriConfig after createconfig"
    exit 1 
}
Write-Host "  Config created: $PriConfig"

# Small delay to ensure file system sync
Start-Sleep -Milliseconds 500

& $makepri new `
    /pr $PackageDir `
    /cf $PriConfig `
    /mn (Join-Path $PackageDir "AppxManifest.xml") `
    /of (Join-Path $PackageDir "resources.pri") `
    /o
if ($LASTEXITCODE -ne 0) { Write-Error "makepri new failed."; exit 1 }

Remove-Item $PriConfig -Force -ErrorAction SilentlyContinue

# ── 6. Code-signing certificate ───────────────────────────────────────────────

Write-Host "Preparing code-signing certificate..." -ForegroundColor Cyan

$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $CertSubject } |
        Select-Object -First 1

if (-not $cert) {
    Write-Host "  Creating self-signed certificate '$CertSubject' (valid 5 years)..." -ForegroundColor DarkCyan
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $CertSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName "winGup Dev Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "  Certificate created (thumbprint: $($cert.Thumbprint))"
} else {
    Write-Host "  Reusing existing certificate (thumbprint: $($cert.Thumbprint))"
}

# Export to a temp PFX for signtool (random password, deleted after signing)
$CertPfx  = Join-Path $env:TEMP "wingup-sign-$($cert.Thumbprint.Substring(0,8)).pfx"
$CertPass = "wingup-build-$(Get-Random)"
$secPass  = ConvertTo-SecureString $CertPass -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $CertPfx -Password $secPass | Out-Null

# Install to TrustedPeople so Windows accepts the sideloaded package
$alreadyTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
                  Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

if (-not $alreadyTrusted) {
    $isAdmin = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isAdmin) {
        Write-Host "  Installing cert to LocalMachine\TrustedPeople..." -ForegroundColor DarkCyan
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
                     "TrustedPeople", "LocalMachine")
        $store.Open("ReadWrite")
        $store.Add($cert)
        $store.Close()
        Write-Host "  Certificate trusted."
    } else {
        Write-Host ""
        Write-Warning @"
Certificate not yet trusted — Add-AppxPackage will fail with 0x800B0109.
Run the following once in an elevated PowerShell, then install normally:

  `$cert = (Get-AuthenticodeSignature "$MsixOut").SignerCertificate
  `$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople","LocalMachine")
  `$store.Open("ReadWrite"); `$store.Add(`$cert); `$store.Close()

Or re-run build-installer.ps1 as Administrator to do this automatically.
"@
    }
}

# ── 7. Pack ───────────────────────────────────────────────────────────────────

Write-Host "Packing MSIX..." -ForegroundColor Cyan

if (-not (Test-Path $DistDir)) { New-Item $DistDir -ItemType Directory | Out-Null }
if (Test-Path $MsixOut) { Remove-Item $MsixOut -Force }

& $makeappx pack /d $PackageDir /p $MsixOut /o
if ($LASTEXITCODE -ne 0) { Write-Error "makeappx pack failed."; exit 1 }

# ── 8. Sign ───────────────────────────────────────────────────────────────────

Write-Host "Signing MSIX..." -ForegroundColor Cyan

& $signtool sign /fd SHA256 /a /f $CertPfx /p $CertPass $MsixOut
if ($LASTEXITCODE -ne 0) { Write-Error "signtool sign failed."; exit 1 }

# Give signtool time to release file handle before cleanup
Start-Sleep -Milliseconds 500
try { Remove-Item $CertPfx -Force -ErrorAction Stop } catch { }

# ── Done ──────────────────────────────────────────────────────────────────────

$sizeMB = [Math]::Round((Get-Item $MsixOut).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $MsixOut ($sizeMB MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Install:" -ForegroundColor Yellow
Write-Host "  Add-AppxPackage -Path `"$MsixOut`"" -ForegroundColor White
Write-Host ""
Write-Host "Uninstall:" -ForegroundColor Yellow
Write-Host "  Get-AppxPackage MagnusSandstrom.winGup | Remove-AppxPackage" -ForegroundColor White
