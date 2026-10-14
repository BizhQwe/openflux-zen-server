# OpenFlux Zen Server: download and run the latest Windows installer.
# Run in an elevated PowerShell for a normal installation.
# All arguments are passed to the installer, e.g. --no-service --dir C:\Zen.
$ErrorActionPreference = 'Stop'
$repository = 'BizhQwe/openflux-zen-server'
if ($env:OPENFLUX_REPOSITORY) { $repository = $env:OPENFLUX_REPOSITORY }
if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'OPENFLUX_REPOSITORY must be a GitHub owner/repository.'
}
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Use install.sh on Linux.'
}

# OS architecture also works from a 32-bit PowerShell on a 64-bit Windows.
$nativeArch = $env:PROCESSOR_ARCHITEW6432
if (-not $nativeArch) { $nativeArch = $env:PROCESSOR_ARCHITECTURE }
$arch = switch ($nativeArch.ToUpperInvariant()) {
    'AMD64' { 'amd64' }
    'ARM64' { 'arm64' }
    'X86'   { '386' }
    default { throw "Unsupported Windows architecture: $nativeArch" }
}
$asset = "openflux-zen-server-installer-windows-$arch.exe"
$releaseUrl = "https://github.com/$repository/releases/latest/download/$asset"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('openflux-install-' + [Guid]::NewGuid().ToString('N'))
$installer = Join-Path $tempRoot $asset
$checksum = "$installer.sha256"
$installExitCode = 1

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Write-Host "OpenFlux Zen Server - Windows $arch"
    Write-Host "Downloading the latest release from $repository..."
    foreach ($download in @(@{Url=$releaseUrl; Path=$installer}, @{Url="$releaseUrl.sha256"; Path=$checksum})) {
        Invoke-WebRequest -Uri $download.Url -OutFile $download.Path -UseBasicParsing -UserAgent 'OpenFlux-Zen-Bootstrap' -ErrorAction Stop
    }
    $expected = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0]
    if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid release SHA-256 file.' }
    $file = [IO.File]::OpenRead($installer)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actual = [BitConverter]::ToString($sha.ComputeHash($file)).Replace('-', '') }
    finally { $sha.Dispose(); $file.Dispose() }
    if ($actual -ine $expected) { throw 'Installer SHA-256 mismatch.' }

    Write-Host 'SHA-256 verified. Starting installation...'
    & $installer @args
    $installExitCode = $LASTEXITCODE
} catch {
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Release asset: $releaseUrl"
} finally {
    # Remove only these two files in the unique temporary directory.
    foreach ($path in @($installer, $checksum)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -ErrorAction SilentlyContinue }
}
exit $installExitCode
