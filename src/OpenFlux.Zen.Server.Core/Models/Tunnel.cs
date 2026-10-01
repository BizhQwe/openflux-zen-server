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

    // OpenFlux v0.2.0+ Session & Transport Features
    public bool EnableShare { get; set; } = true; // --share
    public string? ShareHost { get; set; } // --share-host
    public string? ShareLink { get; set; } // openflux:// link captured from stdout/stderr or constructed
    public string? DirectListen { get; set; } // --direct-listen (e.g. 0.0.0.0:8445)
    public string? DirectDial { get; set; } // --direct-dial
    public string? SessionContext { get; set; } // --session-context
    public bool Negotiate { get; set; } = false; // --negotiate
    public string? Transports { get; set; } // --transports=direct:100,yandex:50
    public int MaxPacketSize { get; set; } = 65000; // --max-packet-size (1280..65000)
    public string? YandexCookiesFile { get; set; } // --yandex-cookies-file

    // Limits & Statistics
    public int ClientLimit { get; set; } = 0; // 0 = unlimited
    public long TrafficLimitBytes { get; set; } = 0; // 0 = unlimited
    public long UploadBytes { get; set; } = 0;
    public long DownloadBytes { get; set; } = 0;
    public int ConnectedClients { get; set; } = 0;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public long UploadRateBytesPerSec { get; set; } = 0;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public long DownloadRateBytesPerSec { get; set; } = 0;

    // State & Persistence
    public bool IsEnabled { get; set; } = false;
    public TunnelStatus Status { get; set; } = TunnelStatus.Stopped;
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastStoppedAt { get; set; }
    public string? ErrorMessage { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? PendingCaptchaUrl { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? PendingCaptchaReason { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? PendingCaptchaProxy { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool PendingCaptchaRemote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public int RestartAttempts { get; set; } = 0;
}
