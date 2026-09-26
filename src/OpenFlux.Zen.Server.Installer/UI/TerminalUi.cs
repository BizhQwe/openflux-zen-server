namespace OpenFlux.Zen.Server.Installer.UI;

public static class TerminalUi
{
    public static string Language { get; set; } = "ru";

    public static bool IsRussian => string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase);

    public static void ShowBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  OpenFlux Zen Server — " + (IsRussian ? "Установщик" : "Installer"));
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', 58));
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void ShowDivider()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', 58));
        Console.ResetColor();
    }

    public static void StartStep(int current, int total, string description)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"  [{current}/{total}] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(description.PadRight(46));
        Console.ResetColor();
    }

    public static void CompleteStep(bool success, string? detail = null)
    {
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OK]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR]");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"      -> {detail}");
            }
        }
        Console.ResetColor();
    }

    public static int AskChoice(string title, string[] options, int defaultIndex)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  {title}:");
        Console.ResetColor();

        for (int i = 0; i < options.Length; i++)
        {
            Console.Write($"    {i + 1}) ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(options[i]);
            Console.ResetColor();
        }

        Console.Write($"  " + (IsRussian ? "Выберите вариант" : "Select option") + $" [1-{options.Length}, default: {defaultIndex + 1}]: ");
        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out var chosen) && chosen >= 1 && chosen <= options.Length)
        {
            Console.WriteLine();
            return chosen - 1;
        }

        Console.WriteLine();
        return defaultIndex;
    }

    public static bool AskYesNo(string question, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n, default: Y]" : "[y/N, default: N]";
        Console.Write($"  {question} {hint}: ");
        var input = Console.ReadLine()?.Trim();
        Console.WriteLine();

        if (string.IsNullOrEmpty(input)) return defaultYes;
        if (input.StartsWith("y", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("n", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultYes;
    }

    public static string AskText(string prompt, string defaultValue = "")
    {
        Console.Write($"  {prompt}" + (!string.IsNullOrEmpty(defaultValue) ? $" [default: {defaultValue}]: " : ": "));
        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) ? defaultValue : input;
    }

    public static void ShowSummaryCard(
        string publicUrl,
        string localUrl,
        string username,
        string password)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  " + new string('=', 62));
        var title = IsRussian 
            ? "OpenFlux Zen Server — УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!" 
            : "OpenFlux Zen Server — SUCCESSFULLY INSTALLED AND STARTED!";
        Console.WriteLine($"  {title}");
        Console.WriteLine("  " + new string('=', 62));
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("    " + (IsRussian ? "Панель управления:" : "Web Dashboard:    ").PadRight(22));
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(publicUrl);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("    " + (IsRussian ? "Локальный адрес:" : "Local Access:     ").PadRight(22));
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(localUrl);

        PrintField(IsRussian ? "Логин:" : "Username:", username);
        PrintField(IsRussian ? "Пароль:" : "Password:", password);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  " + new string('=', 62));
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintField(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"    {label,-20}  ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }
}
