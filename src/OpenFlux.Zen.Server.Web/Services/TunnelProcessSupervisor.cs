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
    private sealed class TunnelState
    {
        public Guid TunnelId { get; init; }
        public Process Process { get; init; } = null!;
        public CancellationTokenSource Cts { get; init; } = null!;
        public long PendingUploadBytes;
        public long PendingDownloadBytes;
        public int LastConnectedSockets;
        public int LastClientCount;
        public readonly ConcurrentDictionary<string, DateTime> ActiveClients = new();
        public ulong LastL3Upload;
        public ulong LastL3Download;
        public Func<Guid, long, long, int, Task> Callback { get; set; } = null!;
    }

    private readonly ILogger<TunnelProcessSupervisor> _logger;
    private readonly IOpenFluxBinaryResolver _binaryResolver;
    private readonly ITunnelLogService _logService;
    private readonly ConcurrentDictionary<Guid, TunnelState> _tunnelStates = new();
    private readonly string _keysDirectory;
    private readonly Timer _flushTimer;

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

        // Periodic flush of accumulated packet bytes & client counts every 500ms
        _flushTimer = new Timer(OnFlushTimerTick, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public bool IsRunning(Guid tunnelId)
    {
        if (_tunnelStates.TryGetValue(tunnelId, out var state))
        {
            try
            {
                return !state.Process.HasExited;
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
        var state = new TunnelState
        {
            TunnelId = tunnel.Id,
            Process = proc,
            Cts = cts,
            Callback = onStatsUpdate
        };
        _tunnelStates[tunnel.Id] = state;

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _logService.AppendLog(tunnel.Id, "stdout", e.Data);
                ParseStats(state, e.Data);
            }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _logService.AppendLog(tunnel.Id, "stderr", e.Data);
                ParseStats(state, e.Data);
            }
        };

        proc.Exited += async (_, _) =>
        {
            FlushState(state);
            _tunnelStates.TryRemove(tunnel.Id, out _);

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

            // Wait a brief moment to catch immediate startup crashes
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
        if (!_tunnelStates.TryGetValue(tunnelId, out var state))
        {
            return true;
        }

        try
        {
            _logService.AppendLog(tunnelId, "system", "Stopping tunnel process...");
            var proc = state.Process;
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
            try { state.Process.Kill(entireProcessTree: true); } catch { }
        }
        finally
        {
            FlushState(state);
            _tunnelStates.TryRemove(tunnelId, out _);
        }

        return true;
    }

    public void StopAll()
    {
        foreach (var (id, state) in _tunnelStates)
        {
            try
            {
                if (!state.Process.HasExited)
                {
                    state.Process.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
        _tunnelStates.Clear();
    }

    private string BuildCommandLineArguments(Tunnel t)
    {
        var parts = new List<string>();

        // Always include -debug flag as strictly required for traffic stats & logs
        parts.Add("-debug");

        // Role on server is strictly EXIT node
        parts.Add("--role=exit");

        // Transport: yandex | vyandex | oneme | cupsonline | mailru
        var transport = string.IsNullOrWhiteSpace(t.Transport) ? "yandex" : t.Transport.Trim();
        parts.Add($"--transport={transport}");

        // Mode: l4 | l3
        var mode = string.IsNullOrWhiteSpace(t.Mode) ? "l4" : t.Mode.Trim();
        parts.Add($"--mode={mode}");

        if (!string.IsNullOrWhiteSpace(t.LocalIp))
        {
            parts.Add($"--local-ip={t.LocalIp.Trim()}");
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

        // Extra custom arguments
        if (!string.IsNullOrWhiteSpace(t.ExtraArgs))
        {
            parts.Add(t.ExtraArgs.Trim());
        }

        return string.Join(" ", parts);
    }

    private void ParseStats(TunnelState state, string line)
    {
        try
        {
            // 1. Fast packet line parsing in L4 mode:
            // "<- 52 bytes - TCP 10.10.10.2:33128 -> 66.90.91.4:8080"
            // "-> 1472 bytes - TCP 62.63.162.194:8080 -> 10.10.10.2:64025"
            if (line.Contains(" bytes - "))
            {
                int arrowIdx = line.IndexOf("<- ");
                bool isUpload = arrowIdx >= 0;
                if (!isUpload)
                {
                    arrowIdx = line.IndexOf("-> ");
                }

                if (arrowIdx >= 0)
                {
                    int bytesIdx = line.IndexOf(" bytes", arrowIdx + 3);
                    if (bytesIdx > arrowIdx + 3)
                    {
                        var byteSpan = line.AsSpan(arrowIdx + 3, bytesIdx - (arrowIdx + 3));
                        if (long.TryParse(byteSpan, out var byteCount))
                        {
                            if (isUpload)
                                Interlocked.Add(ref state.PendingUploadBytes, byteCount);
                            else
                                Interlocked.Add(ref state.PendingDownloadBytes, byteCount);

                            // Extract client IP address to accurately count unique devices
                            int protoIdx = line.IndexOf(" TCP ", bytesIdx);
                            if (protoIdx < 0) protoIdx = line.IndexOf(" UDP ", bytesIdx);
                            if (protoIdx >= 0)
                            {
                                int afterProto = protoIdx + 5;
                                int innerArrow = line.IndexOf(" -> ", afterProto);
                                if (innerArrow > afterProto)
                                {
                                    string ipWithPort = isUpload
                                        ? line.Substring(afterProto, innerArrow - afterProto).Trim()
                                        : line.Substring(innerArrow + 4).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

                                    int colonIdx = ipWithPort.LastIndexOf(':');
                                    var ip = colonIdx > 0 ? ipWithPort.Substring(0, colonIdx) : ipWithPort;
                                    if (!string.IsNullOrWhiteSpace(ip) && (ip.StartsWith("10.") || ip.StartsWith("172.") || ip.StartsWith("192.168.") || ip.StartsWith("100.")))
                                    {
                                        state.ActiveClients[ip] = DateTime.UtcNow;
                                    }
                                }
                            }
                        }
                    }
                }
                return;
            }

            // 2. Parse L4 stats line: [STATS] uptime=... mode=l4 packets=0 connected=41 established=33
            var match = StatsRegex().Match(line);
            if (match.Success)
            {
                int connected = int.Parse(match.Groups[2].Value);
                state.LastConnectedSockets = connected;
                return;
            }

            // 3. Parse L3 stats: [L3-STATS] fromTr=17(+17) toNet=17(+17) | fromNet=92(+92) toCli=11(+11)
            var l3Match = L3StatsRegex().Match(line);
            if (l3Match.Success)
            {
                ulong fromTr = ulong.Parse(l3Match.Groups[1].Value);
                ulong toCli = ulong.Parse(l3Match.Groups[7].Value);

                if (fromTr > state.LastL3Upload)
                {
                    long upDelta = (long)((fromTr - state.LastL3Upload) * 1300);
                    Interlocked.Add(ref state.PendingUploadBytes, upDelta);
                }
                if (toCli > state.LastL3Download)
                {
                    long downDelta = (long)((toCli - state.LastL3Download) * 1300);
                    Interlocked.Add(ref state.PendingDownloadBytes, downDelta);
                }

                state.LastL3Upload = fromTr;
                state.LastL3Download = toCli;
                state.LastConnectedSockets = 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to parse stats line for tunnel {TunnelId}", state.TunnelId);
        }
    }

    private void OnFlushTimerTick(object? _)
    {
        foreach (var state in _tunnelStates.Values)
        {
            FlushState(state);
        }
    }

    private void FlushState(TunnelState state)
    {
        try
        {
            var up = Interlocked.Exchange(ref state.PendingUploadBytes, 0);
            var down = Interlocked.Exchange(ref state.PendingDownloadBytes, 0);

            // Prune clients not seen in last 60 seconds
            var now = DateTime.UtcNow;
            foreach (var (ip, dt) in state.ActiveClients)
            {
                if ((now - dt).TotalSeconds > 60)
                {
                    state.ActiveClients.TryRemove(ip, out _);
                }
            }

            // Calculate unique client device count
            int uniqueClients = state.ActiveClients.Count;
            if (uniqueClients == 0 && state.LastConnectedSockets > 0)
            {
                uniqueClients = 1; // Fallback: active sockets detected, so at least 1 device connected
            }

            if (up > 0 || down > 0 || uniqueClients != state.LastClientCount)
            {
                state.LastClientCount = uniqueClients;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await state.Callback(state.TunnelId, up, down, uniqueClients);
                    }
                    catch { }
                });
            }
        }
        catch { }
    }
}
