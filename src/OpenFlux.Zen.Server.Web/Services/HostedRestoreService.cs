using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public sealed class HostedRestoreService : IHostedService
{
    private readonly ILogger<HostedRestoreService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITunnelManager _tunnelManager;
    private readonly ISettingsService _settingsService;

    public HostedRestoreService(
        ILogger<HostedRestoreService> logger,
        IServiceScopeFactory scopeFactory,
        ITunnelManager tunnelManager,
        ISettingsService settingsService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _tunnelManager = tunnelManager;
        _settingsService = settingsService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing OpenFlux Zen Server database and state recovery...");

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Clear any stale migration lock left by interrupted processes in SQLite
            try
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsLock\";", cancellationToken);
            }
            catch { }

            // Apply migrations or ensure schema is created
            await db.Database.MigrateAsync(cancellationToken);

            // Ensure settings and admin account exist
            var currentSettings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
            if (currentSettings == null)
            {
                await _settingsService.GetSettingsAsync();
                currentSettings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
            }

            if (currentSettings != null)
            {
                // Synchronize SecretPath from environment if specified and different
                var envSecret = Environment.GetEnvironmentVariable("OPENFLUX_SECRET_PATH");
                if (!string.IsNullOrWhiteSpace(envSecret) && !string.Equals(currentSettings.SecretPath, envSecret.Trim('/'), StringComparison.OrdinalIgnoreCase))
                {
                    currentSettings.SecretPath = envSecret.Trim('/');
                    currentSettings.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation("Synchronized secret access path from environment: /{SecretPath}/", currentSettings.SecretPath);
                }

                // Synchronize PublicUrl from environment if specified
                var envPublicUrl = Environment.GetEnvironmentVariable("OPENFLUX_PUBLIC_URL");
                if (!string.IsNullOrWhiteSpace(envPublicUrl) && !string.Equals(currentSettings.PublicUrl, envPublicUrl, StringComparison.OrdinalIgnoreCase))
                {
                    currentSettings.PublicUrl = envPublicUrl;
                    currentSettings.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation("Synchronized public URL from environment: {PublicUrl}", currentSettings.PublicUrl);
                }

                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Panel secret access path: /{SecretPath}/", currentSettings.SecretPath);
            }

            // Recover ONLY tunnels that were enabled before stop
            var enabledTunnels = await db.Tunnels
                .Where(t => t.IsEnabled)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} enabled tunnels to restore on host startup", enabledTunnels.Count);

            foreach (var t in enabledTunnels)
            {
                _logger.LogInformation("Auto-restoring enabled tunnel: {Name} (ID: {Id})", t.Name, t.Id);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _tunnelManager.StartAsync(t.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-restore tunnel {Name}", t.Name);
                    }
                }, cancellationToken);
            }
        }

        _logger.LogInformation("OpenFlux Zen Server initialization complete.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down OpenFlux Zen Server...");
        return Task.CompletedTask;
    }
}
