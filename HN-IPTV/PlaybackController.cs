using LibVLCSharp.Shared;

namespace HnIptv;

internal sealed class PlaybackController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private Media? _media;
    private bool _disposed;
    public event Action? PlaybackError;

    private async Task RunAsync(string operation, Action action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (_disposed) return;
                using var timing = App.Measure(operation);
                action();
            }, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public Task PlayAsync(Uri uri, IntPtr hwnd, int volume, CancellationToken ct, int bufferMilliseconds = 1200) => RunAsync("vlc.tune", () =>
    {
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Video surface is not ready.");
        if (_player == null)
        {
            Core.Initialize();
            _vlc = new LibVLC("--no-video-title-show", "--no-stats");
            _player = new MediaPlayer(_vlc) { Hwnd = hwnd, EnableMouseInput = false, EnableKeyInput = false };
            _player.EncounteredError += (_, _) => PlaybackError?.Invoke();
        }
        // Stop joins VLC threads. Never call it on the WPF dispatcher or a VLC callback.
        using (App.Measure("vlc.stop")) _player.Stop();
        _media?.Dispose(); _media = null;
        ct.ThrowIfCancellationRequested();
        _media = new Media(_vlc!, uri);
        int buffer = Math.Clamp(bufferMilliseconds, 250, 10000);
        _media.AddOption($":network-caching={buffer}");
        _media.AddOption($":live-caching={buffer}");
        _player.Volume = volume;
        if (!_player.Play(_media)) throw new InvalidOperationException("The playback engine could not start the stream.");
    }, ct);

    public Task StopAsync() => RunAsync("vlc.stop", () => { _player?.Stop(); _media?.Dispose(); _media = null; });
    public Task PauseAsync() => RunAsync("vlc.pause", () => { if (_media != null) _player?.Pause(); });
    public Task VolumeAsync(int volume) => RunAsync("vlc.volume", () => { if (_player != null) _player.Volume = volume; });
    internal async Task<(bool Playing, long Time)> ProgressAsync()
    {
        (bool Playing, long Time) result = default;
        await RunAsync("vlc.progress", () => result = (_player?.IsPlaying ?? false, _player?.Time ?? -1));
        return result;
    }
    public Task ShutdownAsync() => RunAsync("vlc.shutdown", () =>
    {
        _disposed = true;
        _player?.Stop();
        if (_player != null) _player.Hwnd = IntPtr.Zero;
        _media?.Dispose(); _player?.Dispose(); _vlc?.Dispose();
        _media = null; _player = null; _vlc = null;
    });
}
