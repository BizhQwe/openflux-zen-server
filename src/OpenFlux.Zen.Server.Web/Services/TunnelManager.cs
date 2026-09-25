using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface ITunnelManager
{
    Task<IReadOnlyList<Tunnel>> GetAllAsync();
    Task<Tunnel?> GetByIdAsync(Guid id);
    Task<Tunnel> CreateAsync(Tunnel tunnel);
    Task<Tunnel?> UpdateAsync(Tunnel tunnel);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> StartAsync(Guid id);
    Task<bool> StopAsync(Guid id);
    Task<bool> ToggleEnableAsync(Guid id, bool isEnabled);
    Task<bool> ResetStatsAsync(Guid id);
}

public sealed class TunnelManager : ITunnelManager
{
    private readonly ILogger<TunnelManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITunnelProcessSupervisor _supervisor;
    private readonly ITunnelLogService _logService;
    private readonly ConcurrentDictionary<Guid, Tunnel> _liveTunnels = new();
    private readonly ConcurrentDictionary<Guid, (long LastUp, long LastDown, DateTime LastTime)> _tunnelRateTrackers = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _isSynced;
    private readonly Timer _statsPersistTimer;
    private readonly Timer _rateDecayTimer;

    public TunnelManager(
        ILogger<TunnelManager> logger,
        IServiceScopeFactory scopeFactory,
        ITunnelProcessSupervisor supervisor,
        ITunnelLogService logService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _supervisor = supervisor;
        _logService = logService;

        // Persist accumulated traffic to DB every 15 seconds
        _statsPersistTimer = new Timer(async _ => await PersistStatsToDbAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        // Decay rates to 0 if no traffic received for > 1.2s
        _rateDecayTimer = new Timer(_ => DecayRates(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public async Task<IReadOnlyList<Tunnel>> GetAllAsync()
    {
        await SyncFromDbIfEmptyAsync();
        return _liveTunnels.Values.OrderBy(t => t.Name).ToList();
    }

    public async Task<Tunnel?> GetByIdAsync(Guid id)
    {
        await SyncFromDbIfEmptyAsync();
        _liveTunnels.TryGetValue(id, out var t);
        return t;
    }

    public async Task<Tunnel> CreateAsync(Tunnel tunnel)
    {
        await _lock.WaitAsync();
        try
        {
            tunnel.Id = Guid.NewGuid();
            tunnel.CreatedAt = DateTime.UtcNow;
            tunnel.UpdatedAt = DateTime.UtcNow;
            tunnel.Status = TunnelStatus.Stopped;
            tunnel.UploadBytes = 0;
            tunnel.DownloadBytes = 0;
            tunnel.ConnectedClients = 0;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Tunnels.Add(tunnel);
                await db.SaveChangesAsync();
            }

            _liveTunnels[tunnel.Id] = tunnel;
            _logger.LogInformation("Created new tunnel: {Name} (ID: {Id})", tunnel.Name, tunnel.Id);

            if (tunnel.IsEnabled)
            {
                _ = Task.Run(async () => await StartAsync(tunnel.Id));
            }

            return tunnel;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Tunnel?> UpdateAsync(Tunnel updated)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_liveTunnels.TryGetValue(updated.Id, out var existing))
            {
                return null;
            }

            var wasRunning = _supervisor.IsRunning(existing.Id);
            if (wasRunning)
            {
                existing.Status = TunnelStatus.Stopping;
                await _supervisor.StopTunnelAsync(existing.Id);
            }

            existing.Name = updated.Name;
            existing.Role = "exit";
            existing.Transport = updated.Transport;
            existing.Inbound = updated.Inbound;
            existing.Socks5Address = updated.Socks5Address;
            existing.Mode = updated.Mode;
            existing.Url = updated.Url;
            existing.MaxToken = updated.MaxToken;
            existing.MaxUid = updated.MaxUid;
            existing.LocalIp = updated.LocalIp;
            existing.Codec = updated.Codec;
            existing.EncryptionKey = updated.EncryptionKey;
            existing.BenchBytes = updated.BenchBytes;
            existing.BenchCompressible = updated.BenchCompressible;
            existing.ExtraArgs = updated.ExtraArgs;
            existing.ClientLimit = updated.ClientLimit;
            existing.TrafficLimitBytes = updated.TrafficLimitBytes;
            existing.IsEnabled = updated.IsEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ErrorMessage = null;
            existing.RestartAttempts = 0;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Tunnels.Update(existing);
                await db.SaveChangesAsync();
            }

            if (existing.IsEnabled)
            {
                var started = await StartAsync(existing.Id);
                if (!started)
                {
                    _logger.LogWarning("Tunnel {Name} ({Id}) failed to restart after settings update", existing.Name, existing.Id);
                }
            }
            else
            {
                existing.Status = TunnelStatus.Stopped;
            }

            return existing;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void DecayRates()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, tracker) in _tunnelRateTrackers)
        {
            var elapsedMs = (now - tracker.LastTime).TotalMilliseconds;
            if (elapsedMs >= 1200)
            {
                if (_liveTunnels.TryGetValue(id, out var t))
                {
                    if (t.UploadRateBytesPerSec > 0 || t.DownloadRateBytesPerSec > 0)
                    {
                        t.UploadRateBytesPerSec = 0;
                        t.DownloadRateBytesPerSec = 0;
                    }
                    if (elapsedMs >= 25000 && t.ConnectedClients > 0)
                    {
                        t.ConnectedClients = 0;
                    }
                }
            }
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            if (_liveTunnels.TryRemove(id, out var tunnel))
            {
                await _supervisor.StopTunnelAsync(id);
                _logService.ClearLogs(id);

                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var entity = await db.Tunnels.FindAsync(id);
                    if (entity != null)
                    {
                        db.Tunnels.Remove(entity);
                        await db.SaveChangesAsync();
                    }
                }
                _logger.LogInformation("Deleted tunnel: {Id}", id);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> StartAsync(Guid id)
    {
        await SyncFromDbIfEmptyAsync();
        if (!_liveTunnels.TryGetValue(id, out var tunnel))
        {
            return false;
        }

        if (tunnel.TrafficLimitBytes > 0 && (tunnel.UploadBytes + tunnel.DownloadBytes) >= tunnel.TrafficLimitBytes)
        {
            _logger.LogWarning("Cannot start tunnel {Name}: traffic limit reached", tunnel.Name);
            tunnel.Status = TunnelStatus.Stopped;
            tunnel.ErrorMessage = "Traffic limit reached";
            return false;
        }

        tunnel.Status = TunnelStatus.Starting;
        tunnel.ErrorMessage = null;

        var success = await _supervisor.StartTunnelAsync(
            tunnel,
            OnStatsUpdateAsync,
            OnProcessExitedAsync);

        if (success)
        {
            tunnel.Status = TunnelStatus.Running;
            tunnel.IsEnabled = true;
            tunnel.LastStartedAt = DateTime.UtcNow;
            tunnel.RestartAttempts = 0;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Tunnels.Where(t => t.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsEnabled, true)
                    .SetProperty(t => t.LastStartedAt, tunnel.LastStartedAt));
            }
            _logger.LogInformation("Tunnel {Name} ({Id}) started successfully", tunnel.Name, tunnel.Id);
            return true;
        }
        else
        {
            tunnel.Status = TunnelStatus.Failed;
            tunnel.ErrorMessage = "Process failed to start";
            _logger.LogError("Failed to start tunnel {Name} ({Id})", tunnel.Name, tunnel.Id);
            return false;
        }
    }

    public async Task<bool> StopAsync(Guid id)
    {
        await SyncFromDbIfEmptyAsync();
        if (!_liveTunnels.TryGetValue(id, out var tunnel))
        {
            return false;
        }

        tunnel.Status = TunnelStatus.Stopping;
        await _supervisor.StopTunnelAsync(id);
        tunnel.Status = TunnelStatus.Stopped;
        tunnel.IsEnabled = false;
        tunnel.LastStoppedAt = DateTime.UtcNow;
        tunnel.ConnectedClients = 0;
        tunnel.UploadRateBytesPerSec = 0;
        tunnel.DownloadRateBytesPerSec = 0;
        _tunnelRateTrackers.TryRemove(id, out _);
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Tunnels.Where(t => t.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsEnabled, false)
                .SetProperty(t => t.LastStoppedAt, tunnel.LastStoppedAt)
                .SetProperty(t => t.ConnectedClients, 0));
        }
        _logger.LogInformation("Tunnel {Name} ({Id}) stopped", tunnel.Name, tunnel.Id);
        return true;
    }

    public async Task<bool> ToggleEnableAsync(Guid id, bool isEnabled)
    {
        await SyncFromDbIfEmptyAsync();
        if (!_liveTunnels.TryGetValue(id, out var tunnel))
        {
            return false;
        }

        tunnel.IsEnabled = isEnabled;
        tunnel.UpdatedAt = DateTime.UtcNow;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Tunnels.FindAsync(id);
            if (entity != null)
            {
                entity.IsEnabled = isEnabled;
                entity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        if (isEnabled)
        {
            return await StartAsync(id);
        }
        else
        {
            return await StopAsync(id);
        }
    }

    public async Task<bool> ResetStatsAsync(Guid id)
    {
        if (!_liveTunnels.TryGetValue(id, out var tunnel))
        {
            return false;
        }

        tunnel.UploadBytes = 0;
        tunnel.DownloadBytes = 0;
        tunnel.ConnectedClients = 0;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Tunnels.FindAsync(id);
            if (entity != null)
            {
                entity.UploadBytes = 0;
                entity.DownloadBytes = 0;
                entity.ConnectedClients = 0;
                await db.SaveChangesAsync();
            }
        }
        return true;
    }

    private Task OnStatsUpdateAsync(Guid tunnelId, long uploadDelta, long downloadDelta, int clients)
    {
        if (!_liveTunnels.TryGetValue(tunnelId, out var tunnel))
        {
            return Task.CompletedTask;
        }

        var now = DateTime.UtcNow;
        tunnel.UploadBytes += uploadDelta;
        tunnel.DownloadBytes += downloadDelta;
        tunnel.ConnectedClients = clients;

        var prev = _tunnelRateTrackers.GetOrAdd(tunnelId, _ => (tunnel.UploadBytes, tunnel.DownloadBytes, now));
        var elapsedSec = (now - prev.LastTime).TotalSeconds;
        if (elapsedSec >= 0.5)
        {
            tunnel.UploadRateBytesPerSec = elapsedSec > 0 ? (long)Math.Max(0, (tunnel.UploadBytes - prev.LastUp) / elapsedSec) : 0;
            tunnel.DownloadRateBytesPerSec = elapsedSec > 0 ? (long)Math.Max(0, (tunnel.DownloadBytes - prev.LastDown) / elapsedSec) : 0;
            _tunnelRateTrackers[tunnelId] = (tunnel.UploadBytes, tunnel.DownloadBytes, now);
        }

        // Check traffic limit
        if (tunnel.TrafficLimitBytes > 0 && (tunnel.UploadBytes + tunnel.DownloadBytes) >= tunnel.TrafficLimitBytes)
        {
            _logger.LogWarning("Traffic limit exceeded for tunnel {Name} ({Id}). Stopping...", tunnel.Name, tunnel.Id);
            _logService.AppendLog(tunnel.Id, "system", $"[LIMIT] Traffic limit reached: {tunnel.UploadBytes + tunnel.DownloadBytes}/{tunnel.TrafficLimitBytes} bytes. Stopping tunnel.");
            _ = Task.Run(async () =>
            {
                await StopAsync(tunnel.Id);
                tunnel.IsEnabled = false;
                tunnel.ErrorMessage = "Traffic limit reached";
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ent = await db.Tunnels.FindAsync(tunnel.Id);
                if (ent != null)
                {
                    ent.IsEnabled = false;
                    ent.Status = TunnelStatus.Stopped;
                    ent.ErrorMessage = "Traffic limit reached";
                    await db.SaveChangesAsync();
                }
            });
        }

        return Task.CompletedTask;
    }

    private async Task OnProcessExitedAsync(Guid tunnelId, string? error)
    {
        if (!_liveTunnels.TryGetValue(tunnelId, out var tunnel))
        {
            return;
        }

        tunnel.ConnectedClients = 0;
        tunnel.UploadRateBytesPerSec = 0;
        tunnel.DownloadRateBytesPerSec = 0;
        _tunnelRateTrackers.TryRemove(tunnelId, out _);

        // If tunnel is supposed to be enabled, attempt auto-recovery
        if (tunnel.IsEnabled)
        {
            tunnel.RestartAttempts++;
            if (tunnel.RestartAttempts <= 5)
            {
                tunnel.Status = TunnelStatus.Starting;
                tunnel.ErrorMessage = $"Unexpected exit (attempt {tunnel.RestartAttempts}/5): {error}";
                _logger.LogWarning("Tunnel {Name} exited unexpectedly. Auto-recovering in 5 seconds (attempt {Attempt}/5)...",
                    tunnel.Name, tunnel.RestartAttempts);

                await Task.Delay(5000);
                if (tunnel.IsEnabled)
                {
                    await StartAsync(tunnelId);
                }
                return;
            }
            else
            {
                tunnel.Status = TunnelStatus.Failed;
                tunnel.ErrorMessage = $"Exceeded max restart attempts: {error}";
                _logger.LogError("Tunnel {Name} failed permanently after 5 restart attempts", tunnel.Name);
            }
        }
        else
        {
            tunnel.Status = TunnelStatus.Stopped;
            tunnel.ErrorMessage = error;
        }
    }

    private async Task SyncFromDbIfEmptyAsync()
    {
        if (_isSynced) return;

        await _lock.WaitAsync();
        try
        {
            if (_isSynced) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tunnels = await db.Tunnels.AsNoTracking().ToListAsync();
            foreach (var t in tunnels)
            {
                t.Status = TunnelStatus.Stopped;
                t.ConnectedClients = 0;
                _liveTunnels.TryAdd(t.Id, t);
            }
            _isSynced = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistStatsToDbAsync()
    {
        try
        {
            if (_liveTunnels.IsEmpty) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var kvp in _liveTunnels)
            {
                var live = kvp.Value;
                var entity = await db.Tunnels.FindAsync(live.Id);
                if (entity != null)
                {
                    entity.UploadBytes = live.UploadBytes;
                    entity.DownloadBytes = live.DownloadBytes;
                    entity.ConnectedClients = live.ConnectedClients;
                    entity.Status = live.Status;
                    entity.LastStartedAt = live.LastStartedAt;
                    entity.LastStoppedAt = live.LastStoppedAt;
                    entity.ErrorMessage = live.ErrorMessage;
                }
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to persist periodic stats to DB");
        }
    }
}
