namespace HnIptv.Playback;

/// <summary>
/// Cross-platform boundary. Implementations host a real video surface and native
/// playback engine: desktop LibVLC, Android Media3 or Android LibVLC as needed.
/// Video views, native audio focus, DRM, and background playback live outside Core.
/// </summary>
public interface IStreamPlayer : IAsyncDisposable
{
    bool IsPlaying { get; }
    Task PlayAsync(StreamRequest request, CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed record StreamRequest(
    Uri Url,
    int NetworkCacheMilliseconds = 1200,
    IReadOnlyDictionary<string, string>? HttpHeaders = null);
