namespace CatalogEntitlementManager.Models;

public sealed class CatalogTitle
{
    public uint AppId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string BuildVersion { get; set; } = string.Empty;
    public int LatestDepotsCount { get; set; }
    public int OutdatedDepotsCount { get; set; }
    public int MissingDepotsCount { get; set; }
    public int DlcCount { get; set; }
    public List<string> ManifestFiles { get; set; } = new();
    public List<uint> IncludedDlcIds { get; set; } = new();
    public List<uint> MissingDlcIds { get; set; } = new();

    public string HeaderImageUrl => $"https://cdn.akamai.steamstatic.com/steam/apps/{AppId}/header.jpg";
}