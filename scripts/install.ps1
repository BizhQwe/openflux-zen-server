# ==============================================================================
# OpenFlux Zen Server - Fast 1-Command Bootstrap Installer (Windows)
# Supports: Windows x64, Windows ARM64, Windows x86
# ==============================================================================

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ""
    Write-Host "  [ERROR] Administrator privileges required." -ForegroundColor Red
    Write-Host "  Please run PowerShell as Administrator (Right click -> Run as administrator) and retry." -ForegroundColor Yellow
    Write-Host ""
    return
}

$nativeArch = $env:PROCESSOR_ARCHITEW6432
if (-not $nativeArch) { $nativeArch = $env:PROCESSOR_ARCHITECTURE }
$arch = switch ($nativeArch.ToUpperInvariant()) {
    'AMD64' { 'windows-amd64' }
    'ARM64' { 'windows-arm64' }
    'X86'   { 'windows-386' }
    default { 'windows-amd64' }
}
$displayArch = switch ($nativeArch.ToUpperInvariant()) {
    'ARM64' { 'win-arm64' }
    'X86'   { 'win-x86' }
    default { 'win-x64' }
}

$tempDir = $env:TEMP
$installerExe = Join-Path $tempDir "openflux-zen-server-installer-$arch.exe"
$repo = if ($env:OPENFLUX_REPOSITORY) { $env:OPENFLUX_REPOSITORY } else { "BizhQwe/openflux-zen-server" }
$releaseUrl = "https://github.com/$repo/releases/latest/download/openflux-zen-server-installer-$arch.exe"
$tagUrl = "https://github.com/$repo/releases/download/v1.1.0/openflux-zen-server-installer-$arch.exe"
$fallbackUrl = "https://github.com/$repo/releases/latest/download/openflux-zen-server-installer-$arch.exe"

Write-Host ""
Write-Host "  OpenFlux Zen Server - Windows ($displayArch)" -ForegroundColor Cyan
Write-Host "  Downloading installer..." -ForegroundColor Gray

$downloaded = $false
foreach ($url in @($releaseUrl, $tagUrl, $fallbackUrl)) {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "OpenFlux-Bootstrap")
        $wc.DownloadFile($url, $installerExe)
        if ((Test-Path $installerExe) -and (Get-Item $installerExe).Length -gt 100000) {
            $downloaded = $true
            break
        }
    } catch {
        try {
            Invoke-WebRequest -Uri $url -OutFile $installerExe -UseBasicParsing -UserAgent 'OpenFlux-Bootstrap'
            if ((Test-Path $installerExe) -and (Get-Item $installerExe).Length -gt 100000) {
                $downloaded = $true
                break
            }
        } catch { }
    }
}

if ($downloaded) {
    & $installerExe $args
    return
} else {
    Write-Host "  [ERROR] Failed to download installer for $displayArch." -ForegroundColor Red
    Write-Host "  Please check internet connection or download manually:" -ForegroundColor Yellow
    Write-Host "  $releaseUrl" -ForegroundColor Yellow
    return
}
