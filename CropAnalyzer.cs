using System;
using System.Collections.Generic;
using System.Linq;

namespace LetterboxImageCropper;

/// <summary>Which sides are searched for bars.</summary>
public enum BarSearch
{
    /// <summary>Top/bottom OR left/right: each axis is detected independently, so letterbox, pillarbox or both are found.</summary>
    Auto,
    /// <summary>Only top/bottom bars (letterbox).</summary>
    TopBottomOnly,
    /// <summary>Only left/right bars (pillarbox).</summary>
    LeftRightOnly,
}

public sealed record AnalysisOptions
{
    public DetectionSettings Detection { get; init; } = new();
    public bool ForceSymmetric { get; init; } = true;
    public BarSearch Search { get; init; } = BarSearch.Auto;
    /// <summary>Extra pixels removed from every side that has a bar (removes soft/blended edge lines).</summary>
    public int ExtraTrim { get; init; }

    public bool SearchTopBottom => Search != BarSearch.LeftRightOnly;
    public bool SearchLeftRight => Search != BarSearch.TopBottomOnly;

    /// <summary>The cropped frame must keep at least this fraction of each image dimension, otherwise the result is rejected.</summary>
    public const double MinFrameFraction = 0.1;
}

public enum ImageStatus
{
    /// <summary>Eligible: there is something to crop and everything that is cut is black in this image.</summary>
    Ok,
    /// <summary>Eligible: like <see cref="Ok"/>, but a dark scene made a bar look thicker than the result (the cut area is still black).</summary>
    DarkScene,
    /// <summary>Not eligible: no black bars detected (with the current mode) - nothing to crop.</summary>
    NothingToCrop,
    /// <summary>Not eligible: something bright (picture, subtitles, logo) lies inside the area that would be cut.</summary>
    ContentInBar,
    /// <summary>Not eligible: the whole image is black.</summary>
    AllDark,
    /// <summary>Not eligible: the crop for this resolution would leave no picture.</summary>
    InvalidCrop,
    /// <summary>Not eligible: the file could not be decoded.</summary>
    Unreadable,
}

public static class ImageStatusExtensions
{
    /// <summary>Only eligible images are exported.</summary>
    public static bool IsEligible(this ImageStatus status) => status is ImageStatus.Ok or ImageStatus.DarkScene;
}

/// <summary>All images of one resolution, and the crop chosen for them.</summary>
public sealed class SizeGroup
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required IReadOnlyList<ImageDetection> Images { get; init; }

    /// <summary>Bars as voted by the images (without extra trim).</summary>
    public required Bars Voted { get; init; }
    /// <summary>Voted bars plus extra trim - the initial crop.</summary>
    public required Bars Suggested { get; init; }
    /// <summary>Crop actually used for export (may be edited manually).</summary>
    public Bars Crop { get; set; }

    public required bool TopBottomConsidered { get; init; }
    public required bool LeftRightConsidered { get; init; }
    /// <summary>Number of images that had at least one clearly lit (confident) edge.</summary>
    public required int ConfidentImages { get; init; }
    /// <summary>Number of images whose detection equals the voted result exactly.</summary>
    public required int MatchingImages { get; init; }
    public required bool Reliable { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public string SizeText => $"{Width} × {Height}";

    /// <summary>Human-readable kind of boxing found by the vote.</summary>
    public string BoxingKind => (Voted.Top + Voted.Bottom > 0, Voted.Left + Voted.Right > 0) switch
    {
        (true, true) => "letterbox + pillarbox",
        (true, false) => "letterbox",
        (false, true) => "pillarbox",
        _ => "no bars",
    };

    /// <summary>True when the current crop removes at least one pixel and leaves a picture.</summary>
    public bool HasSomethingToCrop => Crop != default && Crop.IsValidFor(Width, Height);

    public int EligibleCount => Images.Count(d => StatusOf(d).IsEligible());

    /// <summary>Status of one image relative to the current crop. Depends on <see cref="Crop"/>, so re-query after edits.</summary>
    public ImageStatus StatusOf(ImageDetection d)
    {
        if (!d.IsReadable) return ImageStatus.Unreadable;
        if (d.IsAllDark) return ImageStatus.AllDark;
        if (Crop == default) return ImageStatus.NothingToCrop;
        if (!Crop.IsValidFor(Width, Height)) return ImageStatus.InvalidCrop;

        // The image's own bars must cover the part of the crop that detection vouches for. Crop beyond the voted bars
        // (extra trim, deliberate manual enlargement) is the user's choice and is not held against single images.
        bool thinner = d.Top.Size < Math.Min(Crop.Top, Voted.Top) || d.Bottom.Size < Math.Min(Crop.Bottom, Voted.Bottom) ||
                       d.Left.Size < Math.Min(Crop.Left, Voted.Left) || d.Right.Size < Math.Min(Crop.Right, Voted.Right);
        if (thinner) return ImageStatus.ContentInBar;

        bool thicker = (TopBottomConsidered && (d.Top.Size > Voted.Top || d.Bottom.Size > Voted.Bottom)) ||
                       (LeftRightConsidered && (d.Left.Size > Voted.Left || d.Right.Size > Voted.Right));
        return thicker ? ImageStatus.DarkScene : ImageStatus.Ok;
    }
}

public sealed record AnalysisResult(IReadOnlyList<SizeGroup> Groups, IReadOnlyList<ImageDetection> Unreadable)
{
    public int TotalImages => Groups.Sum(g => g.Images.Count) + Unreadable.Count;
    public int EligibleCount => Groups.Sum(g => g.EligibleCount);
}

/// <summary>
/// Combines per-image detections into one crop per resolution.
/// Dark scenes can only make a bar look thicker and bright content in the bar can only make it look thinner,
/// so the most common value among clearly lit edges is robust against both.
/// Top/bottom and left/right are voted independently: bars on either axis (or both) are found.
/// </summary>
public static class CropAnalyzer
{
    public static AnalysisResult Analyze(IEnumerable<ImageDetection> detections, AnalysisOptions options)
    {
        var all = detections.ToList();
        var unreadable = all.Where(d => !d.IsReadable).ToList();

        var groups = all.Where(d => d.IsReadable)
            .GroupBy(d => (d.Width, d.Height))
            .Select(g => BuildGroup(g.Key.Width, g.Key.Height, g.ToList(), options))
            .OrderByDescending(g => g.Images.Count)
            .ThenByDescending(g => g.Width * g.Height)
            .ToList();

        return new AnalysisResult(groups, unreadable);
    }

    private static SizeGroup BuildGroup(int width, int height, List<ImageDetection> images, AnalysisOptions options)
    {
        double confident = options.Detection.ConfidentEdgeFraction;
        bool searchTb = options.SearchTopBottom, searchLr = options.SearchLeftRight;
        var warnings = new List<string>();
        bool reliable = true;

        int top = 0, bottom = 0, left = 0, right = 0;
        if (searchTb)
        {
            (top, bottom, bool r) = VotePair(
                images.Select(i => i.Top), images.Select(i => i.Bottom), height, "top", "bottom",
                options.ForceSymmetric, confident, warnings);
            reliable &= r;
        }
        if (searchLr)
        {
            (left, right, bool r) = VotePair(
                images.Select(i => i.Left), images.Select(i => i.Right), width, "left", "right",
                options.ForceSymmetric, confident, warnings);
            reliable &= r;
        }

        var voted = new Bars(top, bottom, left, right);
        int trim = Math.Max(0, options.ExtraTrim);
        var suggested = new Bars(
            top > 0 ? top + trim : 0, bottom > 0 ? bottom + trim : 0,
            left > 0 ? left + trim : 0, right > 0 ? right + trim : 0);
        if (!suggested.IsValidFor(width, height))
        {
            suggested = voted;
            warnings.Add("Extra trim ignored: it would leave no picture.");
        }

        int confidentImages = images.Count(i =>
            (searchTb && (i.Top.Strength >= confident || i.Bottom.Strength >= confident)) ||
            (searchLr && (i.Left.Strength >= confident || i.Right.Strength >= confident)));

        int matching = images.Count(i =>
            (!searchTb || (i.Top.Size == top && i.Bottom.Size == bottom)) &&
            (!searchLr || (i.Left.Size == left && i.Right.Size == right)));

        if (!reliable)
            warnings.Insert(0, "No clearly lit frame edge found - verify the crop in the preview.");

        return new SizeGroup
        {
            Width = width,
            Height = height,
            Images = images,
            Voted = voted,
            Suggested = suggested,
            Crop = suggested,
            TopBottomConsidered = searchTb,
            LeftRightConsidered = searchLr,
            ConfidentImages = confidentImages,
            MatchingImages = matching,
            Reliable = reliable,
            Warnings = warnings,
        };
    }

    /// <summary>Votes the two opposite bars of one axis (top/bottom or left/right).</summary>
    private static (int first, int second, bool reliable) VotePair(
        IEnumerable<EdgeInfo> firstEdges, IEnumerable<EdgeInfo> secondEdges, int dimension,
        string firstName, string secondName, bool symmetric, double confident, List<string> warnings)
    {
        var a = firstEdges.ToList();
        var b = secondEdges.ToList();

        var (va, ra) = Vote(a, dimension, confident);
        var (vb, rb) = Vote(b, dimension, confident);
        int first = va, second = vb;
        bool reliable = ra && rb;

        if (symmetric)
        {
            var (both, rBoth) = Vote(a.Concat(b), dimension, confident);
            first = second = both;
            reliable = rBoth;
            if (ra && rb && va != vb)
                warnings.Add($"The {firstName} ({va} px) and {secondName} ({vb} px) bars differ - consider unticking \"Force symmetric\".");
        }

        int minFrame = (int)Math.Ceiling(dimension * AnalysisOptions.MinFrameFraction);
        if (dimension - first - second < minFrame)
        {
            warnings.Add($"Detected {firstName}/{secondName} bars ({first}/{second} px) would leave almost no picture - ignored.");
            return (0, 0, false);
        }
        return (first, second, reliable);
    }

    /// <summary>Most common bar size among confident edges (ties -> the thinner bar, which cuts less).</summary>
    private static (int value, bool reliable) Vote(IEnumerable<EdgeInfo> edges, int dimension, double confident)
    {
        var list = edges.ToList();
        var votes = list.Where(e => e.Strength >= confident && e.Size < dimension).ToList();
        if (votes.Count > 0)
        {
            int mode = votes.GroupBy(e => e.Size)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First().Key;
            return (mode, true);
        }

        // No clearly lit edge: the thinnest detected bar is the safest guess (dark scenes only over-detect).
        var usable = list.Where(e => e.Size < dimension).ToList();
        return (usable.Count > 0 ? usable.Min(e => e.Size) : 0, false);
    }
}
