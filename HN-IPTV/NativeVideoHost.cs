using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HnIptv;

// A single child HWND. No overlay window, LayoutUpdated handler, or minimize-time
// detach/recreate. WPF sizes the child; VLC retains it for the lifetime of playback.
public sealed class NativeVideoHost : HwndHost
{
    public IntPtr VideoHandle { get; private set; }
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        VideoHandle = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x04000000 | 0x02000000 | 0x00000004,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (VideoHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this, VideoHandle);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        VideoHandle = IntPtr.Zero;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
