namespace OpenFlux.Zen.Server.Models;

public sealed class SystemStats
{
    public int TotalTunnels { get; set; }
    public int ActiveTunnels { get; set; }
    public long TotalUploadBytes { get; set; }
    public long TotalDownloadBytes { get; set; }
    public long UploadRateBytesPerSec { get; set; }
    public long DownloadRateBytesPerSec { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double MemoryUsagePercent => MemoryTotalBytes > 0 ? Math.Round((double)MemoryUsedBytes / MemoryTotalBytes * 100.0, 1) : 0;
    public string PanelUptime { get; set; } = "";
    public string SystemUptime { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
}

public sealed class TunnelLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Stream { get; set; } = "stdout"; // stdout | stderr | system
    public string Message { get; set; } = "";
}

public sealed class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class LoginResponse
{
    public bool Success { get; set; }
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string? NewUsername { get; set; }
    public string? NewPassword { get; set; }
}

public sealed class ExportConfigDto
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public List<Tunnel> Tunnels { get; set; } = new();
}

public sealed class CredentialsResponse
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SecretPath { get; set; } = "";
    public string LocalUrl { get; set; } = "";
    public string? PublicUrl { get; set; }
}

public sealed class AutostartRequest
{
    public bool Enabled { get; set; }
}

public sealed class OpenFluxCoreVersionInfo
{
    public string CurrentVersion { get; set; } = "v0.2.0";
    public string? LatestVersion { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string BinaryName { get; set; } = "";
    public string BinaryPath { get; set; } = "";
    public long BinarySizeBytes { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public bool IsUpdating { get; set; }
}

public sealed class OpenFluxCoreUpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public string NewVersion { get; set; } = "";
}

public sealed class PanelVersionInfo
{
    public string CurrentVersion { get; set; } = "v1.0.72";
    public string? LatestVersion { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public bool IsUpdating { get; set; }
}

public sealed class PanelUpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public string NewVersion { get; set; } = "";
}

public sealed class ReleaseItemDto
{
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime? PublishedAt { get; set; }
    public bool Prerelease { get; set; }
    public string? HtmlUrl { get; set; }
}

public sealed class VersionUpdateRequest
{
    public string? TargetVersion { get; set; }
}

public sealed class NetworkPlacementRequest
{
    public string Mode { get; set; } = "local"; // domain | localtunnel | local | localhost
    public string? Domain { get; set; }
}

public sealed class NetworkPlacementResponse
{
    public bool Success { get; set; }
    public string Mode { get; set; } = "local";
    public string? Domain { get; set; }
    public string PublicUrl { get; set; } = "";
    public string? LocaltunnelPassword { get; set; }
    public string Message { get; set; } = "";
}


