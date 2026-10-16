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
    private readonly DispatcherTimer _hover = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _attached, _closed, _fitting;
    private double _aspect = 16d / 9;
    internal bool ControlsVisible => _heading.IsVisible && _controls.IsVisible;
    internal double VideoAspect => _aspect;

    public PictureInPictureWindow(NativeVideoHost home, PlayerSettings settings, int volume,
        Action pause, Action<bool> mute, Action<int> setVolume, Action<PlayerSettings> save)
    {
        _home = home; _save = save; _settings = settings;
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
            else if (e.Key == Key.Space) { pause(); e.Handled = true; }
            else if (e.Key == Key.Tab) ShowControls(true);
        };
        _hover.Tick += (_, _) => UpdateHover();
        Loaded += (_, _) => {
            try {
                // WPF requires an owner to have been shown before it can own
                // another Window. Do this after PiP's own HWND is visible.
                _heading.Owner = this; _controls.Owner = this;
                _home.TransferTo(VideoHost); _attached = true; _hover.Start();
            }
            catch (Exception ex) { App.LogException("pip.attach", ex); Close(); }
        };
        Closed += (_, _) => { _closed = true; _hover.Stop(); _heading.Close(); _controls.Close(); };
    }
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(8, 4, 8, 4),
            ToolTip = text == "Ⅱ" ? "Pause / resume (Space)" : text };
        button.Click += (_, _) => action(); return button;
    }
    private Window Overlay(UIElement content, double height) => new() {
        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, ShowActivated = false, Topmost = true,
        Background = new SolidColorBrush(Color.FromRgb(21, 30, 44)), Foreground = Brushes.White,
        Height = height, Content = content
    };
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
        foreach (var overlay in new[] { _heading, _controls }) { overlay.Left = Left; overlay.Width = ActualWidth; }
        _heading.Top = Top; _controls.Top = Top + Math.Max(0, ActualHeight - _controls.Height);
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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
