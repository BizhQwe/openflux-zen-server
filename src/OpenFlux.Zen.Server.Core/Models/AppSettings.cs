namespace OpenFlux.Zen.Server.Models;

public sealed class AppSettings
{
    public int Id { get; set; } = 1;
    public string Username { get; set; } = "admin";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string SecretPath { get; set; } = "";
    public string ListenHost { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 5000;
    public string? PublicUrl { get; set; }
    public string PublishMode { get; set; } = "local"; // local | domain | zrok
    public string? Domain { get; set; }
    public string? ZrokToken { get; set; }
    public string? ZrokShareUrl { get; set; }
    public bool AutoStartEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
