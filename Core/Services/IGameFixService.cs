using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.Core.Services;

public interface IGameFixService
{
    string? ResolveGameInstallDirectory(string? steamRoot, uint appId);
    IReadOnlyList<string> GetAppliedFixIds(string? installDirectory);
    Task<IReadOnlyList<GameFixDto>> FetchFixesForAppAsync(uint appId, CancellationToken cancellationToken = default);
    Task<FixOperationResult> ApplyFixZipAsync(uint appId, string fixId, string zipFilePath, string installDirectory, CancellationToken cancellationToken = default);
    Task<FixOperationResult> DownloadAndApplyFixAsync(uint appId, GameFixDto fix, string installDirectory, CancellationToken cancellationToken = default);
    Task<FixOperationResult> RevertFixAsync(uint appId, string fixId, string installDirectory, CancellationToken cancellationToken = default);
    Task<FixOperationResult> RunSteamlessDrmStripAsync(uint appId, string installDirectory, Action<string>? onProgress = null, CancellationToken cancellationToken = default);
    void OpenRyuuFixesCatalog(uint appId);
}