using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface IPanelUpdateService
{
    Task<PanelVersionInfo> GetVersionInfoAsync(bool forceCheck = false, CancellationToken ct = default);
    Task<List<ReleaseItemDto>> GetAvailableReleasesAsync(CancellationToken ct = default);
    Task<PanelUpdateResult> UpdatePanelAsync(string? targetVersion = null, CancellationToken ct = default);
}
