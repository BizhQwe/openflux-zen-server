using System.Diagnostics;
using System.Text.Json;
using OpenFlux.Zen.Server.Installer.Packaging;
using OpenFlux.Zen.Server.Installer.Platform;
using OpenFlux.Zen.Server.Installer.Security;
using OpenFlux.Zen.Server.Installer.UI;

namespace OpenFlux.Zen.Server.Installer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--help") || args.Contains("-h"))
        {
            ShowHelp();
            return 0;
        }

        if (args.Contains("--uninstall"))
        {
            return ExecuteUninstall();
        }

        // Elevate or check admin rights
        if (!SystemOperations.IsAdmin())
        {
            if (OperatingSystem.IsWindows() && !args.Contains("--silent"))
            {
                try
                {
                    var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                    return 0;
                }
                catch
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [ERROR] Please run this installer as Administrator!");
                    Console.ResetColor();
                    return 1;
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [ERROR] Root privileges required. Run with: sudo ./openflux-installer");
            Console.ResetColor();
            return 1;
        }

        // Language selection
        var langArg = GetArgValue(args, "--lang") ?? Environment.GetEnvironmentVariable("OPENFLUX_LANGUAGE");
        if (!string.IsNullOrEmpty(langArg))
        {
            TerminalUi.Language = langArg;
        }
        else if (!args.Contains("--silent"))
        {
            Console.WriteLine();
            var langChoice = TerminalUi.AskChoice(
                "Язык / Language",
                new[] { "Русский", "English" },
                0);
            TerminalUi.Language = (langChoice == 1) ? "en" : "ru";
        }
        else
        {
            TerminalUi.Language = "ru";
        }

        var isRu = TerminalUi.IsRussian;
        var installDir = SystemOperations.GetDefaultInstallDir();
        var credPath = Path.Combine(installDir, "data", ".credentials");

        // Load existing settings if upgrading
        var existing = CredentialGenerator.LoadExisting(credPath);

        // Publish Mode
        string publishMode = "domain";
        string? domain = null;
        string? localtunnelPassword = existing.LtPass;

        var modeArg = GetArgValue(args, "--mode") ?? Environment.GetEnvironmentVariable("OPENFLUX_PUBLISH_MODE");
        if (!string.IsNullOrEmpty(modeArg))
        {
            publishMode = modeArg.ToLowerInvariant();
        }
        else if (!args.Contains("--silent"))
        {
            var modeOptions = isRu
                ? new[]
                {
                    "Открытый порт со своим доменом (или внешний IP)",
                    "Localtunnel (доступ без белого IP)",
                    "Локальный доступ (только 127.0.0.1)"
                }
                : new[]
                {
                    "Open port with custom domain (or server public IP)",
                    "Localtunnel (access without public IP)",
                    "Local access only (127.0.0.1)"
                };

            var chosenMode = TerminalUi.AskChoice(
                isRu ? "Сетевое размещение" : "Network accessibility",
                modeOptions,
                0);

            publishMode = chosenMode switch
            {
                0 => "domain",
                1 => "localtunnel",
                _ => "local"
            };

            if (publishMode == "domain")
            {
                var domPrompt = isRu 
                    ? "Введите ваш домен (или нажмите Enter для внешнего IP)" 
                    : "Enter your domain (or press Enter for external server IP)";
                domain = TerminalUi.AskText(domPrompt, existing.Domain);
            }
        }

        // Port selection
        int listenPort = 5000;
        var portArg = GetArgValue(args, "--port") ?? Environment.GetEnvironmentVariable("OPENFLUX_PORT");
        if (int.TryParse(portArg, out var pNum) && pNum > 0 && pNum < 65536)
        {
            listenPort = pNum;
        }

        // Autostart choice
        bool autostart = true;
        if (!args.Contains("--silent"))
        {
            var autoPrompt = isRu
                ? "Включить автозапуск службы при загрузке системы?"
                : "Enable server autostart on system boot?";
            autostart = TerminalUi.AskYesNo(autoPrompt, defaultYes: true);
            Console.WriteLine();
        }

        // -------------------------------------------------------------
        // Step 1: Payload extraction
        // -------------------------------------------------------------
        TerminalUi.StartStep(1, 3, isRu ? "Распаковка компонентов сервера..." : "Unpacking server components...");
        try
        {
            SystemOperations.StopExistingServer();
            await PayloadExtractor.ExtractPayloadAsync(installDir);
            TerminalUi.CompleteStep(true);
        }
        catch (Exception ex)
        {
            TerminalUi.CompleteStep(false, ex.Message);
            return 1;
        }

        // -------------------------------------------------------------
        // Step 2: Credentials and system service configuration
        // -------------------------------------------------------------
        TerminalUi.StartStep(2, 3, isRu ? "Настройка службы и окружения..." : "Configuring system service...");
        string username = !string.IsNullOrEmpty(existing.Username) ? existing.Username : "admin";
        string password = !string.IsNullOrEmpty(existing.Password) ? existing.Password : CredentialGenerator.GeneratePassword(16);
        string secretPath = !string.IsNullOrEmpty(existing.SecretPath) ? existing.SecretPath : CredentialGenerator.GenerateSecretPath(8);

        string localUrl = $"http://127.0.0.1:{listenPort}/{secretPath}/";
        string publicUrl = localUrl;

        try
        {
            if (publishMode == "localtunnel")
            {
                var subPrefix = $"openflux-{(secretPath.Length >= 8 ? secretPath[..8] : secretPath)}";
                publicUrl = $"https://{subPrefix}.loca.lt/{secretPath}/";
                if (string.IsNullOrEmpty(localtunnelPassword))
                {
                    localtunnelPassword = await SystemOperations.FetchPublicIpAsync();
                }
            }
            else if (publishMode == "domain")
            {
                SystemOperations.ConfigureFirewall(listenPort);
                if (!string.IsNullOrEmpty(domain))
                {
                    var cleanDom = domain.Trim().TrimEnd('/');
                    var proto = cleanDom.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                cleanDom.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
                    publicUrl = $"{proto}{cleanDom}:{listenPort}/{secretPath}/";
                }
                else
                {
                    var pubIp = await SystemOperations.FetchPublicIpAsync();
                    publicUrl = !string.IsNullOrEmpty(pubIp) 
                        ? $"http://{pubIp}:{listenPort}/{secretPath}/" 
                        : $"http://<server-ip>:{listenPort}/{secretPath}/";
                }
            }

            CredentialGenerator.SaveCredentials(
                credPath,
                username,
                password,
                secretPath,
                publicUrl,
                publishMode,
                domain,
                localtunnelPassword,
                TerminalUi.Language,
                autostart);

            SystemOperations.RegisterPath(installDir);
            SystemOperations.ConfigureService(
                installDir,
                username,
                password,
                secretPath,
                listenPort,
                publicUrl,
                publishMode,
                TerminalUi.Language,
                autostart);

            TerminalUi.CompleteStep(true);
        }
        catch (Exception ex)
        {
            TerminalUi.CompleteStep(false, ex.Message);
            return 1;
        }

        // -------------------------------------------------------------
        // Step 3: Start server and verify readiness
        // -------------------------------------------------------------
        TerminalUi.StartStep(3, 3, isRu ? "Запуск сервера и проверка туннеля..." : "Starting server & verifying tunnel...");
        try
        {
            SystemOperations.StartServer(
                installDir,
                username,
                password,
                secretPath,
                listenPort,
                publicUrl,
                publishMode,
                TerminalUi.Language);

            if (publishMode == "localtunnel")
            {
                // Wait up to 8 seconds for the live localtunnel connection to register in .credentials
                for (int i = 0; i < 16; i++)
                {
                    await Task.Delay(500);
                    var updated = CredentialGenerator.LoadExisting(credPath);
                    if (!string.IsNullOrEmpty(updated.PublicUrl) && updated.PublicUrl.Contains(".loca.lt"))
                    {
                        publicUrl = updated.PublicUrl;
                        if (!string.IsNullOrEmpty(updated.LtPass))
                        {
                            localtunnelPassword = updated.LtPass;
                        }
                        break;
                    }
                }
            }
            else
            {
                await Task.Delay(1000);
            }

            TerminalUi.CompleteStep(true);
        }
        catch (Exception ex)
        {
            TerminalUi.CompleteStep(false, ex.Message);
            return 1;
        }

        // Show clean summary card
        TerminalUi.ShowSummaryCard(
            publicUrl,
            localUrl,
            username,
            password);

        return 0;
    }

    private static int ExecuteUninstall()
    {
        var isRu = TerminalUi.IsRussian;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(isRu 
            ? "  Удаление OpenFlux Zen Server..." 
            : "  Uninstalling OpenFlux Zen Server...");
        Console.ResetColor();

        SystemOperations.StopExistingServer();

        var installDir = SystemOperations.GetDefaultInstallDir();
        if (OperatingSystem.IsWindows())
        {
            SystemOperations.RunCommand("schtasks.exe", "/delete /tn \"OpenFluxZenServer\" /f >nul 2>&1");
        }
        else
        {
            SystemOperations.RunBash("systemctl disable --now openflux-zen-server.service 2>/dev/null || true");
            try { File.Delete("/etc/systemd/system/openflux-zen-server.service"); } catch { }
            try { File.Delete("/usr/local/bin/OpenFluxZenServer"); } catch { }
            SystemOperations.RunBash("systemctl daemon-reload");
        }

        if (Directory.Exists(installDir))
        {
            try
            {
                // Delete everything except data folder to avoid accidental data loss
                foreach (var f in Directory.GetFiles(installDir))
                {
                    try { File.Delete(f); } catch { }
                }
                foreach (var d in Directory.GetDirectories(installDir))
                {
                    if (!d.EndsWith("data", StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(d, true); } catch { }
                    }
                }
            }
            catch { }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(isRu 
            ? "  OpenFlux Zen Server успешно удален." 
            : "  OpenFlux Zen Server uninstalled successfully.");
        Console.ResetColor();
        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
OpenFlux Zen Server Installer
Usage:
  openflux-installer [options]

Options:
  --silent                Run non-interactive automated installation
  --lang <ru|en>          Set installer and dashboard language
  --mode <tunnel|domain|local> Set publishing mode (localtunnel, domain, local)
  --port <port>           Set listen port (default: 5000)
  --uninstall             Stop service and uninstall application
  -h, --help              Show help message
""");
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
