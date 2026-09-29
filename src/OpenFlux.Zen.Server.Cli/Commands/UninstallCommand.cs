using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using OpenFlux.Zen.Server.Cli.UI;
using OpenFlux.Zen.Server.Common;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class UninstallCommand
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint geteuid();

    public static bool IsAdmin()
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
            if (string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                return geteuid() == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static async Task ExecuteUninstallAsync(string[] cliArgs)
    {
        var isRu = CliUi.IsRussian;
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool force = cliArgs.Any(a => a == "-y" || a == "--yes" || a == "--force");

        if (isWindows && !IsAdmin())
        {
            CliUi.Info(isRu 
                ? "Требуются права Администратора для остановки системных служб и удаления файлов." 
                : "Administrator privileges required to stop system services and delete files.");
            CliUi.Info(isRu 
                ? "Запрос прав через UAC..." 
                : "Elevating permissions via UAC...");

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
                CliUi.Error(isRu 
                    ? "Требуются права Администратора для полного удаления." 
                    : "Administrator privileges are required to uninstall OpenFlux Zen Server.");
                Console.WriteLine(isRu 
                    ? "  Запустите PowerShell от имени Администратора (ПКМ -> Запуск от имени администратора) и повторите." 
                    : "  Please run PowerShell as Administrator (Right-click -> Run as administrator) and retry.");
                return;
            }
        }
        else if (!isWindows && !IsAdmin())
        {
            CliUi.Error(isRu 
                ? "Требуются права root для удаления OpenFlux Zen Server. Запустите: sudo OpenFluxZenServer uninstall -y" 
                : "Root privileges required to uninstall OpenFlux Zen Server. Run with: sudo OpenFluxZenServer uninstall -y");
            return;
        }

        if (!force)
        {
            var confirm = CliUi.AskYesNo(isRu 
                ? "Вы уверены, что хотите полностью удалить OpenFlux Zen Server из системы?" 
                : "Are you sure you want to completely uninstall OpenFlux Zen Server from system?", 
                defaultYes: false);

            if (!confirm)
            {
                CliUi.Warn(isRu ? "Удаление отменено." : "Uninstallation cancelled.");
                return;
            }
        }

        CliUi.Header(isRu 
            ? "OpenFlux Zen Server — Полное удаление" 
            : "OpenFlux Zen Server — Full Uninstallation");

        CliUi.Info(isRu 
            ? "Остановка служб и завершение фоновых процессов..." 
            : "Stopping OpenFlux Zen Server services and processes...");

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

            CliUi.Info(isRu 
                ? "Удаление файлов, конфигураций и баз данных..." 
                : "Completely removing application directories and data...");

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
            ProcessHelper.RunBash("pkill -9 -x OpenFlux.Zen.Server.Web 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -x OpenFlux.Zen.Server 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -x openflux-linux-amd64 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -x openflux-linux-arm64 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -x openflux 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -x zrok 2>/dev/null || true");
            ProcessHelper.RunBash("rm -f /etc/systemd/system/openflux-zen-server.service /etc/systemd/system/openflux-zrok.service /etc/systemd/system/openflux.service");
            ProcessHelper.RunBash("systemctl daemon-reload 2>/dev/null || true");
            ProcessHelper.RunBash("rm -f /usr/local/bin/OpenFluxZenServer /usr/local/bin/openfluxzenserver /usr/local/bin/openflux /usr/local/bin/openflux-zen-server /usr/bin/OpenFluxZenServer /usr/bin/openflux* /tmp/openflux*");
            ProcessHelper.RunBash("rm -f /etc/nginx/conf.d/openflux*.conf /etc/nginx/sites-enabled/openflux* 2>/dev/null; systemctl reload nginx 2>/dev/null || true");

            CliUi.Info(isRu 
                ? "Удаление файлов, конфигураций и баз данных..." 
                : "Completely removing application directories and data...");

            ProcessHelper.RunBash("rm -rf /opt/openflux-zen-server /tmp/openflux*");

            var stillExists = Directory.Exists("/opt/openflux-zen-server") || 
                              File.Exists("/etc/systemd/system/openflux-zen-server.service") || 
                              File.Exists("/usr/local/bin/OpenFluxZenServer");

            if (stillExists)
            {
                Console.WriteLine();
                CliUi.Error(isRu 
                    ? "Не удалось удалить некоторые системные файлы. Убедитесь, что команда запущена с правами root: sudo OpenFluxZenServer uninstall -y" 
                    : "Failed to remove some system files. Please make sure to run with root privileges: sudo OpenFluxZenServer uninstall -y");
                return;
            }
        }

        Console.WriteLine();
        CliUi.Success(isRu 
            ? "OpenFlux Zen Server полностью удален из системы." 
            : "OpenFlux Zen Server has been completely uninstalled from the system.");

        Console.WriteLine();
        CliUi.Divider();
        Console.WriteLine();
    }
}
