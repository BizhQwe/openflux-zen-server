@echo off
setlocal enabledelayedexpansion

:: ==============================================================================
:: OpenFlux Zen Server - Production Installer for Windows (Batch)
:: ==============================================================================

echo ==================================================================
echo          OpenFlux Zen Server - Installation Script (Windows)      
echo ==================================================================
echo.

:: Check Admin Rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please run this batch file as Administrator!
    pause
    exit /b 1
)

set "REPO_ROOT=%~dp0.."
set "INSTALL_DIR=%ProgramFiles%\OpenFluxZenServer"
if not exist "%ProgramFiles%" set "INSTALL_DIR=%LOCALAPPDATA%\OpenFluxZenServer"

echo [1/6] Preparing installation directory: "%INSTALL_DIR%"...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

:: Check for .NET 10 SDK
echo Checking .NET 10 environment...
where dotnet >nul 2>&1
set "NEED_DOTNET=1"
if %errorlevel% equ 0 (
    dotnet --list-sdks 2>nul | findstr /r "^10\." >nul
    if !errorlevel! equ 0 set "NEED_DOTNET=0"
)

if not exist "%ProgramFiles%\dotnet\dotnet.exe" goto check_dotnet_need
set "PATH=%ProgramFiles%\dotnet;%PATH%"
"%ProgramFiles%\dotnet\dotnet.exe" --list-sdks 2>nul | findstr /r "^10\." >nul
if %errorlevel% equ 0 set "NEED_DOTNET=0"

:check_dotnet_need
if "%NEED_DOTNET%"=="1" (
    echo Installing .NET 10 SDK via official Microsoft installer...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $f = \"$env:TEMP\dotnet-install.ps1\"; Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $f -UseBasicParsing; & $f -Channel 10.0 -InstallDir \"$env:ProgramFiles\dotnet\""
    set "PATH=%ProgramFiles%\dotnet;%PATH%"
    set "DOTNET_ROOT=%ProgramFiles%\dotnet"
)

echo [2/6] Building and publishing application binaries (Web and CLI)...
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
taskkill /f /im OpenFlux.Zen.Server.exe >nul 2>&1
taskkill /f /im OpenFluxZenServer.exe >nul 2>&1
taskkill /f /im openflux-windows-amd64.exe >nul 2>&1
taskkill /f /im openflux-windows-arm64.exe >nul 2>&1

dotnet publish "%REPO_ROOT%\src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj" -c Release -o "%INSTALL_DIR%" >nul 2>&1
dotnet publish "%REPO_ROOT%\src\OpenFlux.Zen.Server.Cli\OpenFlux.Zen.Server.Cli.csproj" -c Release -o "%INSTALL_DIR%" >nul 2>&1

if not exist "%INSTALL_DIR%\OpenFlux.Zen.Server.Web.exe" (
    echo [ERROR] Failed to compile OpenFlux Zen Server binaries. Please verify .NET 10 SDK is installed.
    pause
    exit /b 1
)

:: Copy native runtimes
if exist "%REPO_ROOT%\runtimes" (
    xcopy /y /e /i "%REPO_ROOT%\runtimes\*" "%INSTALL_DIR%\runtimes\" >nul 2>&1
)

echo [3/6] Generating credentials and secret path...
set "ADMIN_USER=admin"
if not "%OPENFLUX_ADMIN_USER%"=="" set "ADMIN_USER=%OPENFLUX_ADMIN_USER%"

set "ADMIN_PASS="
if not "%OPENFLUX_ADMIN_PASSWORD%"=="" set "ADMIN_PASS=%OPENFLUX_ADMIN_PASSWORD%"
if "%ADMIN_PASS%"=="" (
    for /f %%i in ('powershell -NoProfile -Command "$c='abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';$b=New-Object byte[] 16;[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b);-join ($b|ForEach-Object { $c[$_%%$c.Length] })"') do set "ADMIN_PASS=%%i"
)

set "SECRET_PATH="
if not "%OPENFLUX_SECRET_PATH%"=="" set "SECRET_PATH=%OPENFLUX_SECRET_PATH%"
if "%SECRET_PATH%"=="" (
    for /f %%i in ('powershell -NoProfile -Command "$sb=New-Object byte[] 8;[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($sb);-join ($sb|ForEach-Object { $_.ToString('x2') })"') do set "SECRET_PATH=%%i"
)
set "LISTEN_PORT=5000"
if not "%OPENFLUX_PORT%"=="" set "LISTEN_PORT=%OPENFLUX_PORT%"

set "LOCAL_URL=http://127.0.0.1:%LISTEN_PORT%/%SECRET_PATH%/"
set "FINAL_URL=%LOCAL_URL%"

:: Save credentials file
if not exist "%INSTALL_DIR%\data" mkdir "%INSTALL_DIR%\data"
(
echo {
echo   "username": "%ADMIN_USER%",
echo   "password": "%ADMIN_PASS%",
echo   "secretPath": "%SECRET_PATH%",
echo   "publicUrl": "%FINAL_URL%",
echo   "updatedAt": "%date% %time%"
echo }
) > "%INSTALL_DIR%\data\.credentials"

echo [4/6] Configuring autostart scheduled task...
schtasks /delete /tn "OpenFluxZenServer" /f >nul 2>&1
schtasks /create /tn "OpenFluxZenServer" /tr "\"%INSTALL_DIR%\OpenFlux.Zen.Server.Web.exe\"" /sc onstart /ru SYSTEM /rl HIGHEST /f >nul 2>&1
if %errorlevel% neq 0 (
    schtasks /create /tn "OpenFluxZenServer" /tr "\"%INSTALL_DIR%\OpenFlux.Zen.Server.Web.exe\"" /sc onlogon /rl HIGHEST /f >nul 2>&1
)

echo [5/6] Registering OpenFluxZenServer in system PATH...
for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "SYS_PATH=%%b"
echo ;%SYS_PATH%; | find /i ";%INSTALL_DIR%;" >nul
if %errorlevel% neq 0 (
    setx PATH "%SYS_PATH%;%INSTALL_DIR%" /m >nul 2>&1
)

echo [6/6] Starting OpenFlux Zen Server...
start "" "%INSTALL_DIR%\OpenFlux.Zen.Server.Web.exe"

timeout /t 2 /nobreak >nul

echo.
echo ==================================================================
echo   OpenFlux Zen Server — SUCCESSFULLY INSTALLED ^& STARTED!          
echo ==================================================================
echo.
echo   Web Dashboard URL:
echo     %FINAL_URL%
echo.
echo   Local Access URL:
echo     %LOCAL_URL%
echo.
echo   Authentication Details:
echo     Username:           %ADMIN_USER%
echo     Password:           %ADMIN_PASS%
echo     Secret Path:        /%SECRET_PATH%/
echo.
echo ------------------------------------------------------------------
echo   CLI command available anywhere: OpenFluxZenServer ^<command^>
echo.
echo     OpenFluxZenServer status       — show server status
echo     OpenFluxZenServer restart      — restart server service
echo     OpenFluxZenServer credentials  — view login credentials and URLs
echo     OpenFluxZenServer autostart    — configure autostart (enable ^| disable)
echo     OpenFluxZenServer help         — show CLI command help
echo ==================================================================
echo.
pause
