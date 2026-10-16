using System.Text.Json;
using HnIptv.Services;

namespace HnIptv.Preferences;

public sealed record PlayerSettings
{
    public bool CaptionsEnabled { get; init; }
    public int BufferMilliseconds { get; init; } = 1200;
    public double PipLeft { get; init; } = 80;
    public double PipTop { get; init; } = 80;
    public double PipWidth { get; init; } = 480;
    public double PipHeight { get; init; } = 320;
    public PlayerSettings Normalize() => this with
    {
        BufferMilliseconds = Math.Clamp(BufferMilliseconds, 250, 10000),
        PipLeft = double.IsFinite(PipLeft) ? PipLeft : 80,
        PipTop = double.IsFinite(PipTop) ? PipTop : 80,
        PipWidth = double.IsFinite(PipWidth) ? Math.Clamp(PipWidth, 256, 1920) : 480,
        PipHeight = double.IsFinite(PipHeight) ? Math.Clamp(PipHeight, 64, 1080) : 320
    };
    public static PlayerSettings Load(string path) => File.Exists(path)
        ? (JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllBytes(path)) ?? new()).Normalize() : new();
    public void Save(string path) => AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, Normalize()));
}
