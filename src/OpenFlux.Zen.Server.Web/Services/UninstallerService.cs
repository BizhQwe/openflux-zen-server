using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenFlux.Zen.Server.Services;

public interface IUninstallerService
{
    Task<bool> TriggerUninstallAsync();
}

public sealed class UninstallerService : IUninstallerService
{
    private readonly ILogger<UninstallerService> _logger;
    private readonly ITunnelProcessSupervisor _supervisor;

    public UninstallerService(ILogger<UninstallerService> logger, ITunnelProcessSupervisor supervisor)
    {
        _logger = logger;
        _supervisor = supervisor;
    }

    public async Task<bool> TriggerUninstallAsync()
    {
        _logger.LogWarning("Complete uninstallation requested. Initiating cleanup...");

        // 1. Stop all active tunnels immediately
        try
        {
            _supervisor.StopAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping tunnels during uninstall");
        }

        // 2. Launch detached platform-specific uninstaller script in temp folder
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var tempDir = Path.GetTempPath();

        if (isWindows)
        {
            var uninstallScript = Path.Combine(tempDir, "oflux_svc_uninstall.bat");
            var progFilesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenFluxZenServer");
            var localAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFluxZenServer");

            var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
sc stop OpenFluxZenServer >nul 2>&1
sc delete OpenFluxZenServer >nul 2>&1
schtasks /delete /tn ""OpenFluxZenServer"" /f >nul 2>&1
schtasks /delete /tn ""OpenFluxZrok"" /f >nul 2>&1
netsh advfirewall firewall delete rule name=""OpenFluxZenServer"" >nul 2>&1
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
taskkill /f /im OpenFlux.Zen.Server.exe >nul 2>&1
taskkill /f /im OpenFluxZenServer.exe >nul 2>&1
taskkill /f /im openflux-windows-amd64.exe >nul 2>&1
taskkill /f /im openflux-windows-arm64.exe >nul 2>&1
taskkill /f /im openflux.exe >nul 2>&1
taskkill /f /im zrok.exe >nul 2>&1
if exist ""{progFilesDir}"" rd /s /q ""{progFilesDir}"" >nul 2>&1
if exist ""{localAppDir}"" rd /s /q ""{localAppDir}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";
            try
            {
                await File.WriteAllTextAsync(uninstallScript, scriptContent);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{uninstallScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch Windows uninstaller script");
            }
        }
        else
        {
            var uninstallScript = Path.Combine(tempDir, "oflux_svc_uninstall.sh");
            var scriptContent = $@"#!/bin/bash
sleep 2
systemctl stop openflux-zen-server.service 2>/dev/null || true
systemctl stop openflux-zrok.service 2>/dev/null || true
systemctl disable openflux-zen-server.service 2>/dev/null || true
systemctl disable openflux-zrok.service 2>/dev/null || true
pkill -9 -f OpenFlux.Zen.Server 2>/dev/null || true
pkill -9 -f openflux 2>/dev/null || true
pkill -9 -f OpenFluxZenServer 2>/dev/null || true
pkill -9 -f zrok 2>/dev/null || true
rm -f /etc/systemd/system/openflux-zen-server.service /etc/systemd/system/openflux-zrok.service /etc/systemd/system/openflux.service
systemctl daemon-reload 2>/dev/null || true
rm -f /usr/local/bin/OpenFluxZenServer /usr/local/bin/openfluxzenserver /usr/local/bin/openflux /usr/local/bin/openflux-zen-server /usr/bin/OpenFluxZenServer /usr/bin/openflux* /tmp/openflux*
rm -f /etc/nginx/conf.d/openflux*.conf /etc/nginx/sites-enabled/openflux* 2>/dev/null
systemctl reload nginx 2>/dev/null || true
rm -rf /opt/openflux-zen-server /tmp/openflux*
rm -f ""$0""
";
            try
            {
                await File.WriteAllTextAsync(uninstallScript, scriptContent);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"\"{uninstallScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch Linux uninstaller script");
            }
        }

        // Schedule graceful process exit
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            Environment.Exit(0);
        });

        return true;
    }
}
