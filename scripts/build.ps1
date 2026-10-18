param(
    [string]$Go = 'go',
    [string[]]$Targets = @(),
    [switch]$All,
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$allowed = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'targets.txt') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($All -or $Targets.Count -eq 0) { $Targets = $allowed }
foreach ($target in $Targets) { if ($target -notin $allowed) { throw "Unsupported target: $target" } }
$previous = @{}
foreach ($name in @('GOOS','GOARCH','GOARM','CGO_ENABLED')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
Push-Location $projectRoot
$bundlePath = Join-Path $projectRoot 'src\OpenFlux.Zen.Server.Installer\payload\bundle.zip'
function Invoke-Go([string[]]$GoArgs) { & $Go @GoArgs; if ($LASTEXITCODE -ne 0) { throw "Go failed: $($GoArgs -join ' ')" } }
try {
    $hostOS = (& $Go env GOHOSTOS).Trim()
    $hostArch = (& $Go env GOHOSTARCH).Trim()
    if ($Test) { Invoke-Go @('test','./src/OpenFlux.Zen.Server.Core/...','./src/OpenFlux.Zen.Server.Web/...','./src/OpenFlux.Zen.Server.Installer/...','./src/OpenFlux.Zen.Server.Cli/...') }
    $dist = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    foreach ($target in $Targets) {
        $parts = $target.Split('-')
        $targetOS = $parts[0]
        $targetArch = $parts[1]
        $env:GOOS = $targetOS
        $env:GOARCH = if ($targetArch -eq 'armv7') { 'arm' } else { $targetArch }
        $env:GOARM = if ($targetArch -eq 'armv7') { '7' } else { '' }
        $env:CGO_ENABLED = '0'
        $suffix = if ($targetOS -eq 'windows') { '.exe' } else { '' }
        $bin = Join-Path $dist $target
        New-Item -ItemType Directory -Force -Path $bin | Out-Null
        Write-Host "Building $target"
        Invoke-Go @('build','-trimpath','-ldflags=-s -w','-o',(Join-Path $bin "openflux-zen-server-core$suffix"),'./src/OpenFlux.Zen.Server.Core')
        Invoke-Go @('build','-trimpath','-ldflags=-s -w','-o',(Join-Path $bin "openflux-zen-server-web$suffix"),'./src/OpenFlux.Zen.Server.Web')
        Invoke-Go @('build','-trimpath','-ldflags=-s -w','-o',(Join-Path $bin "openflux-zen-server-cli$suffix"),'./src/OpenFlux.Zen.Server.Cli')
        $env:GOOS = $hostOS
        $env:GOARCH = $hostArch
        $env:GOARM = ''
        $goArch = if ($targetArch -eq 'armv7') { 'arm' } else { $targetArch }
        Invoke-Go @('run','./src/OpenFlux.Zen.Server.Installer/cmd/bundle','--os',$targetOS,'--arch',$goArch,'--dir',$bin,'--out',$bundlePath)
        $env:GOOS = $targetOS
        $env:GOARCH = $goArch
        $env:GOARM = if ($targetArch -eq 'armv7') { '7' } else { '' }
        $installer = Join-Path $dist "openflux-zen-server-installer-$target$suffix"
        Invoke-Go @('build','-trimpath','-ldflags=-s -w','-o',$installer,'./src/OpenFlux.Zen.Server.Installer')
        $hashStream = [IO.File]::OpenRead($installer)
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $installerHash = [BitConverter]::ToString($hasher.ComputeHash($hashStream)).Replace('-', '').ToLowerInvariant()
        } finally {
            $hashStream.Dispose()
            $hasher.Dispose()
        }
        $checksumLine = $installerHash + '  ' + (Split-Path -Leaf $installer) + "`n"
        [IO.File]::WriteAllText("$installer.sha256", $checksumLine, [Text.Encoding]::ASCII)
        Remove-Item -LiteralPath $bundlePath
        foreach ($oldName in @("openflux-core$suffix", "openflux-zen-server$suffix", "openflux$suffix")) {
            $oldFile = Join-Path $bin $oldName
            if (Test-Path -LiteralPath $oldFile) { Remove-Item -LiteralPath $oldFile }
        }
    }
} finally {
    if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath }
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    Pop-Location
}
