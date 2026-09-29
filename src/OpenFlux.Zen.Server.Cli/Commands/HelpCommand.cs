using OpenFlux.Zen.Server.Cli.UI;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class HelpCommand
{
    public static void PrintHelp()
    {
        var isRu = CliUi.IsRussian;

        CliUi.Header(isRu 
            ? "OpenFlux Zen Server — Управление сервером (CLI)" 
            : "OpenFlux Zen Server — CLI Management Tool");

        if (isRu)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Использование: openflux <команда> [опции]");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Команды управления службой:");
            Console.ResetColor();
            PrintItem("start", "Запустить службу сервера OpenFlux Zen");
            PrintItem("stop", "Остановить службу сервера OpenFlux Zen");
            PrintItem("restart", "Перезапустить службу сервера OpenFlux Zen");
            PrintItem("status", "Показать текущий статус сервера и службы");
            PrintItem("autostart", "Автозапуск при загрузке системы (enable | disable | status)");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Основные команды:");
            Console.ResetColor();
            PrintItem("credentials", "Показать данные входа (логин, пароль, ссылки)");
            PrintItem("uninstall", "Полное удаление OpenFlux Zen Server из системы");
            PrintItem("help", "Показать эту справку");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Примеры:");
            Console.ResetColor();
            PrintExample("openflux status");
            PrintExample("openflux credentials");
            PrintExample("openflux restart");
            PrintExample("openflux autostart enable");
            PrintExample("openflux uninstall -y");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Usage: openflux <command> [options]");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Service Management Commands:");
            Console.ResetColor();
            PrintItem("start", "Start OpenFlux Zen Server service");
            PrintItem("stop", "Stop OpenFlux Zen Server service");
            PrintItem("restart", "Restart OpenFlux Zen Server service");
            PrintItem("status", "Show current running status of the server");
            PrintItem("autostart", "Configure autostart on system boot (enable | disable | status)");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  General Commands:");
            Console.ResetColor();
            PrintItem("credentials", "Display login credentials, secret path & URLs");
            PrintItem("uninstall", "Completely uninstall OpenFlux Zen Server from system");
            PrintItem("help", "Show this help information");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Examples:");
            Console.ResetColor();
            PrintExample("openflux status");
            PrintExample("openflux credentials");
            PrintExample("openflux restart");
            PrintExample("openflux autostart enable");
            PrintExample("openflux uninstall -y");
        }

        Console.WriteLine();
        CliUi.Divider();
        Console.WriteLine();
    }

    private static void PrintItem(string cmd, string desc)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"    {cmd.PadRight(14)}");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(desc);
        Console.ResetColor();
    }

    private static void PrintExample(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("    $ ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(cmd);
        Console.ResetColor();
    }
}
