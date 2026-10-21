using System.Runtime.InteropServices;
namespace HnIptv;
internal static class VideoFrameCopy
{
    internal static byte[]? Bgra(IntPtr pixels, int width, int height, int stride, long bytes)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || pixels == IntPtr.Zero) return null;
        int rowBytes = width * 4;
        long pitch = Math.Abs((long)stride);
        if (pitch < rowBytes || pitch * height > 128L * 1024 * 1024 ||
            bytes < pitch * (height - 1) + rowBytes) return null;
        var data = new byte[checked(rowBytes * height)];
        for (int row = 0; row < height; row++)
            Marshal.Copy(pixels + checked(row * stride), data, row * rowBytes, rowBytes);
        return data;
    }
}
