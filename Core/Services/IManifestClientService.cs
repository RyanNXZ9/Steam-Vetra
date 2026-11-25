using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.Core.Services;

public interface IManifestClientService
{
    Task<ManifestPayload> FetchManifestAsync(uint appId, CancellationToken cancellationToken = default);
    Task<ManifestPayload> LoadFromLocalBundleAsync(uint appId, IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
    Task<string> RequestGameAsync(uint appId, CancellationToken cancellationToken = default);
    Task<string> RequestUpdateAsync(uint appId, string? branch = null, CancellationToken cancellationToken = default);
}