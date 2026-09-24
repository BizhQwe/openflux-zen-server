@echo off
setlocal
if "%~1"=="" set "cmd=help" else set "cmd=%~1"
if /I "%cmd%"=="help" goto help
if /I "%cmd%"=="credentials" goto credentials
if /I "%cmd%"=="uninstall" goto uninstall
:help
echo OpenFluxZenServer help
echo OpenFluxZenServer credentials
echo OpenFluxZenServer uninstall
exit /b 0
:credentials
echo Username: %OPENFLUX_ADMIN_USER%
echo Password is supplied through OPENFLUX_ADMIN_PASSWORD during installation.
exit /b 0
:uninstall
call "%~dp0..\installers\uninstall.bat"
exit /b 0
