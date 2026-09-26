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
                    // Not configured for localtunnel, check again in 10 seconds
                    await Task.Delay(10000, stoppingToken);
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
                _logger.LogWarning(ex, "[Localtunnel] Tunnel session interrupted. Reconnecting in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task RunTunnelSessionAsync(AppSettings settings, CancellationToken stoppingToken)
    {
        var secretClean = (settings.SecretPath ?? "zen").Replace("-", "").ToLowerInvariant();
        var preferredSubdomain = $"openflux-{secretClean.Substring(0, Math.Min(8, secretClean.Length))}";

        _logger.LogInformation("[Localtunnel] Requesting tunnel endpoint from localtunnel.me (subdomain: {Subdomain})...", preferredSubdomain);

        TunnelInfo? info = null;
        try
        {
            var res = await _httpClient.GetStringAsync($"https://localtunnel.me/{preferredSubdomain}", stoppingToken);
            info = JsonSerializer.Deserialize<TunnelInfo>(res);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Localtunnel] Preferred subdomain '{Subdomain}' unavailable, requesting new random tunnel...", preferredSubdomain);
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

        var secretPath = (settings.SecretPath ?? "").Trim('/');
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
        var poolSize = Math.Clamp(info.MaxConnCount <= 0 ? 5 : info.MaxConnCount, 3, 10);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var workers = new List<Task>();

        for (int i = 0; i < poolSize; i++)
        {
            workers.Add(Task.Run(() => StandbyWorkerLoopAsync(remoteHost, remotePort, localPort, linkedCts.Token)));
        }

        await Task.WhenAll(workers);
    }

    private async Task StandbyWorkerLoopAsync(string remoteHost, int remotePort, int localPort, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var remoteClient = new TcpClient { NoDelay = true };
                await remoteClient.ConnectAsync(remoteHost, remotePort, stoppingToken);

                var remoteStream = remoteClient.GetStream();
                var buffer = new byte[8192];

                // Wait for the first bytes from localtunnel.me (incoming browser request)
                int bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                if (bytesRead <= 0)
                {
                    remoteClient.Dispose();
                    continue;
                }

                // Incoming request received! Hand off bridging to a background task so this worker can immediately replenish the pool
                _ = BridgeStreamsAsync(remoteClient, remoteStream, buffer, bytesRead, localPort, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Brief throttle before reconnecting standby socket
                await Task.Delay(500, stoppingToken);
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

                var remoteToLocal = remoteStream.CopyToAsync(localStream, stoppingToken);
                var localToRemote = localStream.CopyToAsync(remoteStream, stoppingToken);

                await Task.WhenAny(remoteToLocal, localToRemote);
            }
            catch { }
        }
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
