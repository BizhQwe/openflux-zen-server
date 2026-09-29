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

        // Check admin / root rights
        if (!SystemOperations.IsAdmin())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            if (OperatingSystem.IsWindows())
            {
                Console.WriteLine("  [ERROR] Administrator privileges required.");
                Console.WriteLine("  Please run PowerShell as Administrator (Right-click -> Run as administrator) and retry.");
            }
            else
            {
                Console.WriteLine("  [ERROR] Root privileges required. Run with: sudo ./openflux-installer");
            }
            Console.ResetColor();
            return 1;
        }

        // Language selection
        var langArg = GetArgValue(args, "--lang");
        if (!string.IsNullOrEmpty(langArg))
        {
            TerminalUi.Language = langArg;
        }
        else if (!args.Contains("--silent"))
        {
            Console.WriteLine();
            var langChoice = TerminalUi.AskChoice(
                "Language / Язык",
                new[] { "English", "Русский" },
                0);
            TerminalUi.Language = (langChoice == 1) ? "ru" : "en";
        }
        else
        {
            TerminalUi.Language = Environment.GetEnvironmentVariable("OPENFLUX_LANGUAGE") ?? "en";
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
        string? decoyMode = existing.DecoyMode ?? Environment.GetEnvironmentVariable("OPENFLUX_DECOY_MODE");
        string? decoyRedirectUrl = existing.DecoyUrl ?? Environment.GetEnvironmentVariable("OPENFLUX_DECOY_REDIRECT_URL");

        var modeArg = GetArgValue(args, "--mode");
        if (!string.IsNullOrEmpty(modeArg))
        {
            publishMode = modeArg.ToLowerInvariant();
        }
        else if (!args.Contains("--silent"))
        {
            var modeOptions = isRu
                ? new[]
                {
                    "Прямое подключение (домен / публичный IP)",
                    "Через Localtunnel (без публичного IP)",
                    "Через локальную сеть (LAN / Wi-Fi)",
                    "Только на этом ПК (localhost)"
                }
                : new[]
                {
                    "Direct connection (domain / public IP)",
                    "Via Localtunnel (no public IP required)",
                    "Local network (LAN / Wi-Fi)",
                    "Only on this PC (localhost)"
                };

            int defaultModeIdx = 0;
            if (existing.PublishMode == "localtunnel") defaultModeIdx = 1;
            else if (existing.PublishMode == "local" || existing.PublishMode == "lan") defaultModeIdx = 2;
            else if (existing.PublishMode == "localhost") defaultModeIdx = 3;

            var chosenMode = TerminalUi.AskChoice(
                isRu ? "Сетевое размещение" : "Network accessibility",
                modeOptions,
                defaultModeIdx);

            publishMode = chosenMode switch
            {
                0 => "domain",
                1 => "localtunnel",
                2 => "local",
                _ => "localhost"
            };

            if (publishMode == "domain")
            {
                var domPrompt = isRu 
                    ? "Введите ваш домен (или нажмите Enter для внешнего IP)" 
                    : "Enter your domain (or press Enter for external server IP)";
                domain = TerminalUi.AskText(domPrompt, existing.Domain);

                // Automatically detect if server or domain already has an existing decoy/website
                bool hasExistingDecoy = SystemOperations.HasLocalDecoySite()
                                        || existing.DecoyMode == "existing"
                                        || !string.IsNullOrEmpty(existing.DecoyUrl);

                if (!hasExistingDecoy && !string.IsNullOrWhiteSpace(domain))
                {
                    hasExistingDecoy = await SystemOperations.CheckDomainHasLiveSiteAsync(domain);
                }

                if (hasExistingDecoy)
                {
                    decoyMode = "existing";
                    if (!string.IsNullOrWhiteSpace(domain))
                    {
                        var cleanDom = domain.Trim().TrimEnd('/');
                        var proto = cleanDom.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                    cleanDom.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "" : "https://";
                        decoyRedirectUrl = $"{proto}{cleanDom}/";
                    }
                }
                else
                {
                    decoyMode = "builtin";
                    decoyRedirectUrl = null;
                }
            }
        }
        else
        {
            publishMode = Environment.GetEnvironmentVariable("OPENFLUX_PUBLISH_MODE")?.ToLowerInvariant() ?? "domain";
            if (string.IsNullOrEmpty(decoyMode) && SystemOperations.HasLocalDecoySite())
            {
                decoyMode = "existing";
            }
        }

        if (publishMode != "domain" && string.IsNullOrEmpty(decoyMode))
        {
            decoyMode = SystemOperations.HasLocalDecoySite() ? "existing" : "auto";
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

        string host = "0.0.0.0";
        string localLanIp = SystemOperations.GetLocalLanIp();
        string localUrl = $"http://127.0.0.1:{listenPort}/{secretPath}/";
        string publicUrl = $"http://{localLanIp}:{listenPort}/{secretPath}/";

        try
        {
            if (publishMode == "localtunnel")
            {
                host = "0.0.0.0";
                SystemOperations.ConfigureFirewall(listenPort);
                var subPrefix = $"openflux-{(secretPath.Length >= 8 ? secretPath[..8] : secretPath)}";
                publicUrl = $"https://{subPrefix}.loca.lt/{secretPath}/";
                if (string.IsNullOrEmpty(localtunnelPassword))
                {
                    localtunnelPassword = await SystemOperations.FetchPublicIpAsync();
                }
            }
            else if (publishMode == "domain")
            {
                host = "0.0.0.0";
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
            else if (publishMode == "local" || publishMode == "lan")
            {
                host = "0.0.0.0";
                SystemOperations.ConfigureFirewall(listenPort);
                publicUrl = $"http://{localLanIp}:{listenPort}/{secretPath}/";
            }
            else // localhost
            {
                host = "127.0.0.1";
                publicUrl = localUrl;
            }

            CredentialGenerator.SaveCredentials(
                credPath,
                username,
                password,
                secretPath,
                host,
                publicUrl,
                publishMode,
                domain,
                localtunnelPassword,
                TerminalUi.Language,
                autostart,
                decoyRedirectUrl,
                decoyMode);

            SystemOperations.RegisterPath(installDir);
            SystemOperations.ConfigureService(
                installDir,
                username,
                password,
                secretPath,
                host,
                listenPort,
                publicUrl,
                publishMode,
                TerminalUi.Language,
                autostart,
                decoyRedirectUrl,
                decoyMode);

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
                host,
                listenPort,
                publicUrl,
                publishMode,
                TerminalUi.Language,
                decoyRedirectUrl,
                decoyMode);

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
            password,
            publishMode);

        return 0;
    }

    private static int ExecuteUninstall()
    {
        var isRu = TerminalUi.IsRussian;
        if (!SystemOperations.IsAdmin())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            if (OperatingSystem.IsWindows())
            {
                Console.WriteLine(isRu 
                    ? "  [ERROR] Требуются права Администратора для полного удаления службы и файлов." 
                    : "  [ERROR] Administrator privileges required to uninstall system service and files.");
                Console.WriteLine(isRu 
                    ? "  Пожалуйста, запустите PowerShell от имени Администратора и повторите." 
                    : "  Please run PowerShell as Administrator (Right-click -> Run as administrator) and retry.");
            }
            else
            {
                Console.WriteLine(isRu 
                    ? "  [ERROR] Требуются права root. Запустите: sudo ./openflux-installer --uninstall" 
                    : "  [ERROR] Root privileges required. Run with: sudo ./openflux-installer --uninstall");
            }
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(isRu 
            ? "  Полное удаление OpenFlux Zen Server из системы..." 
            : "  Completely uninstalling OpenFlux Zen Server...");
        Console.ResetColor();

        SystemOperations.StopExistingServer();

        if (OperatingSystem.IsWindows())
        {
            SystemOperations.RunCommand("schtasks.exe", "/delete /tn \"OpenFluxZenServer\" /f");
            SystemOperations.RunCommand("sc.exe", "stop OpenFluxZenServer >nul 2>&1");
            SystemOperations.RunCommand("sc.exe", "delete OpenFluxZenServer >nul 2>&1");
            SystemOperations.RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"OpenFluxZenServer\"");

            // Clean all machine environment variables
            try
            {
                var envVars = new[] {
                    "OPENFLUX_HOST", "OPENFLUX_PORT", "OPENFLUX_SECRET_PATH",
                    "OPENFLUX_ADMIN_USER", "OPENFLUX_ADMIN_PASSWORD", "OPENFLUX_PUBLIC_URL",
                    "OPENFLUX_PUBLISH_MODE", "OPENFLUX_LANGUAGE", "OPENFLUX_DECOY_REDIRECT_URL",
                    "OPENFLUX_DECOY_MODE", "OPENFLUX_APP_DIR"
                };
                foreach (var ev in envVars)
                {
                    Environment.SetEnvironmentVariable(ev, null, EnvironmentVariableTarget.Machine);
                }

                var curPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                if (curPath.Contains("OpenFluxZenServer", StringComparison.OrdinalIgnoreCase))
                {
                    var clean = string.Join(";", curPath.Split(';').Where(p => !p.Contains("OpenFluxZenServer", StringComparison.OrdinalIgnoreCase)));
                    Environment.SetEnvironmentVariable("Path", clean, EnvironmentVariableTarget.Machine);
                }
            }
            catch { }

            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var progFilesDir = Path.Combine(progFiles, "OpenFluxZenServer");
            var localAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFluxZenServer");

            foreach (var d in new[] { progFilesDir, localAppDir })
            {
                if (Directory.Exists(d))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }

            // Launch detached runner in %TEMP% to wipe any remaining locked files after process exits
            var runnerBat = Path.Combine(Path.GetTempPath(), "oflux_clean.bat");
            var runnerScript = $@"@echo off
timeout /t 1 /nobreak >nul
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
taskkill /f /im openflux-windows-amd64.exe >nul 2>&1
taskkill /f /im openflux-windows-arm64.exe >nul 2>&1
if exist ""{progFilesDir}"" rd /s /q ""{progFilesDir}"" >nul 2>&1
if exist ""{localAppDir}"" rd /s /q ""{localAppDir}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";
            try
            {
                File.WriteAllText(runnerBat, runnerScript);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{runnerBat}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch { }
        }
        else
        {
            SystemOperations.RunBash("systemctl disable --now openflux-zen-server.service 2>/dev/null || true");
            SystemOperations.RunBash("systemctl disable --now openflux-zrok.service 2>/dev/null || true");
            try { File.Delete("/etc/systemd/system/openflux-zen-server.service"); } catch { }
            try { File.Delete("/etc/systemd/system/openflux-zrok.service"); } catch { }
            try { File.Delete("/etc/systemd/system/openflux.service"); } catch { }
            try { File.Delete("/usr/local/bin/OpenFluxZenServer"); } catch { }
            try { File.Delete("/usr/local/bin/openfluxzenserver"); } catch { }
            try { File.Delete("/usr/local/bin/openflux"); } catch { }
            try { File.Delete("/usr/local/bin/openflux-zen-server"); } catch { }
            try { File.Delete("/usr/bin/OpenFluxZenServer"); } catch { }
            SystemOperations.RunBash("systemctl daemon-reload 2>/dev/null || true");

            // Clean nginx reverse proxy config if present
            SystemOperations.RunBash("rm -f /etc/nginx/conf.d/openflux*.conf /etc/nginx/sites-enabled/openflux* 2>/dev/null; systemctl reload nginx 2>/dev/null || true");

            // Wipe /opt/openflux-zen-server completely
            SystemOperations.RunBash("rm -rf /opt/openflux-zen-server /tmp/openflux*");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(isRu 
            ? "  OpenFlux Zen Server полностью удален из системы." 
            : "  OpenFlux Zen Server completely uninstalled from the system.");
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
  --mode <domain|tunnel|local|localhost> Set publishing mode (domain, localtunnel, local [LAN], localhost [127.0.0.1])
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
