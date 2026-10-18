[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$ReleaseDir = 'dist',
    [string]$Go = 'go',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$targets = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'targets.txt') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$releasePath = if ([IO.Path]::IsPathRooted($ReleaseDir)) { [IO.Path]::GetFullPath($ReleaseDir) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $ReleaseDir)) }

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Go $Go -All
    [IO.Directory]::CreateDirectory($releasePath) | Out-Null
    $inputPath = Join-Path $projectRoot 'dist'
} else {
    $inputPath = $releasePath
}
if (-not (Test-Path -LiteralPath $releasePath -PathType Container)) {
    throw "Release directory not found: $releasePath"
}

$assets = @()
$checksums = @()
foreach ($target in $targets) {
    $suffix = if ($target.StartsWith('windows-')) { '.exe' } else { '' }
    $asset = "openflux-zen-server-installer-$target$suffix"
    $installer = Join-Path $inputPath $asset
    $sidecar = "$installer.sha256"
    foreach ($path in @($installer, $sidecar)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Missing installer or SHA-256: $asset"
        }
    }
    $expected = [IO.File]::ReadAllText($sidecar).Trim()
    if ($expected -notmatch '^([0-9a-fA-F]{64}) [ *](.+)$') {
        throw "Invalid SHA-256 file: $asset"
    }
    $expectedHash = $Matches[1]
    if ($Matches[2] -cne $asset) { throw "Invalid SHA-256 filename: $asset" }
    $hashStream = [IO.File]::OpenRead($installer)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $actualHash = [BitConverter]::ToString($hasher.ComputeHash($hashStream)).Replace('-', '').ToLowerInvariant()
    } finally {
        $hashStream.Dispose()
        $hasher.Dispose()
    }
    if ($actualHash -ne $expectedHash) { throw "Installer SHA-256 mismatch: $asset" }
    $assets += $asset
    $checksums += "$actualHash  $asset"
}

# Verify every input before copying, so an incomplete set cannot look ready.
if ($inputPath -ne $releasePath) {
    foreach ($asset in $assets) {
        [IO.File]::Copy((Join-Path $inputPath $asset), (Join-Path $releasePath $asset), $true)
        [IO.File]::Copy((Join-Path $inputPath "$asset.sha256"), (Join-Path $releasePath "$asset.sha256"), $true)
    }
}
[IO.File]::WriteAllText((Join-Path $releasePath 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.Encoding]::ASCII)
Write-Host "Release ready: $($assets.Count) installers, individual SHA-256 files and SHA256SUMS.txt in $releasePath."
