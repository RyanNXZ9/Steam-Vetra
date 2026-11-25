namespace CatalogEntitlementManager.Core.Services;

public interface ILauncherProtocolService
{
    void TriggerInstallAppUri(uint appId);
    void TriggerRunAppUri(uint appId);
    Task<bool> RestartSteamAndDownloadAsync(string? steamDirectory, uint appId, CancellationToken cancellationToken = default);
    Task<bool> RestartSteamClientAsync(string? steamDirectory, CancellationToken cancellationToken = default);
}