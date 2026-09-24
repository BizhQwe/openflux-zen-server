namespace OpenFlux.Zen.Server.Cli.Commands;

public static class HelpCommand
{
    public static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine("                  OpenFlux Zen Server CLI                         ");
        Console.WriteLine("==================================================================");
        Console.ResetColor();
        Console.WriteLine("Usage: OpenFluxZenServer <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Server Management Commands:");
        Console.WriteLine("  start        Start OpenFlux Zen Server service");
        Console.WriteLine("  stop         Stop OpenFlux Zen Server service");
        Console.WriteLine("  restart      Restart OpenFlux Zen Server service");
        Console.WriteLine("  status       Show current running status of the server");
        Console.WriteLine("  autostart    Configure autostart on system boot (enable | disable | status)");
        Console.WriteLine();
        Console.WriteLine("General Commands:");
        Console.WriteLine("  credentials  Display current login credentials, secret path & URLs");
        Console.WriteLine("  uninstall    Completely uninstall OpenFlux Zen Server from system");
        Console.WriteLine("  help         Show this help information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  OpenFluxZenServer start");
        Console.WriteLine("  OpenFluxZenServer stop");
        Console.WriteLine("  OpenFluxZenServer restart");
        Console.WriteLine("  OpenFluxZenServer status");
        Console.WriteLine("  OpenFluxZenServer autostart enable");
        Console.WriteLine("  OpenFluxZenServer autostart disable");
        Console.WriteLine("  OpenFluxZenServer autostart status");
        Console.WriteLine("  OpenFluxZenServer credentials");
        Console.WriteLine("  OpenFluxZenServer uninstall -y");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.ResetColor();
    }
}
