using System.Security.Cryptography;
using System.Text.Json;

namespace OpenFlux.Zen.Server.Installer.Security;

public static class CredentialGenerator
{
    public static string GenerateSecretPath(int byteLength = 8)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GeneratePassword(int length = 16)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }
        return new string(result);
    }

    public static (string Username, string Password, string SecretPath, string PublishMode, string Domain, string PublicUrl, string? LtPass, string Host, string? DecoyUrl, string? DecoyMode) LoadExisting(string credPath)
    {
        if (!File.Exists(credPath)) return ("", "", "", "", "", "", null, "", null, null);
        try
        {
            var json = File.ReadAllText(credPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var u = root.TryGetProperty("username", out var pu) ? pu.GetString() ?? "" : "";
            var p = root.TryGetProperty("password", out var pp) ? pp.GetString() ?? "" : "";
            var s = root.TryGetProperty("secretPath", out var ps) ? ps.GetString() ?? "" : "";
            var m = root.TryGetProperty("publishMode", out var pm) ? pm.GetString() ?? "" : "";
            var d = root.TryGetProperty("domain", out var pd) ? pd.GetString() ?? "" : "";
            var puUrl = root.TryGetProperty("publicUrl", out var ppu) ? ppu.GetString() ?? "" : "";
            var lt = root.TryGetProperty("localtunnelPassword", out var plt) ? plt.GetString() : null;
            var h = root.TryGetProperty("host", out var ph) ? ph.GetString() ?? "" : "";
            var du = root.TryGetProperty("decoyRedirectUrl", out var pdu) ? pdu.GetString() : null;
            var dm = root.TryGetProperty("decoyMode", out var pdm) ? pdm.GetString() : null;
            return (u, p, s, m, d, puUrl, lt, h, du, dm);
        }
        catch
        {
            return ("", "", "", "", "", "", null, "", null, null);
        }
    }

    public static void SaveCredentials(
        string credPath,
        string username,
        string password,
        string secretPath,
        string host,
        string publicUrl,
        string publishMode,
        string? domain,
        string? localtunnelPassword,
        string language,
        bool autostart,
        string? decoyRedirectUrl = null,
        string? decoyMode = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(credPath)!);
        var dict = new Dictionary<string, object?>
        {
            ["username"] = username,
            ["password"] = password,
            ["secretPath"] = secretPath,
            ["host"] = host,
            ["publicUrl"] = publicUrl,
            ["publishMode"] = publishMode,
            ["language"] = language,
            ["autostart"] = autostart,
            ["updatedAt"] = DateTime.UtcNow.ToString("o")
        };

        if (!string.IsNullOrEmpty(domain)) dict["domain"] = domain;
        if (!string.IsNullOrEmpty(localtunnelPassword)) dict["localtunnelPassword"] = localtunnelPassword;
        if (!string.IsNullOrEmpty(decoyRedirectUrl)) dict["decoyRedirectUrl"] = decoyRedirectUrl;
        if (!string.IsNullOrEmpty(decoyMode)) dict["decoyMode"] = decoyMode;

        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(credPath, json);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(credPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
        }
    }
}
