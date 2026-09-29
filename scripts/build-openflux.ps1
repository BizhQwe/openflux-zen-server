param(
    [string]$OutputDir = "",
    [string]$SourceDir = "",
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "runtimes"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "  Official OpenFlux Core Engines (Upstream p1neappleXpress/OpenFlux)" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

$expected = @("openflux-windows-amd64.exe", "openflux-windows-arm64.exe", "openflux-linux-amd64", "openflux-linux-arm64")
$allExist = -not $Force
foreach ($f in $expected) {
    $p = Join-Path $OutputDir $f
    if (-not (Test-Path $p) -or (Get-Item $p).Length -lt 1MB) {
        $allExist = $false
        break
    }
}

if ($allExist) {
    Write-Host "  [OK] Official OpenFlux release binaries verified in $($OutputDir):" -ForegroundColor Green
    foreach ($f in $expected) {
        $p = Join-Path $OutputDir $f
        $sizeMb = [Math]::Round(((Get-Item $p).Length / 1MB), 2)
        Write-Host "    - $f ($sizeMb MB)" -ForegroundColor Gray
    }
    $global:LASTEXITCODE = 0
    return
}

# If missing, attempt to download official release binaries from GitHub
Write-Host "  Fetching official OpenFlux release binaries from GitHub..." -ForegroundColor Cyan
$downloadFailed = $false
$baseUrl = "https://github.com/p1neappleXpress/OpenFlux/releases/download/v0.2.0"

foreach ($f in $expected) {
    $p = Join-Path $OutputDir $f
    if (-not (Test-Path $p) -or (Get-Item $p).Length -lt 1MB) {
        try {
            $url = "$baseUrl/$f"
            Write-Host "    Downloading $f..." -ForegroundColor Gray
            Invoke-WebRequest -Uri $url -OutFile $p -UserAgent "OpenFluxZenServer" -TimeoutSec 60
            $sizeMb = [Math]::Round(((Get-Item $p).Length / 1MB), 2)
            Write-Host "    [OK] Downloaded $f ($sizeMb MB)" -ForegroundColor Green
        } catch {
            Write-Host "    [!] Failed to download $f from GitHub: $_" -ForegroundColor Yellow
            $downloadFailed = $true
        }
    }
}

# If all are now present, we are done
$allPresent = $true
foreach ($f in $expected) {
    $p = Join-Path $OutputDir $f
    if (-not (Test-Path $p) -or (Get-Item $p).Length -lt 1MB) {
        $allPresent = $false
        break
    }
}

if ($allPresent) {
    Write-Host "  [OK] All official OpenFlux runtimes verified." -ForegroundColor Green
    $global:LASTEXITCODE = 0
    return
}

# Fallback: compile with Go if available
$hasGo = $false
try {
    $goVer = (& go version) 2>$null
    if ($LASTEXITCODE -eq 0 -and $goVer) {
        $hasGo = $true
        Write-Host "  Found Go compiler: $goVer" -ForegroundColor Green
    }
} catch { }

if ($hasGo) {
    $srcDir = $SourceDir
    $cleanupSrc = $false

    if ([string]::IsNullOrWhiteSpace($srcDir)) {
        $defaultLocal = "C:\Users\100X-RAY\Downloads\OpenFlux-main\OpenFlux-main"
        if (Test-Path (Join-Path $defaultLocal "main.go")) {
            $srcDir = $defaultLocal
            Write-Host "  Found local OpenFlux source: $srcDir" -ForegroundColor Cyan
        }
    }

    if ([string]::IsNullOrWhiteSpace($srcDir) -or -not (Test-Path (Join-Path $srcDir "main.go"))) {
        $srcDir = Join-Path ([System.IO.Path]::GetTempPath()) "openflux-src-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
        $cleanupSrc = $true
        Write-Host "  Cloning repository https://github.com/p1neappleXpress/OpenFlux.git..." -ForegroundColor Gray
        git clone --depth 1 https://github.com/p1neappleXpress/OpenFlux.git $srcDir
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to clone OpenFlux repository"
        }
    }

    try {
        Push-Location $srcDir
        try {
            Write-Host "  Downloading Go dependencies..." -ForegroundColor Gray
            & go mod tidy
            & go mod download
        } finally {
            Pop-Location
        }

        $env:CGO_ENABLED = "0"
        $targets = @(
            @{ OS = "windows"; Arch = "amd64"; Output = "openflux-windows-amd64.exe" },
            @{ OS = "windows"; Arch = "arm64"; Output = "openflux-windows-arm64.exe" },
            @{ OS = "linux";   Arch = "amd64"; Output = "openflux-linux-amd64" },
            @{ OS = "linux";   Arch = "arm64"; Output = "openflux-linux-arm64" }
        )

        foreach ($t in $targets) {
            $outFile = Join-Path $OutputDir $t.Output
            if (Test-Path $outFile -and (Get-Item $outFile).Length -gt 1MB -and -not $Force) {
                continue
            }
            Write-Host "  Building OpenFlux engine for $($t.OS)-$($t.Arch)..." -ForegroundColor Gray
            $env:GOOS = $t.OS
            $env:GOARCH = $t.Arch
            Push-Location $srcDir
            try {
                & go build -ldflags="-s -w" -o $outFile .
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to compile OpenFlux for $($t.OS)-$($t.Arch)"
                }
            } finally {
                Pop-Location
            }

            $sizeMb = [Math]::Round(((Get-Item $outFile).Length / 1MB), 2)
            Write-Host "  [OK] Built: $($t.Output) ($sizeMb MB)" -ForegroundColor Green
        }

        Get-ChildItem -Path $srcDir -Filter "cookies-*.json" | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $OutputDir $_.Name) -Force
            Write-Host "  [OK] Bundled: $($_.Name)" -ForegroundColor Green
        }
    } finally {
        if ($cleanupSrc) {
            Remove-Item -Path $srcDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# Final verification
foreach ($f in $expected) {
    $p = Join-Path $OutputDir $f
    if (-not (Test-Path $p)) {
        throw "Required OpenFlux binary is missing: $p"
    }
    $sizeMb = [Math]::Round(((Get-Item $p).Length / 1MB), 2)
    Write-Host "  [OK] Runtime verified: $f ($sizeMb MB)" -ForegroundColor Green
}

Write-Host "All OpenFlux runtimes verified and ready in: $OutputDir" -ForegroundColor Green
$global:LASTEXITCODE = 0
