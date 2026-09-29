using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OpenFlux.Zen.Server.Common;

public static class NetworkHelper
{
    public static string GetLocalLanIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint &&
                !IPAddress.IsLoopback(endPoint.Address))
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public static async Task<string> FetchPublicIpAsync(HttpClient? httpClient = null)
    {
        var client = httpClient ?? new HttpClient();
        string[] services = {
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ifconfig.me/ip",
            "https://checkip.amazonaws.com"
        };

        foreach (var svc in services)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var res = (await client.GetStringAsync(svc, cts.Token)).Trim();
                if (!string.IsNullOrEmpty(res) && IPAddress.TryParse(res, out _))
                {
                    return res;
                }
            }
            catch { }
        }

        return "";
    }
}
