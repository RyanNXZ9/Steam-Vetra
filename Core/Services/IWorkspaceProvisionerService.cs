using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.Core.Services;

public sealed class ProvisionResult
{
    public bool Success { get; init; }
    public uint AppId { get; init; }
    public IReadOnlyList<string> WrittenFiles { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;
}

public interface IWorkspaceProvisionerService
{
    Task<ProvisionResult> ProvisionFilesAsync(uint appId, string baseDirectory, ManifestPayload payload, CancellationToken cancellationToken = default);
}