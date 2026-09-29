using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Cli.UI;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Data;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class ServerCommands
{
    public static async Task ExecuteStartAsync()
    {
        var isRu = CliUi.IsRussian;
        CliUi.Info(isRu 
            ? "Запуск службы OpenFlux Zen Server..." 
            : "Starting OpenFlux Zen Server service...");

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
        }

        await Task.Delay(1500);
        await ExecuteStatusAsync();
    }

    public static async Task ExecuteStopAsync()
    {
        var isRu = CliUi.IsRussian;
        CliUi.Info(isRu 
            ? "Остановка службы OpenFlux Zen Server..." 
            : "Stopping OpenFlux Zen Server service...");

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows)
        {
            ProcessHelper.RunCommand("schtasks.exe", "/end /tn \"OpenFluxZenServer\" >nul 2>&1");
            ProcessHelper.RunCommand("sc.exe", "stop OpenFluxZenServer >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im openflux-windows-amd64.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im openflux-windows-arm64.exe >nul 2>&1");
            ProcessHelper.RunCommand("taskkill.exe", "/f /im openflux.exe >nul 2>&1");
        }
        else
        {
            ProcessHelper.RunBash("systemctl stop openflux-zen-server.service 2>/dev/null || true");
            ProcessHelper.RunBash("systemctl stop openflux-zrok.service 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f OpenFlux.Zen.Server 2>/dev/null || true");
            ProcessHelper.RunBash("pkill -9 -f openflux 2>/dev/null || true");
        }

        await Task.Delay(1000);
        await ExecuteStatusAsync();
    }

    public static async Task ExecuteRestartAsync()
    {
        var isRu = CliUi.IsRussian;
        CliUi.Info(isRu 
            ? "Перезапуск службы OpenFlux Zen Server..." 
            : "Restarting OpenFlux Zen Server service...");

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
            await Task.Delay(1500);
            await ExecuteStatusAsync();
        }
    }

    public static Task ExecuteStatusAsync()
    {
        var isRu = CliUi.IsRussian;
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        bool isRunning = false;
        string detail = "";

        if (isWindows)
        {
            var runningProcs = Process.GetProcessesByName("OpenFlux.Zen.Server.Web");
            isRunning = runningProcs.Length > 0;
            detail = isRunning ? $"PID: {runningProcs[0].Id}" : "inactive";
        }
        else
        {
            var (_, output) = ProcessHelper.RunBashWithOutput("systemctl is-active openflux-zen-server.service 2>/dev/null || true");
            var status = output.Trim();
            isRunning = status == "active";
            detail = !string.IsNullOrEmpty(status) ? status : "inactive";
        }

        bool isAutostart = CheckAutostartEnabled(isWindows);
        string? publicUrl = GetConfiguredUrl();

        CliUi.Header(isRu 
            ? "OpenFlux Zen Server — Статус службы" 
            : "OpenFlux Zen Server — Service Status");

        string statusLabel = isRu ? "Статус службы:" : "Service Status:";
        string statusVal = isRunning 
            ? (isRu ? $"РАБОТАЕТ ({detail})" : $"RUNNING ({detail})") 
            : (isRu ? $"ОСТАНОВЛЕН ({detail})" : $"STOPPED ({detail})");

        CliUi.Field(statusLabel, statusVal, isRunning ? ConsoleColor.Green : ConsoleColor.Red);

        if (!string.IsNullOrEmpty(publicUrl))
        {
            string urlLabel = isRu ? "Панель управления:" : "Web Dashboard:";
            CliUi.Field(urlLabel, publicUrl, ConsoleColor.Yellow);
        }

        string autoLabel = isRu ? "Автозапуск:" : "Autostart:";
        string autoVal = isAutostart 
            ? (isRu ? "ВКЛЮЧЕН (при загрузке системы)" : "ENABLED (on system boot)") 
            : (isRu ? "ВЫКЛЮЧЕН" : "DISABLED");

        CliUi.Field(autoLabel, autoVal, isAutostart ? ConsoleColor.Green : ConsoleColor.Yellow);

        Console.WriteLine();
        CliUi.Divider();
        Console.WriteLine();

        return Task.CompletedTask;
    }

    public static async Task ExecuteAutostartAsync(string[] cliArgs)
    {
        var isRu = CliUi.IsRussian;
        var subCmd = cliArgs.Length > 1 ? cliArgs[1].ToLowerInvariant() : "status";
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        switch (subCmd)
        {
            case "enable":
            case "on":
            case "1":
            case "true":
            case "вкл":
            case "включить":
                if (isWindows)
                {
                    ProcessHelper.RunCommand("schtasks.exe", "/change /tn \"OpenFluxZenServer\" /enable >nul 2>&1");
                    ProcessHelper.RunCommand("sc.exe", "config OpenFluxZenServer start= auto >nul 2>&1");
                }
                else
                {
                    ProcessHelper.RunBash("systemctl enable openflux-zen-server.service 2>/dev/null || true");
                }
                await UpdateAutostartDbAsync(true);
                CliUi.Success(isRu 
                    ? "Автозапуск ВКЛЮЧЕН: OpenFlux Zen Server будет запускаться автоматически при загрузке системы." 
                    : "Autostart ENABLED: OpenFlux Zen Server will start automatically on system boot.");
                break;

            case "disable":
            case "off":
            case "0":
            case "false":
            case "выкл":
            case "выключить":
                if (isWindows)
                {
                    ProcessHelper.RunCommand("schtasks.exe", "/change /tn \"OpenFluxZenServer\" /disable >nul 2>&1");
                    ProcessHelper.RunCommand("sc.exe", "config OpenFluxZenServer start= demand >nul 2>&1");
                }
                else
                {
                    ProcessHelper.RunBash("systemctl disable openflux-zen-server.service 2>/dev/null || true");
                }
                await UpdateAutostartDbAsync(false);
                CliUi.Warn(isRu 
                    ? "Автозапуск ВЫКЛЮЧЕН: OpenFlux Zen Server не будет запускаться при загрузке системы." 
                    : "Autostart DISABLED: OpenFlux Zen Server will NOT start on system boot.");
                break;

            default:
                bool isEnabled = CheckAutostartEnabled(isWindows);
                CliUi.Header(isRu 
                    ? "OpenFlux Zen Server — Автозапуск" 
                    : "OpenFlux Zen Server — Autostart Configuration");

                CliUi.Field(isRu ? "Текущий статус:" : "Current Status:", 
                    isEnabled 
                        ? (isRu ? "ВКЛЮЧЕН (при загрузке системы)" : "ENABLED (on system boot)") 
                        : (isRu ? "ВЫКЛЮЧЕН" : "DISABLED"),
                    isEnabled ? ConsoleColor.Green : ConsoleColor.Yellow);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(isRu 
                    ? "  Для изменения выполните: openflux autostart enable | disable" 
                    : "  To change autostart, run: openflux autostart enable | disable");
                Console.ResetColor();

                Console.WriteLine();
                CliUi.Divider();
                Console.WriteLine();
                break;
        }
    }

    private static bool CheckAutostartEnabled(bool isWindows)
    {
        if (isWindows)
        {
            var (_, taskOut) = ProcessHelper.RunCommandWithOutput("schtasks.exe", "/query /tn \"OpenFluxZenServer\" /fo list");
            if (taskOut.Contains("OpenFluxZenServer", StringComparison.OrdinalIgnoreCase))
            {
                return !taskOut.Contains("Disabled", StringComparison.OrdinalIgnoreCase) && 
                       !taskOut.Contains("Отключено", StringComparison.OrdinalIgnoreCase);
            }
            var (_, scOut) = ProcessHelper.RunCommandWithOutput("sc.exe", "qc OpenFluxZenServer");
            return scOut.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var (_, outText) = ProcessHelper.RunBashWithOutput("systemctl is-enabled openflux-zen-server.service 2>/dev/null || true");
            return outText.Trim() == "enabled";
        }
    }

    private static string? GetConfiguredUrl()
    {
        var credPath = AppPaths.GetCredentialsPath();
        if (File.Exists(credPath))
        {
            try
            {
                var content = File.ReadAllText(credPath);
                var doc = JsonSerializer.Deserialize<JsonElement>(content);
                if (doc.TryGetProperty("publicUrl", out var p)) return p.GetString();
            }
            catch { }
        }

        var dbPath = AppPaths.GetDatabasePath();
        if (File.Exists(dbPath))
        {
            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
                using var db = new AppDbContext(options);
                var s = db.Settings.FirstOrDefault(x => x.Id == 1);
                if (s != null && !string.IsNullOrWhiteSpace(s.PublicUrl)) return s.PublicUrl;
            }
            catch { }
        }

        return null;
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
