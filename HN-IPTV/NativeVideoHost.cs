using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;

namespace HnIptv;

// WPF owns a container HWND; the playback engine owns a stable child target.
// PiP reparents that target, never the HwndHost or the engine's output window.
public sealed class NativeVideoHost : HwndHost
{
    public IntPtr VideoHandle { get; private set; }
    public bool CreateVideoTarget { get; init; } = true;
    private IntPtr _container, _attached;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _container = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x04000000 | 0x02000000 | 0x00000004,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_container == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (CreateVideoTarget)
        {
            VideoHandle = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x04000000 | 0x02000000 | 0x00000004,
                0, 0, 1, 1, _container, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (VideoHandle == IntPtr.Zero) { DestroyWindow(_container); _container = IntPtr.Zero; throw new Win32Exception(); }
            _attached = VideoHandle;
        }
        return new HandleRef(this, _container);
    }
    internal void TransferTo(NativeVideoHost destination)
    {
        VerifyAccess();
        if (_attached == IntPtr.Zero || destination._container == IntPtr.Zero)
            throw new InvalidOperationException("Video window is not ready.");
        if (destination._attached != IntPtr.Zero) throw new InvalidOperationException("Video window is occupied.");
        Marshal.SetLastPInvokeError(0);
        var previous = SetParent(_attached, destination._container);
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception();
        destination._attached = _attached;
        _attached = IntPtr.Zero;
        destination.SizeVideo();
    }
    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        SizeVideo();
    }
    private void SizeVideo()
    {
        if (_attached != IntPtr.Zero && GetClientRect(_container, out var rect))
            SetWindowPos(_attached, IntPtr.Zero, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), 0x0004 | 0x0010);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        _container = _attached = VideoHandle = IntPtr.Zero;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
