using System.Collections.Concurrent;
using HnIptv.Playback;
using System.Globalization;
using System.Runtime.InteropServices;

namespace HnIptv;

// Default mpv backend. Each distribution contains one native playback engine.
internal sealed class PlaybackController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _eventStop = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource> _commands = new();
    private Task _events = Task.CompletedTask;
    private IntPtr _handle;
    private long _nextCommand;
    private bool _disposed, _paused, _captionsEnabled;
    private volatile bool _playing;
    public event Action? PlaybackError;

    private async Task RunAsync(Action action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() => { ct.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this); action(); }, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    private void Initialize(IntPtr hwnd)
    {
        if (_handle != IntPtr.Zero) return;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Video surface is not ready.");
        _handle = Native.mpv_create();
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("mpv initialization failed.");
        try
        {
            foreach (var option in new[] { ("config", "no"), ("terminal", "no"), ("osc", "no"),
                ("input-default-bindings", "no"), ("input-vo-keyboard", "no"), ("ytdl", "no"),
                ("sub-create-cc-track", "yes"), ("sub-visibility", _captionsEnabled ? "yes" : "no"),
                ("secondary-sub-visibility", "no"), ("hwdec", "auto-safe"), ("vo", "gpu"), ("gpu-context", "d3d11"), ("cache", "yes") })
                Check(Native.mpv_set_option_string(_handle, option.Item1, option.Item2));
            long window = hwnd.ToInt64();
            Check(Native.mpv_set_option(_handle, "wid", 4, ref window));
            Check(Native.mpv_initialize(_handle));
            _events = Task.Run(EventLoop);
        }
        catch { Native.mpv_terminate_destroy(_handle); _handle = IntPtr.Zero; throw; }
    }
    public async Task PlayAsync(Uri uri, IntPtr hwnd, int volume, CancellationToken ct, int bufferMilliseconds = 1200, string userAgent = "", string referer = "")
    {
        await RunAsync(() =>
        {
            if (userAgent.Contains('\r') || userAgent.Contains('\n') || referer.Contains('\r') || referer.Contains('\n'))
                throw new System.IO.InvalidDataException("Invalid stream headers.");
            Initialize(hwnd);
            Set("user-agent", userAgent.Length == 0 ? "HN-IPTV" : userAgent);
            Set("referrer", referer);
            Set("volume", volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var seconds = (Math.Clamp(bufferMilliseconds, 250, 10000) / 1000d).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Set("cache-pause-wait", seconds);
            Set("demuxer-readahead-secs", seconds);
            Set("pause", "no"); _paused = false; _playing = false;
        }, ct).ConfigureAwait(false);
        await CommandAsync(ct, "loadfile", uri.AbsoluteUri, "replace").ConfigureAwait(false);
    }
    public async Task StopAsync()
    {
        if (_handle == IntPtr.Zero) return;
        await CommandAsync(default, "stop").ConfigureAwait(false);
        _playing = false;
    }
    public Task PauseAsync() => RunAsync(() => { if (_handle != IntPtr.Zero) { _paused = !_paused; Set("pause", _paused ? "yes" : "no"); } });
    internal Task TranslationBufferAsync(bool enabled)=>RunAsync(()=>{if(_handle!=IntPtr.Zero)Set("demuxer-lavf-o",enabled?"live_start_index=0":"");});
    public Task CaptionsAsync(bool enabled, CancellationToken ct = default) => RunAsync(() => {
        if (_handle != IntPtr.Zero) {
            if (enabled) Set("sid", "auto");
            Set("sub-visibility", enabled ? "yes" : "no");
            Set("secondary-sub-visibility", "no");
        }
        _captionsEnabled = enabled;
    }, ct);
    internal async Task<(bool Visible, string Text)> ReadCaptionsAsync()
    {
        (bool Visible, string Text) result = default;
        await RunAsync(() => {
            if (_handle == IntPtr.Zero) return;
            int visible = 0;
            Check(Native.mpv_get_property_flag(_handle, "sub-visibility", 3, ref visible));
            result = (visible != 0, Text("sub-text"));
        }).ConfigureAwait(false);
        return result;
    }
    public Task VolumeAsync(int volume) => RunAsync(() => { if (_handle != IntPtr.Zero) Set("volume", volume.ToString(System.Globalization.CultureInfo.InvariantCulture)); });
    internal async Task<(bool Playing, long Time)> ProgressAsync()
    {
        (bool Playing, long Time) result = default;
        await RunAsync(() =>
        {
            if (_handle == IntPtr.Zero) return;
            double position = 0;
            int status = Native.mpv_get_property(_handle, "time-pos", 5, ref position);
            result = (_playing && !_paused && CoreActive() && status >= 0 && position > 0, status >= 0 ? (long)(position * 1000) : -1);
        }).ConfigureAwait(false);
        return result;
    }
    internal async Task<StreamInfo?> ReadInfoAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try { return await Task.Run(() => {
            if (_disposed || _handle == IntPtr.Zero) return StreamInfo.Empty;
            double fps = Number("estimated-vf-fps");
            if (!double.IsFinite(fps) || fps <= 0) fps = Number("container-fps");
            double rate = Number("cache-speed");
            double position = Number("time-pos");
            bool active = _playing && !_paused && CoreActive() && position > 0;
            return new StreamInfo { Width = Integer("video-params/w"), Height = Integer("video-params/h"),
                DisplayAspect = Number("video-params/aspect"), FramesPerSecond = fps,
                VideoCodec = Text("video-codec"), AudioCodec = Text("audio-codec-name"),
                AudioChannels = Integer("audio-params/channel-count"), AudioSampleRate = Integer("audio-params/samplerate"),
                InputBitsPerSecond = double.IsFinite(rate) && rate >= 0 ? rate * 8 : null,
                PositionMilliseconds = double.IsFinite(position) ? (long)(position * 1000) : -1,
                Playing = active, State = _paused ? "Paused" : active ? "Playing" : _playing ? "Buffering" : "Stopped" };
        }, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private bool CoreActive()
    {
        // mpv flag properties do not convert to DOUBLE. Use MPV_FORMAT_FLAG (3).
        int idle = 1;
        return Native.mpv_get_property_flag(_handle, "core-idle", 3, ref idle) >= 0 && idle == 0;
    }
    private double Number(string name)
    {
        double value = 0;
        return Native.mpv_get_property(_handle, name, 5, ref value) >= 0 ? value : double.NaN;
    }
    private int Integer(string name) { var n = Number(name); return double.IsFinite(n) && n > 0 && n < int.MaxValue ? (int)n : 0; }
    private string Text(string name)
    {
        var pointer = Native.mpv_get_property_string(_handle, name);
        if (pointer == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(pointer) ?? ""; } finally { Native.mpv_free(pointer); }
    }
    private async Task CommandAsync(CancellationToken ct, params string[] args)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = (ulong)Interlocked.Increment(ref _nextCommand);
        await RunAsync(() =>
        {
            _commands[id] = completion;
            var strings = args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            var vector = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
            try
            {
                for (int i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(vector, i * IntPtr.Size, strings[i]);
                Marshal.WriteIntPtr(vector, strings.Length * IntPtr.Size, IntPtr.Zero);
                int error = Native.mpv_command_async(_handle, id, vector);
                if (error < 0) { _commands.TryRemove(id, out _); Check(error); }
            }
            finally { foreach (var item in strings) Marshal.FreeCoTaskMem(item); Marshal.FreeHGlobal(vector); }
        }, ct).ConfigureAwait(false);
        await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }
    private void EventLoop()
    {
        try
        {
            while (!_eventStop.IsCancellationRequested)
            {
                var pointer = Native.mpv_wait_event(_handle, 0.1);
                if (pointer == IntPtr.Zero) continue;
                var entry = Marshal.PtrToStructure<MpvEvent>(pointer);
                if (entry.Id == 5 && _commands.TryRemove(entry.UserData, out var completion))
                {
                    if (entry.Error < 0) completion.TrySetException(new InvalidOperationException($"mpv command failed ({entry.Error})."));
                    else completion.TrySetResult();
                }
                else if (entry.Id == 8) _playing = true; // FILE_LOADED
                else if (entry.Id == 7) // END_FILE; the first two fields are reason/error.
                {
                    _playing = false;
                    if (entry.Data != IntPtr.Zero && Marshal.ReadInt32(entry.Data) == 4) PlaybackError?.Invoke();
                }
                else if (entry.Id == 1) break;
            }
        }
        catch (Exception ex) { App.LogException("mpv.events", ex); }
        finally { foreach (var item in _commands.Values) item.TrySetCanceled(); _commands.Clear(); }
    }
    public async Task ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _eventStop.Cancel();
            await _events.ConfigureAwait(false);
            if (_handle != IntPtr.Zero) await Task.Run(() => Native.mpv_terminate_destroy(_handle)).ConfigureAwait(false);
            _handle = IntPtr.Zero;
        }
        finally { _gate.Release(); }
    }

    // Obtain only the decoded video and native captions, excluding controls and translated overlays.
    internal async Task<byte[]?> CaptureVideoFrameAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try { return await Task.Run(() =>
        {
            if (_disposed || _handle == IntPtr.Zero || !_playing) return null;
            var args = new[] { "screenshot-raw", "subtitles", "bgra" }.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            var vector = Marshal.AllocHGlobal((args.Length + 1) * IntPtr.Size);
            MpvNode result = default;
            try
            {
                for (int i = 0; i < args.Length; i++) Marshal.WriteIntPtr(vector, i * IntPtr.Size, args[i]);
                Marshal.WriteIntPtr(vector, args.Length * IntPtr.Size, IntPtr.Zero);
                Check(Native.mpv_command_ret(_handle, vector, out result));
                if (result.Format != 8 || result.Value == IntPtr.Zero) return null;
                var map = Marshal.PtrToStructure<MpvNodeList>(result.Value);
                int width = 0, height = 0, stride = 0; IntPtr pixels = IntPtr.Zero; long bytes = 0;
                for (int i = 0; i < map.Count; i++)
                {
                    string name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(map.Keys, i * IntPtr.Size)) ?? "";
                    var node = Marshal.PtrToStructure<MpvNode>(map.Values + i * Marshal.SizeOf<MpvNode>());
                    if (name == "w") width = checked((int)node.Value.ToInt64());
                    else if (name == "h") height = checked((int)node.Value.ToInt64());
                    else if (name == "stride") stride = checked((int)node.Value.ToInt64());
                    else if (name == "data" && node.Format == 9) {
                        var array = Marshal.PtrToStructure<MpvByteArray>(node.Value); pixels = array.Data; bytes = checked((long)array.Size);
                    }
                }
                // mpv can return bottom-up frames; preserve small text at up to 1080p.
                var data = VideoFrameCopy.Bgra(pixels, width, height, stride, bytes);
                if (data == null) return null;
                stride = width * 4;
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, data, stride);
                bitmap.Freeze();
                double scale = Math.Min(1, Math.Min(1920d / width, 1080d / height));
                if (scale < 1) {
                    var scaled = new System.Windows.Media.Imaging.TransformedBitmap(bitmap,
                        new System.Windows.Media.ScaleTransform(scale, scale));
                    scaled.Freeze(); bitmap = scaled;
                }
                using var output = new System.IO.MemoryStream();
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); encoder.Save(output);
                return output.ToArray();
            }
            finally {
                Native.mpv_free_node_contents(ref result);
                Marshal.FreeHGlobal(vector); foreach (var arg in args) Marshal.FreeCoTaskMem(arg);
            }
        }, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MpvNode { public IntPtr Value; public int Format; }
    [StructLayout(LayoutKind.Sequential)] private struct MpvNodeList { public int Count; public IntPtr Values, Keys; }
    [StructLayout(LayoutKind.Sequential)] private struct MpvByteArray { public IntPtr Data; public nuint Size; }

    private void Set(string property, string value) => Check(Native.mpv_set_property_string(_handle, property, value));
    private static void Check(int code) { if (code < 0) throw new InvalidOperationException($"mpv operation failed ({code})."); }
    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent { public int Id, Error; public ulong UserData; public IntPtr Data; }
    private static class Native
    {
        private const string Library = "libmpv-2.dll";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command_ret(IntPtr handle, IntPtr args, out MpvNode result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free_node_contents(ref MpvNode node);
        [DllImport(Library, EntryPoint = "mpv_get_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property_flag(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_get_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free(IntPtr pointer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_create();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_initialize(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_terminate_destroy(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_wait_event(IntPtr handle, double timeout);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command_async(IntPtr handle, ulong id, IntPtr args);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_option_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_option(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref long value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_get_property(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref double value);
    }
}
