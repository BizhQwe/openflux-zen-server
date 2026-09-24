using System.Collections.Concurrent;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface ITunnelLogService
{
    void AppendLog(Guid tunnelId, string stream, string message);
    IReadOnlyList<TunnelLogEntry> GetRecentLogs(Guid tunnelId, int count = 200);
    IReadOnlyList<string> GetSystemLogs(int count = 200);
    void ClearLogs(Guid tunnelId);
    string GetLogFilePath(Guid tunnelId);
}

public sealed class TunnelLogService : ITunnelLogService
{
    private readonly string _logsDirectory;
    private readonly string _systemLogsDirectory;
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<TunnelLogEntry>> _memoryLogs = new();
    private readonly ConcurrentDictionary<Guid, object> _fileLocks = new();
    private const int MaxMemoryLogLines = 500;

    public TunnelLogService()
    {
        _logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs", "tunnels");
        _systemLogsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logsDirectory);
    }

    public string GetLogFilePath(Guid tunnelId)
    {
        return Path.Combine(_logsDirectory, $"{tunnelId}.log");
    }

    public void AppendLog(Guid tunnelId, string stream, string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        var entry = new TunnelLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Stream = stream,
            Message = message.TrimEnd()
        };

        // 1. Update in-memory buffer
        var queue = _memoryLogs.GetOrAdd(tunnelId, _ => new ConcurrentQueue<TunnelLogEntry>());
        queue.Enqueue(entry);
        while (queue.Count > MaxMemoryLogLines && queue.TryDequeue(out _)) { }

        // 2. Append to dedicated log file
        var fileLock = _fileLocks.GetOrAdd(tunnelId, _ => new object());
        lock (fileLock)
        {
            try
            {
                var filePath = GetLogFilePath(tunnelId);
                var formatted = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Stream.ToUpperInvariant()}] {entry.Message}{Environment.NewLine}";
                File.AppendAllText(filePath, formatted);
            }
            catch
            {
                // Silently handle transient disk/lock errors
            }
        }
    }

    public IReadOnlyList<TunnelLogEntry> GetRecentLogs(Guid tunnelId, int count = 200)
    {
        if (_memoryLogs.TryGetValue(tunnelId, out var queue) && !queue.IsEmpty)
        {
            return queue.TakeLast(count).ToList();
        }

        // If not in memory, read tail from disk file
        var filePath = GetLogFilePath(tunnelId);
        if (!File.Exists(filePath))
        {
            return Array.Empty<TunnelLogEntry>();
        }

        try
        {
            var lines = File.ReadLines(filePath).TakeLast(count).ToList();
            var result = new List<TunnelLogEntry>();
            foreach (var line in lines)
            {
                result.Add(new TunnelLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Stream = line.Contains("[STDERR]") ? "stderr" : "stdout",
                    Message = line
                });
            }
            return result;
        }
        catch
        {
            return Array.Empty<TunnelLogEntry>();
        }
    }

    public IReadOnlyList<string> GetSystemLogs(int count = 200)
    {
        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var primaryLog = Path.Combine(_systemLogsDirectory, $"panel-{today}.log");
            if (!File.Exists(primaryLog))
            {
                // Look for any panel-*.log
                var files = Directory.GetFiles(_systemLogsDirectory, "panel-*.log");
                if (files.Length > 0)
                {
                    primaryLog = files.OrderByDescending(File.GetLastWriteTimeUtc).First();
                }
                else
                {
                    return Array.Empty<string>();
                }
            }

            return File.ReadLines(primaryLog).TakeLast(count).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void ClearLogs(Guid tunnelId)
    {
        if (_memoryLogs.TryGetValue(tunnelId, out var queue))
        {
            queue.Clear();
        }

        var fileLock = _fileLocks.GetOrAdd(tunnelId, _ => new object());
        lock (fileLock)
        {
            try
            {
                var filePath = GetLogFilePath(tunnelId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch { }
        }
    }
}
