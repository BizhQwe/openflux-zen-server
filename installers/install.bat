@echo off
setlocal
set PREFIX=%ProgramFiles%\OpenFluxZenServer
if not exist "%PREFIX%" mkdir "%PREFIX%"
if not exist "%PREFIX%\.git" git clone https://github.com/BizhQwe/openflux-zen-server.git "%PREFIX%" else git -C "%PREFIX%" pull --ff-only
dotnet publish "%PREFIX%\src\OpenFlux.Zen.Server\OpenFlux.Zen.Server.csproj" -c Release -o "%PREFIX%\app"
xcopy /E /I /Y "%PREFIX%\runtimes" "%PREFIX%\app\runtimes"
sc create OpenFluxZenServer binPath= "dotnet %PREFIX%\app\OpenFlux.Zen.Server.dll" start= auto
sc start OpenFluxZenServer
echo OpenFlux Zen Server installed. Use Task Scheduler or NSSM for hardened service deployments.
