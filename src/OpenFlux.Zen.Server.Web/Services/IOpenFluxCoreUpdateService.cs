using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface IOpenFluxCoreUpdateService
{
    Task<OpenFluxCoreVersionInfo> GetVersionInfoAsync(bool forceCheck = false, CancellationToken ct = default);
    Task<OpenFluxCoreUpdateResult> UpdateCoreAsync(CancellationToken ct = default);
}
