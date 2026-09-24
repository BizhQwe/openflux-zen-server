namespace OpenFlux.Zen.Server.Models;

public sealed class SystemStats
{
    public int TotalTunnels { get; set; }
    public int ActiveTunnels { get; set; }
    public long TotalUploadBytes { get; set; }
    public long TotalDownloadBytes { get; set; }
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
