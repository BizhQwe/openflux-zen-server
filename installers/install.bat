@echo off
setlocal enabledelayedexpansion

:: ==============================================================================
:: OpenFlux Zen Server - Production Installer for Windows
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
if not exist "%INSTALL_DIR%\runtimes" mkdir "%INSTALL_DIR%\runtimes"

echo [2/6] Building and publishing application binaries (Web & CLI)...
dotnet publish "%REPO_ROOT%\src\OpenFlux.Zen.Server.Web\OpenFlux.Zen.Server.Web.csproj" -c Release -o "%INSTALL_DIR%" >nul
dotnet publish "%REPO_ROOT%\src\OpenFlux.Zen.Server.Cli\OpenFlux.Zen.Server.Cli.csproj" -c Release -o "%INSTALL_DIR%" >nul

:: Copy native runtimes
xcopy /y /e /i "%REPO_ROOT%\runtimes\*" "%INSTALL_DIR%\runtimes\" >nul

echo [3/6] Generating credentials and secret path...
set "ADMIN_USER=admin"
if not "%OPENFLUX_ADMIN_USER%"=="" set "ADMIN_USER=%OPENFLUX_ADMIN_USER%"

:: Generate random password and secret path
for /f "tokens=2 delims==" %%a in ('wmic OS Get localdatetime /value') do set "dt=%%a"
set "RANDOM_HEX=%dt:~8,6%%RANDOM%"
set "ADMIN_PASS=Zen%RANDOM%#%RANDOM%"
if not "%OPENFLUX_ADMIN_PASSWORD%"=="" set "ADMIN_PASS=%OPENFLUX_ADMIN_PASSWORD%"
set "SECRET_PATH=zen-%RANDOM_HEX%"
if not "%OPENFLUX_SECRET_PATH%"=="" set "SECRET_PATH=%OPENFLUX_SECRET_PATH%"
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
echo       OpenFlux Zen Server УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!          
echo ==================================================================
echo Панель управления (Секретная ссылка): %FINAL_URL%
echo Локальный адрес:                      %LOCAL_URL%
echo Логин:                                %ADMIN_USER%
echo Пароль:                               %ADMIN_PASS%
echo Секретный путь:                       /%SECRET_PATH%/
echo ------------------------------------------------------------------
echo CLI-команда доступна из любой папки:  OpenFluxZenServer ^<command^>
echo   OpenFluxZenServer credentials  - просмотр текущих данных входа
echo   OpenFluxZenServer help         - справка по командам
echo   OpenFluxZenServer uninstall    - полное удаление панели из системы
echo ==================================================================
echo.
pause
