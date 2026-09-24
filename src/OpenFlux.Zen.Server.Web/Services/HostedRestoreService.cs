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

            // Apply migrations or ensure schema is created
            await db.Database.MigrateAsync(cancellationToken);

            // Ensure settings and admin account exist
            var settings = await _settingsService.GetSettingsAsync();
            _logger.LogInformation("Panel secret access path: /{SecretPath}/", settings.SecretPath);

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
