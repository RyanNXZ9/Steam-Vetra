namespace CatalogEntitlementManager.Models;

public sealed class AppConfig
{
    public string Theme { get; set; } = "Glass";
    public string TargetBaseDirectory { get; set; } = string.Empty;
    public string LuaPluginRelativeDirectory { get; set; } = @"config\stplug-in";
    public string DepotCacheRelativeDirectory { get; set; } = @"depotcache";
    public bool MirrorManifestsToConfigDepotCache { get; set; } = true;
    public string ManifestEndpointUrl { get; set; } = "https://generator.ryuu.lol/api/download/{appId}";
    public string ManifestBranch { get; set; } = "public";
    public int HttpTimeoutSeconds { get; set; } = 30;
    public int HttpMaxRetries { get; set; } = 3;
    public bool UseOfflineManifestGenerator { get; set; } = false;
    public bool RestartSteamAfterProvision { get; set; } = true;
    public List<CatalogTitle> Catalog { get; set; } = new();
}