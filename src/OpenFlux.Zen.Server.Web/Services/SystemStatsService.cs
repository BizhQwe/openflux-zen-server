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

    // CPU Tracking
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck = DateTime.UtcNow;
    private double _lastCpuUsage;
    private long _lastHostCpuTotal;
    private long _lastHostCpuIdle;
    private readonly object _cpuLock = new();

    // Traffic / Network Rate Tracking (from tunnels)
    private long _lastUploadBytes;
    private long _lastDownloadBytes;
    private DateTime _lastTrafficCheck = DateTime.UtcNow;
    private long _lastUploadRate;
    private long _lastDownloadRate;
    private readonly object _trafficLock = new();

    // Host NIC dev tracking (/proc/net/dev)
    private long _lastNetDevRx;
    private long _lastNetDevTx;
    private DateTime _lastNetDevCheck = DateTime.UtcNow;
    private long _hostDownloadRate;
    private long _hostUploadRate;
    private readonly object _netDevLock = new();

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

        var (tunnelUpRate, tunnelDownRate) = CalculateNetworkRates(totalUpload, totalDownload);
        var (hostUpRate, hostDownRate) = GetHostNetworkRates();

        var uploadRate = Math.Max(tunnelUpRate, hostUpRate);
        var downloadRate = Math.Max(tunnelDownRate, hostDownRate);

        var cpuUsage = CalculateCpuUsage();
        var (memUsed, memTotal) = GetMemoryUsage();

        var panelUptime = DateTime.UtcNow - _panelStartedAt;
        var systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new SystemStats
        {
            TotalTunnels = totalTunnels,
            ActiveTunnels = activeTunnels,
            TotalUploadBytes = totalUpload,
            TotalDownloadBytes = totalDownload,
            UploadRateBytesPerSec = uploadRate,
            DownloadRateBytesPerSec = downloadRate,
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

    private (long uploadRate, long downloadRate) CalculateNetworkRates(long currentUpload, long currentDownload)
    {
        lock (_trafficLock)
        {
            var now = DateTime.UtcNow;
            var elapsedSec = (now - _lastTrafficCheck).TotalSeconds;
            if (elapsedSec >= 0.5)
            {
                if (elapsedSec > 0 && (_lastUploadBytes > 0 || _lastDownloadBytes > 0))
                {
                    _lastUploadRate = (long)Math.Max(0, (currentUpload - _lastUploadBytes) / elapsedSec);
                    _lastDownloadRate = (long)Math.Max(0, (currentDownload - _lastDownloadBytes) / elapsedSec);
                }
                else
                {
                    _lastUploadRate = 0;
                    _lastDownloadRate = 0;
                }
                _lastUploadBytes = currentUpload;
                _lastDownloadBytes = currentDownload;
                _lastTrafficCheck = now;
            }
            return (_lastUploadRate, _lastDownloadRate);
        }
    }

    private (long uploadRate, long downloadRate) GetHostNetworkRates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/net/dev"))
        {
            try
            {
                long rxBytes = 0;
                long txBytes = 0;
                foreach (var line in File.ReadLines("/proc/net/dev"))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("lo:") || trimmed.StartsWith("Inter-") || trimmed.StartsWith("face")) continue;
                    var colonIdx = trimmed.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var stats = trimmed.Substring(colonIdx + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (stats.Length >= 9)
                        {
                            if (long.TryParse(stats[0], out var rx)) rxBytes += rx;
                            if (long.TryParse(stats[8], out var tx)) txBytes += tx;
                        }
                    }
                }

                lock (_netDevLock)
                {
                    var now = DateTime.UtcNow;
                    var elapsedSec = (now - _lastNetDevCheck).TotalSeconds;
                    if (elapsedSec >= 0.5)
                    {
                        if (_lastNetDevRx > 0 && elapsedSec > 0)
                        {
                            _hostDownloadRate = (long)Math.Max(0, (rxBytes - _lastNetDevRx) / elapsedSec);
                            _hostUploadRate = (long)Math.Max(0, (txBytes - _lastNetDevTx) / elapsedSec);
                        }
                        _lastNetDevRx = rxBytes;
                        _lastNetDevTx = txBytes;
                        _lastNetDevCheck = now;
                    }
                    return (_hostUploadRate, _hostDownloadRate);
                }
            }
            catch
            {
                // Fallback
            }
        }
        return (0, 0);
    }

    private (long used, long total) GetMemoryUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/meminfo"))
        {
            try
            {
                long memTotalKb = 0;
                long memAvailableKb = 0;
                long memFreeKb = 0;
                long buffersKb = 0;
                long cachedKb = 0;

                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !long.TryParse(parts[1], out var val)) continue;

                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        memTotalKb = val;
                    else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                        memAvailableKb = val;
                    else if (line.StartsWith("MemFree:", StringComparison.OrdinalIgnoreCase))
                        memFreeKb = val;
                    else if (line.StartsWith("Buffers:", StringComparison.OrdinalIgnoreCase))
                        buffersKb = val;
                    else if (line.StartsWith("Cached:", StringComparison.OrdinalIgnoreCase))
                        cachedKb = val;
                }

                if (memTotalKb > 0)
                {
                    if (memAvailableKb <= 0)
                    {
                        memAvailableKb = memFreeKb + buffersKb + cachedKb;
                    }
                    var usedKb = Math.Max(0, memTotalKb - memAvailableKb);
                    return (usedKb * 1024L, memTotalKb * 1024L);
                }
            }
            catch
            {
                // Fallback to GC memory
            }
        }

        var memInfo = GC.GetGCMemoryInfo();
        var fallbackTotal = memInfo.TotalAvailableMemoryBytes > 0 ? memInfo.TotalAvailableMemoryBytes : 1024L * 1024 * 1024 * 4;
        var fallbackUsed = Process.GetCurrentProcess().WorkingSet64;
        return (fallbackUsed, fallbackTotal);
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

            // Attempt host CPU from /proc/stat on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/stat"))
            {
                try
                {
                    var firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
                    if (firstLine != null && firstLine.StartsWith("cpu "))
                    {
                        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            long total = 0;
                            for (int i = 1; i < parts.Length; i++)
                            {
                                if (long.TryParse(parts[i], out var v)) total += v;
                            }
                            long.TryParse(parts[4], out var idle);
                            if (parts.Length >= 6 && long.TryParse(parts[5], out var iowait)) idle += iowait;

                            if (_lastHostCpuTotal > 0)
                            {
                                var deltaTotal = total - _lastHostCpuTotal;
                                var deltaIdle = idle - _lastHostCpuIdle;
                                if (deltaTotal > 0)
                                {
                                    _lastCpuUsage = Math.Clamp(Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100.0, 1), 0.0, 100.0);
                                    _lastHostCpuTotal = total;
                                    _lastHostCpuIdle = idle;
                                    _lastCpuCheck = now;
                                    return _lastCpuUsage;
                                }
                            }
                            _lastHostCpuTotal = total;
                            _lastHostCpuIdle = idle;
                        }
                    }
                }
                catch
                {
                    // Fallback to process CPU
                }
            }

            // Fallback: Process CPU
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
