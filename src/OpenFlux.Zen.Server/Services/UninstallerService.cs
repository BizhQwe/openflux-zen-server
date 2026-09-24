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

        // 2. Launch detached platform-specific uninstaller script
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var baseDir = AppContext.BaseDirectory;

        if (isWindows)
        {
            var uninstallScript = Path.Combine(baseDir, "uninstall_runner.bat");
            var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
sc stop OpenFluxZenServer >nul 2>&1
sc delete OpenFluxZenServer >nul 2>&1
schtasks /delete /tn ""OpenFluxZenServer"" /f >nul 2>&1
taskkill /f /im OpenFlux.Zen.Server.exe >nul 2>&1
rd /s /q ""{baseDir}"" >nul 2>&1
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
            var uninstallScript = Path.Combine(baseDir, "uninstall_runner.sh");
            var scriptContent = $@"#!/bin/bash
sleep 2
systemctl stop openflux-zen-server 2>/dev/null || true
systemctl stop openflux-zrok 2>/dev/null || true
systemctl disable openflux-zen-server 2>/dev/null || true
systemctl disable openflux-zrok 2>/dev/null || true
rm -f /etc/systemd/system/openflux-zen-server.service
rm -f /etc/systemd/system/openflux-zrok.service
systemctl daemon-reload 2>/dev/null || true
rm -f /usr/local/bin/OpenFluxZenServer
rm -f /usr/bin/OpenFluxZenServer
# Remove app folder, preserving any SSL certificates on the server
rm -rf ""{baseDir}""
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
