using LibVLCSharp.Shared;
using HnIptv.Playback;
using System.Diagnostics;

namespace HnIptv;

internal sealed class PlaybackController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private Media? _media;
    private bool _disposed;
    public event Action? PlaybackError;
    private long _lastRateTimestamp;
    private int _lastReadBytes;
    private double? _inputRate;

    private async Task RunAsync(string operation, Action action, CancellationToken ct = default)
    {
        using (App.Measure(operation + ".queue"))
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

    public Task PlayAsync(Uri uri, IntPtr hwnd, int volume, CancellationToken ct, int bufferMilliseconds = 1200, string userAgent = "", string referer = "") => RunAsync("vlc.tune", () =>
    {
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Video surface is not ready.");
        if (_player == null)
        {
            Core.Initialize();
            _vlc = new LibVLC("--no-video-title-show");
            _player = new MediaPlayer(_vlc) { Hwnd = hwnd, EnableMouseInput = false, EnableKeyInput = false };
            _player.EncounteredError += (_, _) => PlaybackError?.Invoke();
        }
        // Stop joins VLC threads. Never call it on the WPF dispatcher or a VLC callback.
        using (App.Measure("vlc.stop")) _player.Stop();
        _media?.Dispose(); _media = null;
        ct.ThrowIfCancellationRequested();
        _lastRateTimestamp = 0; _lastReadBytes = 0; _inputRate = null;
        if (userAgent.Contains('\r') || userAgent.Contains('\n') || referer.Contains('\r') || referer.Contains('\n'))
            throw new System.IO.InvalidDataException("Invalid stream headers.");
        _media = new Media(_vlc!, uri);
        if (userAgent.Length > 0) _media.AddOption(":http-user-agent=" + userAgent);
        if (referer.Length > 0) _media.AddOption(":http-referrer=" + referer);
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
    // Telemetry never queues behind a blocking native Stop.
    internal async Task<StreamInfo?> ReadInfoAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try { return await Task.Run(ReadInfo, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private StreamInfo ReadInfo()
    {
        if (_disposed || _player == null || _media == null) return StreamInfo.Empty;
        var result = new StreamInfo { Playing = _player.IsPlaying, State = _player.State.ToString(), PositionMilliseconds = _player.Time };
        var tracks = _media.Tracks;
        // Some demuxers report a transient/unmapped selected ID. A sole enabled
        // track is unambiguous; never guess between multiple tracks.
        var videoId = _player.VideoTrack;
        if (videoId >= 0 && !tracks.Any(t => t.TrackType == TrackType.Video && t.Id == videoId) && tracks.Count(t => t.TrackType == TrackType.Video) == 1)
            videoId = tracks.First(t => t.TrackType == TrackType.Video).Id;
        var audioId = _player.AudioTrack;
        if (audioId >= 0 && !tracks.Any(t => t.TrackType == TrackType.Audio && t.Id == audioId) && tracks.Count(t => t.TrackType == TrackType.Audio) == 1)
            audioId = tracks.First(t => t.TrackType == TrackType.Audio).Id;
        foreach (var track in tracks)
        {
            if (track.TrackType == TrackType.Video && track.Id == videoId)
            {
                var video = track.Data.Video;
                double aspect = video.Height > 0 ? (double)video.Width / video.Height : 0;
                if (video.SarNum > 0 && video.SarDen > 0) aspect *= (double)video.SarNum / video.SarDen;
                if (video.Orientation >= VideoOrientation.LeftTop) aspect = aspect > 0 ? 1 / aspect : 0;
                result = result with { Width = (int)video.Width, Height = (int)video.Height,
                    DisplayAspect = aspect, FramesPerSecond = video.FrameRateDen > 0 ? (double)video.FrameRateNum / video.FrameRateDen : 0,
                    VideoCodec = _media.CodecDescription(TrackType.Video, track.Codec) ?? "" };
            }
            else if (track.TrackType == TrackType.Audio && track.Id == audioId)
                result = result with { AudioCodec = _media.CodecDescription(TrackType.Audio, track.Codec) ?? "",
                    AudioChannels = (int)track.Data.Audio.Channels, AudioSampleRate = (int)track.Data.Audio.Rate };
        }
        uint width = 0, height = 0;
        if (result.Width <= 0 && _player.Size(0, ref width, ref height) && width > 0 && height > 0)
            result = result with { Width = (int)width, Height = (int)height };
        var stats = _media.Statistics;
        var now = Stopwatch.GetTimestamp();
        if (_lastRateTimestamp != 0)
        {
            double elapsed = Stopwatch.GetElapsedTime(_lastRateTimestamp, now).TotalSeconds;
            if (elapsed >= 0.5)
            {
                _inputRate = stats.ReadBytes >= _lastReadBytes ? (stats.ReadBytes - (double)_lastReadBytes) * 8 / elapsed : null;
                _lastRateTimestamp = now; _lastReadBytes = stats.ReadBytes;
            }
        }
        else { _lastRateTimestamp = now; _lastReadBytes = stats.ReadBytes; }
        return result with { InputBitsPerSecond = _inputRate, DisplayedFrames = stats.DisplayedPictures };
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
