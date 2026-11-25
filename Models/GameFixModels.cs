using System.Text.Json.Serialization;

namespace CatalogEntitlementManager.Models;

public sealed class FixTagDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("slug")] public string Slug { get; set; } = string.Empty;
    [JsonPropertyName("color")] public string? Color { get; set; }
}

public sealed class GameFixDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("tags")] public List<FixTagDto> Tags { get; set; } = new();
    [JsonPropertyName("hasManifest")] public bool HasManifest { get; set; }
    [JsonPropertyName("hasFix")] public bool HasFix { get; set; }
    [JsonPropertyName("manifestFilename")] public string? ManifestFilename { get; set; }
    [JsonPropertyName("fixFilename")] public string? FixFilename { get; set; }
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title)
        ? Title
        : (!string.IsNullOrWhiteSpace(FixFilename) ? FixFilename : "Game Fix Package");

    public string TagsSummary => Tags.Count > 0
        ? string.Join(" • ", Tags.Select(t => t.Name))
        : "Bypass / Fix";
}

public sealed class GameFixesResponseDto
{
    [JsonPropertyName("appid")] public string AppId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("header_image")] public string? HeaderImage { get; set; }
    [JsonPropertyName("fixes")] public List<GameFixDto> Fixes { get; set; } = new();
}

public sealed class FixRecord
{
    [JsonPropertyName("appId")] public uint AppId { get; set; }
    [JsonPropertyName("fixId")] public string FixId { get; set; } = string.Empty;
    [JsonPropertyName("appliedAt")] public string AppliedAt { get; set; } = string.Empty;
    [JsonPropertyName("files")] public List<FixRecordEntry> Files { get; set; } = new();
}

public sealed class FixRecordEntry
{
    [JsonPropertyName("relativePath")] public string RelativePath { get; set; } = string.Empty;
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty; // "modified" or "added"
    [JsonPropertyName("backupPath")] public string? BackupPath { get; set; }
    [JsonPropertyName("hashAfter")] public string? HashAfter { get; set; }
    [JsonPropertyName("hashBefore")] public string? HashBefore { get; set; }
}

public sealed class FixOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? InstallDirectory { get; init; }
    public int FilesAffected { get; init; }
}