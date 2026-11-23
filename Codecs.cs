using System;
using NetVips;
using VipsImage = NetVips.Image;

namespace LetterboxImageCropper;

/// <summary>Decoded 8-bit pixels: <see cref="Bands"/> bytes per pixel, row-major, no padding.</summary>
/// <param name="ColorBands">Leading bands that carry colour (1 = grey, 3 = RGB); any further band is alpha.</param>
public sealed record PixelBuffer(byte[] Data, int Width, int Height, int Bands, int ColorBands);

/// <summary>
/// All image I/O goes through libvips (NetVips). Only the JPEG and PNG loaders are enabled; every other libvips
/// loader is blocked, so no other codec can run - not even for a file whose extension lies about its content.
/// </summary>
public static class Codecs
{
    public const string JpegLoader = "VipsForeignLoadJpegFile";
    public const string PngLoader = "VipsForeignLoadPngFile";

    private static readonly object InitLock = new();
    private static bool _initialized;

    /// <summary>Configures libvips once. Throws when the native library cannot be loaded.</summary>
    public static void Initialize()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            if (!ModuleInitializer.VipsInitialized)
                throw new InvalidOperationException("The libvips native library could not be loaded.", ModuleInitializer.Exception);

            // Block every loader, then re-enable only JPEG and PNG (child classes inherit the block state).
            Operation.Block("VipsForeignLoad", true);
            Operation.Block("VipsForeignLoadJpeg", false);
            Operation.Block("VipsForeignLoadPng", false);
            NetVips.NetVips.BlockUntrusted = true;

            // No operation cache: nothing keeps source files open (they can be overwritten/moved) and memory stays flat.
            Cache.Max = 0;
            _initialized = true;
        }
    }

    /// <summary>"JPEG", "PNG" or null when the file is not a supported image.</summary>
    public static string? FormatOf(string path)
    {
        Initialize();
        string? format = VipsImage.FindLoad(path) switch
        {
            JpegLoader => "JPEG",
            PngLoader => "PNG",
            _ => null,
        };
        // A failed lookup leaves a message in libvips' global error buffer, which would otherwise be
        // prepended to the next, unrelated error.
        if (format is null) vips_error_clear();
        return format;
    }

    /// <summary>One-line, user-readable message for an image I/O error.</summary>
    public static string ErrorMessage(Exception ex)
    {
        if (ex is not VipsException) return ex.Message;
        // libvips lists errors from generic to specific; the last line says what is actually wrong.
        string[] lines = ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        vips_error_clear();
        return lines.Length > 0 ? $"Damaged or unreadable image ({lines[^1]})" : "Damaged or unreadable image";
    }

    [System.Runtime.InteropServices.DllImport("libvips-42.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void vips_error_clear();

    /// <summary>Opens a JPEG or PNG lazily (pixels are decoded when they are first needed).</summary>
    public static VipsImage Load(string path, Enums.Access access = Enums.Access.Sequential)
    {
        if (FormatOf(path) is null)
            throw new NotSupportedException("Not a JPEG or PNG image.");
        // Damaged/truncated files fail (and are listed as unreadable) instead of being silently padded with grey.
        return VipsImage.NewFromFile(path, access: access, failOn: Enums.FailOn.Truncated);
    }

    /// <summary>Decodes a whole file to 8-bit grey/RGB (+ alpha) pixels.</summary>
    public static PixelBuffer Decode(string path)
    {
        using VipsImage image = Load(path);
        return ToPixels(image);
    }

    /// <summary>Converts any supported image (16-bit, CMYK, grey, palette...) to 8-bit grey or sRGB and copies the pixels.</summary>
    public static PixelBuffer ToPixels(VipsImage image)
    {
        using VipsImage eightBit = To8Bit(image);
        byte[] data = eightBit.WriteToMemory<byte>();
        int bands = eightBit.Bands;
        return new PixelBuffer(data, eightBit.Width, eightBit.Height, bands, bands >= 3 ? 3 : 1);
    }

    private static VipsImage To8Bit(VipsImage image)
    {
        if (image.Interpretation == Enums.Interpretation.Cmyk && image.Bands >= 4 && image.Format == Enums.BandFormat.Uchar)
            return CmykToRgbPlain(image);

        bool grey = image.Interpretation is Enums.Interpretation.Bw or Enums.Interpretation.Grey16;
        bool standard = image.Format == Enums.BandFormat.Uchar &&
                        image.Interpretation is Enums.Interpretation.Srgb or Enums.Interpretation.Bw;
        if (standard) return image.Copy();
        return image.Colourspace(grey ? Enums.Interpretation.Bw : Enums.Interpretation.Srgb);
    }

    /// <summary>
    /// Plain (not colour-managed) CMYK -> RGB: R = (255 - C) * (255 - K) / 255, etc.
    /// libvips' colour-managed conversion uses a print profile in which full black becomes a dark grey
    /// (around 40), which would hide black bars from detection. Only used for analysis and preview -
    /// exported files keep their original CMYK data.
    /// </summary>
    private static VipsImage CmykToRgbPlain(VipsImage image)
    {
        using VipsImage cmy = image.ExtractBand(0, n: 3);
        using VipsImage k = image.ExtractBand(3);
        using VipsImage rgbInverse = 255 - cmy;
        using VipsImage kInverse = 255 - k;
        using VipsImage product = rgbInverse * kInverse;
        using VipsImage scaled = product / 255;
        using VipsImage rgb = scaled.Cast(Enums.BandFormat.Uchar);
        return rgb.Copy(interpretation: Enums.Interpretation.Srgb);
    }
}
