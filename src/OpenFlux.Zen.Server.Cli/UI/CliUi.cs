using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Data;

namespace OpenFlux.Zen.Server.Cli.UI;

public static class CliUi
{
    private static string? _language;

    public static string Language
    {
        get
        {
            if (_language != null) return _language;

            // 1. Explicit environment variable
            var envLang = Environment.GetEnvironmentVariable("OPENFLUX_LANGUAGE");
            if (!string.IsNullOrWhiteSpace(envLang))
            {
                _language = envLang.ToLowerInvariant();
                return _language;
            }

            // 2. Load from .credentials
            var credPath = AppPaths.GetCredentialsPath();
            if (File.Exists(credPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(credPath));
                    if (doc.RootElement.TryGetProperty("language", out var l) && !string.IsNullOrWhiteSpace(l.GetString()))
                    {
                        _language = l.GetString()!.ToLowerInvariant();
                        return _language;
                    }
                }
                catch { }
            }

            // 3. Load from database Settings table
            var dbPath = AppPaths.GetDatabasePath();
            if (File.Exists(dbPath))
            {
                try
                {
                    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
                    using var db = new AppDbContext(options);
                    var s = db.Settings.FirstOrDefault(x => x.Id == 1);
                    if (s != null && !string.IsNullOrWhiteSpace(s.Language))
                    {
                        _language = s.Language.ToLowerInvariant();
                        return _language;
                    }
                }
                catch { }
            }

            // 4. Default based on current OS UI culture
            var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            _language = culture == "ru" ? "ru" : "en";
            return _language;
        }
        set => _language = value;
    }

    public static bool IsRussian => string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase);

    public static void Header(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 62));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 62));
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void Divider()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 62));
        Console.ResetColor();
    }

    public static void Field(string label, string value, ConsoleColor valueColor = ConsoleColor.White)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"  {label} ");
        Console.ForegroundColor = valueColor;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  [INFO] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  [OK] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  [WARN] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("  [ERROR] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static bool AskYesNo(string question, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n, default: Y]" : "[y/N, default: N]";
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"  {question} {hint}: ");
        Console.ResetColor();
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input)) return defaultYes;
        if (input.StartsWith("y", StringComparison.OrdinalIgnoreCase) || input.StartsWith("д", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("n", StringComparison.OrdinalIgnoreCase) || input.StartsWith("н", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultYes;
    }
}
