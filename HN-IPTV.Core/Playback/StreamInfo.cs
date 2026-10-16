using System.Globalization;

namespace HnIptv.Playback;

// Detached telemetry: no native handles, URLs, credentials or blocking UI calls.
public sealed record StreamInfo
{
    public static readonly StreamInfo Empty = new();
    public int Width { get; init; }
    public int Height { get; init; }
    public double DisplayAspect { get; init; }
    public double FramesPerSecond { get; init; }
    public double? InputBitsPerSecond { get; init; }
    public string VideoCodec { get; init; } = "";
    public string AudioCodec { get; init; } = "";
    public int AudioChannels { get; init; }
    public int AudioSampleRate { get; init; }
    public long DisplayedFrames { get; init; }
    public long PositionMilliseconds { get; init; } = -1;
    public bool Playing { get; init; }
    public string State { get; init; } = "Stopped";
    public double AspectRatio => double.IsFinite(DisplayAspect) && DisplayAspect is >= 0.2 and <= 5
        ? DisplayAspect : Width > 0 && Height > 0 ? (double)Width / Height : 16d / 9;
    public string ResolutionText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "Unavailable";
    public string FrameRateText => double.IsFinite(FramesPerSecond) && FramesPerSecond > 0
        ? FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture) + " fps" : "Unavailable";
    public string BitrateText => InputBitsPerSecond is double rate && double.IsFinite(rate) && rate >= 0
        ? (rate / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + " Mb/s" : "Unavailable";
    public string AudioText => string.Join(" · ", new[] {
        string.IsNullOrWhiteSpace(AudioCodec) ? "Codec unavailable" : AudioCodec,
        AudioChannels > 0 ? AudioChannels + " channels" : "",
        AudioSampleRate > 0 ? (AudioSampleRate / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + " kHz" : ""
    }.Where(s => s.Length > 0));
}

public static class VideoGeometry
{
    public static (double Width, double Height) Fit(double width, double aspect, double maxWidth, double maxHeight)
    {
        if (!double.IsFinite(aspect) || aspect is < 0.2 or > 5) aspect = 16d / 9;
        width = Math.Clamp(double.IsFinite(width) ? width : 480, 1, Math.Max(1, maxWidth));
        var height = width / aspect;
        if (height > maxHeight) { height = Math.Max(1, maxHeight); width = height * aspect; }
        return (width, height);
    }
}
