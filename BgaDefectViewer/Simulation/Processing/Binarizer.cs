using System.Windows;
using System.Windows.Media.Imaging;

namespace BgaDefectViewer.Simulation.Processing;

/// <summary>
/// KBGA-style simple-threshold binarization. <c>src</c> is a BGRA32 grayscale
/// (B=G=R) WriteableBitmap; <c>dst</c> must be the same size and format. Every
/// pixel ≥ threshold becomes white (255), everything else black (0). Mirrors
/// what KBGA does with <c>BINLEVEL[]</c> in <c>ClassDeviceKensa.BinLv</c>.
/// </summary>
public static class Binarizer
{
    public static unsafe void ApplyThreshold(WriteableBitmap src, WriteableBitmap dst, byte threshold)
    {
        if (src.PixelWidth != dst.PixelWidth || src.PixelHeight != dst.PixelHeight)
            throw new ArgumentException("Source and destination bitmaps must be the same size.");
        if (src.BackBufferStride != dst.BackBufferStride)
            throw new ArgumentException("Source and destination bitmaps must share stride.");

        int w = src.PixelWidth;
        int h = src.PixelHeight;
        int stridePixels = src.BackBufferStride / 4;
        uint* sp = (uint*)src.BackBuffer;

        dst.Lock();
        try
        {
            uint* dp = (uint*)dst.BackBuffer;
            // Process row-by-row in case strides ever diverge (defensive).
            for (int y = 0; y < h; y++)
            {
                uint* sRow = sp + y * stridePixels;
                uint* dRow = dp + y * stridePixels;
                for (int x = 0; x < w; x++)
                {
                    byte b = (byte)(sRow[x] & 0xFFu);
                    dRow[x] = b >= threshold ? 0xFFFFFFFFu : 0xFF000000u;
                }
            }
            dst.AddDirtyRect(new Int32Rect(0, 0, w, h));
        }
        finally
        {
            dst.Unlock();
        }
    }
}
