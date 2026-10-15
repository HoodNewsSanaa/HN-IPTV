using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using HnIptv.Preferences;

namespace HnIptv;

internal sealed class PictureInPictureWindow : Window
{
    internal NativeVideoHost VideoHost { get; } = new() { CreateVideoTarget = false };
    private readonly NativeVideoHost _home;
    private readonly Slider _volume;
    private bool _attached;
    private readonly Action<PlayerSettings> _save;
    private readonly PlayerSettings _settings;

    public PictureInPictureWindow(NativeVideoHost home, PlayerSettings settings, int volume,
        Action pause, Action<bool> mute, Action<int> setVolume, Action<PlayerSettings> save)
    {
        _home = home; _save = save; _settings = settings;
        Title = "HN-IPTV — Picture in picture";
        Topmost = true; MinWidth = 320; MinHeight = 220;
        Width = settings.PipWidth; Height = settings.PipHeight;
        // Keep restored bounds within the Windows virtual desktop, including negative origins.
        Left = Math.Clamp(settings.PipLeft, SystemParameters.VirtualScreenLeft,
            Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(settings.PipTop, SystemParameters.VirtualScreenTop,
            Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        Background = System.Windows.Media.Brushes.Black;
        var root = new DockPanel();
        var controls = new DockPanel { Margin = new Thickness(6) };
        DockPanel.SetDock(controls, Dock.Bottom);
        Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(8, 5, 8, 5) };
            button.Click += (_, _) => action(); return button;
        }
        controls.Children.Add(Button("Ⅱ", pause));
        var muteButton = Button("Mute", () => { });
        bool muted = false;
        muteButton.Click += (_, _) => { muted = !muted; muteButton.Content = muted ? "Unmute" : "Mute"; mute(muted); };
        controls.Children.Add(muteButton);
        var restore = Button("↗ Return", Close); DockPanel.SetDock(restore, Dock.Right); controls.Children.Add(restore);
        _volume = new Slider { Minimum = 0, Maximum = 100, Value = volume, MinWidth = 60, VerticalAlignment = VerticalAlignment.Center };
        _volume.ValueChanged += (_, e) => { if (muted) { muted = false; muteButton.Content = "Mute"; } setVolume((int)e.NewValue); };
        controls.Children.Add(_volume);
        root.Children.Add(controls); root.Children.Add(VideoHost); Content = root;
        Loaded += (_, _) =>
        {
            try { _home.TransferTo(VideoHost); _attached = true; }
            catch (Exception ex) { App.LogException("pip.attach", ex); Close(); }
        };
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_attached)
        {
            try { VideoHost.TransferTo(_home); _attached = false; }
            catch (Exception ex) { App.LogException("pip.return", ex); e.Cancel = true; return; }
        }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _save(_settings with { PipLeft = bounds.Left, PipTop = bounds.Top, PipWidth = bounds.Width, PipHeight = bounds.Height });
        base.OnClosing(e);
    }
}
