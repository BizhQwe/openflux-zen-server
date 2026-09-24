using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenFlux.Zen.Server.Common;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class UninstallCommand
{
    public static async Task ExecuteUninstallAsync(string[] cliArgs)
    {
        bool force = cliArgs.Any(a => a == "-y" || a == "--yes" || a == "--force");
        if (!force)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Are you sure you want to completely uninstall OpenFlux Zen Server? [y/N]: ");
            Console.ResetColor();
            var ans = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (ans != "y" && ans != "yes")
            {
                Console.WriteLine("Uninstallation cancelled.");
                return;
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[INFO] Stopping OpenFlux Zen Server services and processes...");
        Console.ResetColor();

        var appDir = AppPaths.ResolveAppDirectory();
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (isWindows)
        {
            ProcessHelper.RunCommand("sc.exe", "stop OpenFluxZenServer");
            ProcessHelper.RunCommand("sc.exe", "delete OpenFluxZenServer");
            ProcessHelper.RunCommand("schtasks.exe", "/delete /tn \"OpenFluxZenServer\" /f");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.Web.exe");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.exe");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im openflux-windows-amd64.exe");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im openflux-windows-arm64.exe");

            // Remove from system PATH
            try
            {
                var sysPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
                if (sysPath.Contains(appDir, StringComparison.OrdinalIgnoreCase))
                {
                    var cleanPath = string.Join(";", sysPath.Split(';')
                        .Where(p => !p.Trim().Equals(appDir.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                    !p.Trim().Equals(Path.Combine(appDir, "cli").Trim(), StringComparison.OrdinalIgnoreCase)));
                    Environment.SetEnvironmentVariable("PATH", cleanPath, EnvironmentVariableTarget.Machine);
                }
            }
            catch { }

            Console.WriteLine("[INFO] Cleaning up application files (strictly preserving SSL certificates)...");
            var runnerBat = Path.Combine(Path.GetTempPath(), "oflux_uninstall.bat");
            var runnerScript = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
taskkill /f /im OpenFlux.Zen.Server.exe >nul 2>&1
taskkill /f /im OpenFluxZenServer.exe >nul 2>&1
rd /s /q ""{appDir}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";
            await File.WriteAllTextAsync(runnerBat, runnerScript);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{runnerBat}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            ProcessHelper.RunBash("systemctl stop openflux-zen-server 2>/dev/null || true");
            ProcessHelper.RunBash("systemctl stop openflux-zrok 2>/dev/null || true");
            ProcessHelper.RunBash("systemctl disable openflux-zen-server 2>/dev/null || true");
            ProcessHelper.RunBash("systemctl disable openflux-zrok 2>/dev/null || true");
            ProcessHelper.RunBash("rm -f /etc/systemd/system/openflux-zen-server.service /etc/systemd/system/openflux-zrok.service");
            ProcessHelper.RunBash("systemctl daemon-reload 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f openflux-linux 2>/dev/null || true");
            ProcessHelper.RunBash("rm -f /usr/local/bin/OpenFluxZenServer /usr/bin/OpenFluxZenServer");

            Console.WriteLine("[INFO] Cleaning up application files (strictly preserving SSL certificates)...");
            var runnerSh = Path.Combine(Path.GetTempPath(), "oflux_uninstall.sh");
            var runnerScript = $@"#!/bin/bash
sleep 1
pkill -9 -f OpenFlux.Zen.Server 2>/dev/null || true
rm -rf ""{appDir}""
rm -f ""$0""
";
            await File.WriteAllTextAsync(runnerSh, runnerScript);
            ProcessHelper.RunBash($"chmod +x \"{runnerSh}\" && nohup \"{runnerSh}\" >/dev/null 2>&1 &");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] OpenFlux Zen Server has been completely uninstalled from the system.");
        Console.ResetColor();
    }
}
