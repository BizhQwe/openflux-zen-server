using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine("                 OpenFlux Zen Server - Credentials                ");
        Console.WriteLine("==================================================================");
        Console.ResetColor();
        Console.WriteLine($"  Web Dashboard URL : {finalUrl}");
        Console.WriteLine($"  Local Access URL  : {localUrl}");
        Console.WriteLine($"  Secret Path       : /{secretPath.Trim('/')}/");
        Console.WriteLine($"  Username          : {username}");
        Console.WriteLine($"  Password          : {password}");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.ResetColor();
    }
}
