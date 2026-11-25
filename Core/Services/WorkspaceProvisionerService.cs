using System.IO;
using System.Security.Cryptography;
using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.Core.Services;

public sealed class WorkspaceProvisionerService : IWorkspaceProvisionerService
{
    public async Task<ProvisionResult> ProvisionFilesAsync(
        uint appId,
        string baseDirectory,
        ManifestPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Target base directory is not configured.", nameof(baseDirectory));
        }

        var fullBaseDir = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(fullBaseDir);

        var backups = new List<(string TargetPath, bool ExistedBefore, byte[]? PreviousBytes)>();
        var tempFiles = new List<string>();
        var writtenFiles = new List<string>();

        try
        {
            foreach (var entry in payload.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sanitizedRel = entry.RelativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var targetPath = Path.GetFullPath(Path.Combine(fullBaseDir, sanitizedRel));

                if (!targetPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Path traversal blocked for entry '{entry.RelativePath}'.");
                }

                var rawBytes = Convert.FromBase64String(entry.ContentBase64);
                if (!string.IsNullOrWhiteSpace(entry.Sha256Hex))
                {
                    var actualHash = Convert.ToHexString(SHA256.HashData(rawBytes));
                    if (!string.Equals(actualHash, entry.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SHA-256 integrity mismatch on '{entry.RelativePath}'.");
                    }
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var existed = File.Exists(targetPath);
                var previousBytes = existed ? await File.ReadAllBytesAsync(targetPath, cancellationToken) : null;
                backups.Add((targetPath, existed, previousBytes));

                var stagingTempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
                tempFiles.Add(stagingTempPath);

                await File.WriteAllBytesAsync(stagingTempPath, rawBytes, cancellationToken);
                File.Move(stagingTempPath, targetPath, overwrite: true);
                writtenFiles.Add(targetPath);
            }

            return new ProvisionResult
            {
                Success = true,
                AppId = appId,
                WrittenFiles = writtenFiles,
                Message = $"Atomically provisioned {writtenFiles.Count} file(s) for AppID {appId}."
            };
        }
        catch (Exception ex)
        {
            // Rollback any modified or created files
            for (var i = backups.Count - 1; i >= 0; i--)
            {
                var (targetPath, existedBefore, previousBytes) = backups[i];
                try
                {
                    if (existedBefore && previousBytes is not null)
                    {
                        File.WriteAllBytes(targetPath, previousBytes);
                    }
                    else if (!existedBefore && File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                }
                catch
                {
                    // Best-effort rollback on catastrophic I/O errors
                }
            }

            foreach (var tmp in tempFiles)
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch
                {
                    // Ignore leftover temp cleanup error
                }
            }

            return new ProvisionResult
            {
                Success = false,
                AppId = appId,
                WrittenFiles = Array.Empty<string>(),
                Message = $"Provisioning failed and rolled back cleanly: {ex.Message}"
            };
        }
    }
}