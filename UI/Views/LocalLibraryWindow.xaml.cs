using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CatalogEntitlementManager.Storage;
using CatalogEntitlementManager.UI.ViewModels;
using Microsoft.Win32;

namespace CatalogEntitlementManager.UI.Views;

public partial class LocalLibraryWindow : Window
{
    private readonly MainViewModel _catalog;
    private Dictionary<uint, string> _installed = new();
    private Dictionary<uint, string> _cachedCovers = new();
    internal event EventHandler? ProfileSaved;
    private LocalPlayerData _player = new();
    private byte[]? _photo;

    private readonly string _playerFile;

    public LocalLibraryWindow(MainViewModel catalog, string? playerFile = null)
    {
        InitializeComponent();
        _catalog = catalog;
        _playerFile = playerFile ?? LocalGameLibrary.PlayerFile;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner is { } owner)
        {
            foreach (string key in new[] { "GlassPanel", "GlassCard", "GlassEdge", "GlassInput", "CardActionSurface", "CardActionEdge", "CatalogActionStyle", "SecondaryButtonStyle" })
                if (owner.TryFindResource(key) is { } resource) Resources[key] = resource;
        }
        try
        {
            _player = LocalGameLibrary.Load(_playerFile);
            ProfileName.Text = _player.Name;
            _photo = _player.Avatar;
            DisplayProfile();
            RefreshGames();
            var root = _catalog.TargetBaseDirectory;
            (_installed, _cachedCovers) = await Task.Run(() =>
            {
                var installed = LocalGameLibrary.ReadInstalled(root);
                return (installed, LocalGameLibrary.ReadCachedCovers(root, installed.Keys));
            });
            if (IsVisible) RefreshGames();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Feedback.Text = "Could not read local library or profile data.";
        }
    }

    private void RefreshGames()
    {
        var titles = new Dictionary<uint, string>(_installed);
        foreach (var game in _player.Added)
            titles.TryAdd(game.Key, game.Value);
        var choices = new Dictionary<uint, string>(titles);
        foreach (var card in _catalog.AllTitles) choices.TryAdd(card.AppId, card.Title);
        var selected = (Picker.SelectedItem as LibraryGame)?.AppId;
        var options = choices.Select(pair => new LibraryGame(pair.Key, pair.Value, _installed.ContainsKey(pair.Key)))
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Picker.ItemsSource = options;
        Picker.SelectedItem = options.FirstOrDefault(game => game.AppId == selected);
        var art = _catalog.AllTitles.GroupBy(game => game.AppId).ToDictionary(group => group.Key, group => group.First().ArtworkUri);
        var games = options.Where(game => titles.ContainsKey(game.AppId) && !_player.Hidden.Contains(game.AppId))
            .Select(game => game with { ArtworkUri = _cachedCovers.GetValueOrDefault(game.AppId) ?? art.GetValueOrDefault(game.AppId)
                ?? TitleCardViewModel.ResolveArtwork(game.AppId, $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.AppId}/header.jpg") }).ToList();
        Games.ItemsSource = games;
        Feedback.Text = games.Count == 0 ? "Choose a game above to start your collection." : string.Empty;
    }

    private bool SaveChange(Action<LocalPlayerData> change)
    {
        try
        {
            using var gate = new Mutex(false, @"Local\CatalogEntitlementManager.LocalPlayer");
            bool entered;
            try { entered = gate.WaitOne(TimeSpan.FromSeconds(1)); }
            catch (AbandonedMutexException) { entered = true; }
            if (!entered) { Feedback.Text = "Profile is busy. Try again."; return false; }
            try
            {
                var next = LocalGameLibrary.Load(_playerFile);
                change(next);
                LocalGameLibrary.Save(_playerFile, next);
                _player = next;
            }
            finally { gate.ReleaseMutex(); }
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Feedback.Text = "Could not save changes on this device.";
            return false;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Picker.SelectedItem is not LibraryGame card)
        {
            Feedback.Text = "Select a game from the catalog first.";
            return;
        }
        if (SaveChange(data => { data.Hidden.Remove(card.AppId); data.Added[card.AppId] = card.Title; }))
            RefreshGames();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LibraryGame game } &&
            SaveChange(data => { data.Added.Remove(game.AppId); data.Hidden.Add(game.AppId); }))
        {
            RefreshGames();
            Feedback.Text = $"Removed {game.Title} from this list.";
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LibraryGame game }) return;
        var uri = game.Installed ? $"steam://rungameid/{game.AppId}" : $"steam://nav/games/details/{game.AppId}";
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { Feedback.Text = "Windows could not open Steam."; }
    }

    private void Photo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Photo (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png" };
        if (dialog.ShowDialog(this) != true) return;
        ImportPhoto(dialog.FileName);
    }

    private void ImportPhoto(string file)
    {
        try
        {
            using var input = File.OpenRead(file);
            if (input.Length > 16 * 1024 * 1024) { Feedback.Text = "Choose a photo smaller than 16 MB."; return; }
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000)
            { Feedback.Text = "Choose a smaller image."; return; }
            var bitmap = new BitmapImage();
            input.Position = 0;
            bitmap.BeginInit();
            bitmap.StreamSource = input;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = Math.Min(256, frame.PixelWidth);
            else bitmap.DecodePixelHeight = Math.Min(256, frame.PixelHeight);
            bitmap.EndInit();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            _photo = output.ToArray();
            DisplayProfile();
            Feedback.Text = "Photo selected. Save profile to keep it.";
        }
        catch (Exception ex) when (IsFileError(ex) || ex is ArgumentException or System.Runtime.InteropServices.COMException)
        { Feedback.Text = "Could not open that image."; }
    }

    private void ClearPhoto_Click(object sender, RoutedEventArgs e)
    {
        _photo = null;
        DisplayProfile();
        Feedback.Text = "Save profile to remove the photo.";
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileName.Text.Trim();
        if (name.Length == 0) { Feedback.Text = "Enter a profile name."; return; }
        if (!SaveChange(data => { data.Name = name; data.Avatar = _photo; })) return;
        DisplayProfile();
        Feedback.Text = "Profile saved on this device.";
        ProfileSaved?.Invoke(this, EventArgs.Empty);
    }

    private void DisplayProfile()
    {
        var name = _player.Name;
        PlayerHeading.Text = string.IsNullOrWhiteSpace(name) ? "My library" : name;
        Initial.Text = string.IsNullOrWhiteSpace(name) ? "•" : System.Globalization.StringInfo.GetNextTextElement(name).ToUpperInvariant();
        AvatarBrush.ImageSource = DecodeAvatar(_photo);
    }

    internal static BitmapSource? DecodeAvatar(byte[]? photo)
    {
        if (photo is not { Length: > 0 }) return null;
        using var stream = new MemoryStream(photo);
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    internal void SelectProfile() => ProfileTab.IsSelected = true;

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException or InvalidDataException;
}

