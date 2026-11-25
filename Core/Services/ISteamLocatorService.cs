namespace CatalogEntitlementManager.Core.Services;

public sealed class SteamEnvironmentStatus
{
    public bool IsSteamInstalled { get; init; }
    public string SteamPath { get; init; } = string.Empty;
    public bool IsLibraryReady { get; init; }
    public bool IsUnlockerInstalled { get; init; }
    public string LibraryDetail { get; init; } = string.Empty;
    public IReadOnlySet<uint> ProvisionedAppIds { get; init; } = new HashSet<uint>();
}

public interface ISteamLocatorService
{
    string? DetectSteamInstallPath();
    bool IsSteamProcessRunning(out int processId);
    SteamEnvironmentStatus InspectSteamEnvironment(string? baseDirectory, string luaRelDir, string depotRelDir);
    bool EnsureSilentLibraryDirectories(string baseDirectory, string luaRelDir, string depotRelDir, bool mirrorConfigDepotCache);
    Task<bool> EnsureUnlockerInstalledAsync(string baseDirectory, Action<string>? onProgress = null, CancellationToken cancellationToken = default);
}