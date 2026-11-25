using System.Globalization;

namespace CatalogEntitlementManager.UI.ViewModels;

public sealed record SteamStoreGame(uint AppId, string Title, string Genre)
{
    public Uri ArtworkUri => GetArtworkUri(AppId);
    public Uri StoreUri => new($"https://store.steampowered.com/app/{AppId.ToString(CultureInfo.InvariantCulture)}/");

    internal static Uri GetArtworkUri(uint appId) =>
        new($"pack://application:,,,/CatalogEntitlementManager;component/UI/Artwork/{appId.ToString(CultureInfo.InvariantCulture)}.jpg");

    internal static IReadOnlyList<SteamStoreGame> Featured { get; } =
    [
        new(1145360, "Hades", "Roguelite"),
        new(367520, "Hollow Knight", "Metroidvania"),
        new(413150, "Stardew Valley", "Farming Sim"),
        new(620, "Portal 2", "Puzzle"),
        new(588650, "Dead Cells", "Roguelite"),
        new(105600, "Terraria", "Sandbox")
    ];
}