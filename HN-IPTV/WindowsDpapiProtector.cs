using System.ComponentModel;
using System.Runtime.InteropServices;
using HnIptv.Services;

namespace HnIptv;

// DPAPI is Windows-only and tied to the current Windows user. The portable Core
// depends only on IPlaylistProtector. This data cannot simply be copied to Android.
internal sealed class WindowsDpapiProtector : IPlaylistProtector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public byte[] Protect(byte[] data) => ConvertData(data, true);
    public byte[] Unprotect(byte[] data) => ConvertData(data, false);

    private static byte[] ConvertData(byte[] data, bool encrypt)
    {
        var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            DataBlob encrypted;
            bool result = encrypt
                ? CryptProtectData(ref input, "HN-IPTV saved playlists", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out encrypted)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out encrypted);
            if (!result) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Windows-protected playlist library.");
            try
            {
                var output = new byte[encrypted.Length];
                Marshal.Copy(encrypted.Data, output, 0, encrypted.Length);
                return output;
            }
            finally { LocalFree(encrypted.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
