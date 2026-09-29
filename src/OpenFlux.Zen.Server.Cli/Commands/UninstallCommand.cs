using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using OpenFlux.Zen.Server.Common;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class UninstallCommand
{
    [DllImport("libc")]
    private static extern uint geteuid();

    private static bool IsAdmin()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
        else
        {
            try { return geteuid() == 0; } catch { return false; }
        }
    }

    public static async Task ExecuteUninstallAsync(string[] cliArgs)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool force = cliArgs.Any(a => a == "-y" || a == "--yes" || a == "--force");

        if (isWindows && !IsAdmin())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[INFO] Administrator privileges required to stop system services and delete files.");
            Console.WriteLine("[INFO] Elevating permissions via UAC...");
            Console.ResetColor();

            try
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "OpenFluxZenServer.exe";
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "uninstall " + (force ? "-y" : ""),
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var p = Process.Start(psi);
                p?.WaitForExit();
                return;
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Administrator privileges are required to uninstall OpenFlux Zen Server.");
                Console.WriteLine("Please run PowerShell as Administrator (Right-click -> Run as administrator) and retry.");
                Console.ResetColor();
                return;
            }
        }
        else if (!isWindows && !IsAdmin())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] Root privileges required to uninstall system service. Run with: sudo OpenFluxZenServer uninstall");
            Console.ResetColor();
            return;
        }

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

        if (isWindows)
        {
            ProcessHelper.RunCommand("schtasks.exe", "/end /tn \"OpenFluxZenServer\"");
            ProcessHelper.RunCommand("schtasks.exe", "/delete /tn \"OpenFluxZenServer\" /f");
            ProcessHelper.RunCommand("sc.exe", "stop OpenFluxZenServer >nul 2>&1");
            ProcessHelper.RunCommand("sc.exe", "delete OpenFluxZenServer >nul 2>&1");
            ProcessHelper.RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"OpenFluxZenServer\"");

            // Kill all running processes
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im OpenFlux.Zen.Server.Web.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im OpenFlux.Zen.Server.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im OpenFluxZenServer.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im openflux-windows-amd64.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im openflux-windows-arm64.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im openflux.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /t /im zrok.exe >nul 2>&1");

            // Remove from system PATH and clean Machine environment variables
            try
            {
                var envVars = new[] {
                    "OPENFLUX_HOST", "OPENFLUX_PORT", "OPENFLUX_SECRET_PATH",
                    "OPENFLUX_ADMIN_USER", "OPENFLUX_ADMIN_PASSWORD", "OPENFLUX_PUBLIC_URL",
                    "OPENFLUX_PUBLISH_MODE", "OPENFLUX_LANGUAGE", "OPENFLUX_DECOY_REDIRECT_URL",
                    "OPENFLUX_DECOY_MODE", "OPENFLUX_APP_DIR"
                };
                foreach (var ev in envVars)
                {
                    Environment.SetEnvironmentVariable(ev, null, EnvironmentVariableTarget.Machine);
                }

                var sysPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
                if (sysPath.Contains("OpenFluxZenServer", StringComparison.OrdinalIgnoreCase))
                {
                    var cleanPath = string.Join(";", sysPath.Split(';')
                        .Where(p => !p.Contains("OpenFluxZenServer", StringComparison.OrdinalIgnoreCase)));
                    Environment.SetEnvironmentVariable("PATH", cleanPath, EnvironmentVariableTarget.Machine);
                }
            }
            catch { }

            Console.WriteLine("[INFO] Completely removing application directories and data...");
            var progFilesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenFluxZenServer");
            var localAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFluxZenServer");

            foreach (var d in new[] { progFilesDir, localAppDir, appDir })
            {
                if (Directory.Exists(d))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }

            var runnerBat = Path.Combine(Path.GetTempPath(), "oflux_clean.bat");
            var runnerScript = $@"@echo off
timeout /t 1 /nobreak >nul
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
taskkill /f /im openflux-windows-amd64.exe >nul 2>&1
taskkill /f /im openflux-windows-arm64.exe >nul 2>&1
taskkill /f /im openflux.exe >nul 2>&1
if exist ""{progFilesDir}"" rd /s /q ""{progFilesDir}"" >nul 2>&1
if exist ""{localAppDir}"" rd /s /q ""{localAppDir}"" >nul 2>&1
if exist ""{appDir}"" rd /s /q ""{appDir}"" >nul 2>&1
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
            ProcessHelper.RunBash("systemctl disable --now openflux-zen-server.service 2>/dev/null || true");
            ProcessHelper.RunBash("systemctl disable --now openflux-zrok.service 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f OpenFlux.Zen.Server 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f OpenFluxZenServer 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f openflux-linux 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f openflux 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f zrok 2>/dev/null || true");
            ProcessHelper.RunBash("rm -f /etc/systemd/system/openflux-zen-server.service /etc/systemd/system/openflux-zrok.service /etc/systemd/system/openflux.service");
            ProcessHelper.RunBash("systemctl daemon-reload 2>/dev/null || true");
            ProcessHelper.RunBash("rm -f /usr/local/bin/OpenFluxZenServer /usr/local/bin/openfluxzenserver /usr/local/bin/openflux /usr/local/bin/openflux-zen-server /usr/bin/OpenFluxZenServer /usr/bin/openflux* /tmp/openflux*");
            ProcessHelper.RunBash("rm -f /etc/nginx/conf.d/openflux*.conf /etc/nginx/sites-enabled/openflux* 2>/dev/null; systemctl reload nginx 2>/dev/null || true");

            Console.WriteLine("[INFO] Completely removing application directories and data...");
            ProcessHelper.RunBash("rm -rf /opt/openflux-zen-server /tmp/openflux*");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] OpenFlux Zen Server has been completely uninstalled from the system.");
        Console.ResetColor();
    }
}
