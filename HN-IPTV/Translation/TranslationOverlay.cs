using System.Diagnostics;
using HnIptv.Translation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace HnIptv;

internal sealed class TranslationOverlay : Window
{
    private readonly TextBlock _speech = Line(), _screen = Line(), _message = Line();
    private readonly CaptionHistory _history = new();
    private bool _closed, _positioning;
    internal bool IsClosed => _closed;
    private long _messageUntil;
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
        _message.FontSize = 13;
        panel.Children.Add(_message); panel.Children.Add(_screen); panel.Children.Add(_speech); Content = panel;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
        };
        _timer.Tick += (_, _) => Position();
        Closing += (_, _) => { _closed = true; _timer.Stop(); };
        Closed += (_, _) => _timer.Stop(); _timer.Start();
    }
    private static TextBlock Line() => new() {
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210, 10, 15, 24)),
        FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, Padding = new Thickness(10, 5, 10, 5),
        Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed, MaxHeight = 100
    };
    internal void SetSpeech(string text)
    {
        if (_closed || string.IsNullOrWhiteSpace(text)) return;
        if (_speechAt != 0 && Stopwatch.GetElapsedTime(_speechAt).TotalSeconds > 12) _history.Clear();
        Set(_speech, _history.Append(text)); _speechAt = Stopwatch.GetTimestamp(); Position();
    }
    internal void SetScreen(string text)
    {
        if (_closed || string.IsNullOrWhiteSpace(text)) return;
        Set(_screen, text); _screenAt = Stopwatch.GetTimestamp(); Position();
    }
    internal void SetMessage(string text, int seconds = 6) {
        if (_closed) return;
        Set(_message, text); _messageUntil = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency; Position();
    }
    internal void Clear() { _history.Clear(); Set(_speech, ""); Set(_screen, ""); Set(_message, ""); Position(); }
    private static void Set(TextBlock block, string text) {
        block.Text = text.Length > 800 ? text[..800] : text;
        block.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Position()
    {
        if (_closed || _positioning) return;
        _positioning = true;
        try {
        if (_speechAt != 0 && Stopwatch.GetElapsedTime(_speechAt).TotalSeconds > 12) Set(_speech, "");
        if (_screenAt != 0 && Stopwatch.GetElapsedTime(_screenAt).TotalSeconds > 8) Set(_screen, "");
        if (_messageUntil != 0 && Stopwatch.GetTimestamp() > _messageUntil) Set(_message, "");
        var (owner, video) = _target();
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized || !video.IsVisible ||
            video.ActualWidth < 100 || video.ActualHeight < 80 ||
            (_speech.Visibility != Visibility.Visible && _screen.Visibility != Visibility.Visible && _message.Visibility != Visibility.Visible))
        { if (IsVisible) Hide(); return; }
        if (Owner != owner) { Hide(); Owner = owner; }
        Topmost = owner.Topmost;
        if (PresentationSource.FromVisual(video)?.CompositionTarget == null) { if (IsVisible) Hide(); return; }
        var point = video.PointToScreen(new Point(0, 0));
        var transform = PresentationSource.FromVisual(video)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = transform.Transform(point);
        Width = Math.Max(100, video.ActualWidth - 24); Height = Math.Min(280, video.ActualHeight * 0.75);
        _speech.FontSize = _screen.FontSize = Math.Clamp(video.ActualWidth / 40, 12, 21);
        _speech.MaxHeight = Height * 0.38; _screen.MaxHeight = Height * 0.48; _message.MaxHeight = Height * 0.14;
        Left = origin.X + 12; Top = origin.Y + video.ActualHeight - Height - 12;
        if (!_closed && !IsVisible) Show();
        }
        catch (InvalidOperationException) when (_closed) { }
        finally { _positioning = false; }
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
