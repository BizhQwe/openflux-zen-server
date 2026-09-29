using OpenFlux.Zen.Server.Cli.Commands;
using OpenFlux.Zen.Server.Cli.UI;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Check for --lang argument override
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--lang" || args[i] == "-l") && i + 1 < args.Length)
    {
        CliUi.Language = args[i + 1].ToLowerInvariant();
        break;
    }
}

var cleanArgs = args.Where(a => a != "--lang" && a != "-l" && a != "ru" && a != "en").ToArray();

if (cleanArgs.Length == 0)
{
    HelpCommand.PrintHelp();
    return 0;
}

var command = cleanArgs[0].ToLowerInvariant().TrimStart('-', '/');

switch (command)
{
    case "help":
    case "h":
    case "?":
    case "помощь":
    case "справка":
        HelpCommand.PrintHelp();
        return 0;

    case "start":
    case "up":
    case "run":
    case "старт":
    case "запуск":
        await ServerCommands.ExecuteStartAsync();
        return 0;

    case "stop":
    case "down":
    case "стоп":
    case "остановить":
        await ServerCommands.ExecuteStopAsync();
        return 0;

    case "restart":
    case "reboot":
    case "рестарт":
    case "перезапуск":
        await ServerCommands.ExecuteRestartAsync();
        return 0;

    case "status":
    case "info":
    case "статус":
    case "инфо":
        await ServerCommands.ExecuteStatusAsync();
        return 0;

    case "autostart":
    case "autorun":
    case "автозапуск":
        await ServerCommands.ExecuteAutostartAsync(cleanArgs);
        return 0;

    case "credentials":
    case "cred":
    case "creds":
    case "пароль":
    case "пароли":
    case "доступ":
    case "учетка":
        await CredentialsCommand.PrintCredentialsAsync(cleanArgs);
        return 0;

    case "uninstall":
    case "remove":
    case "delete":
    case "удалить":
    case "деинсталляция":
        await UninstallCommand.ExecuteUninstallAsync(cleanArgs);
        return 0;

    default:
        var isRu = CliUi.IsRussian;
        CliUi.Error(isRu 
            ? $"Неизвестная команда: '{cleanArgs[0]}'" 
            : $"Unknown command: '{cleanArgs[0]}'");
        Console.WriteLine();
        HelpCommand.PrintHelp();
        return 1;
}
