param(
    [string[]]$Targets = @("win-x64", "win-arm64", "linux-x64", "linux-arm64"),
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "dist"
}

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "  Building OpenFlux Zen Server Pre-compiled Installers" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "Output Directory: $OutputDir"
Write-Host "Targets: $($Targets -join ', ')"
Write-Host ""

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$installerProjDir = Join-Path $repoRoot "src\OpenFlux.Zen.Server.Installer"
$webProj = Join-Path $repoRoot "src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj"
$cliProj = Join-Path $repoRoot "src\OpenFlux.Zen.Server.Cli\OpenFlux.Zen.Server.Cli.csproj"
$installerProj = Join-Path $installerProjDir "OpenFlux.Zen.Server.Installer.csproj"
$runtimesSrc = Join-Path $repoRoot "runtimes"

# Ensure OpenFlux core binaries are compiled from source
$buildOpenFluxScript = Join-Path $PSScriptRoot "build-openflux.ps1"
if (Test-Path $buildOpenFluxScript) {
    & powershell -ExecutionPolicy Bypass -File $buildOpenFluxScript -OutputDir $runtimesSrc
}

foreach ($rid in $Targets) {
    Write-Host "------------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host ">>> Building Target: $rid" -ForegroundColor Yellow
    Write-Host "------------------------------------------------------------------" -ForegroundColor Yellow

    $guid = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
    $tempStage = Join-Path ([System.IO.Path]::GetTempPath()) "openflux-stage-$rid-$guid"
    New-Item -ItemType Directory -Path $tempStage -Force | Out-Null

    try {
        # 1. Publish Web application
        Write-Host "  [1/4] Publishing Web Panel for $rid..." -ForegroundColor Gray
        & dotnet publish $webProj -c Release -r $rid --self-contained -p:PublishSingleFile=false -o $tempStage | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to publish Web project for $rid" }

        # 2. Publish CLI
        Write-Host "  [2/4] Publishing CLI for $rid..." -ForegroundColor Gray
        & dotnet publish $cliProj -c Release -r $rid --self-contained -p:PublishSingleFile=false -o $tempStage | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to publish CLI project for $rid" }

        # 3. Copy Native Runtime binary
        Write-Host "  [3/4] Copying native OpenFlux binary..." -ForegroundColor Gray
        $nativeRuntimeName = switch ($rid) {
            "win-x64"   { "openflux-windows-amd64.exe" }
            "win-arm64" { "openflux-windows-arm64.exe" }
            "linux-x64" { "openflux-linux-amd64" }
            "linux-arm64" { "openflux-linux-arm64" }
        }

        $runtimesDst = Join-Path $tempStage "runtimes"
        New-Item -ItemType Directory -Path $runtimesDst -Force | Out-Null
        $srcNative = Join-Path $runtimesSrc $nativeRuntimeName
        if (Test-Path $srcNative) {
            Copy-Item -Path $srcNative -Destination (Join-Path $runtimesDst $nativeRuntimeName) -Force
        }

        # Pack payload.zip for standalone distribution and embedding
        $zipDist = Join-Path $OutputDir "openflux-zen-server-$rid.zip"
        Write-Host "  [4/4] Packing payload archive and building Fat Installer..." -ForegroundColor Gray
        [System.IO.Compression.ZipFile]::CreateFromDirectory($tempStage, $zipDist)

        # Copy payload.zip into Installer directory for compilation
        $embeddedZip = Join-Path $installerProjDir "payload.zip"
        Copy-Item -Path $zipDist -Destination $embeddedZip -Force

        # Publish Installer single-file binary with embedded payload
        $installerOut = Join-Path $OutputDir "temp-installer-$rid"
        & dotnet publish $installerProj -c Release -r $rid --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $installerOut | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to publish Installer for $rid" }

        $binaryExt = if ($rid.StartsWith("win")) { ".exe" } else { "" }
        $finalInstallerName = "openflux-installer-$rid$binaryExt"
        $builtBinary = Join-Path $installerOut "openflux-installer$binaryExt"
        $destBinary = Join-Path $OutputDir $finalInstallerName

        Move-Item -Path $builtBinary -Destination $destBinary -Force
        Remove-Item -Path $installerOut -Recurse -Force -ErrorAction SilentlyContinue

        $sizeMb = [Math]::Round(((Get-Item $destBinary).Length / 1MB), 2)
        Write-Host "  [OK] Built: $finalInstallerName ($sizeMb MB)" -ForegroundColor Green
    }
    finally {
        # Clean up temporary stage and embedded zip
        Remove-Item -Path $tempStage -Recurse -Force -ErrorAction SilentlyContinue
        $embeddedZip = Join-Path $installerProjDir "payload.zip"
        if (Test-Path $embeddedZip) {
            Remove-Item -Path $embeddedZip -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ""
Write-Host "==================================================================" -ForegroundColor Green
Write-Host "  Build Completed Successfully!" -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Green
Get-ChildItem -Path $OutputDir | Select-Object Name, @{Name="SizeMB"; Expression={[Math]::Round($_.Length / 1MB, 2)}} | Format-Table -AutoSize
