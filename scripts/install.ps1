# ==============================================================================
# OpenFlux Zen Server - Fast 1-Command Bootstrap Installer (Windows)
# Supports: Windows x64, Windows ARM64
# ==============================================================================

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ""
    Write-Host "  [ERROR] Administrator privileges required." -ForegroundColor Red
    Write-Host "  Please run PowerShell as Administrator (Right click -> Run as administrator) and retry." -ForegroundColor Yellow
    Write-Host ""
    return
}

$arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
    "win-arm64"
} else {
    "win-x64"
}

$tempDir = $env:TEMP
$installerExe = Join-Path $tempDir "openflux-installer-$arch.exe"
$releaseUrl = "https://github.com/BizhQwe/openflux-zen-server/releases/latest/download/openflux-installer-$arch.exe"
$tagUrl = "https://github.com/BizhQwe/openflux-zen-server/releases/download/v1.0.10/openflux-installer-$arch.exe"
$fallbackUrl = "https://github.com/BizhQwe/openflux-zen-server/releases/download/v1.0.9/openflux-installer-$arch.exe"

Write-Host ""
Write-Host "  OpenFlux Zen Server - Windows ($arch)" -ForegroundColor Cyan
Write-Host "  Downloading installer..." -ForegroundColor Gray

$downloaded = $false
foreach ($url in @($releaseUrl, $tagUrl, $fallbackUrl)) {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "OpenFlux-Bootstrap")
        $wc.DownloadFile($url, $installerExe)
        if ((Test-Path $installerExe) -and (Get-Item $installerExe).Length -gt 1000000) {
            $downloaded = $true
            break
        }
    } catch { }
}

if ($downloaded) {
    & $installerExe $args
    return
} else {
    Write-Host "  [ERROR] Failed to download installer for $arch." -ForegroundColor Red
    Write-Host "  Please check internet connection or download manually:" -ForegroundColor Yellow
    Write-Host "  $releaseUrl" -ForegroundColor Yellow
    return
}
