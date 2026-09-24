using System.Text.Json;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface IExportImportService
{
    Task<string> ExportConfigurationJsonAsync();
    Task<(int Imported, int Errors, string Message)> ImportConfigurationJsonAsync(string json);
}

public sealed class ExportImportService : IExportImportService
{
    private readonly ITunnelManager _tunnelManager;
    private readonly ILogger<ExportImportService> _logger;

    public ExportImportService(ITunnelManager tunnelManager, ILogger<ExportImportService> logger)
    {
        _tunnelManager = tunnelManager;
        _logger = logger;
    }

    public async Task<string> ExportConfigurationJsonAsync()
    {
        var tunnels = await _tunnelManager.GetAllAsync();
        var dto = new ExportConfigDto
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            Tunnels = tunnels.Select(t => new Tunnel
            {
                Id = t.Id,
                Name = t.Name,
                Role = t.Role,
                Transport = t.Transport,
                Inbound = t.Inbound,
                Socks5Address = t.Socks5Address,
                Mode = t.Mode,
                Url = t.Url,
                MaxToken = t.MaxToken,
                MaxUid = t.MaxUid,
                LocalIp = t.LocalIp,
                Codec = t.Codec,
                EncryptionKey = t.EncryptionKey,
                BenchBytes = t.BenchBytes,
                BenchCompressible = t.BenchCompressible,
                ExtraArgs = t.ExtraArgs,
                ClientLimit = t.ClientLimit,
                TrafficLimitBytes = t.TrafficLimitBytes,
                IsEnabled = t.IsEnabled,
                Status = TunnelStatus.Stopped,
                UploadBytes = 0,
                DownloadBytes = 0,
                ConnectedClients = 0
            }).ToList()
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public async Task<(int Imported, int Errors, string Message)> ImportConfigurationJsonAsync(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dto = JsonSerializer.Deserialize<ExportConfigDto>(json, options);

            if (dto == null || dto.Tunnels == null || dto.Tunnels.Count == 0)
            {
                // Try deserializing as array of Tunnel directly
                var directList = JsonSerializer.Deserialize<List<Tunnel>>(json, options);
                if (directList != null && directList.Count > 0)
                {
                    dto = new ExportConfigDto { Tunnels = directList };
                }
                else
                {
                    return (0, 1, "No valid tunnels found in JSON");
                }
            }

            int imported = 0;
            int errors = 0;

            foreach (var tunnel in dto.Tunnels)
            {
                try
                {
                    var existing = await _tunnelManager.GetByIdAsync(tunnel.Id);
                    if (existing != null)
                    {
                        await _tunnelManager.UpdateAsync(tunnel);
                    }
                    else
                    {
                        await _tunnelManager.CreateAsync(tunnel);
                    }
                    imported++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to import tunnel {Name}", tunnel.Name);
                    errors++;
                }
            }

            return (imported, errors, $"Successfully imported {imported} tunnels ({errors} errors)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse import JSON");
            return (0, 1, $"JSON parse error: {ex.Message}");
        }
    }
}
