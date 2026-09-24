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
var dbPath = OpenFlux.Zen.Server.Common.AppPaths.GetDatabasePath();
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

    var isHttps = context.Request.IsHttps ||
                  string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

    context.Response.Cookies.Append("zen_auth_token", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Secure = isHttps,
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });

    return Results.Ok(new LoginResponse { Success = true, Token = token, Username = username });
});

app.MapPost("/api/auth/logout", (IAuthService auth, HttpContext context) =>
{
    var token = SecretPathMiddleware.ExtractToken(context);
    if (!string.IsNullOrWhiteSpace(token))
    {
        auth.RevokeToken(token);
    }
    context.Response.Cookies.Delete("zen_auth_token", new CookieOptions { Path = "/" });
    return Results.Ok(new { success = true });
});

app.MapGet("/api/auth/me", async (ISettingsService settingsService) =>
{
    var s = await settingsService.GetSettingsAsync();
    return Results.Ok(new { username = s.Username, secretPath = s.SecretPath, publicUrl = s.PublicUrl });
});

app.MapPost("/api/auth/change-profile", async (ChangePasswordRequest req, IAuthService auth) =>
{
    var (success, msg, newUsername) = await auth.ChangeProfileAsync(req.CurrentPassword, req.NewUsername, req.NewPassword);
    return success ? Results.Ok(new { success = true, message = msg, username = newUsername }) : Results.BadRequest(new { error = msg });
});

app.MapPost("/api/auth/change-password", async (ChangePasswordRequest req, IAuthService auth) =>
{
    var (success, msg, newUsername) = await auth.ChangeProfileAsync(req.CurrentPassword, req.NewUsername, req.NewPassword);
    return success ? Results.Ok(new { success = true, message = msg, username = newUsername }) : Results.BadRequest(new { error = msg });
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

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

