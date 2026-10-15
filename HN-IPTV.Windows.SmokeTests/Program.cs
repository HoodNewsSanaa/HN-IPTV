using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HnIptv;
using HnIptv.Models;

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
        int exit = 1; bool started = false;
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
                channelList.SelectedIndex = 0;
                await (Task)Invoke(window, "PlaySelectedAsync")!;
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["mpeg_ts_started"] = true;
                channelList.SelectedIndex = 1;
                await (Task)Invoke(window, "PlaySelectedAsync")!;
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["hls_started"] = true;
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
                var rapidTunes = new List<Task>();
                for (int i = 0; i < 10; i++) { channelList.SelectedIndex = i % 2; rapidTunes.Add((Task)Invoke(window, "PlaySelectedAsync")!); }
                await Task.WhenAll(rapidTunes);
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["rapid_tune_requests"] = 10;
                var host = (NativeVideoHost)typeof(MainWindow).GetField("VideoView", Private | BindingFlags.Public)!.GetValue(window)!;
                var originalHandle = host.VideoHandle;
                for (int i = 0; i < 5; i++)
                {
                    Invoke(window, "TogglePictureInPicture");
                    var pip = (PictureInPictureWindow?)typeof(MainWindow).GetField("_pip", Private)!.GetValue(window);
                    if (pip == null) throw new InvalidOperationException("PiP failed to open.");
                    window.WindowState = WindowState.Minimized;
                    await Task.Delay(300);
                    if (pip.WindowState == WindowState.Minimized || !pip.IsVisible || !(await controller.ProgressAsync()).Playing)
                        throw new InvalidOperationException("PiP did not survive main-window minimize.");
                    pip.Close(); window.WindowState = WindowState.Normal;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
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
                var before = await controller.ProgressAsync();
                Invoke(window, "ToggleFullscreen"); await Task.Delay(1000);
                Invoke(window, "ToggleFullscreen");
                var after = await controller.ProgressAsync();
                if (!after.Playing || after.Time <= before.Time) throw new InvalidOperationException("Playback did not advance across fullscreen.");
                results["fullscreen_playback_advanced"] = true;
                results["cpu_time_ms"] = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
                results["private_bytes"] = Process.GetCurrentProcess().PrivateMemorySize64;
                results["working_set_bytes"] = Process.GetCurrentProcess().WorkingSet64;
                results["passed"] = true; exit = 0;
            }
            catch (Exception ex)
            { results["passed"] = false; results["error_type"] = (ex.InnerException ?? ex).GetType().Name; Console.Error.WriteLine(ex); }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                window.Close();
            }
        };
        // A dispatcher/native deadlock must fail CI instead of consuming the entire job.
        _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(120)); Environment.Exit(2); });
        app.Run();
        try { Directory.Delete(root, true); } catch (IOException) { }
        return exit;
    }
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
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                string name = request?.Split(' ').ElementAtOrDefault(1)?.TrimStart('/') ?? "";
                if (name != Path.GetFileName(name) || !File.Exists(Path.Combine(directory, name))) return;
                var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, name), _stop.Token);
                string type = name.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t";
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await client.GetStream().WriteAsync(header, _stop.Token);
                await client.GetStream().WriteAsync(bytes, _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
    }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); }
}
