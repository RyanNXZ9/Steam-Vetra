namespace CatalogEntitlementManager.Models;

public sealed class ManifestFileEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public string Sha256Hex { get; set; } = string.Empty;
}

public sealed class ManifestPayload
{
    public uint AppId { get; set; }
    public string ManifestVersion { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public List<ManifestFileEntry> Files { get; set; } = new();
}