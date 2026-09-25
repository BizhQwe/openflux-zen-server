using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Data;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class ServerCommands
{
    public static async Task ExecuteStartAsync()
    {
        Console.WriteLine("[INFO] Starting OpenFlux Zen Server...");
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            var (taskCode, _) = ProcessHelper.RunCommandWithOutput("schtasks.exe", "/run /tn \"OpenFluxZenServer\"");
            if (taskCode != 0)
            {
                var exePath = Path.Combine(AppPaths.ResolveAppDirectory(), "OpenFlux.Zen.Server.Web.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = AppPaths.ResolveAppDirectory(),
                        UseShellExecute = true
                    });
                }
                else
                {
                    ProcessHelper.RunCommand("sc.exe", "start OpenFluxZenServer");
                }
            }
        }
        else
        {
            ProcessHelper.RunBash("systemctl start openflux-zen-server.service");
            if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
            {
                ProcessHelper.RunBash("systemctl start openflux-zrok.service");
            }
        }
        await Task.Delay(1000);
        await ExecuteStatusAsync();
    }

    public static async Task ExecuteStopAsync()
    {
        Console.WriteLine("[INFO] Stopping OpenFlux Zen Server...");
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            ProcessHelper.RunCommand("schtasks.exe", "/end /tn \"OpenFluxZenServer\" >nul 2>&1");
            ProcessHelper.RunCommand("sc.exe", "stop OpenFluxZenServer >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1");
        }
        else
        {
            ProcessHelper.RunBash("systemctl stop openflux-zen-server.service");
            if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
            {
                ProcessHelper.RunBash("systemctl stop openflux-zrok.service");
            }
        }
        await Task.Delay(1000);
        await ExecuteStatusAsync();
    }

    public static async Task ExecuteRestartAsync()
    {
        Console.WriteLine("[INFO] Restarting OpenFlux Zen Server...");
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            await ExecuteStopAsync();
            await Task.Delay(1500);
            await ExecuteStartAsync();
        }
        else
        {
            ProcessHelper.RunBash("systemctl restart openflux-zen-server.service");
            if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
            {
                ProcessHelper.RunBash("systemctl restart openflux-zrok.service");
            }
            await Task.Delay(1000);
            await ExecuteStatusAsync();
        }
    }

    public static Task ExecuteStatusAsync()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            var runningProcs = Process.GetProcessesByName("OpenFlux.Zen.Server.Web");
            bool isRunning = runningProcs.Length > 0;
            Console.ForegroundColor = isRunning ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(isRunning 
                ? $"[STATUS] OpenFlux Zen Server: RUNNING (PID: {runningProcs[0].Id})" 
                : "[STATUS] OpenFlux Zen Server: STOPPED");
            Console.ResetColor();
        }
        else
        {
            var (_, output) = ProcessHelper.RunBashWithOutput("systemctl is-active openflux-zen-server.service 2>/dev/null || true");
            var status = output.Trim();
            bool isRunning = status == "active";

            Console.ForegroundColor = isRunning ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(isRunning 
                ? "[STATUS] OpenFlux Zen Server: RUNNING (active)" 
                : $"[STATUS] OpenFlux Zen Server: NOT RUNNING ({status})");
            Console.ResetColor();

            if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
            {
                var (_, zrokOut) = ProcessHelper.RunBashWithOutput("systemctl is-active openflux-zrok.service 2>/dev/null || true");
                var zrokStatus = zrokOut.Trim();
                Console.WriteLine($"[STATUS] Zrok Public Share : {(zrokStatus == "active" ? "RUNNING (active)" : zrokStatus)}");
            }
        }
        return Task.CompletedTask;
    }

    public static async Task ExecuteAutostartAsync(string[] cliArgs)
    {
        var subCmd = cliArgs.Length > 1 ? cliArgs[1].ToLowerInvariant() : "status";
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        switch (subCmd)
        {
            case "enable":
            case "on":
            case "1":
            case "true":
                if (isWindows)
                {
                    ProcessHelper.RunCommand("schtasks.exe", "/change /tn \"OpenFluxZenServer\" /enable >nul 2>&1");
                    ProcessHelper.RunCommand("sc.exe", "config OpenFluxZenServer start= auto >nul 2>&1");
                }
                else
                {
                    ProcessHelper.RunBash("systemctl enable openflux-zen-server.service 2>/dev/null || true");
                    if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
                    {
                        ProcessHelper.RunBash("systemctl enable openflux-zrok.service 2>/dev/null || true");
                    }
                }
                await UpdateAutostartDbAsync(true);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[OK] Autostart ENABLED: OpenFlux Zen Server will start automatically on system boot.");
                Console.ResetColor();
                break;

            case "disable":
            case "off":
            case "0":
            case "false":
                if (isWindows)
                {
                    ProcessHelper.RunCommand("schtasks.exe", "/change /tn \"OpenFluxZenServer\" /disable >nul 2>&1");
                    ProcessHelper.RunCommand("sc.exe", "config OpenFluxZenServer start= demand >nul 2>&1");
                }
                else
                {
                    ProcessHelper.RunBash("systemctl disable openflux-zen-server.service 2>/dev/null || true");
                    if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
                    {
                        ProcessHelper.RunBash("systemctl disable openflux-zrok.service 2>/dev/null || true");
                    }
                }
                await UpdateAutostartDbAsync(false);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[OK] Autostart DISABLED: OpenFlux Zen Server will NOT start on system boot.");
                Console.ResetColor();
                break;

            default:
                bool isEnabled = false;
                if (isWindows)
                {
                    var (_, taskOut) = ProcessHelper.RunCommandWithOutput("schtasks.exe", "/query /tn \"OpenFluxZenServer\" /fo list");
                    if (taskOut.Contains("OpenFluxZenServer", StringComparison.OrdinalIgnoreCase))
                    {
                        isEnabled = !taskOut.Contains("Disabled", StringComparison.OrdinalIgnoreCase) && !taskOut.Contains("Отключено", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        var (_, scOut) = ProcessHelper.RunCommandWithOutput("sc.exe", "qc OpenFluxZenServer");
                        isEnabled = scOut.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);
                    }
                }
                else
                {
                    var (_, outText) = ProcessHelper.RunBashWithOutput("systemctl is-enabled openflux-zen-server.service 2>/dev/null || true");
                    isEnabled = outText.Trim() == "enabled";
                }
                Console.WriteLine($"[INFO] Autostart Status: {(isEnabled ? "ENABLED" : "DISABLED")}");
                Console.WriteLine("To change autostart, run: OpenFluxZenServer autostart enable | disable");
                break;
        }
    }

    private static async Task UpdateAutostartDbAsync(bool enabled)
    {
        var dbPath = AppPaths.GetDatabasePath();
        if (!File.Exists(dbPath)) return;
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            using var db = new AppDbContext(options);
            var s = await db.Settings.FirstOrDefaultAsync(x => x.Id == 1);
            if (s != null)
            {
                s.AutoStartEnabled = enabled;
                s.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch { }
    }
}
