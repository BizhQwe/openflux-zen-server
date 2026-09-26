param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "runtimes"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "  Building OpenFlux Core Engines from Source" -ForegroundColor Cyan
Write-Host "  Source: https://github.com/p1neappleXpress/OpenFlux" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

$hasGo = $false
try {
    $goVer = (& go version) 2>$null
    if ($LASTEXITCODE -eq 0 -and $goVer) {
        $hasGo = $true
        Write-Host "  Found Go compiler: $goVer" -ForegroundColor Green
    }
} catch { }

if ($hasGo) {
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "openflux-source-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
    try {
        Write-Host "  Cloning repository https://github.com/p1neappleXpress/OpenFlux.git..." -ForegroundColor Gray
        & git clone --depth 1 https://github.com/p1neappleXpress/OpenFlux.git $tempDir | Out-Null

        $env:CGO_ENABLED = "0"

        $targets = @(
            @{ OS = "windows"; Arch = "amd64"; Output = "openflux-windows-amd64.exe" },
            @{ OS = "windows"; Arch = "arm64"; Output = "openflux-windows-arm64.exe" },
            @{ OS = "linux";   Arch = "amd64"; Output = "openflux-linux-amd64" },
            @{ OS = "linux";   Arch = "arm64"; Output = "openflux-linux-arm64" }
        )

        foreach ($t in $targets) {
            Write-Host "  Building OpenFlux engine for $($t.OS)-$($t.Arch)..." -ForegroundColor Gray
            $env:GOOS = $t.OS
            $env:GOARCH = $t.Arch
            $outFile = Join-Path $OutputDir $t.Output
            
            Push-Location $tempDir
            try {
                & go build -ldflags="-s -w" -o $outFile .
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to compile OpenFlux for $($t.OS)-$($t.Arch)"
                }
            } finally {
                Pop-Location
            }

            $sizeMb = [Math]::Round(((Get-Item $outFile).Length / 1MB), 2)
            Write-Host "  ✓ Built: $($t.Output) ($sizeMb MB)" -ForegroundColor Green
        }
    } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  [!] Go compiler not found locally. Checking existing binaries in runtimes/..." -ForegroundColor Yellow
    $allPresent = $true
    $expected = @("openflux-windows-amd64.exe", "openflux-windows-arm64.exe", "openflux-linux-amd64", "openflux-linux-arm64")
    foreach ($f in $expected) {
        $p = Join-Path $OutputDir $f
        if (-not (Test-Path $p)) {
            $allPresent = $false
            break
        }
    }

    if (-not $allPresent) {
        Write-Host "  Downloading pre-compiled engines from release fallback..." -ForegroundColor Gray
        foreach ($f in $expected) {
            $url = "https://github.com/BizhQwe/openflux-zen-server/releases/download/v1.0.3/$f"
            $dest = Join-Path $OutputDir $f
            if (-not (Test-Path $dest)) {
                try {
                    $wc = New-Object System.Net.WebClient
                    $wc.DownloadFile($url, $dest)
                } catch { }
            }
        }
    }
}

Write-Host "OpenFlux runtimes ready in: $OutputDir" -ForegroundColor Green
