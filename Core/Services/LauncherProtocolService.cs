using System.Diagnostics;
using System.IO;

namespace CatalogEntitlementManager.Core.Services;

public sealed class LauncherProtocolService : ILauncherProtocolService
{
    public void TriggerInstallAppUri(uint appId)
    {
        LaunchProtocolUri("steam://open/main");
        LaunchProtocolUri($"steam://install/{appId}");
    }

    public void TriggerRunAppUri(uint appId)
    {
        LaunchProtocolUri("steam://open/main");
        LaunchProtocolUri($"steam://run/{appId}");
    }

    public async Task<bool> RestartSteamAndDownloadAsync(string? steamDirectory, uint appId, CancellationToken cancellationToken = default)
    {

        // If Steam is already running with OpenSteamTool watching config/stplug-in via opensteamtool.toml,
        // Lua files hot-reload without killing Steam! Only start Steam if it is not running yet.
        var isAlreadyRunning = Process.GetProcessesByName("steam").Length > 0 &&
                               Process.GetProcessesByName("steamwebhelper").Length > 0;

        if (isAlreadyRunning)
        {
            await Task.Delay(900, cancellationToken);
            try
            {
                LaunchProtocolUri("steam://open/main");
                LaunchProtocolUri($"steam://install/{appId}");
            }
            catch
            {
            }

            return true;
        }

        var restarted = await RestartSteamClientAsync(steamDirectory, cancellationToken);
        if (restarted)
        {
            // Wait up to 35 seconds so first-time OpenSteamTool signature scanning can finish cleanly
            for (var i = 0; i < 70; i++)
            {
                await Task.Delay(500, cancellationToken);
                if (Process.GetProcessesByName("steamwebhelper").Length > 0)
                {
                    break;
                }
            }

            await Task.Delay(1500, cancellationToken);
            try
            {
                LaunchProtocolUri("steam://open/main");
                LaunchProtocolUri($"steam://install/{appId}");
            }
            catch
            {
            }
        }

        return restarted;
    }

    public async Task<bool> RestartSteamClientAsync(string? steamDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            var running = Process.GetProcessesByName("steam");
            foreach (var proc in running)
            {
                using (proc)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: false);
                        await proc.WaitForExitAsync(cancellationToken);
                    }
                    catch
                    {
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(steamDirectory))
            {
                var exePath = Path.Combine(steamDirectory, "steam.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = steamDirectory,
                        UseShellExecute = true
                    });
                    return true;
                }
            }

            LaunchProtocolUri("steam://open/main");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LaunchProtocolUri(string uri)
    {
        if (!uri.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only native steam:// protocol URIs are permitted.", nameof(uri));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }
}