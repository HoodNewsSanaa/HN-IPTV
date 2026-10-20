using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
namespace HnIptv;

// Windows application loopback, never endpoint/system-wide loopback.
// Format matches Microsoft's ApplicationLoopback sample: 44.1 kHz stereo PCM16.
internal sealed class ProcessAudioCapture : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _chunks = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1) {
        FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private Task _capture = Task.CompletedTask;
    internal ChannelReader<byte[]> Chunks => _chunks.Reader;
    internal Task StartAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            throw new PlatformNotSupportedException("Speech translation requires Windows 11 or Windows build 20348 and newer.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture = Task.Run(() => Run(ready));
        return ready.Task;
    }
    private void Run(TaskCompletionSource ready)
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        try
        {
            using var callback = new Completion();
            Marshal.WriteInt32(callback.Parameters, 0, 1); // PROCESS_LOOPBACK
            Marshal.WriteInt32(callback.Parameters, 4, Environment.ProcessId);
            Marshal.WriteInt32(callback.Parameters, 8, 0); // INCLUDE_TARGET_PROCESS_TREE
            var variant = new PropVariant { Type = 65, Size = 12, Data = callback.Parameters };
            Marshal.StructureToPtr(variant, callback.Variant, false);
            Guid iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, callback.Variant, callback.Pointer, out var operation));
            try { client = callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(10), _stop.Token).GetAwaiter().GetResult(); }
            finally { Marshal.Release(operation); GC.KeepAlive(callback); }
            var format = new WaveFormat { Tag = 1, Channels = 2, Rate = 44100, BytesPerSecond = 176400, Alignment = 4, Bits = 16 };
            // Shared loopback with PCM conversion; poll frequently instead of retaining native event callbacks.
            client.Initialize(0, 0x00020000 | 0x80000000, 0, 0, ref format, IntPtr.Zero);
            Guid service = typeof(IAudioCaptureClient).GUID;
            client.GetService(ref service, out var raw);
            capture = (IAudioCaptureClient)raw;
            client.Start();
            ready.TrySetResult();
            using var samples = new MemoryStream();
            byte[] previous = [];
            while (!_stop.IsCancellationRequested)
            {
                capture.GetNextPacketSize(out uint frames);
                while (frames > 0)
                {
                    capture.GetBuffer(out var data, out frames, out uint flags, out _, out _);
                    try
                    {
                        int size = checked((int)frames * 4);
                        var bytes = new byte[size];
                        if ((flags & 2) == 0 && data != IntPtr.Zero) Marshal.Copy(data, bytes, 0, size);
                        samples.Write(bytes);
                    }
                    finally { capture.ReleaseBuffer(frames); }
                    capture.GetNextPacketSize(out frames);
                }
                if (samples.Length >= 176400 * 5)
                {
                    var current = samples.ToArray();
                    var segment = new byte[previous.Length + current.Length];
                    previous.CopyTo(segment, 0); current.CopyTo(segment, previous.Length);
                    _chunks.Writer.TryWrite(ToWave(segment));
                    previous = current.TakeLast(176400).ToArray(); // one second of context, bounded
                    samples.SetLength(0);
                }
                _stop.Token.WaitHandle.WaitOne(25);
            }
        }
        catch (OperationCanceledException) { ready.TrySetCanceled(); }
        catch (Exception ex) { ready.TrySetException(ex); _chunks.Writer.TryComplete(ex); }
        finally
        {
            if (client != null) { try { client.Stop(); } catch { } }
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
            _chunks.Writer.TryComplete();
        }
    }
    internal static byte[] ToWave(byte[] pcm)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8); writer.Write(pcm.Length + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(44100);
        writer.Write(176400); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8);
        writer.Write(pcm.Length); writer.Write(pcm); return output.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _capture.WaitAsync(TimeSpan.FromSeconds(12)); } catch { }
        // Do not dispose a token wait handle if activation is still finishing.
        if (_capture.IsCompleted) _stop.Dispose();
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public int Size; [FieldOffset(16)] public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat {
        public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Alignment, Bits, Extra;
    }
    // A small native CCW exposes the agile marker explicitly. A normal .NET
    // CCW does not reliably expose IAgileObject on every supported CLR.
    private sealed class Completion : IDisposable
    {
        internal TaskCompletionSource<IAudioClient> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IntPtr Pointer { get; }
        internal IntPtr Parameters { get; } = Marshal.AllocHGlobal(12);
        internal IntPtr Variant { get; } = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        private int _references = 1;
        private static readonly QueryInterfaceDelegate Query = QueryInterface;
        private static readonly ReferenceDelegate Add = AddReference, Release = ReleaseReference;
        private static readonly CompletionDelegate Completed = ActivateCompleted;
        private static readonly IntPtr Vtable = CreateVtable();
        internal Completion()
        {
            Pointer = Marshal.AllocHGlobal(2 * IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, Vtable);
            Marshal.WriteIntPtr(Pointer, IntPtr.Size, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
        }
        private static IntPtr CreateVtable()
        {
            var table = Marshal.AllocHGlobal(4 * IntPtr.Size);
            Marshal.WriteIntPtr(table, 0, Marshal.GetFunctionPointerForDelegate(Query));
            Marshal.WriteIntPtr(table, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(Add));
            Marshal.WriteIntPtr(table, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(Release));
            Marshal.WriteIntPtr(table, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(Completed));
            return table;
        }
        private static Completion Instance(IntPtr pointer) =>
            (Completion)GCHandle.FromIntPtr(Marshal.ReadIntPtr(pointer, IntPtr.Size)).Target!;
        private static int QueryInterface(IntPtr self, ref Guid iid, out IntPtr value)
        {
            if (iid == new Guid("00000000-0000-0000-C000-000000000046") ||
                iid == new Guid("41D949AB-9862-444A-80F6-C261334DA5EB") ||
                iid == new Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"))
            { value = self; AddReference(self); return 0; }
            value = IntPtr.Zero; return unchecked((int)0x80004002);
        }
        private static uint AddReference(IntPtr self) => (uint)Interlocked.Increment(ref Instance(self)._references);
        private static uint ReleaseReference(IntPtr self)
        {
            var instance = Instance(self);
            int count = Interlocked.Decrement(ref instance._references);
            if (count == 0) {
                GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Free();
                Marshal.FreeHGlobal(instance.Parameters); Marshal.FreeHGlobal(instance.Variant);
                Marshal.FreeHGlobal(self);
            }
            return (uint)count;
        }
        private static int ActivateCompleted(IntPtr self, IntPtr operation)
        {
            var completion = Instance(self);
            try {
                var function = Marshal.ReadIntPtr(Marshal.ReadIntPtr(operation), 3 * IntPtr.Size);
                var getResult = Marshal.GetDelegateForFunctionPointer<GetResultDelegate>(function);
                Marshal.ThrowExceptionForHR(getResult(operation, out int result, out var value));
                try {
                    Marshal.ThrowExceptionForHR(result);
                    var client = (IAudioClient)Marshal.GetObjectForIUnknown(value);
                    if (!completion.Result.TrySetResult(client)) Marshal.ReleaseComObject(client);
                }
                finally { if (value != IntPtr.Zero) Marshal.Release(value); }
            }
            catch (Exception ex) { completion.Result.TrySetException(ex); }
            return 0;
        }
        public void Dispose() { Result.TrySetCanceled(); ReleaseReference(Pointer); }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid iid, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReferenceDelegate(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CompletionDelegate(IntPtr self, IntPtr operation);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetResultDelegate(IntPtr self, out int result, out IntPtr value);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient {
        void Initialize(int shareMode, uint flags, long duration, long periodicity, ref WaveFormat format, IntPtr session);
        void GetBufferSize(out uint frames); void GetStreamLatency(out long value); void GetCurrentPadding(out uint frames);
        void IsFormatSupported(int mode, IntPtr format, out IntPtr closest); void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long normal, out long minimum); void Start(); void Stop(); void Reset();
        void SetEventHandle(IntPtr handle); void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong timestamp);
        void ReleaseBuffer(uint frames); void GetNextPacketSize(out uint frames);
    }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, IntPtr parameters,
        IntPtr completion, out IntPtr operation);
}
