# ==============================================================================
# OpenFlux Zen Server - Production 1-Command Installer for Windows (PowerShell)
# Multi-language (RU / EN), Auto .NET 10 Install, Auto Git/Zip Fetch, CLI Setup
# ==============================================================================

$ErrorActionPreference = "Continue"
$Lang = if ($args -and $args.Count -gt 0) { $args[0] } else { "" }

# 1. Administrator Check & Self-Elevation
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[INFO] Requesting Administrator privileges..." -ForegroundColor Yellow
    $cmd = 'irm https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/scripts/install.ps1 | iex'
    Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -NoExit -Command "' + $cmd + '"')
    exit
}

# Resilient Downloader (supports curl.exe, WebClient with browser headers, and Invoke-WebRequest)
function Download-FileWithFallback {
    param(
        [Parameter(Mandatory=$true)][string]$SourceUrl,
        [Parameter(Mandatory=$true)][string]$DestFile,
        [int]$MinBytes = 1000
    )

    if (Test-Path $DestFile) {
        Remove-Item -Path $DestFile -Force -ErrorAction SilentlyContinue
    }

    # 1. Native Windows curl.exe (standard on Windows 10/11 & Windows Server 2019/2022/2025)
    if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        try {
            & curl.exe -sSL -f --retry 3 --connect-timeout 20 -H "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)" "$SourceUrl" -o "$DestFile"
            if ($LASTEXITCODE -eq 0 -and (Test-Path $DestFile) -and ((Get-Item $DestFile).Length -ge $MinBytes)) {
                return $true
            }
        } catch {}
        if (Test-Path $DestFile) { Remove-Item -Path $DestFile -Force -ErrorAction SilentlyContinue }
    }

    # 2. .NET WebClient with realistic User-Agent (avoids GitHub TCP connection reset)
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36")
        $wc.DownloadFile($SourceUrl, $DestFile)
        if ((Test-Path $DestFile) -and ((Get-Item $DestFile).Length -ge $MinBytes)) {
            return $true
        }
    } catch {}
    if (Test-Path $DestFile) { Remove-Item -Path $DestFile -Force -ErrorAction SilentlyContinue }

    # 3. Invoke-WebRequest with User-Agent
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $SourceUrl -OutFile $DestFile -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" -UseBasicParsing -TimeoutSec 120
        if ((Test-Path $DestFile) -and ((Get-Item $DestFile).Length -ge $MinBytes)) {
            return $true
        }
    } catch {}
    if (Test-Path $DestFile) { Remove-Item -Path $DestFile -Force -ErrorAction SilentlyContinue }

    return $false
}

# 2. Language Selection (Prompted in English)
$chosenLang = $Lang
if (-not $chosenLang) {
    Write-Host ""
    Write-Host "==================================================================" -ForegroundColor Cyan
    Write-Host "  OpenFlux Zen Server -- Language Selection" -ForegroundColor Cyan
    Write-Host "==================================================================" -ForegroundColor Cyan
    Write-Host "  1) English (web panel and installer)"
    Write-Host "  2) Russian / Русский (панель управления и установщик)"
    Write-Host "------------------------------------------------------------------" -ForegroundColor Cyan
    $langInput = Read-Host "Select language [1/2, default: 1]"
    if ($langInput -match "2|ru") {
        $chosenLang = "ru"
    } else {
        $chosenLang = "en"
    }
}

# 3. Check or Install .NET 10 SDK
Write-Host ""
if ($chosenLang -eq "ru") {
    Write-Host "[1/8] Проверка и подготовка среды .NET 10..." -ForegroundColor Cyan
} else {
    Write-Host "[1/8] Checking and preparing .NET 10 environment..." -ForegroundColor Cyan
}

$hasDotnet10 = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    try {
        $sdks = dotnet --list-sdks 2>$null
        if ($sdks -match "^10\.") {
            $hasDotnet10 = $true
            Write-Host "[OK] .NET 10 SDK is already installed" -ForegroundColor Green
        }
    } catch {}
}

$defaultDotnetExe = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not $hasDotnet10 -and (Test-Path $defaultDotnetExe)) {
    $env:PATH = "$($env:ProgramFiles)\dotnet;$($env:PATH)"
    try {
        $sdks = & $defaultDotnetExe --list-sdks 2>$null
        if ($sdks -match "^10\.") {
            $hasDotnet10 = $true
            Write-Host "[OK] .NET 10 SDK found in $defaultDotnetExe" -ForegroundColor Green
        }
    } catch {}
}

if (-not $hasDotnet10) {
    if ($chosenLang -eq "ru") {
        Write-Host "Установка .NET 10 SDK через официальный установщик Microsoft..." -ForegroundColor Yellow
    } else {
        Write-Host "Installing .NET 10 SDK via official Microsoft installer..." -ForegroundColor Yellow
    }
    $dotnetInstallerScript = Join-Path $env:TEMP "dotnet-install.ps1"
    $dlDotnet = Download-FileWithFallback "https://dot.net/v1/dotnet-install.ps1" $dotnetInstallerScript 5000
    if (-not $dlDotnet) {
        $msg = if ($chosenLang -eq "ru") { "[ОШИБКА] Не удалось загрузить установщик .NET 10 SDK с https://dot.net" } else { "[ERROR] Failed to download .NET 10 SDK installer from https://dot.net" }
        Write-Host $msg -ForegroundColor Red
        exit 1
    }
    & $dotnetInstallerScript -Channel 10.0 -InstallDir (Join-Path $env:ProgramFiles "dotnet")
    $env:DOTNET_ROOT = Join-Path $env:ProgramFiles "dotnet"
    $env:PATH = "$($env:DOTNET_ROOT);$($env:PATH)"
    
    $currMachinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    if ($currMachinePath -notmatch [regex]::Escape($env:DOTNET_ROOT)) {
        [Environment]::SetEnvironmentVariable("Path", "$($env:DOTNET_ROOT);$currMachinePath", "Machine")
    }
    Write-Host "[OK] .NET 10 SDK installed successfully" -ForegroundColor Green
}

# 4. Fetch Repository Source (Git clone or Zip download fallback)
if ($chosenLang -eq "ru") {
    Write-Host "[2/8] Получение исходного кода OpenFlux Zen Server..." -ForegroundColor Cyan
} else {
    Write-Host "[2/8] Fetching OpenFlux Zen Server repository..." -ForegroundColor Cyan
}

$workDir = Join-Path $env:TEMP "openflux-zen-server-build"
if (Test-Path $workDir) {
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

$scriptDir = if ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { "" }
$localCsproj = if ($scriptDir) { Join-Path $scriptDir "..\src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj" } else { "" }
if ($localCsproj -and (Test-Path $localCsproj)) {
    Write-Host "Using local repository files..." -ForegroundColor Gray
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    $sourceDir = (Resolve-Path "$scriptDir\..").Path
    Copy-Item -Path "$sourceDir\*" -Destination $workDir -Recurse -Force
} else {
    $cloneSuccess = $false
    if (Get-Command git -ErrorAction SilentlyContinue) {
        Write-Host "Cloning repository via git..." -ForegroundColor Gray
        try {
            git clone --depth 1 "https://github.com/BizhQwe/openflux-zen-server.git" $workDir *>$null
            if ($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $workDir "src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj"))) {
                $cloneSuccess = $true
            }
        } catch {
            $cloneSuccess = $false
        }
    }

    if (-not $cloneSuccess) {
        $txtDl = if ($chosenLang -eq "ru") { "Загрузка архива репозитория с GitHub..." } else { "Downloading repository zip archive from GitHub..." }
        Write-Host $txtDl -ForegroundColor Gray
        if (Test-Path $workDir) {
            Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $workDir -Force | Out-Null
        $zipPath = Join-Path $env:TEMP "openflux-zen-server.zip"
        $dlOk = Download-FileWithFallback "https://github.com/BizhQwe/openflux-zen-server/archive/refs/heads/main.zip" $zipPath 1000000
        if (-not $dlOk) {
            $msg = if ($chosenLang -eq "ru") { "[ОШИБКА] Не удалось загрузить архив репозитория OpenFlux Zen Server с GitHub" } else { "[ERROR] Failed to download OpenFlux Zen Server repository archive from GitHub" }
            Write-Host $msg -ForegroundColor Red
            exit 1
        }
        $extractDir = Join-Path $env:TEMP "openflux_extracted"
        if (Test-Path $extractDir) { Remove-Item -Path $extractDir -Recurse -Force -ErrorAction SilentlyContinue }
        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
        $topDir = Get-ChildItem -Path $extractDir -Directory | Select-Object -First 1
        if ($topDir) {
            Copy-Item -Path "$($topDir.FullName)\*" -Destination $workDir -Recurse -Force
        }
        Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 5. Build and Publish Web and CLI
if ($chosenLang -eq "ru") {
    Write-Host "[3/8] Сборка и публикация компонентов..." -ForegroundColor Cyan
} else {
    Write-Host "[3/8] Building and publishing application components..." -ForegroundColor Cyan
}

$InstallDir = Join-Path $env:ProgramFiles "OpenFluxZenServer"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

# Terminate existing running processes
Get-Process -Name "OpenFlux.Zen.Server.Web", "OpenFluxZenServer", "openflux-windows-amd64", "openflux-windows-arm64", "zrok", "zrok2" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$webProj = Join-Path $workDir "src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj"
$cliProj = Join-Path $workDir "src\OpenFlux.Zen.Server.Cli\OpenFlux.Zen.Server.Cli.csproj"

if (-not (Test-Path $webProj)) {
    $msg = if ($chosenLang -eq "ru") { "[ОШИБКА] Проект $webProj не найден." } else { "[ERROR] Project $webProj not found." }
    Write-Host $msg -ForegroundColor Red
    exit 1
}

$dotnetCmd = "dotnet"
$dotnetDefault = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (Test-Path $dotnetDefault) {
    $dotnetCmd = $dotnetDefault
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetCmd = (Get-Command dotnet).Source
}

$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& $dotnetCmd publish $webProj -c Release -o $InstallDir
if ($LASTEXITCODE -ne 0) {
    $msg = if ($chosenLang -eq "ru") { "[ОШИБКА] Сборка Web-панели не удалась." } else { "[ERROR] Failed to compile Web panel." }
    Write-Host $msg -ForegroundColor Red
    exit 1
}

& $dotnetCmd publish $cliProj -c Release -o $InstallDir
if ($LASTEXITCODE -ne 0) {
    $msg = if ($chosenLang -eq "ru") { "[ОШИБКА] Сборка CLI не удалась." } else { "[ERROR] Failed to compile CLI." }
    Write-Host $msg -ForegroundColor Red
    exit 1
}

# Copy native runtimes
$runtimesSrc = Join-Path $workDir "runtimes"
if (Test-Path $runtimesSrc) {
    $runtimesDst = Join-Path $InstallDir "runtimes"
    New-Item -ItemType Directory -Path $runtimesDst -Force | Out-Null
    Copy-Item -Path "$runtimesSrc\*" -Destination $runtimesDst -Recurse -Force
}

# 6. Credentials and Configuration
if ($chosenLang -eq "ru") {
    Write-Host "[4/8] Генерация ключей доступа и учётных данных..." -ForegroundColor Cyan
} else {
    Write-Host "[4/8] Generating security credentials and access keys..." -ForegroundColor Cyan
}

$credFile = Join-Path $InstallDir "data\.credentials"
$existingUser = ""
$existingPass = ""
$existingSecret = ""
$existingMode = ""
$existingDomain = ""
$existingHost = ""
$existingLtPass = ""
if (Test-Path $credFile) {
    try {
        $json = Get-Content $credFile -Raw | ConvertFrom-Json
        $existingUser = $json.username
        $existingPass = $json.password
        $existingSecret = $json.secretPath
        $existingMode = $json.publishMode
        $existingDomain = $json.domain
        $existingHost = $json.host
        $existingLtPass = $json.localtunnelPassword
    } catch {}
}

$adminUser = if ($existingUser) { $existingUser } else { "admin" }
$adminPass = if ($existingPass) { $existingPass } else { -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 16 | ForEach-Object {[char]$_}) }
$secretPath = if ($existingSecret) { $existingSecret } else { -join ((97..102) + (48..57) | Get-Random -Count 16 | ForEach-Object {[char]$_}) }
$listenPort = 5000

$localUrl = "http://127.0.0.1:$listenPort/$secretPath/"
$finalUrl = $localUrl
$publishMode = if ($existingMode) { $existingMode } else { "local" }
$listenHost = if ($existingHost) { $existingHost } else { "127.0.0.1" }
$userDomain = if ($existingDomain) { $existingDomain } else { "" }
$localtunnelPassword = if ($existingLtPass) { $existingLtPass } else { "" }

# 7. Network Accessibility and Publishing Configuration
$env:OPENFLUX_PUBLISH_MODE = $null
$env:OPENFLUX_NETWORK_ACCESS = $null
$env:OPENFLUX_DOMAIN = $null
$env:OPENFLUX_ZROK_TOKEN = $null
[Environment]::SetEnvironmentVariable("OPENFLUX_PUBLISH_MODE", $null, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_NETWORK_ACCESS", $null, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_DOMAIN", $null, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_ZROK_TOKEN", $null, "Machine")

Write-Host ""
if ($chosenLang -eq "ru") {
    Write-Host "[5/8] Настройка сетевого доступа и публикации..." -ForegroundColor Cyan
    Write-Host "  По умолчанию панель доступна только локально на сервере (127.0.0.1)." -ForegroundColor Gray
    $netPrompt = "  Нужна ли сетевая доступность панели из интернета? [y/N]"
} else {
    Write-Host "[5/8] Configuring network accessibility and publishing..." -ForegroundColor Cyan
    Write-Host "  By default, the dashboard is accessible locally only (127.0.0.1)." -ForegroundColor Gray
    $netPrompt = "  Do you want the dashboard accessible from the internet? [y/N]"
}

$netAccessInput = Read-Host "$netPrompt [default: N]"
$netAccess = ($netAccessInput -match "^[Yy]")

if ($netAccess) {
    if ($chosenLang -eq "ru") {
        Write-Host ""
        Write-Host "  Выберите режим публикации:" -ForegroundColor Cyan
        Write-Host "    1) Открытые порты (Собственный домен или внешний IP сервера)"
        Write-Host "    2) Через Localtunnel (Защищённый туннель без открытия портов, без токенов)"
        $pubPrompt = "  Ваш выбор [1/2, default: 1]"
    } else {
        Write-Host ""
        Write-Host "  Select publishing mode:" -ForegroundColor Cyan
        Write-Host "    1) Open ports (Custom domain or External server IP)"
        Write-Host "    2) Via Localtunnel (Encrypted tunnel without open ports, zero-config)"
        $pubPrompt = "  Your choice [1/2, default: 1]"
    }

    $rawChoice = Read-Host "$pubPrompt"
    $pubChoice = if ($rawChoice) { $rawChoice.Trim() } else { "1" }

    if ($pubChoice -match "2|localtunnel|tunnel") {
        $publishMode = "localtunnel"
        $listenHost = "127.0.0.1"

        schtasks.exe /delete /tn "OpenFluxZrok" /f *>$null 2>&1

        $publicIp = ""
        try {
            $publicIp = (& curl.exe -sSL --connect-timeout 4 https://api.ipify.org).Trim()
        } catch {}
        if (-not $publicIp) {
            try {
                $publicIp = (& curl.exe -sSL --connect-timeout 4 https://ifconfig.me/ip).Trim()
            } catch {}
        }
        if (-not $publicIp) {
            try {
                $wc = New-Object System.Net.WebClient
                $publicIp = ($wc.DownloadString("https://api.ipify.org")).Trim()
            } catch {}
        }

        $localtunnelPassword = $publicIp
        $subPrefix = "openflux-" + $secretPath.Substring(0, 8)
        $finalUrl = "https://$subPrefix.loca.lt/$secretPath/"

        if ($chosenLang -eq "ru") {
            Write-Host "  ✓ Выбран Localtunnel: туннель запускается автоматически внутри службы сервера." -ForegroundColor Green
            Write-Host "    Регистрация и токены не требуются." -ForegroundColor Gray
            if ($localtunnelPassword) {
                Write-Host "    Пароль первого входа для loca.lt (IP сервера): $localtunnelPassword" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  ✓ Localtunnel selected: tunnel runs automatically inside server service." -ForegroundColor Green
            Write-Host "    No registration or tokens required." -ForegroundColor Gray
            if ($localtunnelPassword) {
                Write-Host "    First-time loca.lt password (server IP): $localtunnelPassword" -ForegroundColor Yellow
            }
        }
    } else {
        # Choice 1: Open ports
        $publishMode = "domain"
        $listenHost = "0.0.0.0"
        $domainPrompt = if ($chosenLang -eq "ru") { "  Введите ваш домен (или нажмите Enter для внешнего IP сервера)" } else { "  Enter your domain (or press Enter for external server IP)" }
        $userDomain = (Read-Host "$domainPrompt").Trim()

        # Configure Windows Firewall
        netsh advfirewall firewall delete rule name="OpenFluxZenServer" *>$null
        netsh advfirewall firewall add rule name="OpenFluxZenServer" dir=in action=allow protocol=TCP localport=$listenPort *>$null
        $msgFw = if ($chosenLang -eq "ru") { "  ✓ Добавлено правило брандмауэра Windows для входящего TCP порта $listenPort" } else { "  ✓ Windows Firewall rule added for incoming TCP port $listenPort" }
        Write-Host $msgFw -ForegroundColor Green

        if ($userDomain) {
            $proto = if ($userDomain -match "^https?://") { "" } else { "http://" }
            $cleanDomain = $userDomain -replace "^https?://", ""
            $finalUrl = "$proto$cleanDomain`:$listenPort/$secretPath/"
        } else {
            $publicIp = ""
            try {
                $publicIp = (& curl.exe -sSL --connect-timeout 4 https://api.ipify.org).Trim()
            } catch {}
            if (-not $publicIp) {
                try {
                    $publicIp = (& curl.exe -sSL --connect-timeout 4 https://ifconfig.me/ip).Trim()
                } catch {}
            }
            if (-not $publicIp) {
                try {
                    $wc = New-Object System.Net.WebClient
                    $publicIp = ($wc.DownloadString("https://api.ipify.org")).Trim()
                } catch {}
            }
            if ($publicIp) {
                $finalUrl = "http://$publicIp`:$listenPort/$secretPath/"
            } else {
                $finalUrl = "http://<server-ip>`:$listenPort/$secretPath/"
            }
        }
    }
}

# 8. Autostart Question and Task Scheduler Setup
Write-Host ""
if ($chosenLang -eq "ru") {
    Write-Host "[6/8] Настройка автозапуска при загрузке системы..." -ForegroundColor Cyan
} else {
    Write-Host "[6/8] Configuring system autostart..." -ForegroundColor Cyan
}

$autostartPrompt = if ($chosenLang -eq "ru") { "Включить автозапуск сервера при загрузке системы? [Y/n]" } else { "Enable server autostart on system boot? [Y/n]" }
$autostartChoice = Read-Host "$autostartPrompt [default: Y]"
$autostartEnabled = ($autostartChoice -notmatch "^[Nn]")

$dataDir = Join-Path $InstallDir "data"
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$credObj = [PSCustomObject]@{
    username = $adminUser
    password = $adminPass
    secretPath = $secretPath
    publicUrl = $finalUrl
    publishMode = $publishMode
    host = $listenHost
    port = $listenPort
    domain = $userDomain
    localtunnelPassword = $localtunnelPassword
    language = $chosenLang
    autostart = $autostartEnabled
    updatedAt = (Get-Date).ToUniversalTime().ToString("o")
}
$credObj | ConvertTo-Json -Depth 4 | Set-Content -Path $credFile -Encoding UTF8

# Configure Windows Scheduled Task
schtasks.exe /delete /tn "OpenFluxZenServer" /f *>$null
$exePath = Join-Path $InstallDir "OpenFlux.Zen.Server.Web.exe"
$taskAction = '"' + $exePath + '"'
schtasks.exe /create /tn "OpenFluxZenServer" /tr $taskAction /sc onstart /ru SYSTEM /rl HIGHEST /f *>$null
if ($LASTEXITCODE -ne 0) {
    schtasks.exe /create /tn "OpenFluxZenServer" /tr $taskAction /sc onlogon /rl HIGHEST /f *>$null
}
if (-not $autostartEnabled) {
    schtasks.exe /change /tn "OpenFluxZenServer" /disable *>$null
}

# Set persistent Machine environment variables
[Environment]::SetEnvironmentVariable("OPENFLUX_HOST", $listenHost, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_PORT", "$listenPort", "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_SECRET_PATH", $secretPath, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_ADMIN_USER", $adminUser, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_ADMIN_PASSWORD", $adminPass, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_LANGUAGE", $chosenLang, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_PUBLISH_MODE", $publishMode, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_NETWORK_ACCESS", $null, "Machine")
[Environment]::SetEnvironmentVariable("OPENFLUX_DOMAIN", $null, "Machine")

# 9. Register CLI in Machine PATH
Write-Host ""
if ($chosenLang -eq "ru") {
    Write-Host "[7/8] Регистрация OpenFluxZenServer в системном PATH..." -ForegroundColor Cyan
} else {
    Write-Host "[7/8] Registering OpenFluxZenServer CLI command in PATH..." -ForegroundColor Cyan
}

$currMachinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($currMachinePath -notmatch [regex]::Escape($InstallDir)) {
    [Environment]::SetEnvironmentVariable("Path", "$currMachinePath;$InstallDir", "Machine")
}
$env:PATH = "$InstallDir;$($env:PATH)"

# 10. Start Server Process in background
Write-Host ""
if ($chosenLang -eq "ru") {
    Write-Host "[8/8] Запуск OpenFlux Zen Server..." -ForegroundColor Cyan
} else {
    Write-Host "[8/8] Starting OpenFlux Zen Server..." -ForegroundColor Cyan
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exePath
$psi.WorkingDirectory = $InstallDir
$psi.EnvironmentVariables["OPENFLUX_HOST"] = $listenHost
$psi.EnvironmentVariables["OPENFLUX_PORT"] = "$listenPort"
$psi.EnvironmentVariables["OPENFLUX_SECRET_PATH"] = $secretPath
$psi.EnvironmentVariables["OPENFLUX_ADMIN_USER"] = $adminUser
$psi.EnvironmentVariables["OPENFLUX_ADMIN_PASSWORD"] = $adminPass
$psi.EnvironmentVariables["OPENFLUX_LANGUAGE"] = $chosenLang
$psi.EnvironmentVariables["OPENFLUX_PUBLIC_URL"] = $finalUrl
$psi.EnvironmentVariables["OPENFLUX_PUBLISH_MODE"] = $publishMode
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
[System.Diagnostics.Process]::Start($psi) | Out-Null

if ($publishMode -eq "localtunnel") {
    $msgWaitTunnel = if ($chosenLang -eq "ru") { "  Ожидание готовности туннеля Localtunnel..." } else { "  Waiting for Localtunnel readiness..." }
    Write-Host $msgWaitTunnel -ForegroundColor Cyan
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        if (Test-Path $credFile) {
            try {
                $liveCreds = Get-Content $credFile -Raw | ConvertFrom-Json
                if ($liveCreds.publicUrl -and $liveCreds.publicUrl -match "\.loca\.lt" -and $liveCreds.localtunnelPassword) {
                    $finalUrl = $liveCreds.publicUrl
                    $localtunnelPassword = $liveCreds.localtunnelPassword
                    break
                }
            } catch {}
        }
    }
} else {
    Start-Sleep -Seconds 2
}

# 11. Clean, Aligned Summary Card
$title = if ($chosenLang -eq "ru") { "OpenFlux Zen Server -- УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!" } else { "OpenFlux Zen Server -- SUCCESSFULLY INSTALLED AND STARTED!" }
$lblSecret = if ($chosenLang -eq "ru") { "Панель управления (Секретная ссылка)" } else { "Web Dashboard URL (Secret link)" }
$lblLocal = if ($chosenLang -eq "ru") { "Локальный адрес" } else { "Local Access URL" }
$lblCreds = if ($chosenLang -eq "ru") { "Учётные данные" } else { "Authentication Details" }
$lblUser = if ($chosenLang -eq "ru") { "    Логин:              " } else { "    Username:           " }
$lblPass = if ($chosenLang -eq "ru") { "    Пароль:             " } else { "    Password:           " }
$lblSecPath = if ($chosenLang -eq "ru") { "    Секретный путь:     " } else { "    Secret Path:        " }
$lblPubMode = if ($chosenLang -eq "ru") { "    Режим публикации:   " } else { "    Publish Mode:       " }
$lblLang = if ($chosenLang -eq "ru") { "    Язык интерфейса:    " } else { "    Interface Language: " }
$lblAuto = if ($chosenLang -eq "ru") { "    Автозапуск:         " } else { "    Autostart:          " }
$valEnabled = if ($chosenLang -eq "ru") { "Включён" } else { "Enabled" }
$valDisabled = if ($chosenLang -eq "ru") { "Выключен" } else { "Disabled" }

$valMode = switch ($publishMode) {
    "localtunnel" { if ($chosenLang -eq "ru") { "Через Localtunnel (Без токенов)" } else { "Via Localtunnel (Zero-config)" } }
    "domain" { 
        if ($userDomain) { 
            "Domain ($userDomain)" 
        } else { 
            if ($chosenLang -eq "ru") { "Открытые порты (Внешний IP)" } else { "Open ports (External IP)" } 
        } 
    }
    default { if ($chosenLang -eq "ru") { "Локальный (127.0.0.1)" } else { "Local only (127.0.0.1)" } }
}

$lblCli = if ($chosenLang -eq "ru") { "Управление сервером через команду: OpenFluxZenServer <command>" } else { "CLI command available anywhere: OpenFluxZenServer <command>" }
$lblCliStatus = if ($chosenLang -eq "ru") { "текущий статус сервера" } else { "show server status" }
$lblCliRestart = if ($chosenLang -eq "ru") { "перезапуск службы сервера" } else { "restart server service" }
$lblCliCreds = if ($chosenLang -eq "ru") { "просмотр данных входа и ссылок" } else { "view login credentials and URLs" }
$lblCliAuto = if ($chosenLang -eq "ru") { "настройка автозапуска (enable | disable | status)" } else { "configure autostart (enable | disable | status)" }
$lblCliHelp = if ($chosenLang -eq "ru") { "справка по всем командам" } else { "show CLI command help" }

Write-Host ""
Write-Host "==================================================================" -ForegroundColor Green
Write-Host "  $title" -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  $($lblSecret):" -ForegroundColor Cyan
Write-Host "    $finalUrl" -ForegroundColor Yellow
Write-Host ""
Write-Host "  $($lblLocal):" -ForegroundColor Cyan
Write-Host "    $localUrl" -ForegroundColor White
Write-Host ""
Write-Host "  $($lblCreds):" -ForegroundColor Cyan
Write-Host "$lblUser$adminUser" -ForegroundColor White
Write-Host "$lblPass$adminPass" -ForegroundColor White
Write-Host "$lblSecPath/$secretPath/" -ForegroundColor White
Write-Host "$lblPubMode$valMode" -ForegroundColor White
if ($publishMode -eq "localtunnel" -and $localtunnelPassword) {
    $lblLtPass = if ($chosenLang -eq "ru") { "    Пароль loca.lt (IP):" } else { "    loca.lt Password(IP):" }
    $lblLtHint = if ($chosenLang -eq "ru") { "(требуется при первом открытии в браузере)" } else { "(required on first browser visit)" }
    Write-Host "$lblLtPass $localtunnelPassword $lblLtHint" -ForegroundColor Yellow
}
Write-Host "$lblLang$($chosenLang.ToUpper())" -ForegroundColor White
if ($autostartEnabled) {
    Write-Host "$lblAuto$valEnabled" -ForegroundColor Green
} else {
    Write-Host "$lblAuto$valDisabled" -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "------------------------------------------------------------------" -ForegroundColor Green
Write-Host "  $lblCli" -ForegroundColor Yellow
Write-Host ""
Write-Host "    OpenFluxZenServer status       - $lblCliStatus"
Write-Host "    OpenFluxZenServer restart      - $lblCliRestart"
Write-Host "    OpenFluxZenServer credentials  - $lblCliCreds"
Write-Host "    OpenFluxZenServer autostart    - $lblCliAuto"
Write-Host "    OpenFluxZenServer help         - $lblCliHelp"
Write-Host ""
Write-Host "==================================================================" -ForegroundColor Green
Write-Host ""

