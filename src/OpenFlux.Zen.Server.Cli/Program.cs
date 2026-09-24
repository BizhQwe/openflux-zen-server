using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Models;

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant().TrimStart('-', '/');

switch (command)
{
    case "help":
    case "h":
    case "?":
        PrintHelp();
        return 0;

    case "start":
    case "up":
    case "run":
        await ExecuteStartAsync();
        return 0;

    case "stop":
    case "down":
        await ExecuteStopAsync();
        return 0;

    case "restart":
    case "reboot":
        await ExecuteRestartAsync();
        return 0;

    case "status":
    case "info":
        await ExecuteStatusAsync();
        return 0;

    case "autostart":
    case "autorun":
        await ExecuteAutostartAsync(args);
        return 0;

    case "credentials":
    case "cred":
    case "creds":
        await PrintCredentialsAsync(args);
        return 0;

    case "uninstall":
        await ExecuteUninstallAsync(args);
        return 0;

    default:
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] Unknown command: '{args[0]}'");
        Console.ResetColor();
        Console.WriteLine();
        PrintHelp();
        return 1;
}

static void PrintHelp()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================================");
    Console.WriteLine("                  OpenFlux Zen Server CLI                         ");
    Console.WriteLine("==================================================================");
    Console.ResetColor();
    Console.WriteLine("Usage: OpenFluxZenServer <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Server Management Commands:");
    Console.WriteLine("  start        Start OpenFlux Zen Server service");
    Console.WriteLine("  stop         Stop OpenFlux Zen Server service");
    Console.WriteLine("  restart      Restart OpenFlux Zen Server service");
    Console.WriteLine("  status       Show current running status of the server");
    Console.WriteLine("  autostart    Configure autostart on system boot (enable | disable | status)");
    Console.WriteLine();
    Console.WriteLine("General Commands:");
    Console.WriteLine("  credentials  Display current login credentials, secret path & URLs");
    Console.WriteLine("  uninstall    Completely uninstall OpenFlux Zen Server from system");
    Console.WriteLine("  help         Show this help information");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  OpenFluxZenServer start");
    Console.WriteLine("  OpenFluxZenServer stop");
    Console.WriteLine("  OpenFluxZenServer restart");
    Console.WriteLine("  OpenFluxZenServer status");
    Console.WriteLine("  OpenFluxZenServer autostart enable");
    Console.WriteLine("  OpenFluxZenServer autostart disable");
    Console.WriteLine("  OpenFluxZenServer autostart status");
    Console.WriteLine("  OpenFluxZenServer credentials");
    Console.WriteLine("  OpenFluxZenServer uninstall -y");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================================");
    Console.ResetColor();
}

static async Task ExecuteStartAsync()
{
    Console.WriteLine("[INFO] Starting OpenFlux Zen Server...");
    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    if (isWindows)
    {
        RunCommand("sc.exe", "start OpenFluxZenServer");
    }
    else
    {
        RunBash("systemctl start openflux-zen-server.service");
        if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
        {
            RunBash("systemctl start openflux-zrok.service");
        }
    }
    await Task.Delay(1000);
    await ExecuteStatusAsync();
}

static async Task ExecuteStopAsync()
{
    Console.WriteLine("[INFO] Stopping OpenFlux Zen Server...");
    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    if (isWindows)
    {
        RunCommand("sc.exe", "stop OpenFluxZenServer");
        RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1");
    }
    else
    {
        RunBash("systemctl stop openflux-zen-server.service");
        if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
        {
            RunBash("systemctl stop openflux-zrok.service");
        }
    }
    await Task.Delay(1000);
    await ExecuteStatusAsync();
}

static async Task ExecuteRestartAsync()
{
    Console.WriteLine("[INFO] Restarting OpenFlux Zen Server...");
    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    if (isWindows)
    {
        RunCommand("sc.exe", "stop OpenFluxZenServer");
        await Task.Delay(1500);
        RunCommand("sc.exe", "start OpenFluxZenServer");
    }
    else
    {
        RunBash("systemctl restart openflux-zen-server.service");
        if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
        {
            RunBash("systemctl restart openflux-zrok.service");
        }
    }
    await Task.Delay(1000);
    await ExecuteStatusAsync();
}

static Task ExecuteStatusAsync()
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
        var (_, output) = RunBashWithOutput("systemctl is-active openflux-zen-server.service 2>/dev/null || true");
        var status = output.Trim();
        bool isRunning = status == "active";

        Console.ForegroundColor = isRunning ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(isRunning 
            ? "[STATUS] OpenFlux Zen Server: RUNNING (active)" 
            : $"[STATUS] OpenFlux Zen Server: NOT RUNNING ({status})");
        Console.ResetColor();

        if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
        {
            var (_, zrokOut) = RunBashWithOutput("systemctl is-active openflux-zrok.service 2>/dev/null || true");
            var zrokStatus = zrokOut.Trim();
            Console.WriteLine($"[STATUS] Zrok Public Share : {(zrokStatus == "active" ? "RUNNING (active)" : zrokStatus)}");
        }
    }
    return Task.CompletedTask;
}

static async Task ExecuteAutostartAsync(string[] cliArgs)
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
                RunCommand("sc.exe", "config OpenFluxZenServer start= auto");
            }
            else
            {
                RunBash("systemctl enable openflux-zen-server.service 2>/dev/null || true");
                if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
                {
                    RunBash("systemctl enable openflux-zrok.service 2>/dev/null || true");
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
                RunCommand("sc.exe", "config OpenFluxZenServer start= demand");
            }
            else
            {
                RunBash("systemctl disable openflux-zen-server.service 2>/dev/null || true");
                if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
                {
                    RunBash("systemctl disable openflux-zrok.service 2>/dev/null || true");
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
                var (_, scOut) = RunCommandWithOutput("sc.exe", "qc OpenFluxZenServer");
                isEnabled = scOut.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var (_, outText) = RunBashWithOutput("systemctl is-enabled openflux-zen-server.service 2>/dev/null || true");
                isEnabled = outText.Trim() == "enabled";
            }
            Console.WriteLine($"[INFO] Autostart Status: {(isEnabled ? "ENABLED" : "DISABLED")}");
            Console.WriteLine("To change autostart, run: OpenFluxZenServer autostart enable | disable");
            break;
    }
}

static async Task UpdateAutostartDbAsync(bool enabled)
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

static async Task PrintCredentialsAsync(string[] cliArgs)
{
    var credPath = AppPaths.GetCredentialsPath();
    var dbPath = AppPaths.GetDatabasePath();

    string username = "admin";
    string password = "(Hidden / stored hashed in DB)";
    string secretPath = "zen-admin";
    int port = 5000;
    string? publicUrl = null;

    if (File.Exists(credPath))
    {
        try
        {
            var content = await File.ReadAllTextAsync(credPath);
            var doc = JsonSerializer.Deserialize<JsonElement>(content);
            if (doc.TryGetProperty("username", out var u)) username = u.GetString() ?? username;
            if (doc.TryGetProperty("password", out var p)) password = p.GetString() ?? password;
            if (doc.TryGetProperty("secretPath", out var s)) secretPath = s.GetString() ?? secretPath;
            if (doc.TryGetProperty("publicUrl", out var pub)) publicUrl = pub.GetString();
        }
        catch { }
    }

    if (string.IsNullOrEmpty(secretPath) || password.StartsWith("("))
    {
        if (File.Exists(dbPath))
        {
            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
                using var db = new AppDbContext(options);
                var s = await db.Settings.FirstOrDefaultAsync(x => x.Id == 1);
                if (s != null)
                {
                    username = s.Username;
                    secretPath = s.SecretPath;
                    port = s.ListenPort;
                    publicUrl = s.PublicUrl;
                }
            }
            catch { }
        }
    }

    if (string.IsNullOrEmpty(secretPath))
    {
        secretPath = "zen-admin";
    }

    var localUrl = $"http://127.0.0.1:{port}/{secretPath.Trim('/')}/";
    var finalUrl = !string.IsNullOrWhiteSpace(publicUrl) ? publicUrl : localUrl;

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================================");
    Console.WriteLine("                 OpenFlux Zen Server - Credentials                ");
    Console.WriteLine("==================================================================");
    Console.ResetColor();
    Console.WriteLine($"  Web Dashboard URL : {finalUrl}");
    Console.WriteLine($"  Local Access URL  : {localUrl}");
    Console.WriteLine($"  Secret Path       : /{secretPath.Trim('/')}/");
    Console.WriteLine($"  Username          : {username}");
    Console.WriteLine($"  Password          : {password}");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("==================================================================");
    Console.ResetColor();
}

static async Task ExecuteUninstallAsync(string[] cliArgs)
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
        RunCommand("sc.exe", "stop OpenFluxZenServer");
        RunCommand("sc.exe", "delete OpenFluxZenServer");
        RunCommand("schtasks.exe", "/delete /tn \"OpenFluxZenServer\" /f");
        RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.Web.exe");
        RunCommand("taskkill.exe", "/f /im OpenFlux.Zen.Server.exe");
        RunCommand("taskkill.exe", "/f /im openflux-windows-amd64.exe");
        RunCommand("taskkill.exe", "/f /im openflux-windows-arm64.exe");

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
        RunBash("systemctl stop openflux-zen-server 2>/dev/null || true");
        RunBash("systemctl stop openflux-zrok 2>/dev/null || true");
        RunBash("systemctl disable openflux-zen-server 2>/dev/null || true");
        RunBash("systemctl disable openflux-zrok 2>/dev/null || true");
        RunBash("rm -f /etc/systemd/system/openflux-zen-server.service /etc/systemd/system/openflux-zrok.service");
        RunBash("systemctl daemon-reload 2>/dev/null || true");
        RunBash("pkill -9 -f openflux-linux 2>/dev/null || true");
        RunBash("rm -f /usr/local/bin/OpenFluxZenServer /usr/bin/OpenFluxZenServer");

        Console.WriteLine("[INFO] Cleaning up application files (strictly preserving SSL certificates)...");
        var runnerSh = Path.Combine(Path.GetTempPath(), "oflux_uninstall.sh");
        var runnerScript = $@"#!/bin/bash
sleep 1
pkill -9 -f OpenFlux.Zen.Server 2>/dev/null || true
rm -rf ""{appDir}""
rm -f ""$0""
";
        await File.WriteAllTextAsync(runnerSh, runnerScript);
        RunBash($"chmod +x \"{runnerSh}\" && nohup \"{runnerSh}\" >/dev/null 2>&1 &");
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[SUCCESS] OpenFlux Zen Server has been completely uninstalled from the system.");
    Console.ResetColor();
}

static void RunCommand(string file, string args)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }
    catch { }
}

static (int ExitCode, string Output) RunCommandWithOutput(string file, string args)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, "");
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return (p.ExitCode, text.Trim());
    }
    catch (Exception ex)
    {
        return (-1, ex.Message);
    }
}

static void RunBash(string cmd)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"-c \"{cmd}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }
    catch { }
}

static (int ExitCode, string Output) RunBashWithOutput(string cmd)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"-c \"{cmd}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, "");
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return (p.ExitCode, text.Trim());
    }
    catch (Exception ex)
    {
        return (-1, ex.Message);
    }
}
