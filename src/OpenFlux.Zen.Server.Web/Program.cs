using NLog;
using NLog.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Middleware;
using OpenFlux.Zen.Server.Services;
using OpenFlux.Zen.Server.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// 1. Setup Logging
LogManager.Setup().LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config"));
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

// 2. Configure Host and Port
var listenPort = 5000;
string? credHost = null;
string? credMode = null;
var credPath = AppPaths.GetCredentialsPath();
if (File.Exists(credPath))
{
    try
    {
        var credJson = File.ReadAllText(credPath);
        using var doc = System.Text.Json.JsonDocument.Parse(credJson);
        if (doc.RootElement.TryGetProperty("host", out var h)) credHost = h.GetString();
        if (doc.RootElement.TryGetProperty("port", out var p) && p.TryGetInt32(out var pt)) listenPort = pt;
        if (doc.RootElement.TryGetProperty("publishMode", out var pm)) credMode = pm.GetString();
    }
    catch { }
}

if (int.TryParse(Environment.GetEnvironmentVariable("OPENFLUX_PORT"), out var envPort))
{
    listenPort = envPort;
}

var envMode = Environment.GetEnvironmentVariable("OPENFLUX_PUBLISH_MODE");
var mode = !string.IsNullOrWhiteSpace(credMode) ? credMode : (!string.IsNullOrWhiteSpace(envMode) ? envMode : "local");

// Default to 0.0.0.0 for all network modes (domain, localtunnel, local/lan) so LAN/Wi-Fi and phones work.
// Only bind strictly to 127.0.0.1 when localhost-only mode is explicitly selected.
string listenHost;
if (string.Equals(mode, "localhost", StringComparison.OrdinalIgnoreCase))
{
    listenHost = "127.0.0.1";
}
else
{
    listenHost = (!string.IsNullOrWhiteSpace(credHost) && credHost != "127.0.0.1") ? credHost : "0.0.0.0";
}

builder.WebHost.UseUrls($"http://{listenHost}:{listenPort}");

// 3. Register Database
var dbPath = AppPaths.GetDatabasePath();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}", b => b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// 4. Register Application Services (IoC / Dependency Injection)
builder.Services.AddSingleton<IOpenFluxBinaryResolver, OpenFluxBinaryResolver>();
builder.Services.AddSingleton<IOpenFluxCoreUpdateService, OpenFluxCoreUpdateService>();
builder.Services.AddSingleton<IPanelUpdateService, PanelUpdateService>();
builder.Services.AddSingleton<ITunnelLogService, TunnelLogService>();
builder.Services.AddSingleton<ITunnelProcessSupervisor, TunnelProcessSupervisor>();
builder.Services.AddSingleton<ITunnelManager, TunnelManager>();
builder.Services.AddSingleton<ISystemStatsService, SystemStatsService>();
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IExportImportService, ExportImportService>();
builder.Services.AddSingleton<IUninstallerService, UninstallerService>();
builder.Services.AddHostedService<HostedRestoreService>();
builder.Services.AddHostedService<LocaltunnelService>();

builder.Services.AddRouting();

var app = builder.Build();

// 5. Secret Path Middleware (Stealth Protection)
app.UseMiddleware<SecretPathMiddleware>();

// 6. Routing & Static Files (Strict No-Cache to ensure instant UI updates)
app.Use(async (context, next) =>
{
    var p = context.Request.Path.Value ?? "";
    if (p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        p.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        p.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
        p == "/" || string.IsNullOrEmpty(p))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "-1";
    }
    await next();
});

app.UseRouting();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
        ctx.Context.Response.Headers["Expires"] = "-1";
    }
});

// 7. Modular Endpoints (SOLID - Single Responsibility & Separation of Concerns)
app.MapAuthEndpoints();
app.MapTunnelEndpoints();
app.MapLogEndpoints();
app.MapSettingsEndpoints();
app.MapSystemEndpoints();

// 8. SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();
