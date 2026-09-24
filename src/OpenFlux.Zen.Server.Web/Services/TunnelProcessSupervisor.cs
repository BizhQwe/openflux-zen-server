using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface ITunnelProcessSupervisor
{
    Task<bool> StartTunnelAsync(Tunnel tunnel, Func<Guid, long, long, int, Task> onStatsUpdate, Func<Guid, string?, Task> onProcessExited);
    Task<bool> StopTunnelAsync(Guid tunnelId);
    bool IsRunning(Guid tunnelId);
    void StopAll();
}

public sealed partial class TunnelProcessSupervisor : ITunnelProcessSupervisor
{
    private readonly ILogger<TunnelProcessSupervisor> _logger;
    private readonly IOpenFluxBinaryResolver _binaryResolver;
    private readonly ITunnelLogService _logService;
    private readonly ConcurrentDictionary<Guid, Process> _runningProcesses = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _processCts = new();
    private readonly ConcurrentDictionary<Guid, (ulong LastPackets, ulong LastL3Upload, ulong LastL3Download)> _lastStats = new();
    private readonly string _keysDirectory;

    [GeneratedRegex(@"\[STATS\]\s+uptime=\S+\s+mode=\S+\s+packets=(\d+)\s+connected=(\d+)\s+established=(\d+)", RegexOptions.Compiled)]
    private static partial Regex StatsRegex();

    [GeneratedRegex(@"\[L3-STATS\]\s+fromTr=(\d+)\(\+(\d+)\)\s+toNet=(\d+)\(\+(\d+)\)\s+\|\s+fromNet=(\d+)\(\+(\d+)\)\s+toCli=(\d+)\(\+(\d+)\)", RegexOptions.Compiled)]
    private static partial Regex L3StatsRegex();

    public TunnelProcessSupervisor(
        ILogger<TunnelProcessSupervisor> logger,
        IOpenFluxBinaryResolver binaryResolver,
        ITunnelLogService logService)
    {
        _logger = logger;
        _binaryResolver = binaryResolver;
        _logService = logService;
        _keysDirectory = Path.Combine(AppContext.BaseDirectory, "data", "keys");
        Directory.CreateDirectory(_keysDirectory);
    }

    public bool IsRunning(Guid tunnelId)
    {
        if (_runningProcesses.TryGetValue(tunnelId, out var proc))
        {
            try
            {
                return !proc.HasExited;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public async Task<bool> StartTunnelAsync(
        Tunnel tunnel,
        Func<Guid, long, long, int, Task> onStatsUpdate,
        Func<Guid, string?, Task> onProcessExited)
    {
        if (IsRunning(tunnel.Id))
        {
            _logger.LogWarning("Tunnel {TunnelId} ({Name}) is already running", tunnel.Id, tunnel.Name);
            return true;
        }

        var binaryPath = _binaryResolver.GetBinaryPath();
        if (!File.Exists(binaryPath))
        {
            var err = $"OpenFlux binary not found at '{binaryPath}'";
            _logger.LogError("{Error}", err);
            _logService.AppendLog(tunnel.Id, "stderr", err);
            return false;
        }

        var args = BuildCommandLineArguments(tunnel);
        _logger.LogInformation("Starting tunnel {TunnelId} ({Name}): {Binary} {Args}", tunnel.Id, tunnel.Name, binaryPath, args);
        _logService.AppendLog(tunnel.Id, "system", $"Starting tunnel: {binaryPath} {args}");

        var psi = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = args,
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Environment variables for OpenFlux
        psi.EnvironmentVariables["GOMEMLIMIT"] = "256MiB";

        Process proc;
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize process for tunnel {TunnelId}", tunnel.Id);
            _logService.AppendLog(tunnel.Id, "stderr", $"Process initialization error: {ex.Message}");
            return false;
        }

        var cts = new CancellationTokenSource();
        _processCts[tunnel.Id] = cts;
        _lastStats[tunnel.Id] = (0, 0, 0);

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _logService.AppendLog(tunnel.Id, "stdout", e.Data);
                ParseStats(tunnel.Id, e.Data, onStatsUpdate);
            }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _logService.AppendLog(tunnel.Id, "stderr", e.Data);
                ParseStats(tunnel.Id, e.Data, onStatsUpdate);
            }
        };

        proc.Exited += async (_, _) =>
        {
            _runningProcesses.TryRemove(tunnel.Id, out _);
            _processCts.TryRemove(tunnel.Id, out _);
            _lastStats.TryRemove(tunnel.Id, out _);

            int exitCode = -1;
            try { exitCode = proc.ExitCode; } catch { }

            var msg = $"Process exited with code {exitCode}";
            _logger.LogInformation("Tunnel {TunnelId} process exited (Code: {ExitCode})", tunnel.Id, exitCode);
            _logService.AppendLog(tunnel.Id, "system", msg);

            await onProcessExited(tunnel.Id, exitCode == 0 ? null : msg);
        };

        try
        {
            if (!proc.Start())
            {
                _logService.AppendLog(tunnel.Id, "stderr", "Process.Start() returned false");
                return false;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            _runningProcesses[tunnel.Id] = proc;

            // Wait a brief moment to catch immediate startup crashes (e.g. invalid arguments)
            await Task.Delay(350);
            if (proc.HasExited)
            {
                _logger.LogError("Tunnel {TunnelId} exited immediately with code {ExitCode}", tunnel.Id, proc.ExitCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start OpenFlux process for tunnel {TunnelId}", tunnel.Id);
            _logService.AppendLog(tunnel.Id, "stderr", $"Start exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopTunnelAsync(Guid tunnelId)
    {
        if (!_runningProcesses.TryGetValue(tunnelId, out var proc))
        {
            return true;
        }

        try
        {
            _logService.AppendLog(tunnelId, "system", "Stopping tunnel process...");
            if (!proc.HasExited)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    proc.Kill(entireProcessTree: true);
                }
                else
                {
                    try
                    {
                        // On Linux, try SIGTERM first
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "kill",
                            Arguments = $"-15 {proc.Id}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        })?.WaitForExit(1000);
                    }
                    catch
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while stopping tunnel {TunnelId}", tunnelId);
            try { proc.Kill(entireProcessTree: true); } catch { }
        }
        finally
        {
            _runningProcesses.TryRemove(tunnelId, out _);
            _processCts.TryRemove(tunnelId, out _);
            _lastStats.TryRemove(tunnelId, out _);
        }

        return true;
    }

    public void StopAll()
    {
        foreach (var (id, proc) in _runningProcesses)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
        _runningProcesses.Clear();
        _processCts.Clear();
        _lastStats.Clear();
    }

    private string BuildCommandLineArguments(Tunnel t)
    {
        var parts = new List<string>();

        // Always include -debug flag as strictly required
        parts.Add("-debug");

        // Role: exit | client | bench-send | bench-sink
        var role = string.IsNullOrWhiteSpace(t.Role) ? "exit" : t.Role.Trim();
        parts.Add($"--role={role}");

        // Transport: yandex | vyandex | oneme | cupsonline | mailru
        var transport = string.IsNullOrWhiteSpace(t.Transport) ? "yandex" : t.Transport.Trim();
        parts.Add($"--transport={transport}");

        // Mode: only applicable to exit node (l3 | l4)
        if (role == "exit")
        {
            var mode = string.IsNullOrWhiteSpace(t.Mode) ? "l4" : t.Mode.Trim();
            parts.Add($"--mode={mode}");

            if (!string.IsNullOrWhiteSpace(t.LocalIp))
            {
                parts.Add($"--local-ip={t.LocalIp.Trim()}");
            }
        }

        // Inbound: only applicable to client (tun | socks5)
        if (role == "client")
        {
            var inbound = string.IsNullOrWhiteSpace(t.Inbound) ? "socks5" : t.Inbound.Trim();
            parts.Add($"--inbound={inbound}");

            if (inbound == "socks5" && !string.IsNullOrWhiteSpace(t.Socks5Address))
            {
                parts.Add($"--socks5={t.Socks5Address.Trim()}");
            }
        }

        // Codec: batched | legacy
        var codec = string.IsNullOrWhiteSpace(t.Codec) ? "batched" : t.Codec.Trim();
        parts.Add($"--codec={codec}");

        // Url (for yandex, vyandex, cupsonline, mailru)
        if (!string.IsNullOrWhiteSpace(t.Url))
        {
            parts.Add($"--url=\"{t.Url.Trim()}\"");
        }

        // MaxToken & MaxUid (for oneme)
        if (transport == "oneme")
        {
            if (!string.IsNullOrWhiteSpace(t.MaxToken))
            {
                parts.Add($"--maxToken=\"{t.MaxToken.Trim()}\"");
            }
            if (!string.IsNullOrWhiteSpace(t.MaxUid))
            {
                parts.Add($"--maxUid=\"{t.MaxUid.Trim()}\"");
            }
        }

        // Encryption key file
        if (!string.IsNullOrWhiteSpace(t.EncryptionKey))
        {
            var keyFilePath = Path.Combine(_keysDirectory, $"{t.Id}.key");
            File.WriteAllText(keyFilePath, t.EncryptionKey.Trim());
            parts.Add($"--encryption-key-file=\"{keyFilePath}\"");
        }

        // Benchmark options
        if (role == "bench-send")
        {
            if (t.BenchBytes > 0)
            {
                parts.Add($"--bench-bytes={t.BenchBytes}");
            }
            if (t.BenchCompressible)
            {
                parts.Add("--bench-compressible");
            }
        }

        // Extra custom arguments
        if (!string.IsNullOrWhiteSpace(t.ExtraArgs))
        {
            parts.Add(t.ExtraArgs.Trim());
        }

        return string.Join(" ", parts);
    }

    private void ParseStats(Guid tunnelId, string line, Func<Guid, long, long, int, Task> onStatsUpdate)
    {
        try
        {
            // Parse L4 stats: [STATS] uptime=... mode=l4 packets=1234 connected=2 established=1
            var match = StatsRegex().Match(line);
            if (match.Success)
            {
                ulong packets = ulong.Parse(match.Groups[1].Value);
                int connected = int.Parse(match.Groups[2].Value);

                var prev = _lastStats.GetOrAdd(tunnelId, _ => (0, 0, 0));
                long packetDelta = 0;
                if (packets >= prev.LastPackets)
                {
                    packetDelta = (long)(packets - prev.LastPackets);
                }
                _lastStats[tunnelId] = (packets, prev.LastL3Upload, prev.LastL3Download);

                // Estimated packet size ~1300 bytes split evenly between upload and download
                long bytesDelta = packetDelta * 1300;
                long uploadDelta = bytesDelta / 2;
                long downloadDelta = bytesDelta / 2;

                _ = Task.Run(async () =>
                {
                    await onStatsUpdate(tunnelId, uploadDelta, downloadDelta, connected);
                });
                return;
            }

            // Parse L3 stats: [L3-STATS] fromTr=... toNet=... | fromNet=... toCli=...
            var l3Match = L3StatsRegex().Match(line);
            if (l3Match.Success)
            {
                ulong fromTr = ulong.Parse(l3Match.Groups[1].Value);
                ulong toCli = ulong.Parse(l3Match.Groups[7].Value);

                var prev = _lastStats.GetOrAdd(tunnelId, _ => (0, 0, 0));
                long upDelta = 0;
                long downDelta = 0;

                if (fromTr >= prev.LastL3Upload)
                {
                    upDelta = (long)((fromTr - prev.LastL3Upload) * 1300);
                }
                if (toCli >= prev.LastL3Download)
                {
                    downDelta = (long)((toCli - prev.LastL3Download) * 1300);
                }

                _lastStats[tunnelId] = (prev.LastPackets, fromTr, toCli);

                _ = Task.Run(async () =>
                {
                    await onStatsUpdate(tunnelId, upDelta, downDelta, 1);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to parse stats line for tunnel {TunnelId}", tunnelId);
        }
    }
}
