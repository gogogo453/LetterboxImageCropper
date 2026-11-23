using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LetterboxImageCropper;

/// <summary>Finds the images to process in a folder (non-recursive, natural "Explorer" order).</summary>
public static class ImageFiles
{
    public const string OutputFolderName = "_fixed";

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png",
    };

    public static List<string> Enumerate(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(f => Extensions.Contains(Path.GetExtension(f)))
            .ToList();
        files.Sort(NaturalCompare);
        return files;
    }

    public static int NaturalCompare(string a, string b) =>
        StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);
}
