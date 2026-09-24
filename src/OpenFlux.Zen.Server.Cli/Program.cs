using OpenFlux.Zen.Server.Cli.Commands;

if (args.Length == 0)
{
    HelpCommand.PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant().TrimStart('-', '/');

switch (command)
{
    case "help":
    case "h":
    case "?":
        HelpCommand.PrintHelp();
        return 0;

    case "start":
    case "up":
    case "run":
        await ServerCommands.ExecuteStartAsync();
        return 0;

    case "stop":
    case "down":
        await ServerCommands.ExecuteStopAsync();
        return 0;

    case "restart":
    case "reboot":
        await ServerCommands.ExecuteRestartAsync();
        return 0;

    case "status":
    case "info":
        await ServerCommands.ExecuteStatusAsync();
        return 0;

    case "autostart":
    case "autorun":
        await ServerCommands.ExecuteAutostartAsync(args);
        return 0;

    case "credentials":
    case "cred":
    case "creds":
        await CredentialsCommand.PrintCredentialsAsync(args);
        return 0;

    case "uninstall":
        await UninstallCommand.ExecuteUninstallAsync(args);
        return 0;

    default:
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] Unknown command: '{args[0]}'");
        Console.ResetColor();
        Console.WriteLine();
        HelpCommand.PrintHelp();
        return 1;
}
