using System.Text.Json.Serialization;

namespace OpenFlux.Zen.Server.Models;

public sealed class Tunnel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default Tunnel";
    
    // Core OpenFlux settings
    public string Role { get; set; } = "exit"; // exit | client | bench-send | bench-sink
    public string Transport { get; set; } = "yandex"; // yandex | vyandex | oneme | cupsonline | mailru
    public string Inbound { get; set; } = "socks5"; // tun | socks5 (client only)
    public string Socks5Address { get; set; } = ":1080";
    public string Mode { get; set; } = "l4"; // l3 | l4
    public string? Url { get; set; }
    public string? MaxToken { get; set; }
    public string? MaxUid { get; set; }
    public string? LocalIp { get; set; }
    public string Codec { get; set; } = "batched"; // batched | legacy
    public string? EncryptionKey { get; set; }
    public int BenchBytes { get; set; } = 0;
    public bool BenchCompressible { get; set; } = false;
    public string? ExtraArgs { get; set; }

    // Limits & Statistics
    public int ClientLimit { get; set; } = 0; // 0 = unlimited
    public long TrafficLimitBytes { get; set; } = 0; // 0 = unlimited
    public long UploadBytes { get; set; } = 0;
    public long DownloadBytes { get; set; } = 0;
    public int ConnectedClients { get; set; } = 0;

    // State & Persistence
    public bool IsEnabled { get; set; } = false;
    public TunnelStatus Status { get; set; } = TunnelStatus.Stopped;
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastStoppedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public int RestartAttempts { get; set; } = 0;
}
