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
if (int.TryParse(Environment.GetEnvironmentVariable("OPENFLUX_PORT"), out var envPort))
{
    listenPort = envPort;
}
var listenHost = Environment.GetEnvironmentVariable("OPENFLUX_HOST") ?? "127.0.0.1";
builder.WebHost.UseUrls($"http://{listenHost}:{listenPort}");

// 3. Register Database
var dbPath = AppPaths.GetDatabasePath();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

// 4. Register Application Services (IoC / Dependency Injection)
builder.Services.AddSingleton<IOpenFluxBinaryResolver, OpenFluxBinaryResolver>();
builder.Services.AddSingleton<ITunnelLogService, TunnelLogService>();
builder.Services.AddSingleton<ITunnelProcessSupervisor, TunnelProcessSupervisor>();
builder.Services.AddSingleton<ITunnelManager, TunnelManager>();
builder.Services.AddSingleton<ISystemStatsService, SystemStatsService>();
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IExportImportService, ExportImportService>();
builder.Services.AddSingleton<IUninstallerService, UninstallerService>();
builder.Services.AddHostedService<HostedRestoreService>();

builder.Services.AddRouting();

var app = builder.Build();

// 5. Secret Path Middleware (Stealth Protection)
app.UseMiddleware<SecretPathMiddleware>();

// 6. Routing & Static Files
app.UseRouting();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");
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
