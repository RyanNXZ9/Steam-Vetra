using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogEntitlementManager.Core.Services;

namespace CatalogEntitlementManager.Storage;

public sealed class LocalPlayerData
{
    public string Name { get; set; } = string.Empty;
    public byte[]? Avatar { get; set; }
    public Dictionary<uint, string> Added { get; set; } = new();
    public HashSet<uint> Hidden { get; set; } = new();
}

public sealed record LibraryGame(uint AppId, string Title, bool Installed)
{
    public string ArtworkUri { get; init; } = string.Empty;
    public string Status => Installed ? "Installed" : "In collection";
    public string Action => Installed ? "Play" : "Open in Steam";
}

internal static class LocalGameLibrary
{
    internal static readonly string PlayerFile = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "CatalogEntitlementManager", "player.json");

    internal static LocalPlayerData Load(string path)
    {
        if (!File.Exists(path)) return new();
        var data = JsonSerializer.Deserialize<LocalPlayerData>(File.ReadAllText(path));
        if (data is null || data.Added is null || data.Hidden is null)
            throw new InvalidDataException("Invalid player file.");
        return data;
    }

    internal static void Save(string path, LocalPlayerData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static readonly Regex Pair = new(
        "\"(?<key>[^\"\\r\\n]+)\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\\\r\\n])*)\"",
        RegexOptions.CultureInvariant);

    internal static Dictionary<uint, string> ReadInstalled(string configuredRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { configuredRoot, new SteamLocatorService().DetectSteamInstallPath() })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            roots.Add(root);
            var libraries = ReadSmallFile(Path.Combine(root, "steamapps", "libraryfolders.vdf"));
            foreach (Match field in Pair.Matches(libraries))
                if (field.Groups["key"].Value == "path")
                    roots.Add(field.Groups["value"].Value.Replace(@"\\", @"\"));
        }

        var installed = new Dictionary<uint, string>();
        foreach (var root in roots)
        {
            var apps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(apps)) continue;
            string[] files;
            try { files = Directory.GetFiles(apps, "appmanifest_*.acf"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match field in Pair.Matches(ReadSmallFile(file)))
                    fields.TryAdd(field.Groups["key"].Value, field.Groups["value"].Value);
                if (uint.TryParse(fields.GetValueOrDefault("appid"), out var id) && id > 0
                    && fields.TryGetValue("installdir", out var folder) && folder.Length > 0
                    && Path.GetFileName(folder) == folder && folder is not "." and not ".."
                    && Directory.Exists(Path.Combine(apps, "common", folder))
                    && uint.TryParse(fields.GetValueOrDefault("StateFlags"), out var flags) && (flags & 4) != 0
                    && fields.TryGetValue("name", out var title))
                    installed[id] = title;
            }
        }
        return installed;
    }

    internal static Dictionary<uint,string> ReadCachedCovers(string configuredRoot, IEnumerable<uint> games)
    {
        var covers = new Dictionary<uint,string>();
        var install = new SteamLocatorService().DetectSteamInstallPath();
        foreach (var root in new[] { configuredRoot, install }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var cache = Path.Combine(root, "appcache", "librarycache");
            if (!Directory.Exists(cache)) continue;
            foreach (var id in games)
            {
                foreach (var name in new[] { $"{id}_header.jpg", Path.Combine(id.ToString(), "header.jpg"), $"{id}_library_hero.jpg" })
                {
                    var file = Path.Combine(cache, name);
                    if (!File.Exists(file)) continue;
                    covers.TryAdd(id, new Uri(Path.GetFullPath(file)).AbsoluteUri);
                    break;
                }
            }
        }
        return covers;
    }

    private static string ReadSmallFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= 4_000_000 ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return string.Empty; }
    }
}