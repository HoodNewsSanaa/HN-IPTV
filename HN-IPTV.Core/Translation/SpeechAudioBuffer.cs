using System.IO;
namespace HnIptv.Translation;

// Fresh PCM only. The native recognizer owns continuous acoustic context.
public sealed class SpeechAudioBuffer
{
    public const int BytesPerSecond = 176400;
    private readonly MemoryStream _pending = new();
    private long _totalBytes;
    public double AudioEndSeconds => (double)_totalBytes / BytesPerSecond;
    public double LastContextSeconds { get; private set; }
    public byte[]? Append(ReadOnlySpan<byte> pcm)
    {
        _totalBytes += pcm.Length;
        _pending.Write(pcm);
        if (_pending.Length < BytesPerSecond * 0.32) return null;
        LastContextSeconds = 0;
        var segment = _pending.ToArray();
        _pending.SetLength(0);
        return segment;
    }
}
