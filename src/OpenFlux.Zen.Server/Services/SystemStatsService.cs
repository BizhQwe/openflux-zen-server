using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface ISystemStatsService
{
    Task<SystemStats> GetStatsAsync();
}

public sealed class SystemStatsService : ISystemStatsService
{
    private readonly ITunnelManager _tunnelManager;
    private readonly DateTime _panelStartedAt = DateTime.UtcNow;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck = DateTime.UtcNow;
    private double _lastCpuUsage;
    private readonly object _cpuLock = new();

    public SystemStatsService(ITunnelManager tunnelManager)
    {
        _tunnelManager = tunnelManager;
        _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    }

    public async Task<SystemStats> GetStatsAsync()
    {
        var tunnels = await _tunnelManager.GetAllAsync();
        var totalTunnels = tunnels.Count;
        var activeTunnels = tunnels.Count(t => t.Status == TunnelStatus.Running);
        var totalUpload = tunnels.Sum(t => t.UploadBytes);
        var totalDownload = tunnels.Sum(t => t.DownloadBytes);

        var cpuUsage = CalculateCpuUsage();
        var memInfo = GC.GetGCMemoryInfo();
        var currentProc = Process.GetCurrentProcess();
        var memUsed = currentProc.WorkingSet64;
        var memTotal = memInfo.TotalAvailableMemoryBytes > 0 ? memInfo.TotalAvailableMemoryBytes : 1024L * 1024 * 1024 * 4;

        var panelUptime = DateTime.UtcNow - _panelStartedAt;
        var systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new SystemStats
        {
            TotalTunnels = totalTunnels,
            ActiveTunnels = activeTunnels,
            TotalUploadBytes = totalUpload,
            TotalDownloadBytes = totalDownload,
            CpuUsagePercent = cpuUsage,
            MemoryUsedBytes = memUsed,
            MemoryTotalBytes = memTotal,
            PanelUptime = FormatTimeSpan(panelUptime),
            SystemUptime = FormatTimeSpan(systemUptime),
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            DotNetVersion = RuntimeInformation.FrameworkDescription
        };
    }

    private double CalculateCpuUsage()
    {
        lock (_cpuLock)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastCpuCheck).TotalMilliseconds;
            if (elapsed < 500)
            {
                return _lastCpuUsage;
            }

            var currentCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuUsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
            var totalAvailableMs = elapsed * Environment.ProcessorCount;

            _lastCpuUsage = totalAvailableMs > 0 ? Math.Min(100.0, Math.Round((cpuUsedMs / totalAvailableMs) * 100.0, 1)) : 0.0;
            _lastCpuTime = currentCpuTime;
            _lastCpuCheck = now;
            return _lastCpuUsage;
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        }
        if (ts.TotalHours >= 1)
        {
            return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
        }
        return $"{ts.Minutes}m {ts.Seconds}s";
    }
}
