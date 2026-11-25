using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogEntitlementManager.Models;
using CatalogEntitlementManager.Storage;

namespace CatalogEntitlementManager.Core.Services;

public sealed class GameFixService : IGameFixService
{
    private const string FixRecordDir = ".luatools-fix";
    private const string LuaToolsFixesApiUrl = "https://lua.tools/api/denuvo/fixes?appid=";
    private const string SteamlessReleaseApiUrl = "https://api.github.com/repos/atom0s/Steamless/releases/latest";

    private static readonly Regex VdfPathRegex = new(@"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AcfInstallDirRegex = new(@"""installdir""\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SkipExePatterns =
    {
        "redist", "vcredist", "directx", "dxsetup", "vc_redist", "dotnet", "setup", "install", "uninstall",
        "crashreport", "crashhandler", "bugsplat", "easyanticheat", "battleye", "be_service", "launch",
        "_launcher", "bootstrap", "cefprocess", "cef_process", "steamwebhelper", "uplay"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IConfigStore _configStore;
    private readonly HttpClient _httpClient;

    public GameFixService(
        IConfigStore configStore,
        HttpClient? httpClient = null)
    {
        _configStore = configStore;
        _httpClient = httpClient ?? CreateHttpClient();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LuaToolsGui/2.0");
        return client;
    }

    // Exact port of SteamLibraryService.GetInstallDir from LuaTools-main:
    // walks steamapps\libraryfolders.vdf across all drives -> appmanifest_<appid>.acf -> steamapps\common\<installdir>
    public string? ResolveGameInstallDirectory(string? steamRoot, uint appId)
    {
        if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot))
        {
            return null;
        }

        try
        {
            foreach (var libraryRoot in EnumerateLibraryRoots(steamRoot))
            {
                var acfPath = Path.Combine(libraryRoot, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(acfPath))
                {
                    continue;
                }

                var acfText = File.ReadAllText(acfPath);
                var match = AcfInstallDirRegex.Match(acfText);
                if (!match.Success)
                {
                    continue;
                }

                var installDirName = match.Groups[1].Value.Replace(@"\\", @"\");
                var fullGameDir = Path.Combine(libraryRoot, "steamapps", "common", installDirName);
                if (Directory.Exists(fullGameDir))
                {
                    return fullGameDir;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<string> EnumerateLibraryRoots(string steamRoot)
    {
        var fullRoot = Path.GetFullPath(steamRoot);
        yield return fullRoot;

        var vdfPath = Path.Combine(fullRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            yield break;
        }

        string text;
        try
        {
            text = File.ReadAllText(vdfPath);
        }
        catch
        {
            yield break;
        }

        foreach (Match m in VdfPathRegex.Matches(text))
        {
            var candidate = m.Groups[1].Value.Replace(@"\\", @"\");
            if (!string.Equals(candidate, fullRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    public IReadOnlyList<string> GetAppliedFixIds(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return Array.Empty<string>();
        }

        var fixDir = Path.Combine(installDirectory, FixRecordDir);
        if (!Directory.Exists(fixDir))
        {
            return Array.Empty<string>();
        }

        var applied = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(fixDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var rec = JsonSerializer.Deserialize<FixRecord>(File.ReadAllText(file), JsonOpts);
                    if (rec is not null && !string.IsNullOrWhiteSpace(rec.FixId))
                    {
                        applied.Add(rec.FixId);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return applied;
    }

    public async Task<IReadOnlyList<GameFixDto>> FetchFixesForAppAsync(uint appId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{LuaToolsFixesApiUrl}{appId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return GetBuiltInFallbackFixes(appId);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<GameFixesResponseDto>(json, JsonOpts);
            if (parsed?.Fixes is { Count: > 0 })
            {
                return parsed.Fixes;
            }
        }
        catch
        {
        }

        return GetBuiltInFallbackFixes(appId);
    }

    private static IReadOnlyList<GameFixDto> GetBuiltInFallbackFixes(uint appId) => appId switch
    {
        271590 => new[]
        {
            new GameFixDto
            {
                Id = "rockstar-gtav-legacy-fix",
                Title = "Rockstar Games Launcher Bypass (PlayGTAV.exe)",
                Description = "- After applying the fix, run the game with PlayGTAV.exe from the game folder.",
                HasFix = true,
                FixFilename = "271590.zip",
                Tags = new List<FixTagDto> { new() { Name = "Rockstar Games" }, new() { Name = "Generic" } }
            }
        },
        3240220 => new[]
        {
            new GameFixDto
            {
                Id = "rockstar-gtav-enhanced-fix",
                Title = "Rockstar Games Launcher Bypass (Build 24129078)",
                Description = "- After applying the fix, run the game with PlayGTAV.exe from the game folder.",
                HasFix = true,
                FixFilename = "3240220.zip",
                Tags = new List<FixTagDto> { new() { Name = "Rockstar Games" }, new() { Name = "Generic" } }
            }
        },
        1174180 => new[]
        {
            new GameFixDto
            {
                Id = "rockstar-rdr2-fix",
                Title = "Rockstar Social Club Bypass (Launcher.exe)",
                Description = "- After applying the fix, run the game with Launcher.exe from the game folder.",
                HasFix = true,
                FixFilename = "1174180.zip",
                Tags = new List<FixTagDto> { new() { Name = "Rockstar Games" }, new() { Name = "Generic" } }
            }
        },
        1245620 => new[]
        {
            new GameFixDto
            {
                Id = "eldenring-online-fix",
                Title = "Seamless Co-Op & Online Fix (Build 23850278)",
                Description = "- Set ersc_launcher.exe to Run as Administrator\n- Edit SeamlessCoop\\ersc_settings.ini password\n- Launch via ersc_launcher.exe",
                HasFix = true,
                FixFilename = "Elden Ring.zip",
                Tags = new List<FixTagDto> { new() { Name = "Online Fix" } }
            }
        },
        495420 => new[]
        {
            new GameFixDto
            {
                Id = "sod2-online-fix",
                Title = "State of Decay 2 Juggernaut Edition Online Fix",
                Description = "- Online multiplayer fix drop-in replacement files for State of Decay 2.",
                HasFix = true,
                FixFilename = "495420_online_fix.zip",
                Tags = new List<FixTagDto> { new() { Name = "Online Fix" } }
            }
        },
        _ => Array.Empty<GameFixDto>()
    };

    public async Task<FixOperationResult> DownloadAndApplyFixAsync(
        uint appId,
        GameFixDto fix,
        string installDirectory,
        CancellationToken cancellationToken = default)
    {

        // Check local fixes cache folder (<AppBase>/fixes/<AppId>.zip or <FixFilename>) first
        var localFixesDir = Path.Combine(AppContext.BaseDirectory, "fixes");
        Directory.CreateDirectory(localFixesDir);

        var candidateFiles = new List<string>
        {
            Path.Combine(localFixesDir, $"{appId}.zip"),
            Path.Combine(localFixesDir, $"{appId}_fix.zip")
        };
        if (!string.IsNullOrWhiteSpace(fix.FixFilename))
        {
            candidateFiles.Insert(0, Path.Combine(localFixesDir, fix.FixFilename));
        }

        var existingLocalZip = candidateFiles.FirstOrDefault(File.Exists);
        if (!string.IsNullOrEmpty(existingLocalZip))
        {
            return await ApplyFixZipAsync(appId, fix.Id, existingLocalZip, installDirectory, cancellationToken);
        }

        // Probe configured remote fix mirror if available
        var config = _configStore.Load();
        var authKey = Environment.GetEnvironmentVariable("STEAM_VETRA_RYUU_API_KEY") ?? string.Empty;
        var remoteCandidates = new[]
        {
            $"https://generator.ryuu.lol/api/fixes/download/{appId}",
            $"https://lua.tools/api/denuvo/download?fix={Uri.EscapeDataString(fix.Id)}&slot=fix"
        };

        foreach (var url in remoteCandidates)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(authKey))
                {
                    req.Headers.TryAddWithoutValidation("X-Auth-Key", authKey);
                }

                using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!res.IsSuccessStatusCode)
                {
                    continue;
                }

                var rawBytes = await res.Content.ReadAsByteArrayAsync(cancellationToken);
                if (rawBytes.Length >= 4 && rawBytes[0] == 0x50 && rawBytes[1] == 0x4B && rawBytes[2] == 0x03 && rawBytes[3] == 0x04)
                {
                    var cachedZip = Path.Combine(localFixesDir, $"{appId}_fix.zip");
                    await File.WriteAllBytesAsync(cachedZip, rawBytes, cancellationToken);
                    return await ApplyFixZipAsync(appId, fix.Id, cachedZip, installDirectory, cancellationToken);
                }
            }
            catch
            {
            }
        }

        return new FixOperationResult
        {
            Success = false,
            Message = $"Fix archive '{fix.FixFilename ?? $"{appId}.zip"}' requires manual download from generator.ryuu.lol/fixes (or select the downloaded .zip via 'Apply Fix .ZIP')."
        };
    }

    // Exact 4-phase port of ManifestJobFactory.ApplyDenuvoFix from LuaTools-main:
    // Phase 1: Plan & Zip-slip guard
    // Phase 2: Provisional commit of .luatools-fix/<fixKey>.json
    // Phase 3: Backup modified files to .luatools-fix/<fixKey>/<relPath>.bak with SHA-256 HashBefore & HashAfter
    // Phase 4: Settle authoritative record
    public async Task<FixOperationResult> ApplyFixZipAsync(
        uint appId,
        string fixId,
        string zipFilePath,
        string installDirectory,
        CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return new FixOperationResult
            {
                Success = false,
                Message = "Game installation folder was not found. Install the game on Steam or select the game folder."
            };
        }

        if (!File.Exists(zipFilePath))
        {
            return new FixOperationResult
            {
                Success = false,
                Message = $"Fix archive not found: {zipFilePath}"
            };
        }

        return await Task.Run(() =>
        {
            var fixKey = SafeFixKey(string.IsNullOrWhiteSpace(fixId) ? $"fix_{appId}" : fixId);
            var fixDir = Path.Combine(installDirectory, FixRecordDir);
            var recordPath = Path.Combine(fixDir, $"{fixKey}.json");

            var record = new FixRecord
            {
                AppId = appId,
                FixId = fixId,
                AppliedAt = DateTimeOffset.UtcNow.ToString("o")
            };

            using var archive = ZipFile.OpenRead(zipFilePath);
            var plan = new List<(string RelPath, string Dest, string? BakRel, ZipArchiveEntry Entry)>();
            var failed = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var relPath = entry.FullName.Replace('\\', '/');
                var dest = ResolveInside(installDirectory, entry.FullName);
                if (dest is null)
                {
                    failed++;
                    continue;
                }

                var bakRel = File.Exists(dest) ? $"{fixKey}/{relPath}.bak" : null;
                plan.Add((relPath, dest, bakRel, entry));
            }

            if (plan.Count == 0)
            {
                return new FixOperationResult
                {
                    Success = false,
                    Message = "The selected ZIP archive contained no valid files to extract."
                };
            }

            foreach (var (relPath, _, bakRel, _) in plan)
            {
                record.Files.Add(new FixRecordEntry
                {
                    RelativePath = relPath,
                    Action = bakRel is null ? "added" : "modified",
                    BackupPath = bakRel
                });
            }

            Directory.CreateDirectory(fixDir);
            File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOpts));

            var applied = new List<FixRecordEntry>(plan.Count);
            foreach (var (relPath, dest, bakRel, entry) in plan)
            {
                try
                {
                    FixRecordEntry recEntry;
                    if (bakRel is not null)
                    {
                        var bakAbs = Path.Combine(fixDir, bakRel.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(bakAbs))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(bakAbs)!);
                            File.Copy(dest, bakAbs, overwrite: false);
                        }

                        recEntry = new FixRecordEntry
                        {
                            RelativePath = relPath,
                            Action = "modified",
                            BackupPath = bakRel,
                            HashBefore = ComputeFileSha256(dest)
                        };
                    }
                    else
                    {
                        recEntry = new FixRecordEntry
                        {
                            RelativePath = relPath,
                            Action = "added"
                        };
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                    recEntry.HashAfter = ComputeFileSha256(dest);
                    applied.Add(recEntry);
                }
                catch
                {
                    failed++;
                }
            }

            record.Files = applied;
            try
            {
                File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOpts));
            }
            catch
            {
            }

            if (failed > 0)
            {
                return new FixOperationResult
                {
                    Success = false,
                    InstallDirectory = installDirectory,
                    FilesAffected = applied.Count,
                    Message = $"Partially applied ({applied.Count} applied, {failed} failed — close the game if running)."
                };
            }

            return new FixOperationResult
            {
                Success = true,
                InstallDirectory = installDirectory,
                FilesAffected = applied.Count,
                Message = $"Applied {applied.Count} fix file(s) to '{installDirectory}' (originals backed up in {FixRecordDir})."
            };
        }, cancellationToken);
    }

    // Exact port of ManifestJobFactory.RevertDenuvoFix from LuaTools-main
    public async Task<FixOperationResult> RevertFixAsync(
        uint appId,
        string fixId,
        string installDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return new FixOperationResult
            {
                Success = false,
                Message = "Game installation folder not found."
            };
        }

        return await Task.Run(() =>
        {
            var fixKey = SafeFixKey(string.IsNullOrWhiteSpace(fixId) ? $"fix_{appId}" : fixId);
            var fixDir = Path.Combine(installDirectory, FixRecordDir);
            var recordPath = Path.Combine(fixDir, $"{fixKey}.json");

            if (!File.Exists(recordPath))
            {
                // Fallback: look for any record in .luatools-fix if fixId differed
                var anyRecord = Directory.Exists(fixDir)
                    ? Directory.EnumerateFiles(fixDir, "*.json").FirstOrDefault()
                    : null;
                if (anyRecord is null)
                {
                    return new FixOperationResult
                    {
                        Success = false,
                        Message = "No applied fix record (.luatools-fix) found in game directory."
                    };
                }

                recordPath = anyRecord;
                fixKey = Path.GetFileNameWithoutExtension(anyRecord);
            }

            FixRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<FixRecord>(File.ReadAllText(recordPath), JsonOpts);
            }
            catch
            {
                record = null;
            }

            if (record is null)
            {
                return new FixOperationResult
                {
                    Success = false,
                    Message = "Could not parse fix revert record."
                };
            }

            int restored = 0, deleted = 0, errors = 0, conflicts = 0;

            foreach (var entry in record.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dest = ResolveInside(installDirectory, entry.RelativePath);
                if (dest is null)
                {
                    errors++;
                    continue;
                }

                try
                {
                    if (entry.Action == "modified" && !string.IsNullOrEmpty(entry.BackupPath))
                    {
                        var bakAbs = ResolveInside(fixDir, entry.BackupPath);
                        if (bakAbs is null || !File.Exists(bakAbs))
                        {
                            errors++;
                            continue;
                        }

                        if (File.Exists(dest) &&
                            !HashMatches(dest, entry.HashAfter) &&
                            !HashMatches(dest, entry.HashBefore))
                        {
                            conflicts++;
                            continue;
                        }

                        File.Copy(bakAbs, dest, overwrite: true);
                        restored++;
                    }
                    else if (entry.Action == "added")
                    {
                        if (File.Exists(dest))
                        {
                            if (!HashMatches(dest, entry.HashAfter))
                            {
                                conflicts++;
                                continue;
                            }

                            File.Delete(dest);
                            deleted++;
                        }
                    }
                }
                catch
                {
                    errors++;
                }
            }

            if (conflicts > 0)
            {
                return new FixOperationResult
                {
                    Success = false,
                    InstallDirectory = installDirectory,
                    Message = $"Revert stopped: {conflicts} file(s) were modified after this fix was applied."
                };
            }

            if (errors > 0)
            {
                return new FixOperationResult
                {
                    Success = false,
                    InstallDirectory = installDirectory,
                    Message = $"Partial revert ({errors} file(s) locked). Close the game and try again."
                };
            }

            var backupSubDir = Path.Combine(fixDir, fixKey);
            try { if (Directory.Exists(backupSubDir)) Directory.Delete(backupSubDir, recursive: true); } catch { }
            try { if (File.Exists(recordPath)) File.Delete(recordPath); } catch { }

            return new FixOperationResult
            {
                Success = true,
                InstallDirectory = installDirectory,
                FilesAffected = restored + deleted,
                Message = $"Reverted fix cleanly ({restored} original file(s) restored, {deleted} added file(s) removed)."
            };
        }, cancellationToken);
    }

    // Exact port of SteamlessService from LuaTools-main:
    // Downloads atom0s/Steamless from GitHub, scans game executables, strips SteamStub DRM, backs up to .bak
    public async Task<FixOperationResult> RunSteamlessDrmStripAsync(
        uint appId,
        string installDirectory,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return new FixOperationResult
            {
                Success = false,
                Message = "Game installation directory not found on disk."
            };
        }

        var toolDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatalogEntitlementManager",
            "steamless");
        var cliPath = Path.Combine(toolDir, "Steamless.CLI.exe");

        if (!File.Exists(cliPath))
        {
            onProgress?.Invoke("Downloading atom0s/Steamless CLI from GitHub...");
            var downloaded = await EnsureSteamlessCliAsync(toolDir, cliPath, cancellationToken);
            if (!downloaded)
            {
                return new FixOperationResult
                {
                    Success = false,
                    Message = "Could not download Steamless.CLI.exe from GitHub."
                };
            }
        }

        var exes = Directory.EnumerateFiles(installDirectory, "*.exe", SearchOption.AllDirectories)
            .Where(p => !SkipExePatterns.Any(s => Path.GetFileName(p).Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (exes.Count == 0)
        {
            return new FixOperationResult
            {
                Success = false,
                Message = "No game executables (.exe) found in installation directory."
            };
        }

        int patched = 0, unchanged = 0;
        foreach (var exe in exes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                onProgress?.Invoke($"Scanning {Path.GetFileName(exe)} with Steamless...");
                var psi = new ProcessStartInfo(cliPath, $"\"{exe}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = toolDir
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    await proc.WaitForExitAsync(cancellationToken);
                }

                var unpacked = exe + ".unpacked.exe";
                if (File.Exists(unpacked))
                {
                    var bak = exe + ".bak";
                    if (!File.Exists(bak))
                    {
                        File.Copy(exe, bak);
                    }

                    File.Delete(exe);
                    File.Move(unpacked, exe);
                    patched++;
                }
                else
                {
                    unchanged++;
                }
            }
            catch
            {
                unchanged++;
                try { File.Delete(exe + ".unpacked.exe"); } catch { }
            }
        }

        return new FixOperationResult
        {
            Success = true,
            InstallDirectory = installDirectory,
            FilesAffected = patched,
            Message = patched > 0
                ? $"Steamless removed SteamStub DRM from {patched} executable(s) ({unchanged} unchanged; originals backed up to .exe.bak)."
                : $"Steamless scanned {exes.Count} executable(s): no SteamStub DRM wrapper detected (or game uses external fix)."
        };
    }

    private async Task<bool> EnsureSteamlessCliAsync(string toolDir, string cliPath, CancellationToken ct)
    {
        try
        {
            using var res = await _httpClient.GetAsync(SteamlessReleaseApiUrl, ct);
            if (!res.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("assets", out var assets)) return false;

            string? zipUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(zipUrl)) return false;

            Directory.CreateDirectory(toolDir);
            var zipPath = Path.Combine(toolDir, "steamless.zip");
            using (var dl = await _httpClient.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                dl.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await dl.Content.CopyToAsync(fs, ct);
            }

            ZipFile.ExtractToDirectory(zipPath, toolDir, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { }
            return File.Exists(cliPath);
        }
        catch
        {
            return false;
        }
    }

    public void OpenRyuuFixesCatalog(uint appId)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://generator.ryuu.lol/fixes",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static string SafeFixKey(string fixId)
        => string.Concat(fixId.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static string? ResolveInside(string root, string relative)
    {
        try
        {
            var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool HashMatches(string filePath, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return true;
        if (!File.Exists(filePath)) return false;
        return string.Equals(ComputeFileSha256(filePath), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}