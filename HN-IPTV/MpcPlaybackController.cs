using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using HnIptv.Playback;

namespace HnIptv;

// Experimental, isolated MPC-HC slave process. It never changes an installed
// MPC-HC profile: prepare-mpc.ps1 supplies a private portable INI beside this EXE.
internal sealed class PlaybackController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource<IntPtr> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private HwndSource? _messages;
    private Dispatcher? _messageDispatcher;
    private IntPtr _receiver, _window, _target;
    private bool _disposed;
    private volatile int _playState = 2, _loadState;
    private long _position = -1;
    public event Action? PlaybackError;

    private async Task RunAsync(Action action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await Task.Run(() => { ct.ThrowIfCancellationRequested(); if (!_disposed) action(); }, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task InitializeAsync(IntPtr target, CancellationToken ct)
    {
        if (_process != null) return;
        _target = target;
        if (target == IntPtr.Zero) throw new InvalidOperationException("Video surface is not ready.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try {
                _messageDispatcher = Dispatcher.CurrentDispatcher;
                _messages = new HwndSource(new HwndSourceParameters("HN-IPTV MPC notifications") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
                _messages.AddHook(Message); _receiver = _messages.Handle; ready.TrySetResult();
                Dispatcher.Run(); _messages.Dispose();
            } catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "MPC notifications" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "mpc-hc", "mpc-hc64.exe")) {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "mpc-hc") };
        foreach (var argument in new[] { "/new", "/slave", _receiver.ToInt64().ToString(CultureInfo.InvariantCulture), "/viewpreset", "1" }) start.ArgumentList.Add(argument);
        _process = Process.Start(start) ?? throw new InvalidOperationException("MPC-HC could not start.");
        _window = await _connected.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        await Task.Run(() => {
            if (GetWindowThreadProcessId(_window, out var processId) == 0 || processId != _process.Id)
                throw new InvalidOperationException("MPC-HC handshake process did not match.");
            long style = GetWindowLongPtr(_window, -16).ToInt64();
            SetWindowLongPtr(_window, -16, new IntPtr((style & ~0x80CF0000L) | 0x50000000L));
            SetMenu(_window, IntPtr.Zero); SetParent(_window, target); Resize();
        }, ct).ConfigureAwait(false);
    }
    public async Task PlayAsync(Uri uri, IntPtr hwnd, int volume, CancellationToken ct, int bufferMilliseconds = 1200)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await InitializeAsync(hwnd, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            _playState = 2; Interlocked.Exchange(ref _position, -1);
            await Task.Run(() => { Send(0xA0003010, volume.ToString(CultureInfo.InvariantCulture)); Send(0xA0000000, uri.AbsoluteUri); Resize(); }, ct).ConfigureAwait(false);
        } finally { _gate.Release(); }
    }
    public Task StopAsync() => RunAsync(() => { if (_window != IntPtr.Zero) Send(0xA0000002); _playState = 2; Interlocked.Exchange(ref _position, -1); });
    public Task PauseAsync() => RunAsync(() => { if (_window != IntPtr.Zero) Send(0xA0000003); });
    public Task VolumeAsync(int volume) => RunAsync(() => { if (_window != IntPtr.Zero) Send(0xA0003010, volume.ToString(CultureInfo.InvariantCulture)); });
    internal async Task<(bool Playing, long Time)> ProgressAsync()
    {
        await RunAsync(() => { if (_window != IntPtr.Zero) { Send(0xA0003004); Resize(); } }).ConfigureAwait(false);
        long time = Interlocked.Read(ref _position);
        return (_loadState == 2 && _playState == 0 && time > 0, time);
    }
    internal async Task<StreamInfo?> ReadInfoAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try { return await Task.Run(() => {
            if (_window == IntPtr.Zero || _disposed) return StreamInfo.Empty;
            Send(0xA0003004); Resize();
            long time = Interlocked.Read(ref _position);
            return new StreamInfo { PositionMilliseconds = time, Playing = _loadState == 2 && _playState == 0 && time > 0,
                State = _playState == 1 ? "Paused" : _loadState == 2 && _playState == 0 ? "Playing" : "Buffering" };
            // Slave API does not expose trustworthy resolution/FPS/bitrate/codec fields.
        }, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    internal (long PrivateBytes, long WorkingSet, double CpuMilliseconds) ChildMetrics()
    {
        if (_process == null || _process.HasExited) return default;
        _process.Refresh(); return (_process.PrivateMemorySize64, _process.WorkingSet64, _process.TotalProcessorTime.TotalMilliseconds);
    }
    private void Resize()
    {
        if (_window != IntPtr.Zero && GetClientRect(_target, out var rect))
            SetWindowPos(_window, IntPtr.Zero, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), 0x0034);
    }
    private IntPtr Message(IntPtr hwnd, int message, IntPtr sender, IntPtr data, ref bool handled)
    {
        if (message != 0x004A || data == IntPtr.Zero) return IntPtr.Zero;
        var entry = Marshal.PtrToStructure<CopyData>(data);
        if (entry.Length <= 0 || entry.Length > 65536 || entry.Text == IntPtr.Zero) return IntPtr.Zero;
        string value = Marshal.PtrToStringUni(entry.Text, entry.Length / 2)?.TrimEnd('\0') ?? "";
        uint command = unchecked((uint)entry.Command.ToInt64());
        if (command == 0x50000000 && long.TryParse(value, out var window)) _connected.TrySetResult(new IntPtr(window));
        else if (_window != IntPtr.Zero && sender == _window) {
            if (command == 0x50000001 && int.TryParse(value, out var load)) { _loadState = load; if (load == 4) PlaybackError?.Invoke(); }
            else if (command == 0x50000002 && int.TryParse(value, out var state)) _playState = state;
            else if (command == 0x50000007 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                Interlocked.Exchange(ref _position, (long)(seconds * 1000));
        }
        handled = true; return new IntPtr(1);
    }
    private void Send(uint command, string value = "")
    {
        var text = Marshal.StringToHGlobalUni(value);
        try {
            var copy = new CopyData { Command = new IntPtr(command), Length = (value.Length + 1) * 2, Text = text };
            if (SendMessageTimeout(_window, 0x004A, _receiver, ref copy, 0x0002, 5000, out _) == IntPtr.Zero)
                throw new TimeoutException("MPC-HC did not answer its control message.");
        } finally { Marshal.FreeHGlobal(text); }
    }
    public async Task ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            if (_disposed) return; _disposed = true;
            if (_window != IntPtr.Zero) { try { await Task.Run(() => Send(0xA0004006)).ConfigureAwait(false); } catch (TimeoutException) { } }
            if (_process != null) {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { _process.Kill(true); }
                _process.Dispose();
            }
            _messageDispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
            _window = IntPtr.Zero;
        } finally { _gate.Release(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct CopyData { public IntPtr Command; public int Length; public IntPtr Text; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr sender, ref CopyData data, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetMenu(IntPtr hwnd, IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
