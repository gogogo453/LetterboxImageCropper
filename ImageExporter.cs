using System;
using System.IO;
using NetVips;
using VipsImage = NetVips.Image;

namespace LetterboxImageCropper;

public enum OutputFormat
{
    /// <summary>Same format as each source file; JPEGs reuse their own estimated quality and chroma subsampling.</summary>
    SameAsSource,
    Jpeg,
    Png,
}

public sealed record ExportOptions(OutputFormat Format, int JpegQuality, bool JpegChroma444);

public static class ImageExporter
{
    /// <summary>Used only when a source JPEG's quality cannot be estimated from its header.</summary>
    public const int FallbackJpegQuality = 95;

    /// <summary>
    /// PNG settings: zlib level 6 with adaptive row filters. libvips' default (no filter) produces files roughly
    /// 40 % larger than the sources; adaptive filtering keeps them at about the original size.
    /// </summary>
    private const int PngCompression = 6;

    /// <summary>Output file name (extension changes only when converting to another format).</summary>
    public static string TargetFileName(string sourcePath, ExportOptions options) => options.Format switch
    {
        OutputFormat.Jpeg when !IsJpegExtension(sourcePath) => Path.ChangeExtension(Path.GetFileName(sourcePath), ".jpg"),
        OutputFormat.Png => Path.ChangeExtension(Path.GetFileName(sourcePath), ".png"),
        _ => Path.GetFileName(sourcePath),
    };

    private static bool IsJpegExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";

    /// <summary>Crops one image and writes it to <paramref name="targetPath"/> (atomically, via a temporary file).</summary>
    public static void Export(string sourcePath, string targetPath, Bars crop, ExportOptions options)
    {
        string format = Codecs.FormatOf(sourcePath) ?? throw new NotSupportedException("Not a JPEG or PNG image.");
        string tempPath = targetPath + ".part";
        try
        {
            // Images are disposed before the file is moved, so no handle is left open on either file.
            using (VipsImage image = Codecs.Load(sourcePath))
            {
                if (!crop.IsValidFor(image.Width, image.Height))
                    throw new InvalidOperationException($"Crop {crop} does not fit a {image.Width}×{image.Height} image.");

                using VipsImage cropped = image.ExtractArea(crop.Left, crop.Top, crop.FrameWidth(image.Width), crop.FrameHeight(image.Height));
                Save(cropped, sourcePath, format, tempPath, options);
            }
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void Save(VipsImage image, string sourcePath, string sourceFormat, string path, ExportOptions options)
    {
        // Metadata (EXIF, XMP, IPTC, ICC profile) is carried over in every case.
        switch (options.Format)
        {
            case OutputFormat.Jpeg:
                SaveJpeg(image, path, options.JpegQuality, options.JpegChroma444);
                break;

            case OutputFormat.Png:
                SavePng(image, path, keepPalette: false);
                break;

            default:
                if (sourceFormat == "JPEG")
                {
                    JpegHeaderInfo? header = JpegHeader.Read(sourcePath);
                    // libvips can write 4:2:0 or no subsampling; any other source subsampling is kept without loss as 4:4:4.
                    bool fullChroma = header?.Chroma is not (JpegChroma.Ratio420 or null);
                    SaveJpeg(image, path, header?.Quality ?? FallbackJpegQuality, fullChroma);
                }
                else
                {
                    SavePng(image, path, keepPalette: true);
                }
                break;
        }
    }

    private static void SaveJpeg(VipsImage image, string path, int quality, bool chroma444) =>
        image.Jpegsave(path,
            q: Math.Clamp(quality, 1, 100),
            subsampleMode: chroma444 ? Enums.ForeignSubsample.Off : Enums.ForeignSubsample.On,
            keep: Enums.ForeignKeep.All);

    private static void SavePng(VipsImage image, string path, bool keepPalette)
    {
        // Palette (indexed) PNGs stay indexed when saved "as source"; everything else keeps its bit depth.
        bool palette = keepPalette && image.Contains("palette") && Convert.ToBoolean(image.Get("palette"));
        int? bitDepth = palette && image.Contains("bits-per-sample") ? Convert.ToInt32(image.Get("bits-per-sample")) : null;
        image.Pngsave(path,
            compression: PngCompression,
            filter: Enums.ForeignPngFilter.All,
            palette: palette ? true : null,
            bitdepth: bitDepth,
            keep: Enums.ForeignKeep.All);
    }
}
