using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface IPanelUpdateService
{
    Task<PanelVersionInfo> GetVersionInfoAsync(bool forceCheck = false, CancellationToken ct = default);
    Task<PanelUpdateResult> UpdatePanelAsync(CancellationToken ct = default);
}
