using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace HnIptv;

internal sealed class TranslationOverlay : Window
{
    private readonly TextBlock _speech = Line(), _screen = Line();
    private readonly Func<(Window Owner, FrameworkElement Video)> _target;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private long _speechAt, _screenAt;
    internal TranslationOverlay(Func<(Window, FrameworkElement)> target)
    {
        _target = target;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false; Focusable = false;
        IsHitTestVisible = false; Height = 160; Width = 600;
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        panel.Children.Add(_screen); panel.Children.Add(_speech); Content = panel;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
        };
        _timer.Tick += (_, _) => Position();
        Closed += (_, _) => _timer.Stop(); _timer.Start();
    }
    private static TextBlock Line() => new() {
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210, 10, 15, 24)),
        FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, Padding = new Thickness(10, 5, 10, 5),
        Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed, MaxHeight = 100
    };
    internal void SetSpeech(string text) { Set(_speech, text); _speechAt = Stopwatch.GetTimestamp(); Position(); }
    internal void SetScreen(string text) { Set(_screen, text); _screenAt = Stopwatch.GetTimestamp(); Position(); }
    internal void Clear() { Set(_speech, ""); Set(_screen, ""); Position(); }
    private static void Set(TextBlock block, string text) {
        block.Text = text.Length > 800 ? text[..800] : text;
        block.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Position()
    {
        if (_speechAt != 0 && Stopwatch.GetElapsedTime(_speechAt).TotalSeconds > 12) Set(_speech, "");
        if (_screenAt != 0 && Stopwatch.GetElapsedTime(_screenAt).TotalSeconds > 8) Set(_screen, "");
        var (owner, video) = _target();
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized || !video.IsVisible ||
            video.ActualWidth < 100 || video.ActualHeight < 80 ||
            (_speech.Visibility != Visibility.Visible && _screen.Visibility != Visibility.Visible))
        { if (IsVisible) Hide(); return; }
        if (Owner != owner) { Hide(); Owner = owner; }
        Topmost = owner.Topmost;
        var point = video.PointToScreen(new Point(0, 0));
        var transform = PresentationSource.FromVisual(video)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = transform.Transform(point);
        Width = Math.Max(100, video.ActualWidth - 24); Height = Math.Min(210, video.ActualHeight * 0.6);
        Left = origin.X + 12; Top = origin.Y + video.ActualHeight - Height - 12;
        if (!IsVisible) Show();
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
