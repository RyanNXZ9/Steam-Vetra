using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

using CatalogEntitlementManager.UI.ViewModels;
using Microsoft.Win32;

namespace CatalogEntitlementManager.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (SystemParameters.HighContrast || !OperatingSystem.IsWindowsVersionAtLeast(10))
            return;

        var source = (HwndSource)PresentationSource.FromVisual(this);
        if (SetBackgroundBlur(source.Handle, true))
            source.AddHook(OnMoveOrResize);
    }

    private IntPtr OnMoveOrResize(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int enterSizeMove = 0x0231, exitSizeMove = 0x0232;
        if (message == enterSizeMove || message == exitSizeMove)
            SetBackgroundBlur(hwnd, message == exitSizeMove);
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State, Flags, Color, Animation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttribute
    {
        public int Attribute;
        public IntPtr Policy;
        public nuint Size;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttribute attribute);

    private static bool SetBackgroundBlur(IntPtr hwnd, bool enabled)
    {
        const int accentPolicyAttribute = 19, blurBehind = 3;
        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new AccentPolicy { State = enabled ? blurBehind : 0 }, buffer, false);
            var attribute = new CompositionAttribute { Attribute = accentPolicyAttribute, Policy = buffer, Size = (nuint)size };
            return SetWindowCompositionAttribute(hwnd, ref attribute);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        RefreshPlayerAvatar();
        ApplyTheme(_viewModel.SelectedTheme);
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        base.OnClosed(e);
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTheme))
        {
            ApplyTheme(_viewModel.SelectedTheme);
        }
    }

    private void ApplyTheme(string theme)
    {
        var (light, accent, baseColor) = theme switch
        {
            "GTA 6" => ("#FFDDD0", "#E969AA", "#341B38"),
            "CodeNexus" => ("#E1F0FF", "#1674FF", "#102649"),
            _ => ("#F1F7FA", "#8DABB9", "#18232E")
        };

        var highlight = (Color)ColorConverter.ConvertFromString(light);
        var color = (Color)ColorConverter.ConvertFromString(accent);
        var shade = (Color)ColorConverter.ConvertFromString(baseColor);
        Resources["DesktopGlassTint"] = new SolidColorBrush(WithAlpha(shade, 68));
        // Keep the game grid readable even while native blur pauses during a drag.
        Resources["CatalogVeil"] = new SolidColorBrush(WithAlpha(shade, 148));
        Resources["CardActionSurface"] = new SolidColorBrush(WithAlpha(shade, 216));
        Resources["CardActionEdge"] = new SolidColorBrush(WithAlpha(highlight, 66));
        Resources["GlassCard"] = Gradient(WithAlpha(color, 76), WithAlpha(shade, 172));
        Resources["GlassPanel"] = Gradient(WithAlpha(color, 115), WithAlpha(shade, 222));
        Resources["GlassEdge"] = Gradient(WithAlpha(highlight, 208), WithAlpha(color, 77));
        Resources["GlassAction"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(highlight, 180), 0),
            new(WithAlpha(color, 130), 0.18),
            new(WithAlpha(shade, 190), 0.64),
            new(WithAlpha(color, 165), 1)
        }, 90);
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private void GlassPanel_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && SystemParameters.ClientAreaAnimation && sender is Border panel)
        {
            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var shift = new TranslateTransform();
            panel.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(9, 0, duration) { EasingFunction = ease });
            panel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
                0.28, 1, duration) { EasingFunction = ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        }
    }

    private static LinearGradientBrush Gradient(Color start, Color end) =>
        new(start, end, 55);

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OpenLibrary_OnClick(object sender, RoutedEventArgs e)
        => OpenLocalLibrary(false);

    private void OpenProfile_OnClick(object sender, RoutedEventArgs e)
        => OpenLocalLibrary(true);

    private void OpenLocalLibrary(bool profile)
    {
        var library = new LocalLibraryWindow(_viewModel) { Owner = this };
        library.ProfileSaved += (_, _) => RefreshPlayerAvatar();
        if (profile) library.SelectProfile();
        library.ShowDialog();
    }

    private void RefreshPlayerAvatar()
    {
        try
        {
            var player = Storage.LocalGameLibrary.Load(Storage.LocalGameLibrary.PlayerFile);
            PlayerInitial.Text = string.IsNullOrWhiteSpace(player.Name) ? "○" : System.Globalization.StringInfo.GetNextTextElement(player.Name).ToUpperInvariant();
            PlayerAvatar.ImageSource = LocalLibraryWindow.DecodeAvatar(player.Avatar);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or NotSupportedException or System.IO.InvalidDataException or System.Runtime.InteropServices.COMException)
        {
            PlayerAvatar.ImageSource = null;
        }
    }

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenStore(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TitleCardViewModel game }) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://store.steampowered.com/app/{game.AppId}/") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not open the store.", "Game Catalog");
        }
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}

