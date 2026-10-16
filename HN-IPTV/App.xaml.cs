using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;

namespace HnIptv;

public partial class App : Application
{
    private static readonly Channel<string> Log = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private static Task _logWriter = Task.CompletedTask;
    private Timer? _watchdog;
    private Mutex? _instanceMutex;
    private Mutex? _installerMutex;
    private int _heartbeatPending;
    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();
    public static string DiagnosticLogPath => Path.Combine(AppStorage.DataDirectory, "diagnostics.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => LogFatalException("dispatcher.unhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        { if (args.ExceptionObject is Exception ex) LogFatalException("clr.unhandled", ex); };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        { LogException("task.unobserved", args.Exception); args.SetObserved(); };
        base.OnStartup(e);
        try { await Task.Run(AppStorage.Initialize); }
        catch (Exception)
        {
            MessageBox.Show("HN-IPTV could not open its data folder. Extract the portable ZIP to a writable folder and check available space.",
                "HN-IPTV startup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1); return;
        }
        _instanceMutex = new Mutex(false, "HN-IPTV-Data-" + HnIptv.Preferences.ChannelPreferences.Hash(AppStorage.DataDirectory.ToUpperInvariant()), out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("HN-IPTV is already open for this data folder.", "HN-IPTV");
            Shutdown(); return;
        }
        _installerMutex = new Mutex(false, "HN-IPTV-Application");
        _logWriter = Task.Run(WriteLogAsync);
        var window = new MainWindow();
        MainWindow = window;
        window.ContentRendered += (_, _) => LogDiagnostic($"startup.window_ms={StartupClock.ElapsedMilliseconds}");
        window.Show();
        _watchdog = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _heartbeatPending, 1) != 0 || Dispatcher.HasShutdownStarted) return;
            var clock = Stopwatch.StartNew();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _heartbeatPending, 0);
                if (clock.ElapsedMilliseconds > 250) LogDiagnostic($"dispatcher.delay_ms={clock.ElapsedMilliseconds}");
            }));
        }, null, 2000, 2000);
        LogDiagnostic($"app.start version={BuildInfo.Version} engine={BuildInfo.Engine}");
    }

    public static void LogDiagnostic(string entry) => Log.Writer.TryWrite($"{DateTimeOffset.UtcNow:O} {entry}");
    public static void LogException(string context, Exception exception)
    {
        // Messages, URLs, usernames, file paths and VLC debug output are never logged.
        LogDiagnostic($"{context} {HnIptv.Diagnostics.SafeException.Describe(exception)}");
    }
    private static void LogFatalException(string context, Exception exception)
    {
        LogException(context, exception);
        // The async log queue may not flush before termination. Only the fatal
        // path writes synchronously; regular UI/network handlers keep async logging.
        try
        {
            var path = Path.Combine(AppStorage.DataDirectory, "crash.log");
            if (File.Exists(path) && new FileInfo(path).Length > 131072) File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {context} {HnIptv.Diagnostics.SafeException.Describe(exception)}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
    public static IDisposable Measure(string operation) => new Timing(operation);
    private sealed class Timing(string operation) : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public void Dispose() => LogDiagnostic($"{operation} duration_ms={_clock.Elapsed.TotalMilliseconds:F1}");
    }
    private static async Task WriteLogAsync()
    {
        await foreach (var entry in Log.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (File.Exists(DiagnosticLogPath) && new FileInfo(DiagnosticLogPath).Length > 1_048_576)
                    File.Move(DiagnosticLogPath, DiagnosticLogPath + ".1", true);
                var batch = new StringBuilder(entry).AppendLine();
                for (int i = 1; i < 32 && Log.Reader.TryRead(out var next); i++) batch.AppendLine(next);
                await File.AppendAllTextAsync(DiagnosticLogPath, batch.ToString()).ConfigureAwait(false);
            }
            catch (Exception) { /* Diagnostics cannot interrupt playback or saving. */ }
        }
    }
    public static async Task FlushDiagnosticsAsync()
    {
        Log.Writer.TryComplete();
        await _logWriter.ConfigureAwait(false);
    }
    protected override void OnExit(ExitEventArgs e) { _watchdog?.Dispose(); _instanceMutex?.Dispose(); _installerMutex?.Dispose(); base.OnExit(e); }
}
