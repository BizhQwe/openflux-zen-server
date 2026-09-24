using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Extensions.Logging;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Middleware;
using OpenFlux.Zen.Server.Models;
using OpenFlux.Zen.Server.Services;

// CLI Dispatcher: strictly supports only 3 commands: help, credentials, uninstall
if (args.Length > 0)
{
    var cmd = args[0].ToLowerInvariant().TrimStart('-', '/');
    switch (cmd)
    {
        case "help":
        case "h":
        case "?":
            PrintHelp();
            return;
        case "credentials":
        case "cred":
        case "creds":
            await PrintCredentialsAsync();
            return;
        case "uninstall":
            await ExecuteUninstallCliAsync(args);
            return;
        default:
            Console.WriteLine($"Unknown command: {args[0]}");
            PrintHelp();
            return;
    }
}

var builder = WebApplication.CreateBuilder(args);

// 1. Setup NLog
LogManager.Setup().LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config"));
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

// 2. Configure Host and Port
var listenPort = 5000;
if (int.TryParse(Environment.GetEnvironmentVariable("OPENFLUX_PORT"), out var envPort))
{
    listenPort = envPort;
}

var listenHost = Environment.GetEnvironmentVariable("OPENFLUX_HOST") ?? "127.0.0.1";
builder.WebHost.UseUrls($"http://{listenHost}:{listenPort}");

// 3. Register Database
var dbPath = Path.Combine(AppContext.BaseDirectory, "data", "openflux.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

// 4. Register Application Services
builder.Services.AddSingleton<IOpenFluxBinaryResolver, OpenFluxBinaryResolver>();
builder.Services.AddSingleton<ITunnelLogService, TunnelLogService>();
builder.Services.AddSingleton<ITunnelProcessSupervisor, TunnelProcessSupervisor>();
builder.Services.AddSingleton<ITunnelManager, TunnelManager>();
builder.Services.AddSingleton<ISystemStatsService, SystemStatsService>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<ISettingsService>(sp => (AuthService)sp.GetRequiredService<IAuthService>());
builder.Services.AddSingleton<IExportImportService, ExportImportService>();
builder.Services.AddSingleton<IUninstallerService, UninstallerService>();
builder.Services.AddHostedService<HostedRestoreService>();

// CORS & Routing
builder.Services.AddRouting();

var app = builder.Build();

// 5. Secret Path Middleware (Stealth Protection)
app.UseMiddleware<SecretPathMiddleware>();

// 6. Routing (must run after SecretPathMiddleware so routes match the rewritten Path!)
app.UseRouting();

// 7. Static Files (UI)
app.UseDefaultFiles();
app.UseStaticFiles();

// 7. API Routes

// Health
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Authentication
app.MapPost("/api/auth/login", async (LoginRequest req, IAuthService auth, HttpContext context) =>
{
    var (success, token, username) = await auth.LoginAsync(req.Username, req.Password);
    if (!success)
    {
        return Results.Json(new LoginResponse { Success = false, Message = "Invalid username or password" }, statusCode: 401);
    }

    context.Response.Cookies.Append("zen_auth_token", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps,
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });

    return Results.Ok(new LoginResponse { Success = true, Token = token, Username = username });
});

app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete("zen_auth_token");
    return Results.Ok(new { success = true });
});

app.MapGet("/api/auth/me", async (ISettingsService settingsService) =>
{
    var s = await settingsService.GetSettingsAsync();
    return Results.Ok(new { username = s.Username, secretPath = s.SecretPath, publicUrl = s.PublicUrl });
});

app.MapPost("/api/auth/change-password", async (ChangePasswordRequest req, IAuthService auth) =>
{
    var success = await auth.ChangePasswordAsync(req.CurrentPassword, req.NewPassword);
    return success ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Invalid current password" });
});

app.MapGet("/api/credentials", async (IAuthService auth) =>
{
    var creds = await auth.GetCredentialsAsync();
    return Results.Ok(creds);
});

// Settings
app.MapGet("/api/settings", async (ISettingsService settingsService) =>
{
    var s = await settingsService.GetSettingsAsync();
    return Results.Ok(s);
});

app.MapPost("/api/settings", async (AppSettings settings, ISettingsService settingsService) =>
{
    var updated = await settingsService.UpdateSettingsAsync(settings);
    return Results.Ok(updated);
});

app.MapPost("/api/settings/regenerate-secret", async (ISettingsService settingsService) =>
{
    var newSecret = await settingsService.RegenerateSecretPathAsync();
    return Results.Ok(new { secretPath = newSecret });
});

// Statistics
app.MapGet("/api/stats", async (ISystemStatsService statsService) =>
{
    var stats = await statsService.GetStatsAsync();
    return Results.Ok(stats);
});

// Tunnels CRUD & Actions
app.MapGet("/api/tunnels", async (ITunnelManager manager) =>
{
    var tunnels = await manager.GetAllAsync();
    return Results.Ok(tunnels);
});

app.MapGet("/api/tunnels/{id:guid}", async (Guid id, ITunnelManager manager) =>
{
    var tunnel = await manager.GetByIdAsync(id);
    return tunnel != null ? Results.Ok(tunnel) : Results.NotFound();
});

app.MapPost("/api/tunnels", async (Tunnel tunnel, ITunnelManager manager) =>
{
    var created = await manager.CreateAsync(tunnel);
    return Results.Created($"/api/tunnels/{created.Id}", created);
});

app.MapPut("/api/tunnels/{id:guid}", async (Guid id, Tunnel tunnel, ITunnelManager manager) =>
{
    tunnel.Id = id;
    var updated = await manager.UpdateAsync(tunnel);
    return updated != null ? Results.Ok(updated) : Results.NotFound();
});

app.MapDelete("/api/tunnels/{id:guid}", async (Guid id, ITunnelManager manager) =>
{
    var deleted = await manager.DeleteAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/tunnels/{id:guid}/start", async (Guid id, ITunnelManager manager) =>
{
    var started = await manager.StartAsync(id);
    return started ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Failed to start tunnel" });
});

app.MapPost("/api/tunnels/{id:guid}/stop", async (Guid id, ITunnelManager manager) =>
{
    var stopped = await manager.StopAsync(id);
    return stopped ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Failed to stop tunnel" });
});

app.MapPost("/api/tunnels/{id:guid}/toggle", async (Guid id, HttpContext context, ITunnelManager manager) =>
{
    bool enable = true;
    if (context.Request.Query.TryGetValue("enable", out var enableVal))
    {
        bool.TryParse(enableVal, out enable);
    }
    var success = await manager.ToggleEnableAsync(id, enable);
    return Results.Ok(new { success, isEnabled = enable });
});

app.MapPost("/api/tunnels/{id:guid}/reset-stats", async (Guid id, ITunnelManager manager) =>
{
    var success = await manager.ResetStatsAsync(id);
    return Results.Ok(new { success });
});

// Logs
app.MapGet("/api/tunnels/{id:guid}/logs", (Guid id, HttpContext context, ITunnelLogService logService) =>
{
    int tail = 200;
    if (context.Request.Query.TryGetValue("tail", out var tailVal) && int.TryParse(tailVal, out var parsed))
    {
        tail = Math.Min(parsed, 1000);
    }
    var logs = logService.GetRecentLogs(id, tail);
    return Results.Ok(logs);
});

app.MapDelete("/api/tunnels/{id:guid}/logs", (Guid id, ITunnelLogService logService) =>
{
    logService.ClearLogs(id);
    return Results.NoContent();
});

app.MapGet("/api/logs/system", (HttpContext context, ITunnelLogService logService) =>
{
    int tail = 200;
    if (context.Request.Query.TryGetValue("tail", out var tailVal) && int.TryParse(tailVal, out var parsed))
    {
        tail = Math.Min(parsed, 1000);
    }
    var logs = logService.GetSystemLogs(tail);
    return Results.Ok(logs);
});

// Configuration Export / Import
app.MapGet("/api/config/export", async (IExportImportService exportImport) =>
{
    var json = await exportImport.ExportConfigurationJsonAsync();
    return Results.Content(json, "application/json; charset=utf-8");
});

app.MapPost("/api/config/import", async (HttpContext context, IExportImportService exportImport) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var json = await reader.ReadToEndAsync();
    var (imported, errors, message) = await exportImport.ImportConfigurationJsonAsync(json);
    return Results.Ok(new { imported, errors, message });
});

// Complete System Uninstall
app.MapPost("/api/system/uninstall", async (ChangePasswordRequest req, IAuthService auth, IUninstallerService uninstaller) =>
{
    var settings = await auth.GetCredentialsAsync();
    if (!AuthService.VerifyPassword(req.CurrentPassword, (await ((ISettingsService)auth).GetSettingsAsync()).PasswordHash, (await ((ISettingsService)auth).GetSettingsAsync()).PasswordSalt))
    {
        return Results.BadRequest(new { error = "Invalid password confirmation for uninstallation" });
    }

    _ = Task.Run(async () => await uninstaller.TriggerUninstallAsync());
    return Results.Ok(new { success = true, message = "Uninstallation initiated. Server is terminating." });
});

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

// -------------------------------------------------------------
// CLI Helper Functions (help, credentials, uninstall)
// -------------------------------------------------------------
static void PrintHelp()
{
    Console.WriteLine(@"
OpenFlux Zen Server CLI
Usage: OpenFluxZenServer <command>

Commands:
  help          Show this help message
  credentials   Display current login credentials and panel URL
  uninstall     Completely remove OpenFlux Zen Server from the system
");
}

static async Task PrintCredentialsAsync()
{
    var candidates = new List<string>
    {
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory(),
        Path.Combine(AppContext.BaseDirectory, "..", "..", ".."),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "OpenFlux.Zen.Server"),
        "/opt/openflux-zen-server/app",
        "/opt/openflux-zen-server",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenFluxZenServer")
    };

    string? credsFile = null;
    string? dbPath = null;

    foreach (var dir in candidates)
    {
        var cf = Path.Combine(dir, "data", ".credentials");
        if (File.Exists(cf) && credsFile == null) credsFile = cf;

        var db = Path.Combine(dir, "data", "openflux.db");
        if (File.Exists(db) && dbPath == null) dbPath = db;
    }

    string username = "admin";
    string password = "unknown";
    string secretPath = "";
    string? publicUrl = null;
    int port = 5000;

    if (credsFile != null && File.Exists(credsFile))
    {
        try
        {
            var content = await File.ReadAllTextAsync(credsFile);
            var doc = JsonSerializer.Deserialize<JsonElement>(content);
            if (doc.TryGetProperty("username", out var u)) username = u.GetString() ?? username;
            if (doc.TryGetProperty("password", out var p)) password = p.GetString() ?? password;
            if (doc.TryGetProperty("secretPath", out var s)) secretPath = s.GetString() ?? secretPath;
            if (doc.TryGetProperty("publicUrl", out var pub)) publicUrl = pub.GetString();
        }
        catch { }
    }

    if (string.IsNullOrEmpty(secretPath) && File.Exists(dbPath))
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

    if (string.IsNullOrEmpty(secretPath))
    {
        secretPath = "zen-admin";
    }

    var localUrl = $"http://127.0.0.1:{port}/{secretPath.Trim('/')}/";
    var finalUrl = !string.IsNullOrWhiteSpace(publicUrl) ? publicUrl : localUrl;

    Console.WriteLine("==================================================");
    Console.WriteLine("        OpenFlux Zen Server - Credentials         ");
    Console.WriteLine("==================================================");
    Console.WriteLine($"Panel URL:    {finalUrl}");
    Console.WriteLine($"Local URL:    {localUrl}");
    Console.WriteLine($"Secret Path:  /{secretPath.Trim('/')}/");
    Console.WriteLine($"Username:     {username}");
    Console.WriteLine($"Password:     {password}");
    Console.WriteLine("==================================================");
}

static async Task ExecuteUninstallCliAsync(string[] cliArgs)
{
    bool force = cliArgs.Any(a => a == "-y" || a == "--yes" || a == "--force");
    if (!force)
    {
        Console.Write("Are you sure you want to completely uninstall OpenFlux Zen Server? [y/N]: ");
        var ans = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (ans != "y" && ans != "yes")
        {
            Console.WriteLine("Uninstallation cancelled.");
            return;
        }
    }

    Console.WriteLine("[INFO] Stopping OpenFlux Zen Server services and processes...");
    var baseDir = AppContext.BaseDirectory;
    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    if (isWindows)
    {
        Process.Start(new ProcessStartInfo { FileName = "sc.exe", Arguments = "stop OpenFluxZenServer", UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);
        Process.Start(new ProcessStartInfo { FileName = "sc.exe", Arguments = "delete OpenFluxZenServer", UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);
        Process.Start(new ProcessStartInfo { FileName = "schtasks.exe", Arguments = "/delete /tn \"OpenFluxZenServer\" /f", UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);

        try
        {
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var binDir = Path.Combine(baseDir, "cli");
            if (userPath.Contains(binDir))
            {
                var newPath = string.Join(";", userPath.Split(';').Where(p => !p.Equals(binDir, StringComparison.OrdinalIgnoreCase) && !p.Equals(baseDir, StringComparison.OrdinalIgnoreCase)));
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
            }
        }
        catch { }

        Console.WriteLine("[INFO] Cleaning up application files (preserving SSL certificates)...");
        var runnerBat = Path.Combine(Path.GetTempPath(), "oflux_uninstall.bat");
        await File.WriteAllTextAsync(runnerBat, $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /f /im OpenFlux.Zen.Server.exe >nul 2>&1
rd /s /q ""{baseDir}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
");
        Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"{runnerBat}\"", UseShellExecute = false, CreateNoWindow = true });
    }
    else
    {
        RunBash("systemctl stop openflux-zen-server 2>/dev/null || true");
        RunBash("systemctl stop openflux-zrok 2>/dev/null || true");
        RunBash("systemctl disable openflux-zen-server 2>/dev/null || true");
        RunBash("systemctl disable openflux-zrok 2>/dev/null || true");
        RunBash("rm -f /etc/systemd/system/openflux-zen-server.service /etc/systemd/system/openflux-zrok.service");
        RunBash("systemctl daemon-reload 2>/dev/null || true");
        RunBash("rm -f /usr/local/bin/OpenFluxZenServer /usr/bin/OpenFluxZenServer");

        Console.WriteLine("[INFO] Cleaning up application files (preserving SSL certificates)...");
        var runnerSh = Path.Combine(Path.GetTempPath(), "oflux_uninstall.sh");
        await File.WriteAllTextAsync(runnerSh, $@"#!/bin/bash
sleep 1
rm -rf ""{baseDir}""
rm -f ""$0""
");
        RunBash($"chmod +x \"{runnerSh}\" && nohup \"{runnerSh}\" >/dev/null 2>&1 &");
    }

    Console.WriteLine("[SUCCESS] OpenFlux Zen Server has been completely uninstalled from the system.");
}

static void RunBash(string cmd)
{
    try
    {
        var psi = new ProcessStartInfo { FileName = "bash", Arguments = $"-c \"{cmd}\"", UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }
    catch { }
}
