using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CatalogEntitlementManager.UI.ViewModels;

namespace CatalogEntitlementManager.UI.Views;

public sealed class HoldButton : Button
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(850) };
    private bool _holding;
    private bool _confirmed;
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(850);
    private Key? _key;
    private object? _startingItem;
    private bool RequiresHold => false;

    public HoldButton()
    {
        _timer.Tick += (_, _) =>
        {
            if (!_holding || !IsEnabled || !IsVisible || !RequiresHold || !ReferenceEquals(DataContext, _startingItem))
            {
                Cancel();
                return;
            }
            _timer.Stop();
            if (!_confirmed && SystemParameters.ClientAreaAnimation)
            {
                _confirmed = true;
                if (Hint is { } hint) hint.Text = "✓";
                var shake = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                foreach (var (ms, x) in new[] { (0, 0), (27, -2), (60, 2), (94, -1), (145, 0) })
                    shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
                var shift = new TranslateTransform();
                RenderTransform = shift;
                shift.BeginAnimation(TranslateTransform.XProperty, shake);
                _timer.Interval = TimeSpan.FromMilliseconds(155);
                _timer.Start();
                return;
            }
            OnClick();
            Cancel();
        };
        Unloaded += (_, _) => Cancel();
        DataContextChanged += (_, _) => Cancel();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Cancel(); };
    }

    private Border? Glow => GetTemplateChild("PointerGlow") as Border;
    private Border? Fill => GetTemplateChild("HoldFill") as Border;
    private TextBlock? Hint => GetTemplateChild("HoldHint") as TextBlock;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (RequiresHold && Glow?.Background is RadialGradientBrush source && ActualWidth > 0 && ActualHeight > 0)
        {
            var brush = source.IsFrozen ? source.Clone() : source;
            var point = e.GetPosition(this);
            brush.Center = brush.GradientOrigin = new Point(point.X / ActualWidth, point.Y / ActualHeight);
            Glow.Background = brush;
            Glow.Opacity = 1;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Glow is { } glow) glow.Opacity = 0;
        if (!_confirmed && _key is null) Cancel();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!RequiresHold) { base.OnMouseLeftButtonDown(e); return; }
        e.Handled = true;
        Focus();
        if (CaptureMouse()) Start(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!RequiresHold && !_holding) { base.OnMouseLeftButtonUp(e); return; }
        e.Handled = true;
        if (!_confirmed) Cancel();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_confirmed && _key is null) Cancel();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (RequiresHold && e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat) Start(e.Key);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (RequiresHold && e.Key is Key.Space or Key.Enter) { e.Handled = true; if (!_confirmed) Cancel(); return; }
        base.OnKeyUp(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Cancel();
    }

    private void Start(Key? key)
    {
        if (_holding) return;
        _holding = true;
        _key = key;
        _startingItem = DataContext;
        _timer.Interval = HoldDuration;
        if (Hint is { } hint) { hint.Text = "Segure"; hint.Visibility = Visibility.Visible; }
        if (GetTemplateChild("Label") is ContentPresenter label) label.Opacity = 0;
        if (Fill is { } fill)
        {
            var scale = new ScaleTransform(0, 1);
            fill.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, _timer.Interval));
        }
        _timer.Start();
    }

    private void Cancel()
    {
        _timer.Stop();
        _holding = false;
        _confirmed = false;
        _startingItem = null;
        _key = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (Hint is { } hint) hint.Visibility = Visibility.Collapsed;
        if (GetTemplateChild("Label") is ContentPresenter label) label.Opacity = 1;
        if (Fill?.RenderTransform is ScaleTransform { IsFrozen: false } scale)
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    }
}