using System;
using System.Collections.Generic;
using System.IO;

namespace LetterboxImageCropper;

/// <summary>Chroma subsampling of a JPEG file, as stored in its frame header.</summary>
public enum JpegChroma
{
    Unknown,
    Grayscale,
    Ratio444,
    Ratio422,
    Ratio420,
    Ratio440,
    Ratio411,
    Ratio410,
    Cmyk,
}

/// <summary>Encoding parameters of a JPEG file, read from its header (no image decoding).</summary>
public sealed record JpegHeaderInfo(int? Quality, JpegChroma Chroma, bool Progressive)
{
    public string ChromaText => Chroma switch
    {
        JpegChroma.Grayscale => "grayscale",
        JpegChroma.Ratio444 => "4:4:4",
        JpegChroma.Ratio422 => "4:2:2",
        JpegChroma.Ratio420 => "4:2:0",
        JpegChroma.Ratio440 => "4:4:0",
        JpegChroma.Ratio411 => "4:1:1",
        JpegChroma.Ratio410 => "4:1:0",
        JpegChroma.Cmyk => "CMYK",
        _ => "",
    };
}

/// <summary>
/// Reads a JPEG's quantization tables and frame header. The quality is estimated by finding the standard
/// (IJG/libjpeg) quality setting whose scaled tables are closest to the file's tables - this is how most encoders
/// derive their tables, so the result is exact for them and a close approximation for others.
/// </summary>
public static class JpegHeader
{
    // JPEG standard (Annex K) tables in natural (row-major) order.
    private static readonly int[] StdLuminance =
    {
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    };

    private static readonly int[] StdChrominance =
    {
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    };

    /// <summary>Position in the file's zig-zag sequence -> natural (row-major) index.</summary>
    private static readonly int[] ZigZagToNatural =
    {
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    };

    /// <summary>Returns null when the file is not a readable JPEG.</summary>
    public static JpegHeaderInfo? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            return Read(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static JpegHeaderInfo? Read(Stream s)
    {
        if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8) return null; // SOI

        var tables = new Dictionary<int, int[]>(); // table id -> natural-order values
        int[]? componentTables = null;
        (int Id, int H, int V)[]? components = null;
        bool progressive = false;

        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) break;
            if (b != 0xFF) continue;            // tolerate garbage between segments
            int marker;
            do { marker = s.ReadByte(); } while (marker == 0xFF); // fill bytes
            if (marker < 0 || marker == 0xD9) break;               // EOF / EOI
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue; // no length

            int length = ReadUInt16(s) - 2;
            if (length < 0) break;
            byte[] data = new byte[length];
            if (s.ReadAtLeast(data, length, throwOnEndOfStream: false) < length) break;

            if (marker == 0xDB) ParseDqt(data, tables);
            else if (IsStartOfFrame(marker))
            {
                progressive = marker is 0xC2 or 0xC6 or 0xCA or 0xCE;
                int count = data.Length > 5 ? data[5] : 0;
                components = new (int, int, int)[count];
                componentTables = new int[count];
                for (int i = 0; i < count && 6 + i * 3 + 2 < data.Length; i++)
                {
                    int o = 6 + i * 3;
                    components[i] = (data[o], data[o + 1] >> 4, data[o + 1] & 0x0F);
                    componentTables[i] = data[o + 2];
                }
            }
            else if (marker == 0xDA) break; // start of scan - all tables needed are known by now
        }

        if (components is null || componentTables is null) return null;
        return new JpegHeaderInfo(EstimateQuality(tables, componentTables), ChromaOf(components), progressive);
    }

    private static bool IsStartOfFrame(int marker) =>
        marker >= 0xC0 && marker <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    private static int ReadUInt16(Stream s)
    {
        int hi = s.ReadByte(), lo = s.ReadByte();
        return hi < 0 || lo < 0 ? -1 : (hi << 8) | lo;
    }

    private static void ParseDqt(byte[] data, Dictionary<int, int[]> tables)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            int precision = data[pos] >> 4, id = data[pos] & 0x0F;
            pos++;
            int size = precision == 0 ? 64 : 128;
            if (pos + size > data.Length) return;
            var natural = new int[64];
            for (int k = 0; k < 64; k++)
                natural[ZigZagToNatural[k]] = precision == 0 ? data[pos + k] : (data[pos + 2 * k] << 8) | data[pos + 2 * k + 1];
            tables[id] = natural;
            pos += size;
        }
    }

    private static int? EstimateQuality(Dictionary<int, int[]> tables, int[] componentTables)
    {
        if (componentTables.Length == 0 || !tables.TryGetValue(componentTables[0], out int[]? luma)) return null;
        int[]? chroma = componentTables.Length >= 3 && componentTables[1] != componentTables[0] &&
                        tables.TryGetValue(componentTables[1], out int[]? c) ? c : null;

        int bestQuality = 0;
        long bestError = long.MaxValue;
        for (int q = 1; q <= 100; q++)
        {
            int scale = q < 50 ? 5000 / q : 200 - q * 2;
            long error = TableError(luma, StdLuminance, scale) + (chroma is null ? 0 : TableError(chroma, StdChrominance, scale));
            if (error <= bestError) // ties -> the higher quality
            {
                bestError = error;
                bestQuality = q;
            }
        }
        return bestQuality;
    }

    private static long TableError(int[] actual, int[] standard, int scale)
    {
        long error = 0;
        for (int i = 0; i < 64; i++)
        {
            int expected = Math.Clamp((standard[i] * scale + 50) / 100, 1, 255);
            error += Math.Abs(actual[i] - expected);
        }
        return error;
    }

    private static JpegChroma ChromaOf((int Id, int H, int V)[] components)
    {
        switch (components.Length)
        {
            case 1:
                return JpegChroma.Grayscale;
            case 4:
                return JpegChroma.Cmyk;
            case 3:
                // Adobe-style RGB JPEGs (component ids 'R','G','B') are never subsampled.
                if (components[0].Id == 'R' && components[1].Id == 'G' && components[2].Id == 'B') return JpegChroma.Ratio444;
                var (_, hy, vy) = components[0];
                var (_, hc, vc) = components[1];
                if (hc == 0 || vc == 0 || hy % hc != 0 || vy % vc != 0) return JpegChroma.Unknown;
                return (hy / hc, vy / vc) switch
                {
                    (1, 1) => JpegChroma.Ratio444,
                    (2, 1) => JpegChroma.Ratio422,
                    (2, 2) => JpegChroma.Ratio420,
                    (1, 2) => JpegChroma.Ratio440,
                    (4, 1) => JpegChroma.Ratio411,
                    (4, 2) => JpegChroma.Ratio410,
                    _ => JpegChroma.Unknown,
                };
            default:
                return JpegChroma.Unknown;
        }
    }
}
