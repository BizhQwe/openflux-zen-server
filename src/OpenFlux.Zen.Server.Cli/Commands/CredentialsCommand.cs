using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Cli.UI;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Data;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class CredentialsCommand
{
    public static async Task PrintCredentialsAsync(string[] cliArgs)
    {
        var credPath = AppPaths.GetCredentialsPath();
        var dbPath = AppPaths.GetDatabasePath();

        string username = "admin";
        string password = "(Hidden / stored hashed in DB)";
        string secretPath = "zen-admin";
        int port = 5000;
        string? publicUrl = null;
        string? localtunnelPassword = null;
        string? publishMode = null;

        if (File.Exists(credPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(credPath);
                var doc = JsonSerializer.Deserialize<JsonElement>(content);
                if (doc.TryGetProperty("username", out var u)) username = u.GetString() ?? username;
                if (doc.TryGetProperty("password", out var p)) password = p.GetString() ?? password;
                if (doc.TryGetProperty("secretPath", out var s)) secretPath = s.GetString() ?? secretPath;
                if (doc.TryGetProperty("publicUrl", out var pub)) publicUrl = pub.GetString();
                if (doc.TryGetProperty("localtunnelPassword", out var lp)) localtunnelPassword = lp.GetString();
                if (doc.TryGetProperty("publishMode", out var pm)) publishMode = pm.GetString();
            }
            catch { }
        }

        if (string.IsNullOrEmpty(secretPath) || password.StartsWith("("))
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
                    using var db = new AppDbContext(options);
                    var s = await db.Settings.FirstOrDefaultAsync(x => x.Id == 1);
                    if (s != null)
                    {
                        username = s.Username;
                        secretPath = s.SecretPath;
                        port = s.ListenPort;
                        publicUrl = s.PublicUrl;
                        if (string.IsNullOrEmpty(publishMode)) publishMode = s.PublishMode;
                    }
                }
                catch { }
            }
        }

        if (string.IsNullOrEmpty(secretPath))
        {
            secretPath = "zen-admin";
        }

        var localUrl = $"http://127.0.0.1:{port}/{secretPath.Trim('/')}/";
        var finalUrl = !string.IsNullOrWhiteSpace(publicUrl) ? publicUrl : localUrl;
        var isRu = CliUi.IsRussian;
        bool isLan = publishMode == "local" || publishMode == "lan";

        CliUi.Header(isRu 
            ? "OpenFlux Zen Server — Учётные данные и доступ" 
            : "OpenFlux Zen Server — Credentials & Access");

        string mainLabel = isLan
            ? (isRu ? "Вход по LAN / Wi-Fi:" : "LAN / Wi-Fi Access:")
            : (isRu ? "Панель управления:" : "Web Dashboard:");

        string localLabel = isLan
            ? (isRu ? "Этот ПК (localhost):" : "This PC (localhost):")
            : (isRu ? "Локальный адрес:" : "Local Access:");

        CliUi.Field(mainLabel, finalUrl, ConsoleColor.Yellow);

        if (finalUrl != localUrl)
        {
            CliUi.Field(localLabel, localUrl, ConsoleColor.White);
        }

        if (!string.IsNullOrWhiteSpace(localtunnelPassword) && finalUrl.Contains(".loca.lt"))
        {
            string ltLabel = isRu ? "Пароль Localtunnel (IP):" : "Localtunnel Password (IP):";
            CliUi.Field(ltLabel, localtunnelPassword, ConsoleColor.Yellow);
        }

        string userLabel = isRu ? "Логин:" : "Username:";
        string passLabel = isRu ? "Пароль:" : "Password:";
        string pathLabel = isRu ? "Секретный путь:" : "Secret Path:";

        CliUi.Field(userLabel, username);
        CliUi.Field(passLabel, password);
        CliUi.Field(pathLabel, $"/{secretPath.Trim('/')}/");

        Console.WriteLine();
        CliUi.Divider();
        Console.WriteLine();
    }
}
