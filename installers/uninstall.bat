@echo off
setlocal
echo ==================================================================
echo       OpenFlux Zen Server - Uninstallation (Windows)             
echo ==================================================================

set /p CONFIRM="Are you sure you want to completely uninstall OpenFlux Zen Server? (y/N): "
if /i not "%CONFIRM%"=="y" (
    echo Uninstallation cancelled.
    exit /b 0
)

echo [INFO] Stopping service and tasks...
taskkill /f /im OpenFlux.Zen.Server.exe >nul 2>&1
schtasks /delete /tn "OpenFluxZenServer" /f >nul 2>&1

set "INSTALL_DIR=%ProgramFiles%\OpenFluxZenServer"
if not exist "%INSTALL_DIR%" set "INSTALL_DIR=%LOCALAPPDATA%\OpenFluxZenServer"

echo [INFO] Removing files (preserving any SSL certificates on system)...
if exist "%INSTALL_DIR%" (
    rd /s /q "%INSTALL_DIR%" >nul 2>&1
)

echo [SUCCESS] OpenFlux Zen Server has been completely uninstalled.
exit /b 0
