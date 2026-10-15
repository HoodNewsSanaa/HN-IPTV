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
                var channelList = (ListBox)typeof(MainWindow).GetField("ChannelsList", Private | BindingFlags.Public)!.GetValue(window)!;
                var controller = (PlaybackController)typeof(MainWindow).GetField("_playback", Private)!.GetValue(window)!;
                var kind = Enum.Parse(typeof(MainWindow).GetNestedType("SourceKind", BindingFlags.NonPublic)!, "M3u");
                var channels = new[] {
                    new Channel { Id = "ts", Name = "TS fixture", GroupId = "test", GroupName = "Test", Command = server.Url + "segment00.ts" },
                    new Channel { Id = "hls", Name = "HLS fixture", GroupId = "test", GroupName = "Test", Command = server.Url + "fixture.m3u8" }
                };
                await (Task)Invoke(window, "SetChannelsAsync", channels, new[] { new ChannelGroup("test", "Test") }, kind, "Test", "test", false)!;
                await UntilAsync(() => Task.FromResult(channelList.Items.Count == 2));
                channelList.SelectedIndex = 0;
                await (Task)Invoke(window, "PlaySelectedAsync")!;
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["mpeg_ts_started"] = true;
                channelList.SelectedIndex = 1;
                await (Task)Invoke(window, "PlaySelectedAsync")!;
                await UntilAsync(async () => (await controller.ProgressAsync()).Playing);
                results["hls_started"] = true;
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
