# ==============================================================================
# OpenFlux Zen Server — Fast 1-Command Bootstrap Installer (Windows)
# Supports: Windows x64, Windows ARM64
# ==============================================================================

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

$arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
    "win-arm64"
} else {
    "win-x64"
}

$tempDir = $env:TEMP
$installerExe = Join-Path $tempDir "openflux-installer-$arch.exe"
$releaseUrl = "https://github.com/BizhQwe/openflux-zen-server/releases/latest/download/openflux-installer-$arch.exe"
$fallbackUrl = "https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/dist/openflux-installer-$arch.exe"

Write-Host ""
Write-Host "  OpenFlux Zen Server — Windows ($arch)" -ForegroundColor Cyan
Write-Host "  Загрузка установщика..." -ForegroundColor Gray

$downloaded = $false
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    $wc = New-Object System.Net.WebClient
    $wc.Headers.Add("User-Agent", "OpenFlux-Bootstrap")
    $wc.DownloadFile($releaseUrl, $installerExe)
    if ((Test-Path $installerExe) -and (Get-Item $installerExe).Length -gt 1000000) {
        $downloaded = $true
    }
} catch { }

if (-not $downloaded) {
    try {
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "OpenFlux-Bootstrap")
        $wc.DownloadFile($fallbackUrl, $installerExe)
        if ((Test-Path $installerExe) -and (Get-Item $installerExe).Length -gt 1000000) {
            $downloaded = $true
        }
    } catch { }
}

if ($downloaded) {
    & $installerExe $args
    exit $LASTEXITCODE
} else {
    Write-Host "  [!] Готовый релиз не найден. Запуск локальной сборки..." -ForegroundColor Yellow
    # Fallback to local build if repo cloned or SDK installed
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $buildScript = Join-Path $repoRoot "scripts\build-installers.ps1"
    if (Test-Path $buildScript) {
        & powershell -ExecutionPolicy Bypass -File $buildScript -Targets @($arch)
        $builtExe = Join-Path $repoRoot "dist\openflux-installer-$arch.exe"
        if (Test-Path $builtExe) {
            & $builtExe $args
            exit $LASTEXITCODE
        }
    }
    Write-Host "  [ОШИБКА] Не удалось загрузить или собрать установщик." -ForegroundColor Red
    exit 1
}
