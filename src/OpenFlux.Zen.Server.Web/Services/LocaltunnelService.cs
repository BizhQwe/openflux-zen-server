using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public sealed class LocaltunnelService : BackgroundService
{
    private readonly ILogger<LocaltunnelService> _logger;
    private readonly ISettingsService _settingsService;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public LocaltunnelService(
        ILogger<LocaltunnelService> logger,
        ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly for the main Kestrel web server to initialize and begin listening
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                var publishMode = Environment.GetEnvironmentVariable("OPENFLUX_PUBLISH_MODE") ?? settings.PublishMode;

                if (!string.Equals(publishMode, "localtunnel", StringComparison.OrdinalIgnoreCase))
                {
                    // Check again in 5 seconds
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                await RunTunnelSessionAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Localtunnel] Tunnel session ended or interrupted. Reconnecting in 3 seconds...");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }

    private async Task RunTunnelSessionAsync(AppSettings settings, CancellationToken stoppingToken)
    {
        var secretPath = (settings.SecretPath ?? "").Trim('/');
        var secLower = secretPath.ToLowerInvariant();
        var subPrefix = $"openflux-{(secLower.Length >= 8 ? secLower.Substring(0, 8) : secLower)}";
        string preferredSubdomain = subPrefix;

        if (!string.IsNullOrEmpty(settings.PublicUrl) && Uri.TryCreate(settings.PublicUrl, UriKind.Absolute, out var existingUri))
        {
            var parts = existingUri.Host.Split('.');
            if (parts.Length >= 3 && parts[^2] == "loca" && parts[^1] == "lt")
            {
                preferredSubdomain = parts[0];
            }
        }

        _logger.LogInformation("[Localtunnel] Requesting tunnel endpoint from localtunnel.me (subdomain: {Subdomain})...", preferredSubdomain);

        TunnelInfo? info = null;
        try
        {
            var res = await _httpClient.GetStringAsync($"https://localtunnel.me/{preferredSubdomain}", stoppingToken);
            info = JsonSerializer.Deserialize<TunnelInfo>(res);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Localtunnel] Preferred subdomain '{Subdomain}' request failed, requesting random tunnel...", preferredSubdomain);
        }

        if (info == null || info.Port == 0)
        {
            var res = await _httpClient.GetStringAsync("https://localtunnel.me/?new", stoppingToken);
            info = JsonSerializer.Deserialize<TunnelInfo>(res);
        }

        if (info == null || info.Port == 0)
        {
            throw new InvalidOperationException("Failed to obtain tunnel endpoint from localtunnel.me");
        }

        var publicUrl = $"{info.Url.TrimEnd('/')}/{secretPath}/";
        settings.PublicUrl = publicUrl;
        await _settingsService.UpdateSettingsAsync(settings);

        // Fetch server public IP (for the localtunnel browser friendly reminder page)
        string? publicIp = null;
        try
        {
            publicIp = (await _httpClient.GetStringAsync("https://api.ipify.org", stoppingToken)).Trim();
        }
        catch
        {
            try
            {
                publicIp = (await _httpClient.GetStringAsync("https://ifconfig.me/ip", stoppingToken)).Trim();
            }
            catch { }
        }

        _settingsService.UpdatePublicUrlInCredentials(publicUrl, publicIp);

        _logger.LogInformation("==================================================================");
        _logger.LogInformation("  [Localtunnel] Tunnel established: {Url}", publicUrl);
        if (!string.IsNullOrEmpty(publicIp))
        {
            _logger.LogInformation("  [Localtunnel] Browser tunnel password (Server IP): {Ip}", publicIp);
        }
        _logger.LogInformation("==================================================================");

        var remoteHost = "localtunnel.me";
        var remotePort = info.Port;
        var localPort = settings.ListenPort;
        var poolSize = Math.Clamp(info.MaxConnCount <= 0 ? 3 : info.MaxConnCount, 2, 8);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var workers = new List<Task>();

        for (int i = 0; i < poolSize; i++)
        {
            workers.Add(Task.Run(() => StandbyWorkerLoopAsync(remoteHost, remotePort, localPort, sessionCts)));
        }

        // Periodic health watchdog: verifies tunnel is reachable and not returning 503 Tunnel Unavailable
        workers.Add(Task.Run(async () =>
        {
            while (!sessionCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, sessionCts.Token);
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{info.Url.TrimEnd('/')}/api/health");
                    req.Headers.Add("bypass-tunnel-reminder", "true");
                    using var resp = await _httpClient.SendAsync(req, sessionCts.Token);
                    if ((int)resp.StatusCode == 503)
                    {
                        _logger.LogWarning("[Localtunnel] Endpoint returned 503 Tunnel Unavailable. Re-registering session...");
                        sessionCts.Cancel();
                        break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogDebug("[Localtunnel] Heartbeat check error: {Message}", ex.Message);
                }
            }
        }, sessionCts.Token));

        await Task.WhenAny(workers);
        sessionCts.Cancel();
        await Task.WhenAll(workers.Select(async w => { try { await w; } catch { } }));
    }

    private async Task StandbyWorkerLoopAsync(string remoteHost, int remotePort, int localPort, CancellationTokenSource sessionCts)
    {
        int consecutiveErrors = 0;
        var token = sessionCts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var remoteClient = new TcpClient { NoDelay = true };
                await remoteClient.ConnectAsync(remoteHost, remotePort, token);
                consecutiveErrors = 0;

                var remoteStream = remoteClient.GetStream();
                var buffer = new byte[8192];

                int bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (bytesRead <= 0)
                {
                    remoteClient.Dispose();
                    continue;
                }

                // Incoming request received! Hand off bridging to a background task so this worker can immediately replenish the standby socket
                _ = BridgeStreamsAsync(remoteClient, remoteStream, buffer, bytesRead, localPort, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused || ex.SocketErrorCode == SocketError.HostNotFound)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= 3)
                {
                    _logger.LogWarning("[Localtunnel] Port {Port} refused connection. Tunnel expired on server side. Triggering re-establishment...", remotePort);
                    sessionCts.Cancel();
                    break;
                }
                await Task.Delay(1000, token);
            }
            catch
            {
                consecutiveErrors++;
                if (consecutiveErrors >= 5)
                {
                    sessionCts.Cancel();
                    break;
                }
                await Task.Delay(500, token);
            }
        }
    }

    private async Task BridgeStreamsAsync(
        TcpClient remoteClient,
        NetworkStream remoteStream,
        byte[] initialBuffer,
        int initialBytes,
        int localPort,
        CancellationToken stoppingToken)
    {
        using (remoteClient)
        {
            try
            {
                using var localClient = new TcpClient { NoDelay = true };
                await localClient.ConnectAsync("127.0.0.1", localPort, stoppingToken);
                using var localStream = localClient.GetStream();

                await localStream.WriteAsync(initialBuffer.AsMemory(0, initialBytes), stoppingToken);
                await localStream.FlushAsync(stoppingToken);

                var remoteToLocal = CopyStreamAsync(remoteStream, localStream, stoppingToken);
                var localToRemote = CopyStreamAsync(localStream, remoteStream, stoppingToken);

                await Task.WhenAny(remoteToLocal, localToRemote);
            }
            catch { }
        }
    }

    private static async Task CopyStreamAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buf = new byte[8192];
        int read;
        try
        {
            while ((read = await source.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
            {
                await destination.WriteAsync(buf.AsMemory(0, read), ct);
                await destination.FlushAsync(ct);
            }
        }
        catch { }
    }

    private sealed class TunnelInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("max_conn_count")]
        public int MaxConnCount { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }
}
