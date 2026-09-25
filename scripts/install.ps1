# ==============================================================================
# OpenFlux Zen Server - Production 1-Command Installer for Windows (PowerShell)
# Multi-language (RU / EN), Auto .NET 10 Install, Auto Git/Zip Fetch, CLI Setup
# ==============================================================================

$ErrorActionPreference = "Stop"
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
        & curl.exe -fL --retry 3 --connect-timeout 20 -H "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)" "$SourceUrl" -o "$DestFile" 2>$null
        if ($LASTEXITCODE -eq 0 -and (Test-Path $DestFile) -and ((Get-Item $DestFile).Length -ge $MinBytes)) {
            return $true
        }
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
        if (Test-Path $DestFile) { Remove-Item -Path $DestFile -Force -ErrorAction SilentlyContinue }
    } catch {}

    # 3. Invoke-WebRequest with User-Agent
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $SourceUrl -OutFile $DestFile -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" -UseBasicParsing -TimeoutSec 120
        if ((Test-Path $DestFile) -and ((Get-Item $DestFile).Length -ge $MinBytes)) {
            return $true
        }
    } catch {}

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
    Write-Host "[1/7] Проверка и подготовка среды .NET 10..." -ForegroundColor Cyan
} else {
    Write-Host "[1/7] Checking and preparing .NET 10 environment..." -ForegroundColor Cyan
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
        $msg = if ($chosenLang -eq "ru") { "Не удалось загрузить установщик .NET 10 SDK с https://dot.net" } else { "Failed to download .NET 10 SDK installer from https://dot.net" }
        throw $msg
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
    Write-Host "[2/7] Получение исходного кода OpenFlux Zen Server..." -ForegroundColor Cyan
} else {
    Write-Host "[2/7] Fetching OpenFlux Zen Server repository..." -ForegroundColor Cyan
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
            git clone --depth 1 "https://github.com/BizhQwe/openflux-zen-server.git" $workDir
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
            $msg = if ($chosenLang -eq "ru") { "Не удалось загрузить архив репозитория OpenFlux Zen Server с GitHub" } else { "Failed to download OpenFlux Zen Server repository archive from GitHub" }
            throw $msg
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
    Write-Host "[3/7] Сборка и публикация компонентов..." -ForegroundColor Cyan
} else {
    Write-Host "[3/7] Building and publishing application components..." -ForegroundColor Cyan
}

$InstallDir = Join-Path $env:ProgramFiles "OpenFluxZenServer"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

# Terminate existing running processes
Get-Process -Name "OpenFlux.Zen.Server.Web", "OpenFluxZenServer", "openflux-windows-amd64", "openflux-windows-arm64" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$webProj = Join-Path $workDir "src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj"
$cliProj = Join-Path $workDir "src\OpenFlux.Zen.Server.Cli\OpenFlux.Zen.Server.Cli.csproj"

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
& $dotnetCmd publish $cliProj -c Release -o $InstallDir

# Copy native runtimes
$runtimesSrc = Join-Path $workDir "runtimes"
if (Test-Path $runtimesSrc) {
    $runtimesDst = Join-Path $InstallDir "runtimes"
    New-Item -ItemType Directory -Path $runtimesDst -Force | Out-Null
    Copy-Item -Path "$runtimesSrc\*" -Destination $runtimesDst -Recurse -Force
}

# 6. Credentials and Configuration
if ($chosenLang -eq "ru") {
    Write-Host "[4/7] Генерация ключей доступа и учётных данных..." -ForegroundColor Cyan
} else {
    Write-Host "[4/7] Generating security credentials and access keys..." -ForegroundColor Cyan
}

$credFile = Join-Path $InstallDir "data\.credentials"
$existingUser = ""
$existingPass = ""
$existingSecret = ""
if (Test-Path $credFile) {
    try {
        $json = Get-Content $credFile -Raw | ConvertFrom-Json
        $existingUser = $json.username
        $existingPass = $json.password
        $existingSecret = $json.secretPath
    } catch {}
}

$adminUser = if ($existingUser) { $existingUser } else { "admin" }
$adminPass = if ($existingPass) { $existingPass } else { -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 16 | ForEach-Object {[char]$_}) }
$secretPath = if ($existingSecret) { $existingSecret } else { -join ((97..102) + (48..57) | Get-Random -Count 16 | ForEach-Object {[char]$_}) }
$listenPort = 5000

$localUrl = "http://127.0.0.1:$listenPort/$secretPath/"
$finalUrl = $localUrl

# 7. Autostart Question and Task Scheduler Setup
if ($chosenLang -eq "ru") {
    Write-Host "[5/7] Настройка автозапуска при загрузке системы..." -ForegroundColor Cyan
} else {
    Write-Host "[5/7] Configuring system autostart..." -ForegroundColor Cyan
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
    language = $chosenLang
    autostart = $autostartEnabled
    updatedAt = (Get-Date).ToUniversalTime().ToString("o")
}
$credObj | ConvertTo-Json -Depth 4 | Set-Content -Path $credFile -Encoding UTF8

# Configure Windows Scheduled Task
schtasks.exe /delete /tn "OpenFluxZenServer" /f 2>$null | Out-Null
$exePath = Join-Path $InstallDir "OpenFlux.Zen.Server.Web.exe"
$taskAction = '"' + $exePath + '"'
schtasks.exe /create /tn "OpenFluxZenServer" /tr $taskAction /sc onstart /ru SYSTEM /rl HIGHEST /f 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    schtasks.exe /create /tn "OpenFluxZenServer" /tr $taskAction /sc onlogon /rl HIGHEST /f 2>$null | Out-Null
}
if (-not $autostartEnabled) {
    schtasks.exe /change /tn "OpenFluxZenServer" /disable 2>$null | Out-Null
}

# 8. Register CLI in Machine PATH
if ($chosenLang -eq "ru") {
    Write-Host "[6/7] Регистрация OpenFluxZenServer в системном PATH..." -ForegroundColor Cyan
} else {
    Write-Host "[6/7] Registering OpenFluxZenServer CLI command in PATH..." -ForegroundColor Cyan
}

$currMachinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($currMachinePath -notmatch [regex]::Escape($InstallDir)) {
    [Environment]::SetEnvironmentVariable("Path", "$currMachinePath;$InstallDir", "Machine")
}
$env:PATH = "$InstallDir;$($env:PATH)"

# 9. Start Server Process
if ($chosenLang -eq "ru") {
    Write-Host "[7/7] Запуск OpenFlux Zen Server..." -ForegroundColor Cyan
} else {
    Write-Host "[7/7] Starting OpenFlux Zen Server..." -ForegroundColor Cyan
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exePath
$psi.WorkingDirectory = $InstallDir
$psi.EnvironmentVariables["OPENFLUX_HOST"] = "127.0.0.1"
$psi.EnvironmentVariables["OPENFLUX_PORT"] = "$listenPort"
$psi.EnvironmentVariables["OPENFLUX_SECRET_PATH"] = $secretPath
$psi.EnvironmentVariables["OPENFLUX_ADMIN_USER"] = $adminUser
$psi.EnvironmentVariables["OPENFLUX_ADMIN_PASSWORD"] = $adminPass
$psi.EnvironmentVariables["OPENFLUX_LANGUAGE"] = $chosenLang
$psi.EnvironmentVariables["OPENFLUX_PUBLIC_URL"] = $finalUrl
$psi.UseShellExecute = $false
[System.Diagnostics.Process]::Start($psi) | Out-Null

Start-Sleep -Seconds 2

# 10. Clean, Aligned Summary Card
$title = if ($chosenLang -eq "ru") { "OpenFlux Zen Server -- УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!" } else { "OpenFlux Zen Server -- SUCCESSFULLY INSTALLED AND STARTED!" }
$lblSecret = if ($chosenLang -eq "ru") { "Панель управления (Секретная ссылка)" } else { "Web Dashboard URL (Secret link)" }
$lblLocal = if ($chosenLang -eq "ru") { "Локальный адрес" } else { "Local Access URL" }
$lblCreds = if ($chosenLang -eq "ru") { "Учётные данные" } else { "Authentication Details" }
$lblUser = if ($chosenLang -eq "ru") { "    Логин:              " } else { "    Username:           " }
$lblPass = if ($chosenLang -eq "ru") { "    Пароль:             " } else { "    Password:           " }
$lblSecPath = if ($chosenLang -eq "ru") { "    Секретный путь:     " } else { "    Secret Path:        " }
$lblLang = if ($chosenLang -eq "ru") { "    Язык интерфейса:    " } else { "    Interface Language: " }
$lblAuto = if ($chosenLang -eq "ru") { "    Автозапуск:         " } else { "    Autostart:          " }
$valEnabled = if ($chosenLang -eq "ru") { "Включён" } else { "Enabled" }
$valDisabled = if ($chosenLang -eq "ru") { "Выключен" } else { "Disabled" }
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
