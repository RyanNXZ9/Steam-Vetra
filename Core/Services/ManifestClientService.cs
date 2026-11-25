using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogEntitlementManager.Models;
using CatalogEntitlementManager.Storage;

namespace CatalogEntitlementManager.Core.Services;

public sealed class ManifestClientService : IManifestClientService
{
    private readonly IConfigStore _configStore;
    private readonly HttpClient _httpClient;

    public ManifestClientService(
        IConfigStore configStore,
        HttpClient? httpClient = null)
    {
        _configStore = configStore;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ManifestPayload> FetchManifestAsync(uint appId, CancellationToken cancellationToken = default)
    {

        var config = _configStore.Load();
        if (config.UseOfflineManifestGenerator)
        {
            await Task.Delay(120, cancellationToken);
            return BuildOfflineManifestPayload(appId, config);
        }

        var maxRetries = Math.Clamp(config.HttpMaxRetries, 1, 5);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(config.HttpTimeoutSeconds, 5, 120));
        var zipUrl = BuildDownloadUrl(config.ManifestEndpointUrl, appId, fileType: null, config.ManifestBranch);
        var luaUrl = BuildDownloadUrl(config.ManifestEndpointUrl, appId, fileType: "lua", config.ManifestBranch);
                var authKey = Environment.GetEnvironmentVariable("STEAM_VETRA_RYUU_API_KEY") ?? string.Empty;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                using var request = CreateAuthenticatedGetRequest(zipUrl, authKey);
                using var response = await _httpClient.SendAsync(request, cts.Token);

                // If the game is not cached on the generator yet (404), automatically trigger /request?appid={appId} once
                if (response.StatusCode == HttpStatusCode.NotFound && attempt == 1)
                {
                    await TryTriggerGeneratorRequestAsync(config, appId, authKey, cts.Token);
                }

                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var rawBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

                if (IsZipArchive(rawBytes) ||
                    contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = ExtractZipBundleToPayload(appId, rawBytes, config);

                    // If the ZIP from ?file_type=manifest only contained .manifest files without a .lua script,
                    // also fetch ?file_type=lua from the generator so config/stplug-in/<AppID>.lua is guaranteed present.
                    var hasLua = payload.Files.Any(f => f.RelativePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
                    if (!hasLua)
                    {
                        var extraLua = await TryFetchExplicitLuaAsync(luaUrl, authKey, appId, config, cts.Token);
                        if (extraLua is not null)
                        {
                            payload.Files.Insert(0, extraLua);
                        }
                    }

                    if (!payload.Files.Any(f => f.RelativePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidDataException($"No Lua script was returned for AppID {appId}.");
                    }

                    return payload;
                }

                var text = Encoding.UTF8.GetString(rawBytes);
                if (text.TrimStart().StartsWith("addappid", StringComparison.OrdinalIgnoreCase) ||
                    text.TrimStart().StartsWith("--", StringComparison.OrdinalIgnoreCase))
                {
                    return WrapRawLuaInPayload(appId, rawBytes, config);
                }

                var jsonPayload = JsonSerializer.Deserialize<ManifestPayload>(text, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (jsonPayload is null || jsonPayload.Files.Count == 0)
                {
                    throw new InvalidDataException($"Manifest payload for AppID {appId} was empty.");
                }

                var normalized = NormalizePayloadPaths(jsonPayload, config);
                if (!normalized.Files.Any(f => f.RelativePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException($"No Lua script was returned for AppID {appId}.");
                }

                return normalized;
            }
            catch (Exception ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw new HttpRequestException($"Could not fetch a manifest for AppID {appId}.", lastError);
    }

    public async Task<string> RequestGameAsync(uint appId, CancellationToken cancellationToken = default)
    {
        var config = _configStore.Load();
        var baseHost = ExtractBaseHost(config.ManifestEndpointUrl);
        var url = $"{baseHost}/request?appid={appId}";
        return await SendGeneratorCommandAsync(url, Environment.GetEnvironmentVariable("STEAM_VETRA_RYUU_API_KEY"), cancellationToken);
    }

    public async Task<string> RequestUpdateAsync(uint appId, string? branch = null, CancellationToken cancellationToken = default)
    {
        var config = _configStore.Load();
        var baseHost = ExtractBaseHost(config.ManifestEndpointUrl);
        var effectiveBranch = string.IsNullOrWhiteSpace(branch)
            ? (string.IsNullOrWhiteSpace(config.ManifestBranch) ? "public" : config.ManifestBranch.Trim())
            : branch.Trim();

        var url = effectiveBranch.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? $"{baseHost}/requestupdate?appid={appId}&branch=public"
            : $"{baseHost}/requestbranch?appid={appId}&branch={Uri.EscapeDataString(effectiveBranch)}";

        return await SendGeneratorCommandAsync(url, Environment.GetEnvironmentVariable("STEAM_VETRA_RYUU_API_KEY"), cancellationToken);
    }

    private async Task<string> SendGeneratorCommandAsync(string url, string? authKey, CancellationToken cancellationToken)
    {
                using var request = CreateAuthenticatedGetRequest(url, authKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode} OK" : body.Trim();
    }

    private async Task TryTriggerGeneratorRequestAsync(
        AppConfig config,
        uint appId,
        string authKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var baseHost = ExtractBaseHost(config.ManifestEndpointUrl);
            var reqUrl = $"{baseHost}/request?appid={appId}";
            using var req = CreateAuthenticatedGetRequest(reqUrl, authKey);
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
        }
        catch
        {
        }
    }

    private async Task<ManifestFileEntry?> TryFetchExplicitLuaAsync(
        string luaUrl,
        string authKey,
        uint appId,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            using var req = CreateAuthenticatedGetRequest(luaUrl, authKey);
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var luaBytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            if (luaBytes.Length == 0 || IsZipArchive(luaBytes))
            {
                return null;
            }

            var luaRelDir = NormalizeRelDir(config.LuaPluginRelativeDirectory, @"config\stplug-in");
            return CreateEntry(Path.Combine(luaRelDir, $"{appId}.lua"), luaBytes);
        }
        catch
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateAuthenticatedGetRequest(string url, string? authKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(authKey))
        {
            request.Headers.TryAddWithoutValidation("X-Auth-Key", authKey.Trim());
        }

        return request;
    }

    private static string BuildDownloadUrl(string endpointTemplate, uint appId, string? fileType, string? branch)
    {
        var raw = string.IsNullOrWhiteSpace(endpointTemplate)
            ? "https://generator.ryuu.lol/api/download/{appId}"
            : endpointTemplate.Trim();

        string url;
        if (raw.Contains("{appId}", StringComparison.OrdinalIgnoreCase))
        {
            url = raw.Replace("{appId}", appId.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        else if (raw.Contains("{appid}", StringComparison.OrdinalIgnoreCase))
        {
            url = raw.Replace("{appid}", appId.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            url = $"{raw.TrimEnd('/')}/{appId}";
        }

        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(fileType))
        {
            queryParams.Add($"file_type={Uri.EscapeDataString(fileType)}");
        }

        if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            queryParams.Add($"branch={Uri.EscapeDataString(branch.Trim())}");
        }

        if (queryParams.Count == 0)
        {
            return url;
        }

        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{string.Join("&", queryParams)}";
    }

    private static string ExtractBaseHost(string endpointUrl)
    {
        if (Uri.TryCreate(endpointUrl.Replace("{appId}", "440", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Authority}";
        }

        return "https://generator.ryuu.lol";
    }

    public async Task<ManifestPayload> LoadFromLocalBundleAsync(
        uint appId,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {

        var config = _configStore.Load();
        var luaRelDir = NormalizeRelDir(config.LuaPluginRelativeDirectory, @"config\stplug-in");
        var depotRelDir = NormalizeRelDir(config.DepotCacheRelativeDirectory, "depotcache");

        var entries = new List<ManifestFileEntry>();

        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                continue;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

            if (ext == ".zip" || IsZipArchive(bytes))
            {
                var zipPayload = ExtractZipBundleToPayload(appId, bytes, config);
                entries.AddRange(zipPayload.Files);
            }
            else if (ext == ".lua")
            {
                var fileName = $"{appId}.lua";
                entries.Add(CreateEntry(Path.Combine(luaRelDir, fileName), bytes));
            }
            else if (ext == ".manifest")
            {
                var fileName = Path.GetFileName(path);
                entries.Add(CreateEntry(Path.Combine(depotRelDir, fileName), bytes));
                if (config.MirrorManifestsToConfigDepotCache && !string.Equals(depotRelDir, @"config\depotcache", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(CreateEntry(Path.Combine("config", "depotcache", fileName), bytes));
                }
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("No valid .lua, .manifest, or .zip files were found in selection.");
        }

        return new ManifestPayload
        {
            AppId = appId,
            ManifestVersion = "local-bundle",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = entries
        };
    }

    private static bool IsZipArchive(byte[] bytes)
        => bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;

    private static ManifestPayload ExtractZipBundleToPayload(uint appId, byte[] zipBytes, AppConfig config)
    {
        var luaRelDir = NormalizeRelDir(config.LuaPluginRelativeDirectory, @"config\stplug-in");
        var depotRelDir = NormalizeRelDir(config.DepotCacheRelativeDirectory, "depotcache");
        var entries = new List<ManifestFileEntry>();

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var zipEntry in archive.Entries)
        {
            if (string.IsNullOrEmpty(zipEntry.Name))
            {
                continue;
            }

            var ext = Path.GetExtension(zipEntry.Name).ToLowerInvariant();
            if (ext is not (".lua" or ".manifest"))
            {
                continue;
            }

            using var entryStream = zipEntry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            var raw = buffer.ToArray();

            if (ext == ".lua")
            {
                var targetName = zipEntry.Name.Equals($"{appId}.lua", StringComparison.OrdinalIgnoreCase)
                    ? zipEntry.Name
                    : $"{appId}.lua";
                entries.Add(CreateEntry(Path.Combine(luaRelDir, targetName), raw));
            }
            else if (ext == ".manifest")
            {
                entries.Add(CreateEntry(Path.Combine(depotRelDir, zipEntry.Name), raw));
                if (config.MirrorManifestsToConfigDepotCache && !string.Equals(depotRelDir, @"config\depotcache", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(CreateEntry(Path.Combine("config", "depotcache", zipEntry.Name), raw));
                }
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException($"ZIP archive for AppID {appId} did not contain any .lua or .manifest files.");
        }

        return new ManifestPayload
        {
            AppId = appId,
            ManifestVersion = "ryuu-zip-bundle",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = entries
        };
    }

    private static ManifestPayload WrapRawLuaInPayload(uint appId, byte[] luaBytes, AppConfig config)
    {
        var luaRelDir = NormalizeRelDir(config.LuaPluginRelativeDirectory, @"config\stplug-in");
        return new ManifestPayload
        {
            AppId = appId,
            ManifestVersion = "ryuu-raw-lua",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = new List<ManifestFileEntry>
            {
                CreateEntry(Path.Combine(luaRelDir, $"{appId}.lua"), luaBytes)
            }
        };
    }

    private static ManifestPayload NormalizePayloadPaths(ManifestPayload payload, AppConfig config)
    {
        var luaRelDir = NormalizeRelDir(config.LuaPluginRelativeDirectory, @"config\stplug-in");
        var depotRelDir = NormalizeRelDir(config.DepotCacheRelativeDirectory, "depotcache");
        var normalizedFiles = new List<ManifestFileEntry>();

        foreach (var file in payload.Files)
        {
            var ext = Path.GetExtension(file.RelativePath).ToLowerInvariant();
            var fileName = Path.GetFileName(file.RelativePath);
            if (ext == ".lua")
            {
                normalizedFiles.Add(new ManifestFileEntry
                {
                    RelativePath = Path.Combine(luaRelDir, fileName),
                    ContentBase64 = file.ContentBase64,
                    Sha256Hex = file.Sha256Hex
                });
            }
            else if (ext == ".manifest")
            {
                normalizedFiles.Add(new ManifestFileEntry
                {
                    RelativePath = Path.Combine(depotRelDir, fileName),
                    ContentBase64 = file.ContentBase64,
                    Sha256Hex = file.Sha256Hex
                });

                if (config.MirrorManifestsToConfigDepotCache && !string.Equals(depotRelDir, @"config\depotcache", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedFiles.Add(new ManifestFileEntry
                    {
                        RelativePath = Path.Combine("config", "depotcache", fileName),
                        ContentBase64 = file.ContentBase64,
                        Sha256Hex = file.Sha256Hex
                    });
                }
            }
            else
            {
                normalizedFiles.Add(file);
            }
        }

        payload.Files = normalizedFiles;
        return payload;
    }

    private static ManifestPayload BuildOfflineManifestPayload(uint appId, AppConfig config)
    {
        var luaRelDir = NormalizeRelDir(config.LuaPluginRelativeDirectory, @"config\stplug-in");
        var depotRelDir = NormalizeRelDir(config.DepotCacheRelativeDirectory, "depotcache");

        var catalogItem = config.Catalog.FirstOrDefault(c => c.AppId == appId);
        var manifestNames = catalogItem?.ManifestFiles is { Count: > 0 }
            ? catalogItem.ManifestFiles
            : new List<string> { $"{appId + 1}_8492019482716543210.manifest" };

        var dlcIds = catalogItem?.IncludedDlcIds ?? new List<uint>();

        var luaBuilder = new StringBuilder();
        luaBuilder.AppendLine($"-- Local configuration script for AppID {appId}");
        luaBuilder.AppendLine($"addappid({appId})");

        foreach (var mf in manifestNames)
        {
            var stem = Path.GetFileNameWithoutExtension(mf);
            var split = stem.Split('_');
            if (split.Length == 2 && uint.TryParse(split[0], out var depId))
            {
                luaBuilder.AppendLine($"addappid({depId}, 1, \"a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90\")");
                luaBuilder.AppendLine($"setManifestid({depId}, \"{split[1]}\", 0)");
            }
        }

        foreach (var dlcId in dlcIds)
        {
            luaBuilder.AppendLine($"addappid({dlcId})");
        }

        var luaBytes = Encoding.UTF8.GetBytes(luaBuilder.ToString());
        var files = new List<ManifestFileEntry>
        {
            CreateEntry(Path.Combine(luaRelDir, $"{appId}.lua"), luaBytes)
        };

        foreach (var mf in manifestNames)
        {
            var manifestHeader = JsonSerializer.Serialize(new
            {
                appId,
                manifestFile = mf,
                provisionedAtUtc = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true });
            var manifestBytes = Encoding.UTF8.GetBytes(manifestHeader);

            files.Add(CreateEntry(Path.Combine(depotRelDir, mf), manifestBytes));
            if (config.MirrorManifestsToConfigDepotCache && !string.Equals(depotRelDir, @"config\depotcache", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(CreateEntry(Path.Combine("config", "depotcache", mf), manifestBytes));
            }
        }

        return new ManifestPayload
        {
            AppId = appId,
            ManifestVersion = "1.0.0-offline",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = files
        };
    }

    private static string NormalizeRelDir(string? configured, string fallback)
        => string.IsNullOrWhiteSpace(configured)
            ? fallback
            : configured.Replace('/', Path.DirectorySeparatorChar).Trim().TrimStart(Path.DirectorySeparatorChar);

    private static ManifestFileEntry CreateEntry(string relativePath, byte[] bytes) => new()
    {
        RelativePath = relativePath.Replace('\\', '/'),
        ContentBase64 = Convert.ToBase64String(bytes),
        Sha256Hex = Convert.ToHexString(SHA256.HashData(bytes))
    };
}