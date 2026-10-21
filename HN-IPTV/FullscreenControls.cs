using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace HnIptv;

// Owned HWND is necessary to draw above the native video window.
internal sealed class FullscreenControls : Window
{
    private readonly Window _home;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private Point _last;
    private bool _closed, _updating;
    private Button? _captions, _speech, _screen, _mute, _pause;
    private readonly Action _exit;
    internal bool ControlsVisible => IsVisible;
    private readonly Stopwatch _idle = Stopwatch.StartNew();
    public FullscreenControls(Window home, Action play, Action pause, Action stop, Action mute, Action captions,
        Action exit, Action<int> volume, int initialVolume, Action? translateSpeech = null, Action? translateText = null)
    {
        _home = home; _exit = exit; Owner = home; ShowActivated = false; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(24, 35, 56));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        Button Add(string label, Action action) {
            var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            button.Click += (_, _) => action(); panel.Children.Add(button); return button;
        }
        Add("Play", play); _pause = Add("Pause", pause); Add("Stop", stop); _mute = Add("Mute (M)", mute);
        if (BuildInfo.Engine == "mpv") _captions = Add("CC", captions);
        if (translateSpeech != null) _speech = Add("Translate speech", translateSpeech);
        if (translateText != null && BuildInfo.Engine == "mpv") _screen = Add("Translate text", translateText);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = initialVolume, Width = 90,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volume" };
        slider.ValueChanged += (_, e) => volume((int)e.NewValue);
        panel.Children.Add(slider); Add("Exit fullscreen", exit); Content = panel;
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape || (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None))
            { e.Handled = true; _exit(); return; }
            if (e.Key == Key.Tab) { _idle.Restart(); return; }
            var forwarded = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key) { RoutedEvent = PreviewKeyDownEvent };
            home.RaiseEvent(forwarded); e.Handled = forwarded.Handled; };
        _timer.Tick += (_, _) => Tick();
        _home.Activated += HomeActivated;
        _home.Deactivated += HomeDeactivated;
        Closing += (_, _) => { _closed = true; _timer.Stop(); };
        Closed += (_, _) => { _timer.Stop(); _home.Activated -= HomeActivated; _home.Deactivated -= HomeDeactivated; };
        _timer.Start();
    }
    private void HomeActivated(object? sender, EventArgs e) => Reveal();
    private void HomeDeactivated(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() => {
        if (!_closed && !_home.IsActive && !IsActive && IsVisible) Hide();
    }));
    internal void Reveal()
    {
        if (_closed) return;
        _idle.Restart();
        Tick();
    }
    private void Tick()
    {
        if (_closed || _updating) return;
        _updating = true;
        try {
        if (!_home.IsVisible || _home.WindowState == WindowState.Minimized || (!_home.IsActive && !IsActive))
        { if (IsVisible) Hide(); return; }
        if (PresentationSource.FromVisual(_home)?.CompositionTarget == null || !GetCursorPos(out var cursor)) return;
        var position = _home.PointFromScreen(new Point(cursor.X, cursor.Y));
        bool inside = position.X >= 0 && position.Y >= 0 && position.X < _home.ActualWidth && position.Y < _home.ActualHeight;
        if (inside && position != _last) { _last = position; _idle.Restart(); }
        bool show = IsMouseOver || IsKeyboardFocusWithin || _idle.Elapsed < TimeSpan.FromSeconds(3);
        if (show && !_closed && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
        if (IsVisible) {
            var point = _home.PointToScreen(new Point(Math.Max(0, (_home.ActualWidth - ActualWidth) / 2),
                Math.Max(0, _home.ActualHeight - ActualHeight - 24)));
            var transform = PresentationSource.FromVisual(_home)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var location = transform.Transform(point); Left = location.X; Top = location.Y;
        }
        }
        catch (InvalidOperationException) when (_closed) { }
        finally { _updating = false; }
    }
    internal void SetToggleStates(bool captions, bool speech, bool screen, bool muted, bool paused)
    {
        if (_closed) return;
        foreach (var (button, active) in new[] { (_captions, captions), (_speech, speech), (_screen, screen), (_mute, muted), (_pause, paused) })
            if (button != null) ToggleVisual.SetIsActive(button, active);
        if (_captions != null) _captions.Content = captions ? "CC: On" : "CC: Off";
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
