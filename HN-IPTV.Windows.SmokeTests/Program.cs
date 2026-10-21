using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using HnIptv;
using HnIptv.Models;
using HnIptv.Playback;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "hn-iptv-ui-" + Guid.NewGuid().ToString("N"));
        AppStorage.TestDataDirectory = Path.Combine(root, "data");
        string report = args.FirstOrDefault() ?? Path.Combine(Environment.CurrentDirectory, "windows-smoke.json");
        var clock = Stopwatch.StartNew();
        int exit = 1; bool started = false; string stage = "startup";
        var results = new Dictionary<string, object>();
        using var server = new FixtureServer(Path.Combine(AppContext.BaseDirectory, "fixtures"));
        var app = new App(); app.InitializeComponent();
        app.Activated += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow window) return;
            started = true;
            try
            {
                await UntilAsync(() => Task.FromResult(window.IsLoaded));
                await (Task)typeof(MainWindow).GetField("_startupLoad", Private)!.GetValue(window)!;
                results["window_ready_ms"] = clock.Elapsed.TotalMilliseconds;
                results["engine"] = BuildInfo.Engine;
                results["app_sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))).ToLowerInvariant();
                var channelList = (ListBox)typeof(MainWindow).GetField("ChannelsList", Private | BindingFlags.Public)!.GetValue(window)!;
                var controller = (PlaybackController)typeof(MainWindow).GetField("_playback", Private)!.GetValue(window)!;
                var kind = Enum.Parse(typeof(MainWindow).GetNestedType("SourceKind", BindingFlags.NonPublic)!, "M3u");
                var channels = new[] {
                    new Channel { Id = "ts", Name = "TS fixture", GroupId = "test", GroupName = "Test", Command = server.Url + "segment00.ts" },
                    new Channel { Id = "hls", Name = "HLS fixture", GroupId = "test", GroupName = "Test", Command = server.Url + "fixture.m3u8" }
                };
                await (Task)Invoke(window, "SetChannelsAsync", channels, new[] { new ChannelGroup("test", "Test"), new ChannelGroup("empty", "Empty") }, kind, "Test", "test", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 2));
                // Reproduce stale cancellation sources across an empty category,
                // a no-guide channel, catalog replacement, and repeated Stop.
                var epgField = typeof(MainWindow).GetField("_epgCts", Private)!;
                epgField.SetValue(window, new CancellationTokenSource());
                channelList.SelectedIndex = -1;
                channelList.SelectedIndex = 0;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (epgField.GetValue(window) is not null)
                    throw new InvalidOperationException("No-guide selection retained a cancellation source.");
                var tuneField = typeof(MainWindow).GetField("_tuneCts", Private)!;
                tuneField.SetValue(window, new CancellationTokenSource());
                Invoke(window, "Stop_Click", window, new RoutedEventArgs());
                Invoke(window, "Stop_Click", window, new RoutedEventArgs());
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (tuneField.GetValue(window) is not null)
                    throw new InvalidOperationException("Stop retained a cancellation source.");
                epgField.SetValue(window, new CancellationTokenSource());
                await (Task)Invoke(window, "SetChannelsAsync", channels, new[] { new ChannelGroup("test", "Test"), new ChannelGroup("empty", "Empty") }, kind, "Test", "test", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 2));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                results["selection_cancellation_regression"] = true;
                // Identical refreshed metadata must retain the actual bound objects,
                // not merely produce the same channel count.
                var activeField = typeof(MainWindow).GetField("_activePlaylist", Private)!;
                var previousProfile = activeField.GetValue(window);
                var cachedM3u = Path.Combine(root, "cached.m3u");
                File.WriteAllText(cachedM3u, "#EXTM3U\n#EXTINF:-1 group-title=\"Test\",Cached One\n" + server.Url + "segment00.ts");
                var cacheFixture = await HnIptv.Services.M3uService.ParseFileAsync(cachedM3u);
                var cacheProfile = new HnIptv.Services.PlaylistProfile { LocalPath = cachedM3u,
                    CachedChannels = cacheFixture.ToList(), LastChannelRefresh = DateTimeOffset.UtcNow, LastLogoRefresh = DateTimeOffset.UtcNow };
                activeField.SetValue(window, cacheProfile);
                await (Task)Invoke(window, "SetChannelsAsync", cacheFixture, new[] { new ChannelGroup("Test", "Test") }, kind, "Cache fixture", "cache", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 1));
                channelList.SelectedIndex = 0;
                var cachedItemsSource = channelList.ItemsSource;
                var cachedSelected = channelList.SelectedItem;
                await (Task)Invoke(window, "RefreshActiveAsync", true)!;
                if (!ReferenceEquals(cachedItemsSource, channelList.ItemsSource) || !ReferenceEquals(cachedSelected, channelList.SelectedItem))
                    throw new InvalidOperationException("Unchanged refresh replaced cached rows or selection.");
                activeField.SetValue(window, previousProfile);
                await (Task)Invoke(window, "SetChannelsAsync", channels, new[] { new ChannelGroup("test", "Test"), new ChannelGroup("empty", "Empty") }, kind, "Test", "test", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 2));
                results["unchanged_catalog_retains_objects"] = true;
                var groups = (ListBox)typeof(MainWindow).GetField("GroupsList", Private | BindingFlags.Public)!.GetValue(window)!;
                for (int i = 0; i < 8; i++)
                {
                    groups.SelectedItem = groups.Items.Cast<ChannelGroup>().Single(g => g.Id == "empty");
                    await UntilAsync(() => Task.FromResult(channelList.Items.Count == 0));
                    groups.SelectedItem = groups.Items.Cast<ChannelGroup>().Single(g => g.Id == "test");
                    await UntilAsync(() => Task.FromResult(channelList.Items.Count == 2));
                }
                results["empty_category_round_trips"] = 8;
                int resets = 0;
                var visible = (HnIptv.Navigation.BulkObservableCollection<Channel>)typeof(MainWindow).GetField("_visibleChannels", Private)!.GetValue(window)!;
                visible.CollectionChanged += (_, _) => resets++;
                channelList.SelectedIndex = 0;
                var selectedChannel = (Channel)channelList.SelectedItem;
                Invoke(window, "ToggleFavorite", selectedChannel); Invoke(window, "ToggleFavorite", selectedChannel);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (resets != 0) throw new InvalidOperationException("Favorite toggle rebuilt an unchanged browser.");
                results["favorites_preserved_browser"] = true;
                var large = Enumerable.Range(0, 50000).Select(i => new Channel { Id = "large-" + i, Name = "Channel " + i,
                    GroupId = "G" + i % 100, GroupName = "Group " + i % 100, Command = server.Url + "segment00.ts" }).ToArray();
                double maxDispatcherGap = 0;
                var heartbeatClock = Stopwatch.StartNew();
                double lastHeartbeat = 0;
                var heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
                heartbeat.Tick += (_, _) => { double now = heartbeatClock.Elapsed.TotalMilliseconds;
                    maxDispatcherGap = Math.Max(maxDispatcherGap, now - lastHeartbeat); lastHeartbeat = now; };
                heartbeat.Start();
                var catalogClock = Stopwatch.StartNew();
                await (Task)Invoke(window, "SetChannelsAsync", large, Enumerable.Range(0, 100).Select(i => new ChannelGroup("G" + i, "Group " + i)).ToArray(), kind, "Large fixture", "test", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == large.Length));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                results["large_catalog_bind_ms"] = catalogClock.Elapsed.TotalMilliseconds;
                var groupClock = Stopwatch.StartNew();
                groups.SelectedItem = groups.Items.Cast<ChannelGroup>().Single(g => g.Id == "G1");
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 500));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                heartbeat.Stop();
                results["large_group_change_ms"] = groupClock.Elapsed.TotalMilliseconds;
                results["large_catalog_max_dispatcher_gap_ms"] = maxDispatcherGap;
                await (Task)Invoke(window, "SetChannelsAsync", channels, new[] { new ChannelGroup("test", "Test"), new ChannelGroup("empty", "Empty") }, kind, "Test", "test", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 2));
                channelList.SelectedIndex = 0;
                stage = "initial tune";
                var initialTune = Stopwatch.StartNew();
                channelList.UpdateLayout();
                var row = (ListBoxItem)channelList.ItemContainerGenerator.ContainerFromIndex(0);
                var click = row.PointToScreen(new Point(row.ActualWidth / 2, row.ActualHeight / 2));
                SetCursorPos((int)click.X, (int)click.Y);
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["single_click_playback"] = true;
                results["initial_tune_progress_ms"] = initialTune.Elapsed.TotalMilliseconds;
                results["mpeg_ts_started"] = true;
                stage = "HLS tune";
                channelList.SelectedIndex = 1;
                await (Task)Invoke(window, "PlaySelectedAsync")!;
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["hls_started"] = true;
                await Task.Delay(500);
                stage = "stream information";
                StreamInfo? stream = null;
                await UntilAsync(async () => { stream = await controller.ReadInfoAsync(); return stream != null && (BuildInfo.Engine == "mpc" || stream.Width > 0); });
                results["stream_resolution"] = stream!.ResolutionText;
                results["stream_fps"] = stream.FramesPerSecond;
                results["stream_audio"] = stream.AudioText;
                if (BuildInfo.Engine != "mpc" && (stream.Width <= 0 || stream.Height <= 0 || stream.FramesPerSecond <= 0))
                    throw new InvalidOperationException("Video metadata unavailable for known fixture.");
                Invoke(window, "StreamInfo_Click", window, new RoutedEventArgs());
                var infoPanel = (FrameworkElement)window.FindName("StreamInfoPanel");
                if (infoPanel.Visibility != Visibility.Visible) throw new InvalidOperationException("Stream info toggle did not open.");
                if (BuildInfo.Engine != "mpc") {
                    await UntilAsync(() => Task.FromResult(((TextBlock)window.FindName("StreamVideoInfo")).Text.Contains("160 × 90") &&
                        (BuildInfo.Engine != "vlc" || ((TextBlock)window.FindName("StreamAudioInfo")).Text.Contains("44.1"))));
                    results["stream_info_panel_live_values"] = true;
                    results["active_audio_metadata_available"] = stream.AudioSampleRate > 0;
                }
                Invoke(window, "StreamInfo_Click", window, new RoutedEventArgs());
                if (infoPanel.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Stream info toggle did not close.");
                results["stream_info_toggle"] = true;
                // Cancel requests occupying all four logo slots. A new category's
                // logo must be available immediately, rather than after four slow HTTP waits.
                using (var logos = new ChannelLogoCache(Path.Combine(root, "logo-test"))) {
                    using var obsolete = new CancellationTokenSource();
                    var old = Enumerable.Range(0, 4).Select(i => logos.GetAsync(server.Url + $"slow-logo-{i}.png", TimeSpan.FromHours(1), obsolete.Token)).ToArray();
                    await Task.Delay(200); obsolete.Cancel();
                    foreach (var request in old) { try { await request; } catch (OperationCanceledException) { } }
                    var logoClock = Stopwatch.StartNew();
                    var current = await logos.GetAsync(server.Url + "fast-logo.png", TimeSpan.FromHours(1), default).WaitAsync(TimeSpan.FromSeconds(2));
                    if (current == null) throw new InvalidOperationException("Current category logo did not load after cancellation.");
                    results["logo_after_cancel_ms"] = logoClock.Elapsed.TotalMilliseconds;
                    int fetched = server.RequestCount("fast-logo.png");
                    var same = await logos.GetAsync(server.Url.Replace("http:", "HTTP:") + "fast-logo.png", TimeSpan.FromHours(1), default);
                    if (!ReferenceEquals(same, current) || server.RequestCount("fast-logo.png") != fetched)
                        throw new InvalidOperationException("Equivalent logo URLs bypassed the memory cache.");
                    using var reopened = new ChannelLogoCache(Path.Combine(root, "logo-test"));
                    foreach (var file in Directory.GetFiles(Path.Combine(root, "logo-test"), "*.image"))
                        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
                    var stale = await reopened.GetCachedAsync(server.Url + "fast-logo.png", default);
                    if (stale == null || server.RequestCount("fast-logo.png") != fetched)
                        throw new InvalidOperationException("Disk-cached logo was not available without a network reload.");
                    results["logo_memory_and_stale_disk_cache"] = true;
                }
                var tuneTimes = new List<double>();
                for (int i = 0; i < 6; i++)
                {
                    channelList.SelectedIndex = i % 2;
                    var tuneClock = Stopwatch.StartNew();
                    await (Task)Invoke(window, "PlaySelectedAsync")!;
                    await UntilAsync(async () => { var progress = await controller.ProgressAsync(); return progress.Playing && progress.Time > 0; });
                    tuneTimes.Add(tuneClock.Elapsed.TotalMilliseconds);
                }
                results["time_to_progress_ms"] = tuneTimes; // Decoder progress, not proof of a rendered first frame.
                stage = "rapid tuning";
                var rapidTunes = new List<Task>();
                for (int i = 0; i < 10; i++) { channelList.SelectedIndex = i % 2; rapidTunes.Add((Task)Invoke(window, "PlaySelectedAsync")!); }
                await Task.WhenAll(rapidTunes);
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["rapid_tune_requests"] = 10;
                var host = (NativeVideoHost)typeof(MainWindow).GetField("VideoView", Private | BindingFlags.Public)!.GetValue(window)!;
                var originalHandle = host.VideoHandle;
                if (BuildInfo.Engine == "mpv")
                {
                    var method = typeof(PlaybackController).GetMethod("CaptureVideoFrameAsync", Private)!;
                    var frame = await (Task<byte[]?>)method.Invoke(controller, new object[] { CancellationToken.None })!;
                    if (frame == null || frame.Length < 8 || frame[0] != 137 || frame[1] != 80)
                        throw new InvalidOperationException("Video-only frame capture did not return a PNG.");
                    results["video_only_frame_capture"] = true;
                    await using var capture = new ProcessAudioCapture();
                    await capture.StartAsync();
                    results["process_loopback_activation"] = true;
                }
                stage = "PiP";
                for (int i = 0; i < 5; i++)
                {
                    Invoke(window, "Window_PreviewKeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(window), Environment.TickCount, Key.P) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    var pip = (PictureInPictureWindow?)typeof(MainWindow).GetField("_pip", Private)!.GetValue(window);
                    if (pip == null) throw new InvalidOperationException("PiP failed to open.");
                    await UntilAsync(() => Task.FromResult(pip.IsLoaded));
                    ((DispatcherTimer)typeof(MainWindow).GetField("_infoTimer", Private)!.GetValue(window)!).Stop();
                    pip.SetAspect(4d / 3);
                    pip.Width = 600; await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    if (pip.WindowStyle != WindowStyle.None || Math.Abs(pip.ActualWidth / pip.ActualHeight - 4d / 3) > 0.02)
                        throw new InvalidOperationException("PiP chrome or aspect constraints failed.");
                    if (i == 0) {
                        var grip = ((List<Window>)typeof(PictureInPictureWindow).GetField("_resizeHandles", Private)!.GetValue(pip)!)[7];
                        var start = grip.PointToScreen(new Point(3, 3));
                        double originalWidth = pip.ActualWidth;
                        SetCursorPos((int)start.X, (int)start.Y);
                        // Native button input enters the real Windows sizing loop.
                        var drag = Task.Run(async () => {
                            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                            await Task.Delay(200);
                            SetCursorPos((int)start.X + 80, (int)start.Y + 60);
                            await Task.Delay(200); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                        });
                        await drag; await Task.Delay(200);
                        if (Math.Abs(pip.ActualWidth - originalWidth) < 20 || Math.Abs(pip.ActualWidth / pip.ActualHeight - 4d / 3) > 0.03)
                            throw new InvalidOperationException("Native PiP resize drag did not preserve aspect.");
                        results["pip_native_resize_drag"] = true;
                    }
                    pip.ShowControls(false);
                    if (pip.ControlsVisible) throw new InvalidOperationException("PiP bars remained visible.");
                    pip.ShowControls(true);
                    if (!pip.ControlsVisible) throw new InvalidOperationException("PiP hover bars could not open above native video.");
                    pip.ShowControls(false);
                    if (i == 0 && GetCursorPos(out var savedCursor)) {
                        try {
                            var inside = pip.PointToScreen(new Point(pip.ActualWidth / 2, pip.ActualHeight / 2));
                            if (SetCursorPos((int)inside.X, (int)inside.Y)) {
                                await Task.Delay(350);
                                if (!pip.ControlsVisible) throw new InvalidOperationException("Native-video hover did not reveal PiP controls.");
                                var outside = pip.PointToScreen(new Point(-20, -20));
                                SetCursorPos((int)outside.X, (int)outside.Y); await Task.Delay(350);
                                if (pip.ControlsVisible) throw new InvalidOperationException("PiP controls remained visible after mouse leave.");
                                results["native_hover_show_hide"] = true;
                            }
                        } finally { SetCursorPos(savedCursor.X, savedCursor.Y); }
                    }
                    TranslationOverlay? translationBeforeReturn = null;
                    if (i == 4)
                    {
                        typeof(MainWindow).GetField("_translateSpeech", Private)!.SetValue(window, true);
                        Invoke(window, "ResetTranslationOverlay");
                        translationBeforeReturn = (TranslationOverlay)typeof(MainWindow).GetField("_translationOverlay", Private)!.GetValue(window)!;
                        translationBeforeReturn.SetSpeech("Local translation overlay fixture");
                        await UntilAsync(() => Task.FromResult(translationBeforeReturn.IsVisible && translationBeforeReturn.Owner == pip));
                    }
                    window.WindowState = WindowState.Minimized;
                    await Task.Delay(300);
                    if (pip.WindowState == WindowState.Minimized || !pip.IsVisible || !(await controller.ProgressAsync()).Playing)
                        throw new InvalidOperationException("PiP did not survive main-window minimize.");
                    pip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pip), Environment.TickCount, Key.P) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    if ((PictureInPictureWindow?)typeof(MainWindow).GetField("_pip", Private)!.GetValue(window) != null) throw new InvalidOperationException("P did not return from PiP.");
                    window.WindowState = WindowState.Normal;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    if (i == 4)
                    {
                        var restoredTranslation = (TranslationOverlay)typeof(MainWindow).GetField("_translationOverlay", Private)!.GetValue(window)!;
                        if (ReferenceEquals(restoredTranslation, translationBeforeReturn))
                            throw new InvalidOperationException("PiP return retained the closed translation overlay.");
                        restoredTranslation.SetSpeech("Restored local translation overlay");
                        await UntilAsync(() => Task.FromResult(restoredTranslation.IsVisible && restoredTranslation.Owner == window));
                        typeof(MainWindow).GetField("_translateSpeech", Private)!.SetValue(window, false);
                        Invoke(window, "ResetTranslationOverlay");
                        results["translation_overlay_survives_pip_return"] = true;
                    }
                    if (host.VideoHandle != originalHandle || !(await controller.ProgressAsync()).Playing)
                        throw new InvalidOperationException("PiP replaced the video target or interrupted playback.");
                }
                results["pip_round_trips"] = 5;
                var restoreTimes = new List<double>();
                for (int i = 0; i < 10; i++)
                {
                    window.WindowState = WindowState.Minimized;
                    await Task.Delay(200);
                    if (!(await controller.ProgressAsync()).Playing) throw new InvalidOperationException("Playback stopped while minimized.");
                    var restoreClock = Stopwatch.StartNew();
                    window.WindowState = WindowState.Normal;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    restoreTimes.Add(restoreClock.Elapsed.TotalMilliseconds);
                    await Task.Delay(200);
                }
                results["minimize_restore_count"] = restoreTimes.Count;
                results["restore_ms"] = restoreTimes;
                stage = "panel resize";
                var playerColumn = (ColumnDefinition)window.FindName("PlayerColumn");
                var channelColumn = (ColumnDefinition)window.FindName("ChannelsColumn");
                var groupColumn = (ColumnDefinition)window.FindName("GroupsColumn");
                var divider = (FrameworkElement)window.FindName("PlayerDivider");
                double playerBefore = playerColumn.ActualWidth, channelsBefore = channelColumn.ActualWidth;
                var dividerPoint = divider.PointToScreen(new Point(divider.ActualWidth / 2, divider.ActualHeight / 2));
                SetCursorPos((int)dividerPoint.X, (int)dividerPoint.Y); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(100); SetCursorPos((int)dividerPoint.X - 70, (int)dividerPoint.Y);
                await Task.Delay(150); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(150);
                if (playerColumn.ActualWidth < playerBefore + 30 || channelColumn.ActualWidth > channelsBefore - 30)
                    throw new InvalidOperationException("Player divider did not resize neighboring columns.");
                double originalWindowWidth = window.Width, originalMinWidth = window.MinWidth;
                double actualWindowWidth = window.ActualWidth, savedPlayerWidth = playerColumn.ActualWidth;
                results["panel_resize_before"] = new { actualWindowWidth, player = savedPlayerWidth, channels = channelColumn.ActualWidth, groups = groupColumn.ActualWidth };
                window.MinWidth = Math.Min(originalMinWidth, actualWindowWidth - 100);
                window.Width = actualWindowWidth - 80; await Task.Delay(200);
                results["panel_resize_smaller"] = new { window = window.ActualWidth, player = playerColumn.ActualWidth };
                await UntilAsync(() => Task.FromResult(playerColumn.ActualWidth < savedPlayerWidth - 5));
                if (groupColumn.ActualWidth < groupColumn.MinWidth || channelColumn.ActualWidth < channelColumn.MinWidth)
                    throw new InvalidOperationException("Resizing violated panel minimum widths.");
                window.Width = originalWindowWidth; window.MinWidth = originalMinWidth;
                await UntilAsync(() => Task.FromResult(Math.Abs(playerColumn.ActualWidth - savedPlayerWidth) < 3));
                results["native_panel_resize_drag"] = true;
                stage = "fullscreen";
                var before = await controller.ProgressAsync();
                bool wasTopmost = window.Topmost;
                Invoke(window, "ToggleFullscreen"); await Task.Delay(1000);
                if (window.Topmost != wasTopmost) throw new InvalidOperationException("Fullscreen forced always-on-top.");
                var other = new Window { Title = "Alt-Tab regression fixture", Width = 320, Height = 180 };
                other.Show(); other.Activate();
                await UntilAsync(() => Task.FromResult(other.IsActive));
                window.Activate(); other.Close();
                await UntilAsync(() => Task.FromResult(window.IsActive));
                var fullscreen = (FullscreenControls)typeof(MainWindow).GetField("_fullscreenControls", Private)!.GetValue(window)!;
                await UntilAsync(() => Task.FromResult(fullscreen.ControlsVisible));
                fullscreen.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fullscreen),
                    Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await UntilAsync(() => Task.FromResult(!(bool)typeof(MainWindow).GetField("_isFullScreen", Private)!.GetValue(window)!));
                fullscreen.Reveal(); // A queued activation must never reopen a closed window.
                results["fullscreen_reactivation_and_escape"] = true;
                for (int pass = 0; pass < 3; pass++) {
                    Invoke(window, "ToggleFullscreen");
                    var overlay = new TranslationOverlay(() => (window, host));
                    overlay.SetScreenRegions(new[] { new HnIptv.Translation.ScreenTranslationRegion(
                        0.15, 0.7, 0.5, 0.1, "Translated headline at its original position", "#203040", "#FFFFFF") }, 1920, 1080, "en");
                    if (overlay.PositionedRegionCount != 1) throw new InvalidOperationException("Positioned translation was not drawn.");
                    if (Math.Abs(overlay.Width-host.ActualWidth)>2 || Math.Abs(overlay.Height-host.ActualHeight)>2)
                        throw new InvalidOperationException("Positioned overlay does not follow the video viewport.");
                    overlay.SetSpeech("Continuous speech caption");
                    overlay.SetSpeech(""); // A silent chunk must preserve the previous caption.
                    await UntilAsync(() => Task.FromResult(overlay.IsVisible));
                    window.Activate();
                    Invoke(window, "ToggleFullscreen");
                    overlay.SetScreenRegions(new[] { new HnIptv.Translation.ScreenTranslationRegion(
                        0.15, 0.7, 0.5, 0.1, "Translated headline after fullscreen") }, 1920, 1080, "en");
                    overlay.Close();
                    overlay.SetSpeech("Late cancelled result");
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                }
                results["translation_fullscreen_lifecycle"] = true;
                results["positioned_translation_viewport"] = true;
                IntPtr pixelFixture = Marshal.AllocHGlobal(16);
                try {
                    Marshal.Copy(new byte[] { 1,2,3,255, 4,5,6,255, 7,8,9,255, 10,11,12,255 }, 0, pixelFixture, 16);
                    var pixels = VideoFrameCopy.Bgra(pixelFixture + 8, 2, 2, -8, 16);
                    if (pixels == null || pixels[0] != 7 || pixels[8] != 1) throw new InvalidOperationException("Bottom-up video capture failed.");
                    if (VideoFrameCopy.Bgra(pixelFixture, 2, 2, 4, 16) != null) throw new InvalidOperationException("Invalid video stride accepted.");
                } finally { Marshal.FreeHGlobal(pixelFixture); }
                results["negative_stride_video_frame"] = true;
                var after = await controller.ProgressAsync();
                if (!after.Playing || after.Time <= before.Time) throw new InvalidOperationException("Playback did not advance across fullscreen.");
                results["fullscreen_playback_advanced"] = true;
                await UntilAsync(() => Task.FromResult(Math.Abs(playerColumn.ActualWidth - savedPlayerWidth) < 3));
                results["resized_layout_survived_fullscreen"] = true;
                // Include the decoder child in MPC-HC memory and CPU comparisons.
                if (BuildInfo.Engine == "mpv") {
                    stage = "captions";
                    var captionChannel = new Channel { Id = "captions", Name = "Caption fixture", GroupId = "test", GroupName = "Test", Command = server.Url + "captions.mkv" };
                    await (Task)Invoke(window, "SetChannelsAsync", new[] { captionChannel }, new[] { new ChannelGroup("test", "Test") }, kind, "Caption fixture", "test", false)!;
                    await UntilAsync(() => Task.FromResult(channelList.Items.Count == 1)); channelList.SelectedIndex = 0;
                    await (Task)Invoke(window, "PlaySelectedAsync")!;
                    await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                    async Task<(bool Visible, string Text)> CaptionState() => await (Task<(bool Visible, string Text)>)typeof(PlaybackController).GetMethod("ReadCaptionsAsync", Private)!.Invoke(controller, null)!;
                    if ((await CaptionState()).Visible) throw new InvalidOperationException("Captions should default to off.");
                    var cc = (Button)window.FindName("CaptionsButton");
                    cc.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await UntilAsync(async () => { var state = await CaptionState(); return state.Visible && state.Text.Contains("HN-IPTV caption test"); });
                    if (!cc.Content.ToString()!.Contains("On")) throw new InvalidOperationException("CC button state did not update.");
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    if (!ToggleVisual.GetIsActive(cc) || cc.Background is not System.Windows.Media.SolidColorBrush ccBrush ||
                        ccBrush.Color != System.Windows.Media.Color.FromRgb(11, 23, 40))
                        throw new InvalidOperationException("Enabled CC does not show the darker active color.");
                    await UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(AppStorage.DataDirectory, "player-settings.json")) &&
                        HnIptv.Preferences.PlayerSettings.Load(Path.Combine(AppStorage.DataDirectory, "player-settings.json")).CaptionsEnabled));
                    await (Task)Invoke(window, "TuneSelectedAsync", true, false)!;
                    await UntilAsync(async () => { var state = await CaptionState(); return state.Visible && state.Text.Contains("HN-IPTV caption test"); });
                    cc.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await UntilAsync(async () => !(await CaptionState()).Visible);
                    Invoke(window, "TogglePictureInPicture");
                    var captionPip = (PictureInPictureWindow)typeof(MainWindow).GetField("_pip", Private)!.GetValue(window)!;
                    await UntilAsync(() => Task.FromResult(captionPip.IsLoaded)); captionPip.ShowControls(true);
                    var pipCc = (Button)typeof(PictureInPictureWindow).GetField("_captionsButton", Private)!.GetValue(captionPip)!;
                    pipCc.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await UntilAsync(async () => (await CaptionState()).Visible);
                    captionPip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(captionPip), Environment.TickCount, Key.C) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    await UntilAsync(async () => !(await CaptionState()).Visible); captionPip.Close();
                    results["captions_decoded_toggle_and_retune"] = true;
                    if (ToggleVisual.GetIsActive(cc)) throw new InvalidOperationException("Disabled CC still shows active color.");
                    results["captions_pip_button_and_shortcut"] = true;
                    results["toggle_button_active_colors"] = true;
                }
                var child = typeof(PlaybackController).GetMethod("ChildMetrics", Private | BindingFlags.Public)?.Invoke(controller, null);
                if (child is ValueTuple<long, long, double> childMetrics) {
                    results["child_private_bytes"] = childMetrics.Item1; results["child_working_set_bytes"] = childMetrics.Item2; results["child_cpu_ms"] = childMetrics.Item3;
                }
                results["cpu_time_ms"] = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
                results["private_bytes"] = Process.GetCurrentProcess().PrivateMemorySize64;
                results["working_set_bytes"] = Process.GetCurrentProcess().WorkingSet64;
                results["passed"] = true; exit = 0;
            }
            catch (Exception ex)
            {
                results["passed"] = false; results["error_type"] = (ex.InnerException ?? ex).GetType().Name; Console.Error.WriteLine(ex);
                await Task.Delay(150); // Give the async sanitized diagnostic writer time to finish.
                if (File.Exists(App.DiagnosticLogPath)) {
                    var diagnostics = File.ReadAllLines(App.DiagnosticLogPath).TakeLast(40).ToArray();
                    results["diagnostics"] = diagnostics;
                    foreach (var line in diagnostics) Console.Error.WriteLine(line);
                }
            }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                window.Close();
            }
        };
        // A dispatcher/native deadlock must fail CI instead of consuming the entire job.
        _ = Task.Run(async () => {
            await Task.Delay(TimeSpan.FromSeconds(120));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, engine = BuildInfo.Engine, error_type = "WatchdogTimeout", stage }));
            foreach (var child in Process.GetProcessesByName("mpc-hc64")) {
                try { if (child.MainModule?.FileName.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) == true) child.Kill(true); } catch { }
            }
            Environment.Exit(2);
        });
        app.Run();
        try { Directory.Delete(root, true); } catch (IOException) { }
        return exit;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static object? Invoke(MainWindow window, string method, params object[] args)
        => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, args);
    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        { if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(); await Task.Delay(100); }
    }
}

// Loopback-only HTTP fixtures: no provider credentials or external network.
internal sealed class FixtureServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    public string Url { get; }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _headers = new();
    public int RequestCount(string name) => _counts.GetValueOrDefault(name);
    public string RequestHeaders(string name) => _headers.GetValueOrDefault(name) ?? "";
    public FixtureServer(string directory)
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = ServeAsync(client, directory);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    private async Task ServeAsync(TcpClient client, string directory)
    {
        using (client)
        {
            try
            {
                using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, leaveOpen: true);
                string? request = await reader.ReadLineAsync(_stop.Token);
                var requestHeaders = new StringBuilder();
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line) requestHeaders.AppendLine(line);
                string name = request?.Split(' ').ElementAtOrDefault(1)?.TrimStart('/') ?? "";
                if (name != Path.GetFileName(name)) return;
                _counts.AddOrUpdate(name, 1, (_, count) => count + 1);
                _headers[name] = requestHeaders.ToString();
                bool logo = name == "fast-logo.png" || name.StartsWith("slow-logo-", StringComparison.Ordinal);
                if (name.StartsWith("slow-logo-", StringComparison.Ordinal)) await Task.Delay(5000, _stop.Token);
                if (!logo && !File.Exists(Path.Combine(directory, name))) return;
                var bytes = logo ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC")
                    : await File.ReadAllBytesAsync(Path.Combine(directory, name), _stop.Token);
                string type = logo ? "image/png" : name.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t";
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await client.GetStream().WriteAsync(header, _stop.Token);
                await client.GetStream().WriteAsync(bytes, _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
    }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); }
}
