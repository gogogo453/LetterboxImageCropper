using System;

namespace LetterboxImageCropper;

/// <summary>Bar thickness (in pixels) on each side of an image.</summary>
public readonly record struct Bars(int Top, int Bottom, int Left, int Right)
{
    public int FrameWidth(int imageWidth) => imageWidth - Left - Right;
    public int FrameHeight(int imageHeight) => imageHeight - Top - Bottom;

    public bool IsValidFor(int imageWidth, int imageHeight) =>
        Top >= 0 && Bottom >= 0 && Left >= 0 && Right >= 0 &&
        FrameWidth(imageWidth) > 0 && FrameHeight(imageHeight) > 0;
}

/// <summary>Generic, user-adjustable tolerances. Nothing here describes a particular film.</summary>
public sealed record DetectionSettings
{
    /// <summary>A pixel counts as "picture" when max(R,G,B) is above this value (0..254).</summary>
    public int BlackThreshold { get; init; } = 32;

    /// <summary>Fraction of pixels in a line that may exceed the threshold while the line still counts as bar (noise, dust, compression artefacts).</summary>
    public double NoiseAllowance { get; init; } = 0.005;

    /// <summary>
    /// An edge is "confident" when at least this fraction of the pixels in the first picture lines is bright,
    /// i.e. the picture clearly starts there instead of fading out of a dark scene.
    /// </summary>
    public double ConfidentEdgeFraction { get; init; } = 0.2;

    /// <summary>Number of picture lines after the edge used to measure edge confidence.</summary>
    public const int EdgeProbeLines = 3;

    /// <summary>
    /// Lossy compression smears a faint halo from the picture into the bar lines next to it (JPEG works in 8×8 blocks,
    /// so the halo never reaches further than one block). Within this many lines after the bar, lines whose average
    /// is still at black level and that are followed by a clearly brighter line are treated as part of the bar.
    /// </summary>
    public const int HaloLines = 8;

    /// <summary>The first picture line must be at least this many times brighter (on average) than the halo lines it absorbs.</summary>
    public const double HaloStepRatio = 2.0;
}

/// <summary>A detected bar on one side of an image.</summary>
/// <param name="Size">Number of bar lines from that edge. Equals the image dimension when the whole image is dark.</param>
/// <param name="Strength">Fraction of bright pixels in the first picture lines after the bar (0..1).</param>
public readonly record struct EdgeInfo(int Size, double Strength);

/// <param name="Format">"JPEG" or "PNG".</param>
/// <param name="Jpeg">Quality/chroma read from the JPEG header; null for other formats.</param>
public sealed record SourceInfo(string Format, JpegHeaderInfo? Jpeg)
{
    public static readonly SourceInfo Unknown = new("?", null);

    public string Describe() => Jpeg?.Quality is int q
        ? $"{Format} q≈{q} {Jpeg.ChromaText}".TrimEnd()
        : Format;

    public static SourceInfo Read(string path)
    {
        string? format = Codecs.FormatOf(path);
        if (format is null) return Unknown;
        return new SourceInfo(format, format == "JPEG" ? JpegHeader.Read(path) : null);
    }
}

/// <summary>Detection result for one file.</summary>
public sealed record ImageDetection(
    string Path, int Width, int Height,
    EdgeInfo Top, EdgeInfo Bottom, EdgeInfo Left, EdgeInfo Right,
    SourceInfo Source, string? Error = null)
{
    public bool IsReadable => Error is null;

    /// <summary>No picture line at all (fully black/dark frame) - carries no information about the bars.</summary>
    public bool IsAllDark => IsReadable && Top.Size >= Height;

    public string FileName => System.IO.Path.GetFileName(Path);

    public static ImageDetection Failed(string path, string error) =>
        new(path, 0, 0, default, default, default, default, SourceInfo.Unknown, error);
}

/// <summary>Measures the black bars of a single image.</summary>
public static class BarDetector
{
    public static ImageDetection Detect(string path, DetectionSettings settings)
    {
        try
        {
            SourceInfo source = SourceInfo.Read(path);
            if (source.Format == SourceInfo.Unknown.Format)
                return ImageDetection.Failed(path, "Not a JPEG or PNG image.");
            PixelBuffer pixels = Codecs.Decode(path);
            return Detect(path, pixels, settings, source);
        }
        catch (Exception ex)
        {
            return ImageDetection.Failed(path, Codecs.ErrorMessage(ex));
        }
    }

    public static ImageDetection Detect(string path, PixelBuffer image, DetectionSettings settings, SourceInfo source)
    {
        int width = image.Width, height = image.Height;
        int threshold = settings.BlackThreshold;
        int bands = image.Bands, colorBands = image.ColorBands;
        byte[] data = image.Data;

        // Per row and per column: number of "picture" (above-threshold) pixels and the sum of max(R,G,B).
        // Only the colour bands count; an alpha band is ignored.
        var rowBright = new int[height];
        var colBright = new int[width];
        var rowSum = new long[height];
        var colSum = new long[width];

        for (int y = 0; y < height; y++)
        {
            int offset = y * width * bands;
            int count = 0;
            long sum = 0;
            for (int x = 0; x < width; x++, offset += bands)
            {
                int max = data[offset];
                if (colorBands == 3)
                {
                    if (data[offset + 1] > max) max = data[offset + 1];
                    if (data[offset + 2] > max) max = data[offset + 2];
                }
                sum += max;
                colSum[x] += max;
                if (max > threshold)
                {
                    count++;
                    colBright[x]++;
                }
            }
            rowBright[y] = count;
            rowSum[y] = sum;
        }

        var rows = new LineProfile(rowBright, rowSum, width);
        var cols = new LineProfile(colBright, colSum, height);
        return new ImageDetection(
            path, width, height,
            Top: FindEdge(rows, fromStart: true, settings),
            Bottom: FindEdge(rows, fromStart: false, settings),
            Left: FindEdge(cols, fromStart: true, settings),
            Right: FindEdge(cols, fromStart: false, settings),
            Source: source);
    }

    /// <param name="Bright">Number of above-threshold pixels per line.</param>
    /// <param name="Sum">Sum of max(R,G,B) per line.</param>
    /// <param name="LineLength">Number of pixels in each line.</param>
    private sealed record LineProfile(int[] Bright, long[] Sum, int LineLength)
    {
        public int Count => Bright.Length;
    }

    private static EdgeInfo FindEdge(LineProfile profile, bool fromStart, DetectionSettings settings)
    {
        int n = profile.Count;
        int length = profile.LineLength;
        int Index(int i) => fromStart ? i : n - 1 - i;
        int BrightAt(int i) => profile.Bright[Index(i)];
        double MeanAt(int i) => profile.Sum[Index(i)] / (double)length;

        // 1. Lines that are black except for a tiny amount of noise.
        int noiseLimit = (int)Math.Floor(length * settings.NoiseAllowance);
        int size = 0;
        while (size < n && BrightAt(size) <= noiseLimit) size++;

        if (size >= n) return new EdgeInfo(n, 0); // whole image is dark

        // 2. Compression halo: dim lines (average still at black level) right before a clearly brighter line.
        //    Only next to a real bar - a soft/dim picture edge without any black line is not a bar.
        if (size == 0) return Confidence(0);
        int end = Math.Min(n, size + DetectionSettings.HaloLines);
        int candidate = size;
        double haloMax = 0;
        while (candidate < end && MeanAt(candidate) <= settings.BlackThreshold)
        {
            haloMax = Math.Max(haloMax, MeanAt(candidate));
            candidate++;
        }
        if (candidate > size && candidate < end && MeanAt(candidate) > settings.BlackThreshold &&
            MeanAt(candidate) >= haloMax * DetectionSettings.HaloStepRatio)
        {
            size = candidate;
        }

        return Confidence(size);

        // 3. Confidence: how much of the first picture lines is bright.
        EdgeInfo Confidence(int edge)
        {
            int probeEnd = Math.Min(n, edge + DetectionSettings.EdgeProbeLines);
            double fraction = 0;
            for (int i = edge; i < probeEnd; i++) fraction += BrightAt(i) / (double)length;
            return new EdgeInfo(edge, fraction / (probeEnd - edge));
        }
    }
}
