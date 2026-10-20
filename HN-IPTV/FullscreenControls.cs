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
    private readonly Stopwatch _idle = Stopwatch.StartNew();
    public FullscreenControls(Window home, Action play, Action pause, Action stop, Action mute, Action captions,
        Action exit, Action<int> volume, int initialVolume)
    {
        _home = home; Owner = home; ShowActivated = false; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(24, 35, 56));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        void Add(string label, Action action) {
            var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            button.Click += (_, _) => action(); panel.Children.Add(button);
        }
        Add("Play", play); Add("Pause", pause); Add("Stop", stop); Add("Mute (M)", mute);
        if (BuildInfo.Engine == "mpv") Add("CC", captions);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = initialVolume, Width = 90,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volume" };
        slider.ValueChanged += (_, e) => volume((int)e.NewValue);
        panel.Children.Add(slider); Add("Exit fullscreen", exit); Content = panel;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Tab) { _idle.Restart(); return; }
            var forwarded = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key) { RoutedEvent = PreviewKeyDownEvent };
            home.RaiseEvent(forwarded); e.Handled = forwarded.Handled; };
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) => _timer.Stop();
        _timer.Start();
    }
    private void Tick()
    {
        if (!_home.IsVisible || _home.WindowState == WindowState.Minimized || (!_home.IsActive && !IsActive))
        { if (IsVisible) Hide(); return; }
        if (!GetCursorPos(out var cursor)) return;
        var position = _home.PointFromScreen(new Point(cursor.X, cursor.Y));
        bool inside = position.X >= 0 && position.Y >= 0 && position.X < _home.ActualWidth && position.Y < _home.ActualHeight;
        if (inside && position != _last) { _last = position; _idle.Restart(); }
        bool show = IsMouseOver || IsKeyboardFocusWithin || (inside && _idle.Elapsed < TimeSpan.FromSeconds(2.5));
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
        if (IsVisible) {
            var point = _home.PointToScreen(new Point(Math.Max(0, (_home.ActualWidth - ActualWidth) / 2),
                Math.Max(0, _home.ActualHeight - ActualHeight - 24)));
            var transform = PresentationSource.FromVisual(_home)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var location = transform.Transform(point); Left = location.X; Top = location.Y;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
