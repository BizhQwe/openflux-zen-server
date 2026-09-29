using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OpenFlux.Zen.Server.Installer.Platform;

public static class SystemOperations
{
    public static bool IsAdmin()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
        else
        {
            return geteuid() == 0;
        }
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    public static string GetDefaultInstallDir()
    {
        if (OperatingSystem.IsWindows())
        {
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(progFiles))
            {
                return Path.Combine(progFiles, "OpenFluxZenServer");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFluxZenServer");
        }
        else
        {
            return "/opt/openflux-zen-server/app";
        }
    }

    public static void StopExistingServer()
    {
        if (OperatingSystem.IsWindows())
        {
            RunCommand("schtasks.exe", "/end /tn \"OpenFluxZenServer\"");

            foreach (var procName in new[] { 
                "OpenFlux.Zen.Server.Web", 
                "OpenFlux.Zen.Server", 
                "OpenFluxZenServer", 
                "openflux", 
                "openflux-windows-amd64", 
                "openflux-windows-arm64", 
                "zrok" 
            })
            {
                foreach (var proc in Process.GetProcessesByName(procName))
                {
                    try
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                    catch { }
                }
            }

            RunCommand("taskkill.exe", "/f /t /im OpenFlux.Zen.Server.Web.exe");
            RunCommand("taskkill.exe", "/f /t /im OpenFlux.Zen.Server.exe");
            RunCommand("taskkill.exe", "/f /t /im OpenFluxZenServer.exe");
            RunCommand("taskkill.exe", "/f /t /im openflux-windows-amd64.exe");
            RunCommand("taskkill.exe", "/f /t /im openflux-windows-arm64.exe");
            RunCommand("taskkill.exe", "/f /t /im openflux.exe");
            RunCommand("taskkill.exe", "/f /t /im zrok.exe");
            Thread.Sleep(500);
        }
        else
        {
            RunBash("systemctl stop openflux-zen-server.service 2>/dev/null || true");
            RunBash("systemctl stop openflux-zrok.service 2>/dev/null || true");

            // Safely kill running server daemons and core engine, explicitly excluding current installer process
            KillProcessesExceptSelf(new[] {
                "OpenFlux.Zen.Server.Web",
                "OpenFlux.Zen.Server",
                "openflux-linux-amd64",
                "openflux-linux-arm64",
                "openflux",
                "zrok"
            });
            Thread.Sleep(500);
        }
    }

    private static void KillProcessesExceptSelf(string[] processNames)
    {
        var currentPid = Environment.ProcessId;
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == currentPid) continue;

                    var name = proc.ProcessName;
                    if (processNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        proc.Kill();
                        proc.WaitForExit(1500);
                    }
                }
                catch { }
            }
        }
        catch { }

        // Also safe exact matching kill without -f (never match full cmdline substrings)
        foreach (var name in processNames)
        {
            RunBash($"pkill -9 -x \"{name}\" 2>/dev/null || true");
        }
    }

    public static void ConfigureFirewall(int port)
    {
        if (OperatingSystem.IsWindows())
        {
            RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"OpenFluxZenServer\"");
            RunCommand("netsh.exe", $"advfirewall firewall add rule name=\"OpenFluxZenServer\" dir=in action=allow protocol=TCP localport={port}");
        }
        else
        {
            RunBash($"ufw allow {port}/tcp >/dev/null 2>&1 || true");
            RunBash($"firewall-cmd --add-port={port}/tcp --permanent >/dev/null 2>&1 && firewall-cmd --reload >/dev/null 2>&1 || true");
        }
    }

    public static string GetLocalLanIp()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint &&
                !System.Net.IPAddress.IsLoopback(endPoint.Address))
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public static void RegisterPath(string installDir)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var cur = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                if (!cur.Split(';').Any(p => string.Equals(p.Trim(), installDir.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    Environment.SetEnvironmentVariable("Path", $"{cur.TrimEnd(';')};{installDir}", EnvironmentVariableTarget.Machine);
                }
            }
            catch { }
        }
        else
        {
            var cliPath = Path.Combine(installDir, "OpenFluxZenServer");
            if (File.Exists(cliPath))
            {
                RunBash($"ln -sf \"{cliPath}\" /usr/local/bin/OpenFluxZenServer 2>/dev/null || true");
                RunBash($"ln -sf \"{cliPath}\" /usr/local/bin/openfluxzenserver 2>/dev/null || true");
                RunBash($"ln -sf \"{cliPath}\" /usr/local/bin/openflux 2>/dev/null || true");
                RunBash($"ln -sf \"{cliPath}\" /usr/local/bin/openflux-zen-server 2>/dev/null || true");
                RunBash($"chmod +x /usr/local/bin/OpenFluxZenServer /usr/local/bin/openfluxzenserver /usr/local/bin/openflux /usr/local/bin/openflux-zen-server 2>/dev/null || true");
            }
        }
    }

    public static void ConfigureService(
        string installDir,
        string username,
        string password,
        string secretPath,
        string host,
        int port,
        string publicUrl,
        string publishMode,
        string language,
        bool autostart,
        string? decoyRedirectUrl = null,
        string? decoyMode = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var exePath = Path.Combine(installDir, "OpenFlux.Zen.Server.Web.exe");

            RunCommandArgs("schtasks.exe", "/delete", "/tn", "OpenFluxZenServer", "/f");
            var res = RunCommandArgs("schtasks.exe", "/create", "/tn", "OpenFluxZenServer", "/tr", $"\"{exePath}\"", "/sc", "onstart", "/ru", "SYSTEM", "/rl", "HIGHEST", "/f");
            if (res != 0)
            {
                RunCommandArgs("schtasks.exe", "/create", "/tn", "OpenFluxZenServer", "/tr", $"\"{exePath}\"", "/sc", "onlogon", "/rl", "HIGHEST", "/f");
            }

            if (!autostart)
            {
                RunCommandArgs("schtasks.exe", "/change", "/tn", "OpenFluxZenServer", "/disable");
            }

            // Set persistent environment variables for the machine
            try
            {
                Environment.SetEnvironmentVariable("OPENFLUX_HOST", host, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_PORT", port.ToString(), EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_SECRET_PATH", secretPath, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_ADMIN_USER", username, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_ADMIN_PASSWORD", password, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_PUBLIC_URL", publicUrl, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_PUBLISH_MODE", publishMode, EnvironmentVariableTarget.Machine);
                Environment.SetEnvironmentVariable("OPENFLUX_LANGUAGE", language, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrEmpty(decoyRedirectUrl))
                    Environment.SetEnvironmentVariable("OPENFLUX_DECOY_REDIRECT_URL", decoyRedirectUrl, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrEmpty(decoyMode))
                    Environment.SetEnvironmentVariable("OPENFLUX_DECOY_MODE", decoyMode, EnvironmentVariableTarget.Machine);
            }
            catch { }
        }
        else
        {
            var exePath = Path.Combine(installDir, "OpenFlux.Zen.Server.Web");
            var extraEnv = "";
            if (!string.IsNullOrEmpty(decoyRedirectUrl)) extraEnv += $"\nEnvironment=OPENFLUX_DECOY_REDIRECT_URL={decoyRedirectUrl}";
            if (!string.IsNullOrEmpty(decoyMode)) extraEnv += $"\nEnvironment=OPENFLUX_DECOY_MODE={decoyMode}";

            var serviceContent = $"""
[Unit]
Description=OpenFlux Zen Server Management Panel
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory={installDir}
ExecStart={exePath}
Restart=always
RestartSec=5
Environment=OPENFLUX_HOST={host}
Environment=OPENFLUX_PORT={port}
Environment=OPENFLUX_SECRET_PATH={secretPath}
Environment=OPENFLUX_ADMIN_USER={username}
Environment=OPENFLUX_ADMIN_PASSWORD={password}
Environment=OPENFLUX_PUBLIC_URL={publicUrl}
Environment=OPENFLUX_PUBLISH_MODE={publishMode}
Environment=OPENFLUX_LANGUAGE={language}{extraEnv}
Environment=DOTNET_gcServer=0
Environment=DOTNET_GCHeapHardLimit=80000000

[Install]
WantedBy=multi-user.target
""";

            File.WriteAllText("/etc/systemd/system/openflux-zen-server.service", serviceContent);
            RunBash("systemctl daemon-reload");

            if (autostart)
            {
                RunBash("systemctl enable openflux-zen-server.service >/dev/null 2>&1");
            }
            else
            {
                RunBash("systemctl disable openflux-zen-server.service >/dev/null 2>&1 || true");
            }
        }
    }

    public static void StartServer(string installDir, string username, string password, string secretPath, string host, int port, string publicUrl, string publishMode, string language, string? decoyRedirectUrl = null, string? decoyMode = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var exePath = Path.Combine(installDir, "OpenFlux.Zen.Server.Web.exe");
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = installDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            psi.Environment["OPENFLUX_HOST"] = host;
            psi.Environment["OPENFLUX_PORT"] = port.ToString();
            psi.Environment["OPENFLUX_SECRET_PATH"] = secretPath;
            psi.Environment["OPENFLUX_ADMIN_USER"] = username;
            psi.Environment["OPENFLUX_ADMIN_PASSWORD"] = password;
            psi.Environment["OPENFLUX_PUBLIC_URL"] = publicUrl;
            psi.Environment["OPENFLUX_PUBLISH_MODE"] = publishMode;
            psi.Environment["OPENFLUX_LANGUAGE"] = language;
            if (!string.IsNullOrEmpty(decoyRedirectUrl)) psi.Environment["OPENFLUX_DECOY_REDIRECT_URL"] = decoyRedirectUrl;
            if (!string.IsNullOrEmpty(decoyMode)) psi.Environment["OPENFLUX_DECOY_MODE"] = decoyMode;

            Process.Start(psi);
        }
        else
        {
            RunBash("systemctl restart openflux-zen-server.service --no-block");
        }
    }

    public static async Task<string> FetchPublicIpAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            return (await client.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch
        {
            try
            {
                return (await client.GetStringAsync("https://ifconfig.me/ip")).Trim();
            }
            catch
            {
                return "";
            }
        }
    }

    public static bool HasLocalDecoySite()
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            if (File.Exists("/var/www/html/index.html") || File.Exists("/var/www/html/index.htm"))
            {
                return true;
            }
            if (Directory.Exists("/var/www/html") && Directory.EnumerateFileSystemEntries("/var/www/html").Any())
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    public static async Task<bool> CheckDomainHasLiveSiteAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        try
        {
            var host = domain.Trim().TrimEnd('/');
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                host = host["http://".Length..];
            else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = host["https://".Length..];

            if (host.Contains(':')) host = host.Split(':')[0];
            if (host.Contains('/')) host = host.Split('/')[0];
            if (string.IsNullOrWhiteSpace(host)) return false;

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                AllowAutoRedirect = true
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(2.5)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) OpenFluxDecoyDetector");

            try
            {
                var resp = await client.GetAsync($"https://{host}/");
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }

            try
            {
                var resp = await client.GetAsync($"http://{host}/");
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { }
        }
        catch { }
        return false;
    }

    public static int RunCommand(string fileName, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit();
            return p?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    public static int RunCommandArgs(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    public static int RunBash(string command)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit();
            return p?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }
}
