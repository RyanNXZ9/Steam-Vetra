using CatalogEntitlementManager.Models;

namespace CatalogEntitlementManager.UI.ViewModels;

public sealed class TitleCardViewModel : ObservableObject
{
    private bool _isReadyInLibrary;
    private bool _isProvisioning;

    public uint AppId { get; }
    public string Title { get; }
    public string Publisher { get; }
    public string Genre { get; }
    public string BuildVersion { get; }
    public string HeaderImageUrl { get; }
    public string ArtworkUri => ResolveArtwork(AppId, HeaderImageUrl);

    internal static string ResolveArtwork(uint appId, string fallback) =>
        appId is 730 or 570 or 1245620 or 1091500 or 2050650 or 1174180 or 271590 or 3240220
        ? SteamStoreGame.GetArtworkUri(appId).AbsoluteUri
        : SteamStoreGame.Featured.FirstOrDefault(game => game.AppId == appId)?.ArtworkUri.AbsoluteUri ?? fallback;
    public int LatestDepotsCount { get; }
    public int OutdatedDepotsCount { get; }
    public int MissingDepotsCount { get; }
    public int DlcCount { get; }
    public IReadOnlyList<string> ManifestFiles { get; }
    public IReadOnlyList<uint> IncludedDlcIds { get; }
    public IReadOnlyList<uint> MissingDlcIds { get; }

    public string DepotSummaryBadge => $"Latest ({LatestDepotsCount}) • DLC ({DlcCount})" +
        (MissingDepotsCount > 0 ? $" • Missing ({MissingDepotsCount})" : string.Empty);

    public string ManifestTooltipSummary
    {
        get
        {
            var lines = new List<string> { $"{AppId}.lua" };
            if (ManifestFiles.Count > 0)
            {
                lines.AddRange(ManifestFiles);
            }
            if (IncludedDlcIds.Count > 0)
            {
                lines.Add($"DLCs in Lua ({IncludedDlcIds.Count}): {string.Join(", ", IncludedDlcIds.Take(10))}" +
                          (IncludedDlcIds.Count > 10 ? "..." : string.Empty));
            }
            if (MissingDlcIds.Count > 0)
            {
                lines.Add($"Missing DLCs ({MissingDlcIds.Count}): {string.Join(", ", MissingDlcIds)}");
            }
            return string.Join("\n", lines);
        }
    }

    public bool IsReadyInLibrary
    {
        get => _isReadyInLibrary;
        set
        {
            if (SetProperty(ref _isReadyInLibrary, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(LibraryStatusText));
            }
        }
    }

    public bool IsProvisioning
    {
        get => _isProvisioning;
        set
        {
            if (SetProperty(ref _isProvisioning, value))
            {
                OnPropertyChanged(nameof(PrimaryActionText));
            }
        }
    }

    public string StatusBadgeText => IsReadyInLibrary ? "In Library" : "Available";
    public string LibraryStatusText => IsReadyInLibrary ? "Added to Steam Library" : "Ready to add";
    public string PrimaryActionText => IsProvisioning ? "Adding to Steam..." : "Add to Steam";

    public TitleCardViewModel(CatalogTitle model, bool readyInLibrary = false)
    {
        AppId = model.AppId;
        Title = model.Title;
        Publisher = model.Publisher;
        Genre = model.Genre;
        BuildVersion = model.BuildVersion;
        HeaderImageUrl = model.HeaderImageUrl;
        LatestDepotsCount = model.LatestDepotsCount > 0 ? model.LatestDepotsCount : Math.Max(1, model.ManifestFiles.Count);
        OutdatedDepotsCount = model.OutdatedDepotsCount;
        MissingDepotsCount = model.MissingDepotsCount;
        DlcCount = model.DlcCount > 0 ? model.DlcCount : model.IncludedDlcIds.Count;
        ManifestFiles = model.ManifestFiles;
        IncludedDlcIds = model.IncludedDlcIds;
        MissingDlcIds = model.MissingDlcIds;
        _isReadyInLibrary = readyInLibrary;
    }

}