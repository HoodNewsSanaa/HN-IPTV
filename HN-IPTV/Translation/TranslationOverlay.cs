using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HnIptv.Translation;
namespace HnIptv;

internal sealed class TranslationOverlay : Window
{
    private readonly TextBlock _speech = Line(), _screen = Line(), _message = Line();
    private readonly CaptionHistory _history = new();
    internal TimedCaptions SpeechSchedule { get; init; } = new();
    internal Func<double?>? SpeechPlaybackClock { get; set; }
    internal void ScheduleSpeech(string text,double start,double end) { SpeechSchedule.AddParagraph(text,start,end);Position(); }
    private readonly Queue<(SpeechCaptionCue Cue, long At)> _speechQueue = new();
    private long _cueUntil;
    private readonly Canvas _regions = new() { IsHitTestVisible = false };
    private readonly StackPanel _captions = new();
    private readonly ScreenRegionTracker _tracker = new();
    private readonly Dictionary<long, Border> _labels = [];
    private IReadOnlyList<TrackedScreenRegion> _tracked = [];
    private readonly Func<(Window Owner, FrameworkElement Video)> _target;
    private readonly Func<double>? _aspect;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _closed, _positioning, _positioned;
    private int _frameWidth, _frameHeight;
    private string _outputLanguage = "en";
    private long _messageUntil, _speechAt, _screenAt;
    internal bool IsClosed => _closed;
    internal int PositionedRegionCount => _labels.Count;
    internal Func<Window?>? ControlsWindow { get; init; }
    internal double CaptionBottomInset => _captions.Margin.Bottom;
    internal bool SpeechCaptionVisible => _speech.Visibility == Visibility.Visible;
    internal double SpeechCaptionBottom => _speech.TranslatePoint(new Point(0,_speech.ActualHeight),(UIElement)Content).Y;

    internal TranslationOverlay(Func<(Window, FrameworkElement)> target, Func<double>? aspect = null)
    {
        _target = target; _aspect = aspect;
        _speech.HorizontalAlignment = HorizontalAlignment.Center;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false; Focusable = false;
        IsHitTestVisible = false; Height = 160; Width = 600;
        var root = new Grid { ClipToBounds = true };
        root.Children.Add(_regions);
        _captions.VerticalAlignment = VerticalAlignment.Bottom;
        _message.FontSize = 13;
        _captions.Children.Add(_message); _captions.Children.Add(_screen); _captions.Children.Add(_speech);
        root.Children.Add(_captions); Content = root;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
        };
        _timer.Tick += (_, _) => Position();
        Closing += (_, _) => { _closed = true; _timer.Stop(); };
        Closed += (_, _) => _timer.Stop(); _timer.Start();
    }
    private static TextBlock Line() => new() {
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210,10,15,24)),
        FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, Padding = new Thickness(10,5,10,5),
        Margin = new Thickness(0,3,0,0), Visibility = Visibility.Collapsed, MaxHeight = 110
    };
    internal void QueueSpeech(IReadOnlyList<SpeechCaptionCue> cues)
    {
        if (_closed) return;
        foreach (var cue in cues) {
            if (_speechQueue.Count >= 4) _speechQueue.Dequeue();
            _speechQueue.Enqueue((cue, Stopwatch.GetTimestamp()));
        }
        Position();
    }
    private void AdvanceSpeechCue()
    {
        if(SpeechPlaybackClock!=null) {
            var position=SpeechPlaybackClock();
            Set(_speech,position.HasValue?SpeechSchedule.At(position.Value):"");
            _speechAt=Stopwatch.GetTimestamp();return;
        }
        long now = Stopwatch.GetTimestamp();
        if (_cueUntil > now) return;
        if (_cueUntil != 0) { Set(_speech,""); _cueUntil = 0; }
        while (_speechQueue.TryDequeue(out var pending)) {
            if (Stopwatch.GetElapsedTime(pending.At).TotalSeconds > 12) continue;
            _history.Clear(); Set(_speech,pending.Cue.Text); _speechAt = now;
            _cueUntil = now + (long)(Math.Clamp(pending.Cue.DurationSeconds,1.5,6)*Stopwatch.Frequency);
            break;
        }
    }
    internal void SetSpeech(string text, bool replace = false)
    {
        if (_closed || string.IsNullOrWhiteSpace(text)) return;
        _speechQueue.Clear(); _cueUntil = 0;
        if (replace || (_speechAt != 0 && Stopwatch.GetElapsedTime(_speechAt).TotalSeconds > 12)) _history.Clear();
        Set(_speech, _history.Append(text)); _speechAt = Stopwatch.GetTimestamp(); Position();
    }
    internal void SetScreen(string text)
    {
        if (_closed || string.IsNullOrWhiteSpace(text)) return;
        _positioned = false; ClearRegions();
        Set(_screen, text); _screenAt = Stopwatch.GetTimestamp(); Position();
    }
    internal void SetScreenRegions(IReadOnlyList<ScreenTranslationRegion> regions, int frameWidth, int frameHeight, string target)
    {
        if (_closed) return;
        _positioned = true; _frameWidth = frameWidth; _frameHeight = frameHeight; _outputLanguage = target;
        Set(_screen, "");
        _tracked = _tracker.Update(regions);
        _screenAt = Stopwatch.GetTimestamp(); Position();
    }
    internal void SetMessage(string text, int seconds = 6)
    {
        if (_closed) return;
        Set(_message, text); _messageUntil = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency; Position();
    }
    internal void Clear()
    {
        SpeechSchedule.Clear();
        _speechQueue.Clear(); _cueUntil = 0;
        _history.Clear(); ClearRegions(); Set(_speech,""); Set(_screen,""); Set(_message,""); Position();
    }
    private void ClearRegions()
    {
        _tracker.Clear(); _tracked = []; _labels.Clear(); _regions.Children.Clear();
    }
    private static void Set(TextBlock block, string text)
    {
        block.Text = text.Length > 800 ? text[..800] : text;
        block.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Position()
    {
        if (_closed || _positioning) return;
        _positioning = true;
        try {
            AdvanceSpeechCue();
            if (_speechAt != 0 && Stopwatch.GetElapsedTime(_speechAt).TotalSeconds > 12) Set(_speech,"");
            if (_screenAt != 0 && Stopwatch.GetElapsedTime(_screenAt).TotalSeconds > (_positioned ? 3 : 8)) {
                Set(_screen,""); ClearRegions();
            }
            if (_messageUntil != 0 && Stopwatch.GetTimestamp() > _messageUntil) Set(_message,"");
            var (owner, video) = _target();
            if (!owner.IsVisible || owner.WindowState == WindowState.Minimized || !video.IsVisible ||
                video.ActualWidth < 100 || video.ActualHeight < 80 ||
                (_tracked.Count == 0 && _speech.Visibility != Visibility.Visible &&
                 _screen.Visibility != Visibility.Visible && _message.Visibility != Visibility.Visible))
            { if (IsVisible) Hide(); return; }
            var source = PresentationSource.FromVisual(video);
            if (source?.CompositionTarget == null) { if (IsVisible) Hide(); return; }
            if (Owner != owner) { Hide(); Owner = owner; }
            Topmost = owner.Topmost;
            var origin = source.CompositionTarget.TransformFromDevice.Transform(video.PointToScreen(new Point(0,0)));
            Width = video.ActualWidth; Height = video.ActualHeight; Left = origin.X; Top = origin.Y;
            double aspect = _aspect?.Invoke() ?? (_frameHeight > 0 ? (double)_frameWidth / _frameHeight : Width / Height);
            if (!double.IsFinite(aspect) || aspect <= 0)
                aspect = _frameHeight > 0 ? (double)_frameWidth / _frameHeight : Width / Height;
            var viewport = ScreenOverlayLayout.Viewport(Width,Height,aspect);
            RenderRegions(viewport);
            _speech.FontSize = _screen.FontSize = Math.Clamp(viewport.Width / 40,12,21);
            double bottomInset = Math.Max(12,Height-viewport.Y-viewport.Height+12);
            if (ControlsWindow?.Invoke() is { IsVisible: true } controls && controls.ActualWidth > 0 &&
                controls.ActualHeight > 0 && PresentationSource.FromVisual(controls)?.CompositionTarget != null) {
                // Transform screen pixels through the video host for mixed-DPI monitors.
                var controlTop = video.PointFromScreen(controls.PointToScreen(new Point(0,0)));
                var controlBottom = video.PointFromScreen(controls.PointToScreen(new Point(controls.ActualWidth,controls.ActualHeight)));
                if (controlTop.X < viewport.X+viewport.Width && controlBottom.X > viewport.X &&
                    controlTop.Y < viewport.Y+viewport.Height && controlBottom.Y > viewport.Y)
                    bottomInset = Math.Max(bottomInset, Height-controlTop.Y+12);
            }
            _captions.Margin = new Thickness(viewport.X+12,viewport.Y+12,
                Math.Max(12,Width-viewport.X-viewport.Width+12),bottomInset);
            _captions.MaxHeight = Math.Min(280,viewport.Height*0.65);
            _speech.MaxWidth = Math.Min(760,viewport.Width*0.85);
            _speech.MaxHeight = Math.Min(_speech.FontSize*2.7+10,viewport.Height*0.3);
            _screen.MaxHeight = Math.Min(140,viewport.Height*0.35);
            // Avoid covering translated news banners with spoken subtitles.
            _captions.VerticalAlignment = _tracked.Any(r => r.Region.Y+r.Region.Height > 0.72)
                ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            if (!_closed && !IsVisible) Show();
        }
        catch (InvalidOperationException) when (_closed) { }
        finally { _positioning = false; }
    }
    private void RenderRegions(OverlayRectangle viewport)
    {
        var ids = _tracked.Select(r => r.Id).ToHashSet();
        foreach (var id in _labels.Keys.Where(id => !ids.Contains(id)).ToArray()) {
            _regions.Children.Remove(_labels[id]); _labels.Remove(id);
        }
        foreach (var tracked in _tracked)
        {
            var r = tracked.Region;
            var mapped = ScreenOverlayLayout.Map(r,viewport);
            if (mapped.Width < 4 || mapped.Height < 4) {
                if (_labels.TryGetValue(tracked.Id, out var tiny)) tiny.Visibility = Visibility.Collapsed;
                continue;
            }
            if (!_labels.TryGetValue(tracked.Id,out var border)) {
                border = new Border { Padding = new Thickness(2,1,2,1), ClipToBounds = true,
                    Child = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold } };
                _labels.Add(tracked.Id,border); _regions.Children.Add(border);
            }
            double width = Math.Min(viewport.X+viewport.Width-mapped.X,mapped.Width+4);
            double height = Math.Min(viewport.Y+viewport.Height-mapped.Y,mapped.Height+2);
            // A second line is preferable to illegibly shrinking a longer translation.
            if (r.Text.Length*height*0.4 > width) {
                double next = _tracked.Where(other => other.Id != tracked.Id)
                    .Select(other => ScreenOverlayLayout.Map(other.Region,viewport))
                    .Where(other => other.Y >= mapped.Y+mapped.Height-1 &&
                        other.X < mapped.X+width && other.X+other.Width > mapped.X)
                    .Select(other => other.Y-2).DefaultIfEmpty(viewport.Y+viewport.Height).Min();
                height = Math.Min(Math.Max(height,Math.Min(height*2.1,90)),Math.Max(height,next-mapped.Y));
            }
            Canvas.SetLeft(border,Math.Max(viewport.X,mapped.X-2));
            Canvas.SetTop(border,Math.Max(viewport.Y,mapped.Y-1));
            var text = (TextBlock)border.Child;
            bool fit = border.Width != width || border.Height != height || text.Text != r.Text;
            border.Width = Math.Max(1,width); border.Height = Math.Max(1,height);
            border.Visibility = Visibility.Visible;
            string style = r.Background + r.Foreground + _outputLanguage;
            if ((string?)border.Tag != style) {
                border.Tag = style; border.Background = ColorBrush(r.Background,Colors.Black);
                text.Foreground = ColorBrush(r.Foreground,Colors.White);
                text.FlowDirection = _outputLanguage is "ar" or "fa" or "ur" or "he" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            }
            if (text.Text != r.Text) text.Text = r.Text;
            if (fit) {
                double low = 5, high = Math.Clamp(mapped.Height*0.85,8,40);
                for (int pass=0;pass<7;pass++) {
                    text.FontSize = (low+high)/2;
                    text.Measure(new Size(Math.Max(1,width-4),double.PositiveInfinity));
                    if (text.DesiredSize.Height > height-2 || text.DesiredSize.Width > width-3) high=text.FontSize;
                    else low=text.FontSize;
                }
                text.FontSize=low;
            }
        }
    }
    private static Brush ColorBrush(string value,Color fallback)
    {
        if (value.Length==7 && value[0]=='#' &&
            int.TryParse(value.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out int rgb))
            return new SolidColorBrush(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));
        return new SolidColorBrush(fallback);
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
}
