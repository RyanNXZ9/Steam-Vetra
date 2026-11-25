using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CatalogEntitlementManager.Core.Services;

public sealed class SteamLocatorService : ISteamLocatorService
{
    private const string BstManifestUrl =
        "https://raw.githubusercontent.com/madoiscool/BetterSteamTools/refs/heads/updates/opensteamtool/latest.toml";

    private const string OstNightlyReleasesUrl =
        "https://api.github.com/repos/madoiscool/OST-Nightly/releases/latest";

    private const string OstLuaPath = "config/stplug-in";
    private static readonly string[] UnlockerDllNames = { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" };

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LuaToolsGui/2.0");
        return client;
    }

    public string? DetectSteamInstallPath()
    {
        (RegistryHive Hive, RegistryView View, string SubKey, string Value)[] locations =
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath")
        };

        foreach (var (hive, view, subKey, value) in locations)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subKey, writable: false);
                var rawPath = key?.GetValue(value)?.ToString();
                if (!string.IsNullOrWhiteSpace(rawPath))
                {
                    var normalized = Path.GetFullPath(rawPath.Trim().Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(Path.Combine(normalized, "steam.exe")))
                    {
                        return normalized;
                    }
                }
            }
            catch
            {
            }
        }

        var defaultCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam");

        return Directory.Exists(defaultCandidate) ? defaultCandidate : null;
    }

    public bool IsSteamProcessRunning(out int processId)
    {
        processId = 0;
        try
        {
            var processes = Process.GetProcessesByName("steam");
            if (processes.Length > 0)
            {
                processId = processes[0].Id;
                foreach (var p in processes)
                {
                    p.Dispose();
                }

                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public SteamEnvironmentStatus InspectSteamEnvironment(string? baseDirectory, string luaRelDir, string depotRelDir)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return new SteamEnvironmentStatus
            {
                IsSteamInstalled = false,
                LibraryDetail = "Steam Directory Not Found"
            };
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        var stPluginPath = Path.GetFullPath(Path.Combine(fullBase, luaRelDir.Replace('/', Path.DirectorySeparatorChar)));
        var depotCachePath = Path.GetFullPath(Path.Combine(fullBase, depotRelDir.Replace('/', Path.DirectorySeparatorChar)));

        var provisionedAppIds = new HashSet<uint>();
        if (Directory.Exists(stPluginPath))
        {
            try
            {
                foreach (var luaFile in Directory.EnumerateFiles(stPluginPath, "*.lua", SearchOption.TopDirectoryOnly))
                {
                    var stem = Path.GetFileNameWithoutExtension(luaFile);
                    if (uint.TryParse(stem, out var parsedId))
                    {
                        provisionedAppIds.Add(parsedId);
                    }
                }
            }
            catch
            {
            }
        }

        var hasStDir = Directory.Exists(stPluginPath);
        var hasDepotDir = Directory.Exists(depotCachePath);
        var hasUnlocker = IsUnlockerPresentOnDisk(fullBase);

        return new SteamEnvironmentStatus
        {
            IsSteamInstalled = File.Exists(Path.Combine(fullBase, "steam.exe")) || Directory.Exists(fullBase),
            SteamPath = fullBase,
            IsLibraryReady = hasStDir && hasDepotDir && hasUnlocker,
            IsUnlockerInstalled = hasUnlocker,
            LibraryDetail = hasUnlocker
                ? $"Library Active ({provisionedAppIds.Count} Registered)"
                : $"Sync Core Ready ({provisionedAppIds.Count} Queued)",
            ProvisionedAppIds = provisionedAppIds
        };
    }

    public bool EnsureSilentLibraryDirectories(string baseDirectory, string luaRelDir, string depotRelDir, bool mirrorConfigDepotCache)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        var stPluginPath = Path.GetFullPath(Path.Combine(fullBase, luaRelDir.Replace('/', Path.DirectorySeparatorChar)));
        var depotCachePath = Path.GetFullPath(Path.Combine(fullBase, depotRelDir.Replace('/', Path.DirectorySeparatorChar)));

        Directory.CreateDirectory(stPluginPath);
        Directory.CreateDirectory(depotCachePath);

        if (mirrorConfigDepotCache)
        {
            var configDepotCache = Path.GetFullPath(Path.Combine(fullBase, "config", "depotcache"));
            Directory.CreateDirectory(configDepotCache);
        }

        try
        {
            EnsureOpenSteamToolLuaPath(fullBase);
        }
        catch
        {
        }

        // Remove .cef-enable-remote-debugging so Steam boots 100% clean with no CEF injected plugin menu
        var cefDebugFlag = Path.Combine(fullBase, ".cef-enable-remote-debugging");
        if (File.Exists(cefDebugFlag))
        {
            try
            {
                File.Delete(cefDebugFlag);
            }
            catch
            {
            }
        }

        return true;
    }

    public async Task<bool> EnsureUnlockerInstalledAsync(
        string baseDirectory,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return false;
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        EnsureOpenSteamToolLuaPath(fullBase);

        if (IsUnlockerPresentOnDisk(fullBase))
        {
            return true;
        }

        onProgress?.Invoke("Configuring native Steam Library bridge (BetterSteamTools / OpenSteamTool)...");

        var staging = Path.Combine(Path.GetTempPath(), "CEM_Unlocker_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            // 1. Resolve latest release zip URL from BetterSteamTools latest.toml or OST-Nightly GitHub Releases
            var (zipUrl, expectedPayloadSha256) = await ResolveUnlockerZipUrlAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                onProgress?.Invoke("Could not reach unlocker release endpoint.");
                return false;
            }

            var zipPath = Path.Combine(staging, "OpenSteamTool-Release.zip");
            using (var response = await Http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var match = UnlockerDllNames.FirstOrDefault(w => w.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                    if (match is null || extracted.ContainsKey(match)) continue;

                    var stagedFile = Path.Combine(staging, match);
                    entry.ExtractToFile(stagedFile, overwrite: true);
                    extracted[match] = stagedFile;
                }
            }

            if (extracted.Count < UnlockerDllNames.Length)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedPayloadSha256) &&
                extracted.TryGetValue("OpenSteamTool.dll", out var payloadPath))
            {
                var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(payloadPath, cancellationToken)));
                if (!actualHash.Equals(expectedPayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    onProgress?.Invoke("SHA-256 verification failed on OpenSteamTool.dll.");
                    return false;
                }
            }

            // If Steam is running and holding dwmapi.dll/xinput1_4.dll, stop it cleanly before placing the bridge DLLs
            var wasSteamRunning = IsSteamProcessRunning(out _);
            if (wasSteamRunning)
            {
                foreach (var proc in Process.GetProcessesByName("steam"))
                {
                    using (proc)
                    {
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(6000);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            foreach (var dllName in UnlockerDllNames)
            {
                var destPath = Path.Combine(fullBase, dllName);
                File.Copy(extracted[dllName], destPath, overwrite: true);
            }

            EnsureOpenSteamToolLuaPath(fullBase);
            onProgress?.Invoke("Native Steam Library bridge installed and hot-reload configured in opensteamtool.toml.");
            return true;
        }
        catch (Exception ex)
        {
            onProgress?.Invoke($"Bridge setup note: {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static bool IsUnlockerPresentOnDisk(string steamRoot)
    {
        var hasOst = File.Exists(Path.Combine(steamRoot, "OpenSteamTool.dll")) &&
                     File.Exists(Path.Combine(steamRoot, "dwmapi.dll")) &&
                     File.Exists(Path.Combine(steamRoot, "xinput1_4.dll"));
        var hasLegacyHid = File.Exists(Path.Combine(steamRoot, "hid.dll"));
        return hasOst || hasLegacyHid;
    }

    private static async Task<(string? ZipUrl, string? PayloadSha256)> ResolveUnlockerZipUrlAsync(CancellationToken ct)
    {
        // Primary: BetterSteamTools latest.toml (from LuaTools-main UnlockerService.cs)
        try
        {
            var toml = await Http.GetStringAsync(BstManifestUrl, ct);
            string? version = null, sha = null;
            foreach (var rawLine in toml.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim().Trim('"');
                if (k == "version") version = v;
                else if (k == "sha256") sha = v;
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                var zipName = $"OpenSteamTool-{version}-Release.zip";
                var url = $"https://github.com/madoiscool/BetterSteamTools/releases/download/{version}/{zipName}";
                return (url, sha);
            }
        }
        catch
        {
        }

        // Secondary fallback: madoiscool/OST-Nightly latest release
        try
        {
            var json = await Http.GetStringAsync(OstNightlyReleasesUrl, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        var dl = asset.GetProperty("browser_download_url").GetString();
                        return (dl, null);
                    }
                }
            }
        }
        catch
        {
        }

        return (null, null);
    }

    // Exact port of UnlockerService.EnsureOpenSteamToolLuaPath from LuaTools-main:
    // Registers [lua] paths = ["config/stplug-in"] in <Steam>/opensteamtool.toml so Lua scripts hot-reload
    public static void EnsureOpenSteamToolLuaPath(string steamRoot)
    {
        var tomlPath = Path.Combine(steamRoot, "opensteamtool.toml");
        if (!File.Exists(tomlPath))
        {
            File.WriteAllText(tomlPath, $"[lua]\npaths = [\"{OstLuaPath}\"]\n");
            return;
        }

        var lines = File.ReadAllLines(tomlPath).ToList();
        var luaHeader = lines.FindIndex(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith('#') && Regex.IsMatch(t, @"^\[\s*lua\s*\]");
        });

        if (luaHeader < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add(string.Empty);
            lines.Add("[lua]");
            lines.Add($"paths = [\"{OstLuaPath}\"]");
            File.WriteAllLines(tomlPath, lines);
            return;
        }

        var sectionEnd = lines.FindIndex(luaHeader + 1, l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith('#') && Regex.IsMatch(t, @"^\[[^\[].*\]");
        });
        if (sectionEnd < 0) sectionEnd = lines.Count;

        var pathsStart = -1;
        for (var i = luaHeader + 1; i < sectionEnd; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith('#')) continue;
            if (Regex.IsMatch(t, @"^paths\s*="))
            {
                pathsStart = i;
                break;
            }
        }

        if (pathsStart < 0)
        {
            lines.Insert(luaHeader + 1, $"paths = [\"{OstLuaPath}\"]");
            File.WriteAllLines(tomlPath, lines);
            return;
        }

        var pathsEnd = pathsStart;
        while (pathsEnd < sectionEnd && !lines[pathsEnd].Contains(']')) pathsEnd++;
        if (pathsEnd >= sectionEnd) pathsEnd = sectionEnd - 1;

        var block = string.Join("\n", lines.GetRange(pathsStart, pathsEnd - pathsStart + 1));
        if (Regex.IsMatch(block, @"[""']\s*" + Regex.Escape(OstLuaPath).Replace("/", @"[/\\]+") + @"\s*[""']", RegexOptions.IgnoreCase))
        {
            return;
        }

        var closeLine = pathsEnd;
        var line = lines[closeLine];
        var bracket = line.LastIndexOf(']');
        if (bracket < 0) return;

        var before = line[..bracket].TrimEnd();
        var arrayEmpty = Regex.IsMatch(before, @"\[\s*$");
        var newBefore = arrayEmpty
            ? before + $" \"{OstLuaPath}\""
            : before + $", \"{OstLuaPath}\"";
        lines[closeLine] = newBefore + line[bracket..];

        File.WriteAllLines(tomlPath, lines);
    }
}