using System.IO;
using System.Text.Json;
using CatalogEntitlementManager.Storage;

namespace CatalogEntitlementManager.UI.ViewModels;

public sealed record TitleCardViewModel(uint AppId, string Title, string Publisher, string Genre)
{
    public string ArtworkUri => ResolveArtwork(AppId, string.Empty);

    internal static string ResolveArtwork(uint appId, string fallback) =>
        string.IsNullOrEmpty(fallback) ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg" : fallback;
}

public sealed class MainViewModel : ObservableObject
{
    private string _query = "";
    private string _selectedTheme = "Glass";
    private readonly string _themePath = Path.Combine(Path.GetDirectoryName(LocalGameLibrary.PlayerFile)!, "theme.txt");
    public IReadOnlyList<string> Themes { get; } = new[] { "Glass", "GTA 6", "CodeNexus" };
    public IReadOnlyList<TitleCardViewModel> AllTitles { get; }
    public string TargetBaseDirectory => LocalGameLibrary.DetectSteamPath() ?? string.Empty;

    public MainViewModel()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        AllTitles = JsonSerializer.Deserialize<List<TitleCardViewModel>>(File.ReadAllText(file)) ?? new();
        if (File.Exists(_themePath) && File.ReadAllText(_themePath).Trim() is string theme && Themes.Contains(theme))
            _selectedTheme = theme;
    }

    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) OnPropertyChanged(nameof(FilteredTitles)); }
    }

    public IEnumerable<TitleCardViewModel> FilteredTitles => AllTitles.Where(game =>
        game.Title.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)
        || game.Genre.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)
        || game.AppId.ToString().Contains(Query.Trim(), StringComparison.Ordinal));

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Themes.Contains(value) || !SetProperty(ref _selectedTheme, value)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_themePath)!);
            File.WriteAllText(_themePath, value);
        }
    }
}

