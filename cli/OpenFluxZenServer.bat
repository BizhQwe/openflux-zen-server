@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "APP_DIR=%SCRIPT_DIR%.."

if exist "%APP_DIR%\OpenFlux.Zen.Server.exe" (
    "%APP_DIR%\OpenFlux.Zen.Server.exe" %*
) else if exist "%APP_DIR%\publish\OpenFlux.Zen.Server.exe" (
    "%APP_DIR%\publish\OpenFlux.Zen.Server.exe" %*
) else if exist "%APP_DIR%\publish\OpenFlux.Zen.Server.dll" (
    dotnet "%APP_DIR%\publish\OpenFlux.Zen.Server.dll" %*
) else if exist "%APP_DIR%\src\OpenFlux.Zen.Server\bin\Release\net10.0\OpenFlux.Zen.Server.exe" (
    "%APP_DIR%\src\OpenFlux.Zen.Server\bin\Release\net10.0\OpenFlux.Zen.Server.exe" %*
) else if exist "%APP_DIR%\src\OpenFlux.Zen.Server\bin\Debug\net10.0\OpenFlux.Zen.Server.exe" (
    "%APP_DIR%\src\OpenFlux.Zen.Server\bin\Debug\net10.0\OpenFlux.Zen.Server.exe" %*
) else if exist "%APP_DIR%\src\OpenFlux.Zen.Server\bin\Debug\net10.0\OpenFlux.Zen.Server.dll" (
    dotnet "%APP_DIR%\src\OpenFlux.Zen.Server\bin\Debug\net10.0\OpenFlux.Zen.Server.dll" %*
) else (
    dotnet run --project "%APP_DIR%\src\OpenFlux.Zen.Server" -- %*
)
