using System;
using System.Drawing;
using System.Drawing.Imaging;
using NetVips;
using VipsImage = NetVips.Image;

namespace LetterboxImageCropper;

/// <summary>A GDI+ bitmap for on-screen preview, plus the size of the original image it represents.</summary>
public sealed class PreviewImage : IDisposable
{
    /// <summary>Very large images are downscaled for display only (export always uses the original).</summary>
    private const int MaxPreviewDimension = 4096;

    public Bitmap Bitmap { get; }
    public int SourceWidth { get; }
    public int SourceHeight { get; }

    private PreviewImage(Bitmap bitmap, int sourceWidth, int sourceHeight)
    {
        Bitmap = bitmap;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    public static unsafe PreviewImage Load(string path)
    {
        PixelBuffer pixels;
        int sourceWidth, sourceHeight;
        using (VipsImage image = Codecs.Load(path))
        {
            sourceWidth = image.Width;
            sourceHeight = image.Height;
            if (Math.Max(sourceWidth, sourceHeight) > MaxPreviewDimension)
            {
                using VipsImage small = image.ThumbnailImage(MaxPreviewDimension, height: MaxPreviewDimension, size: Enums.Size.Down, noRotate: true);
                pixels = Codecs.ToPixels(small);
            }
            else
            {
                pixels = Codecs.ToPixels(image);
            }
        }

        int width = pixels.Width, height = pixels.Height, bands = pixels.Bands;
        bool hasAlpha = bands is 2 or 4;
        byte[] src = pixels.Data;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Format32bppArgb is stored as B,G,R,A bytes.
            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
                int s = y * width * bands;
                for (int x = 0; x < width; x++, s += bands)
                {
                    byte r = src[s], g = r, b = r, a = 255;
                    if (bands >= 3) { g = src[s + 1]; b = src[s + 2]; }
                    if (hasAlpha) a = src[s + bands - 1];
                    row[x * 4] = b;
                    row[x * 4 + 1] = g;
                    row[x * 4 + 2] = r;
                    row[x * 4 + 3] = a;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new PreviewImage(bitmap, sourceWidth, sourceHeight);
    }

    public void Dispose() => Bitmap.Dispose();
}
