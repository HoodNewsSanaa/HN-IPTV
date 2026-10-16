using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using HnIptv.Preferences;
using HnIptv.Playback;

namespace HnIptv;

internal sealed class PictureInPictureWindow : Window
{
    internal NativeVideoHost VideoHost { get; } = new() { CreateVideoTarget = false };
    private readonly NativeVideoHost _home;
    private readonly Action<PlayerSettings> _save;
    private readonly PlayerSettings _settings;
    private readonly Window _heading, _controls;
    private readonly TextBlock _title;
    private readonly Action? _toggleCaptions;
    private Button? _captionsButton;
    private readonly List<Window> _resizeHandles = new();
    private readonly DispatcherTimer _hover = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _attached, _closed, _fitting;
    private double _aspect = 16d / 9;
    internal bool ControlsVisible => _heading.IsVisible && _controls.IsVisible;
    internal double VideoAspect => _aspect;

    public PictureInPictureWindow(NativeVideoHost home, PlayerSettings settings, int volume,
        Action pause, Action<bool> mute, Action<int> setVolume, Action<PlayerSettings> save, Action? toggleCaptions = null, bool captionsEnabled = false)
    {
        _home = home; _save = save; _settings = settings; _toggleCaptions = toggleCaptions;
        Title = "HN-IPTV — Picture in picture";
        Topmost = true; MinWidth = 256; MinHeight = 1;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight = 0, CornerRadius = new CornerRadius(0), GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(6), UseAeroCaptionButtons = false });
        Background = Brushes.Black; Content = VideoHost;
        var size = VideoGeometry.Fit(settings.PipWidth, _aspect, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        Width = size.Width; Height = size.Height;
        Left = Math.Clamp(settings.PipLeft, SystemParameters.VirtualScreenLeft,
            Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(settings.PipTop, SystemParameters.VirtualScreenTop,
            Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));

        _title = new TextBlock { Text = "HN-IPTV", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 6, 0) };
        var top = new DockPanel();
        var back = Button("↗ Return", Close); DockPanel.SetDock(back, Dock.Right); top.Children.Add(back); top.Children.Add(_title);
        _heading = Overlay(top, 38);
        // Owned HWND overlays remain above the native video surface (a WPF Grid
        // overlay would be obscured by HwndHost's airspace).
        top.MouseLeftButtonDown += (_, e) => {
            if (e.OriginalSource is TextBlock || e.OriginalSource == top)
            { if (e.ClickCount == 2) Close(); else if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); }
        };
        var controls = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        controls.Children.Add(Button("Ⅱ", pause));
        if (BuildInfo.Engine == "mpv" && toggleCaptions != null) {
            _captionsButton = Button("CC", toggleCaptions);
            _captionsButton.ToolTip = "Toggle closed captions (C)";
            SetCaptionsEnabled(captionsEnabled); controls.Children.Add(_captionsButton);
        }
        bool muted = false;
        var muteButton = Button("Mute", () => { });
        muteButton.Click += (_, _) => { muted = !muted; muteButton.Content = muted ? "Unmute" : "Mute"; mute(muted); };
        controls.Children.Add(muteButton);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = volume, MinWidth = 48, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { if (muted) { muted = false; muteButton.Content = "Mute"; } setVolume((int)e.NewValue); };
        controls.Children.Add(slider); _controls = Overlay(controls, 44);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        LocationChanged += (_, _) => PositionOverlays();
        SizeChanged += (_, _) => { if (!_fitting && IsLoaded) FitVideo(); PositionOverlays(); };
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.P && !e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None) { Close(); e.Handled = true; }
            else if (CaptionKey(e)) { }
            else if (e.Key == Key.Space) { pause(); e.Handled = true; }
            else if (e.Key == Key.Tab) ShowControls(true);
        };
        _hover.Tick += (_, _) => UpdateHover();
        Loaded += (_, _) => {
            try {
                // WPF requires an owner to have been shown before it can own
                // another Window. Do this after PiP's own HWND is visible.
                _heading.Owner = this; _controls.Owner = this;
                // Separate transparent HWND strips sit above the native video.
                // WindowChrome alone cannot receive its mouse hit tests.
                foreach (int hit in new[] { 10, 11, 12, 15, 13, 14, 16, 17 }) {
                    int edge = hit;
                    var grip = new Window { Owner = this, WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                        ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                        Cursor = hit is 10 or 11 ? Cursors.SizeWE : hit is 12 or 15 ? Cursors.SizeNS : hit is 13 or 17 ? Cursors.SizeNWSE : Cursors.SizeNESW };
                    grip.MouseLeftButtonDown += (_, e) => { e.Handled = true; ReleaseCapture();
                        SendMessage(new WindowInteropHelper(this).Handle, 0x00A1, new IntPtr(edge), IntPtr.Zero); };
                    grip.PreviewKeyDown += (_, e) => { if (CaptionKey(e)) return; if (e.Key == Key.P && !e.IsRepeat) { e.Handled = true; Close(); } };
                    _resizeHandles.Add(grip);
                }
                PositionOverlays(); foreach (var grip in _resizeHandles) grip.Show();
                _home.TransferTo(VideoHost); _attached = true; _hover.Start();
            }
            catch (Exception ex) { App.LogException("pip.attach", ex); Close(); }
        };
        Closed += (_, _) => { _closed = true; _hover.Stop(); _heading.Close(); _controls.Close(); foreach (var grip in _resizeHandles) grip.Close(); };
    }
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(8, 4, 8, 4),
            ToolTip = text == "Ⅱ" ? "Pause / resume (Space)" : text };
        button.Click += (_, _) => action(); return button;
    }
    private Window Overlay(UIElement content, double height)
    {
        var overlay = new Window {
        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, ShowActivated = false, Topmost = true,
        Background = new SolidColorBrush(Color.FromRgb(21, 30, 44)), Foreground = Brushes.White,
        Height = height, Content = content
        };
        overlay.PreviewKeyDown += (_, e) => {
            if (CaptionKey(e)) return;
            if ((e.Key == Key.P && !e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None) || e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        return overlay;
    }
    private bool CaptionKey(KeyEventArgs e)
    {
        if (e.Key != Key.C || e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || BuildInfo.Engine != "mpv") return false;
        _toggleCaptions?.Invoke(); e.Handled = true; return true;
    }
    internal void SetCaptionsEnabled(bool enabled)
    {
        if (_captionsButton != null) _captionsButton.Content = enabled ? "CC: On" : "CC: Off";
    }
    internal void SetChannelTitle(string text) { Title = "HN-IPTV — " + text; _title.Text = text; }
    internal void SetAspect(double aspect)
    {
        if (!double.IsFinite(aspect) || aspect is < 0.2 or > 5 || Math.Abs(aspect - _aspect) < 0.001) return;
        _aspect = aspect; FitVideo();
    }
    private void FitVideo()
    {
        if (_fitting || _closed) return;
        _fitting = true;
        try {
            var size = VideoGeometry.Fit(Width, _aspect, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Width = size.Width; Height = size.Height;
        } finally { _fitting = false; }
    }
    private void PositionOverlays()
    {
        if (_closed || _heading == null || _controls == null || ActualWidth <= 0) return;
        const double edge = 7;
        foreach (var overlay in new[] { _heading, _controls }) { overlay.Left = Left + edge; overlay.Width = Math.Max(1, ActualWidth - 2 * edge); }
        _heading.Top = Top + edge; _controls.Top = Top + Math.Max(edge, ActualHeight - edge - _controls.Height);
        var regions = new[] {
            new Rect(0, edge, edge, Math.Max(1, ActualHeight - 2 * edge)),
            new Rect(ActualWidth - edge, edge, edge, Math.Max(1, ActualHeight - 2 * edge)),
            new Rect(edge, 0, Math.Max(1, ActualWidth - 2 * edge), edge),
            new Rect(edge, ActualHeight - edge, Math.Max(1, ActualWidth - 2 * edge), edge),
            new Rect(0, 0, edge, edge), new Rect(ActualWidth - edge, 0, edge, edge),
            new Rect(0, ActualHeight - edge, edge, edge), new Rect(ActualWidth - edge, ActualHeight - edge, edge, edge) };
        for (int i = 0; i < _resizeHandles.Count; i++) {
            var grip = _resizeHandles[i]; var r = regions[i];
            grip.Left = Left + r.X; grip.Top = Top + r.Y; grip.Width = r.Width; grip.Height = r.Height;
        }
    }
    private void UpdateHover()
    {
        if (_closed || WindowState == WindowState.Minimized) { ShowControls(false); return; }
        bool hover = false;
        if (GetCursorPos(out var point) && GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
            hover = point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
        ShowControls(hover || _heading.IsMouseCaptureWithin || _controls.IsMouseCaptureWithin);
    }
    internal void ShowControls(bool show)
    {
        if (_closed || !IsLoaded) return;
        PositionOverlays();
        foreach (var overlay in new[] { _heading, _controls }) {
            if (show && !overlay.IsVisible) overlay.Show();
            else if (!show && overlay.IsVisible) overlay.Hide();
        }
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0214) // WM_SIZING: constrain the client rectangle to the display aspect.
        {
            var r = Marshal.PtrToStructure<NativeRect>(lParam);
            int edge = wParam.ToInt32();
            if (edge is 3 or 6) r.Right = r.Left + (int)Math.Round((r.Bottom - r.Top) * _aspect);
            else {
                int height = (int)Math.Round((r.Right - r.Left) / _aspect);
                if (edge is 4 or 5) r.Top = r.Bottom - height; else r.Bottom = r.Top + height;
            }
            Marshal.StructureToPtr(r, lParam, false); handled = true; return new IntPtr(1);
        }
        return IntPtr.Zero;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_attached) {
            try { VideoHost.TransferTo(_home); _attached = false; }
            catch (Exception ex) { App.LogException("pip.return", ex); e.Cancel = true; return; }
        }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _save(_settings with { PipLeft = bounds.Left, PipTop = bounds.Top, PipWidth = bounds.Width, PipHeight = bounds.Height });
        base.OnClosing(e);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
