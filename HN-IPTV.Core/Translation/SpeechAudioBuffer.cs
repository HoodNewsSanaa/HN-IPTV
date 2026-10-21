using System.IO;
using System.Runtime.InteropServices;
namespace HnIptv.Translation;

// Phrase boundaries reduce cut words; a hard limit keeps continuous speech responsive.
public sealed class SpeechAudioBuffer
{
    public const int BytesPerSecond = 176400;
    private readonly MemoryStream _pending = new();
    private byte[] _context = [];
    private int _quietBytes;
    private long _totalBytes;
    public double AudioEndSeconds => (double)_totalBytes / BytesPerSecond;
    public double LastContextSeconds { get; private set; }
    public byte[]? Append(ReadOnlySpan<byte> pcm)
    {
        _totalBytes += pcm.Length;
        _pending.Write(pcm);
        long energy = 0;
        var samples = MemoryMarshal.Cast<byte, short>(pcm);
        foreach (short sample in samples) energy += (long)sample * sample;
        _quietBytes = samples.Length == 0 || energy / Math.Max(1, samples.Length) < 4096 ? _quietBytes + pcm.Length : 0;
        if (_pending.Length < BytesPerSecond * 1.5 &&
            (_pending.Length < BytesPerSecond * 0.8 || _quietBytes < BytesPerSecond * 0.2)) return null;
        LastContextSeconds = (double)_context.Length / BytesPerSecond;
        var current = _pending.ToArray();
        var segment = new byte[_context.Length + current.Length];
        _context.CopyTo(segment, 0); current.CopyTo(segment, _context.Length);
        int tail = Math.Min(segment.Length, BytesPerSecond * 25 / 2);
        _context = segment.AsSpan(segment.Length - tail).ToArray();
        _pending.SetLength(0); _quietBytes = 0;
        return segment;
    }
}
