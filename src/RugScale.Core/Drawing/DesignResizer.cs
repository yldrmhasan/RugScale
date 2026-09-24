using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>Where the original design lands inside a resized (crop/pad) canvas — matches the anchor grid in Texcelle's own Resize Design dialog.</summary>
public enum ResizeAnchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, Center, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
}

/// <summary>
/// How pixels get resampled when "Scale Image" is on — Texcelle offers several named modes
/// (Normal, With background, Smart, Sequential, With Priority colors); RugCAD's own take on
/// "a few different scale modes so different distortion trade-offs are available" per the user's
/// request, built to fit an INDEXED (palette + byte-per-pixel) document rather than true color.
///
/// EVERY mode here only ever outputs colours the SOURCE DESIGN ALREADY USED — never some other
/// entry that merely happens to sit in the palette. That matters because a design's palette can
/// hold 256 entries while the design itself paints with a handful: snapping a blended colour to
/// the nearest entry across the whole palette pulled in unrelated colours and visibly changed the
/// design's colour scheme (reported by the user for AreaAverage). See <see cref="UsedColors"/>.
/// </summary>
public enum ScaleMode
{
    /// <summary>Every new pixel copies its single nearest source pixel — blocky, but never invents a color; the natural choice for enlarging pixel art.</summary>
    NearestNeighbor,

    /// <summary>
    /// The only mode that behaves differently when ENLARGING. Runs the classic EPX/Scale2x
    /// pixel-art doubling (Eric Johnston / Andrea Mazzoleni) as many times as needed, then lands
    /// on the exact requested size with a <see cref="Dominant"/> pass. EPX rounds off the diagonal
    /// "staircase" by expanding each pixel from its own neighbours — it only ever copies existing
    /// pixels, so like every other mode here it cannot invent a colour.
    ///
    /// Every other mode collapses to plain nearest-neighbour when enlarging, because each new
    /// pixel's source footprint is a single pixel: a majority vote, a rarity vote and an average
    /// over one pixel all return that same pixel. That is why Dominant/PreserveDetail/AreaAverage
    /// looked identical on a stretch (reported by the user) — they are downscaling strategies.
    ///
    /// Known trade-offs of EPX: it rounds 90° corners and the sharp tips of triangles/arrows, and
    /// is inconsistent on 1:1 vs 2:1 slopes.
    /// </summary>
    EdgeSmooth,

    /// <summary>
    /// Each new pixel takes the colour that occupies the most of its source footprint (a majority
    /// vote). Never blends and never shifts a colour, so a shrunk design keeps exactly the palette
    /// it had — the safest downscale for flat-colour rug patterns, and unlike NearestNeighbor it
    /// doesn't pick an arbitrary single sample, which is what makes rows/columns double up
    /// unevenly at non-integer scale factors.
    /// </summary>
    Dominant,

    /// <summary>
    /// Like <see cref="Dominant"/>, but when a footprint contains several colours the one that is
    /// RAREST in the design as a whole wins instead of the most common one. Single-pixel outlines
    /// and fine detail survive a heavy downscale instead of being out-voted by the background;
    /// the trade-off is that sparse noise is preserved just as eagerly.
    /// </summary>
    PreserveDetail,

    /// <summary>
    /// RugCAD's motif/topology scaler for discrete motif-based carpet artwork. This mode owns
    /// connected motifs, branches, repeat/rapport and designer symmetry. It deliberately does NOT
    /// run the curve/fill contour engine; curve-heavy outlined floral artwork has its own
    /// <see cref="CurveFill"/> mode.
    /// </summary>
    RugScale,

    /// <summary>
    /// RugScale Curve & Fill: for outlined, curved, filled floral/ornamental artwork. Reconstructs
    /// the categorical boundaries of every used palette region as smooth source-guided contours,
    /// then fills the enclosed regions with their ORIGINAL indexed colours. This is separate from
    /// motif/topology RugScale so curve training can never hollow or recolour motif-based designs.
    /// </summary>
    CurveFill,

    /// <summary>
    /// RugScale Leaf / Petal Arcs: specialist mode for elongated filled floral forms whose visual
    /// quality depends on an elegant base-to-apex arc and controlled taper. It starts from the
    /// safe Curve & Fill result, learns eligible leaf/petal regions as centreline + width profile,
    /// refines only their boundary band, then replays indexed separators/Pixel-Cord outlines.
    /// Complex/branched regions automatically fall back to Curve & Fill.
    /// </summary>
    LeafPetalArcs,

    /// <summary>Bilinearly blends the 4 nearest source pixels' colors, then snaps the blend to the closest colour the design already uses — softer diagonal/curve edges than NearestNeighbor at the cost of some colour bleeding.</summary>
    Smooth,

    /// <summary>Averages every source pixel that falls inside each new pixel's footprint, then snaps to the closest colour the design already uses — the standard "downscale without aliasing" approach; falls back to NearestNeighbor when enlarging (there's no area to average).</summary>
    AreaAverage,
}

/// <summary>
/// Resizes a DesignDocument to new pixel dimensions, either by cropping/padding (the canvas
/// dimensions change but existing pixels keep their original size and position, anchored per
/// <see cref="ResizeAnchor"/>) or by scaling (the whole image is resampled to fit the new
/// dimensions, per <see cref="ScaleMode"/>) — mirrors the "Scale Image" checkbox in Texcelle's
/// Resize Design dialog: checked = scale, unchecked = crop/pad (RugCAD's default, matching the
/// user's explicit instruction that crop/pad is what should happen when Scale Image is off).
/// </summary>
public static class DesignResizer
{
    /// <summary>Crops and/or pads to the new size without resampling — existing pixels keep their exact original size, extra/missing area is anchored per <paramref name="anchor"/>. New area is filled with <paramref name="fillIndex"/>.</summary>
    public static DesignDocument CropOrPad(DesignDocument source, int newWidth, int newHeight, ResizeAnchor anchor, byte fillIndex)
    {
        var (offsetX, offsetY) = AnchorOffset(anchor, source.Width, source.Height, newWidth, newHeight);
        return CropOrPad(source, newWidth, newHeight, offsetX, offsetY, fillIndex);
    }

    /// <summary>
    /// Same as the anchor-based overload, but the caller says exactly where the original's
    /// top-left corner lands in the new canvas — i.e. how many pixels are added (positive) or cut
    /// (negative) off the left/top edges, with the right/bottom edges getting whatever's left
    /// over. The Resize dialog's per-edge distribution boxes feed this directly, so an anchor is
    /// just a shortcut for filling those numbers in rather than a separate code path.
    /// </summary>
    public static DesignDocument CropOrPad(DesignDocument source, int newWidth, int newHeight, int offsetX, int offsetY, byte fillIndex)
    {
        if (newWidth <= 0 || newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(newWidth), "New dimensions must be positive.");

        var result = new DesignDocument(newWidth, newHeight, source.Palette);
        for (var y = 0; y < newHeight; y++)
            for (var x = 0; x < newWidth; x++)
                result.SetPixel(x, y, fillIndex);

        for (var y = 0; y < source.Height; y++)
        {
            var ty = y + offsetY;
            if (ty < 0 || ty >= newHeight)
                continue;
            for (var x = 0; x < source.Width; x++)
            {
                var tx = x + offsetX;
                if (tx < 0 || tx >= newWidth)
                    continue;
                result.SetPixel(tx, ty, source.GetPixel(x, y));
            }
        }

        return result;
    }

    /// <summary>Resamples the whole image to the new dimensions per <paramref name="mode"/> — see ScaleMode's own doc comments for what each one does.</summary>
    public static DesignDocument Scale(
        DesignDocument source,
        int newWidth,
        int newHeight,
        ScaleMode mode) =>
        Scale(
            source,
            newWidth,
            newHeight,
            mode,
            sourceWarpDensity: 1,
            sourceWeftDensity: 1,
            targetWarpDensity: 1,
            targetWeftDensity: 1);

    /// <summary>
    /// Quality-aware scaling overload. Density arguments matter to RugScale enlargement: physical
    /// geometry follows the requested dimensions, but curve/stroke pixel thickness follows the
    /// target/source warp+weft density ratio rather than the width/height scale ratio.
    /// Other scale modes intentionally retain their existing raster semantics.
    /// </summary>
    public static DesignDocument Scale(
        DesignDocument source,
        int newWidth,
        int newHeight,
        ScaleMode mode,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        if (newWidth <= 0 || newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(newWidth), "New dimensions must be positive.");

        var result = new DesignDocument(newWidth, newHeight, source.Palette);
        var effectiveMode = mode == ScaleMode.AreaAverage && (newWidth > source.Width || newHeight > source.Height)
            ? ScaleMode.NearestNeighbor // no source area to average when enlarging
            : mode;

        switch (effectiveMode)
        {
            case ScaleMode.NearestNeighbor:
                ScaleNearestNeighbor(source, result);
                break;
            case ScaleMode.EdgeSmooth:
                ScaleEdgeSmooth(source, result);
                break;
            case ScaleMode.Dominant:
                ScaleByVote(source, result, preserveDetail: false);
                break;
            case ScaleMode.PreserveDetail:
                ScaleByVote(source, result, preserveDetail: true);
                break;
            case ScaleMode.RugScale:
                RugScaleEngine.Resize(
                    source,
                    result,
                    sourceWarpDensity,
                    sourceWeftDensity,
                    targetWarpDensity,
                    targetWeftDensity);
                break;
            case ScaleMode.CurveFill:
                CurveFillScaleEngine.Resize(
                    source,
                    result,
                    sourceWarpDensity,
                    sourceWeftDensity,
                    targetWarpDensity,
                    targetWeftDensity);
                break;
            case ScaleMode.LeafPetalArcs:
                LeafPetalArcScaleEngine.Resize(
                    source,
                    result,
                    sourceWarpDensity,
                    sourceWeftDensity,
                    targetWarpDensity,
                    targetWeftDensity);
                break;
            case ScaleMode.Smooth:
                ScaleSmooth(source, result);
                break;
            case ScaleMode.AreaAverage:
                ScaleAreaAverage(source, result);
                break;
        }

        return result;
    }

    /// <summary>How many times EPX may double before the result is resampled down to the exact target — 3 passes is up to 8x, far past the point where extra passes still change the look.</summary>
    private const int MaxEdgeSmoothPasses = 3;

    /// <summary>
    /// Doubles with EPX until the image covers the requested size, then lands exactly on it with a
    /// Dominant pass (the loop stops as soon as both dimensions reach the target, so the
    /// intermediate is at most one doubling past it). See <see cref="ScaleMode.EdgeSmooth"/>.
    /// </summary>
    private static void ScaleEdgeSmooth(DesignDocument source, DesignDocument result)
    {
        var scaled = source;
        for (var pass = 0; pass < MaxEdgeSmoothPasses; pass++)
        {
            if (scaled.Width >= result.Width && scaled.Height >= result.Height)
                break;
            scaled = ApplyEpx(scaled);
        }

        if (scaled.Width == result.Width && scaled.Height == result.Height)
        {
            for (var y = 0; y < result.Height; y++)
                for (var x = 0; x < result.Width; x++)
                    result.SetPixel(x, y, scaled.GetPixel(x, y));
            return;
        }

        ScaleByVote(scaled, result, preserveDetail: false);
    }

    /// <summary>
    /// One EPX/Scale2x pass: each source pixel P becomes a 2x2 block, and a corner is replaced by
    /// a neighbour only where two adjacent neighbours agree and the opposing ones don't — which is
    /// what rounds a diagonal staircase. Rules per the published algorithm, with A/B/C/D being the
    /// pixels above/right/left/below P (edges clamp to P itself):
    ///     1=A if C==A and C!=D and A!=B      2=B if A==B and A!=C and B!=D
    ///     3=C if D==C and D!=B and C!=A      4=D if B==D and B!=A and D!=C
    /// </summary>
    private static DesignDocument ApplyEpx(DesignDocument source)
    {
        var result = new DesignDocument(source.Width * 2, source.Height * 2, source.Palette);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var p = source.GetPixel(x, y);
                var a = y > 0 ? source.GetPixel(x, y - 1) : p;
                var b = x < source.Width - 1 ? source.GetPixel(x + 1, y) : p;
                var c = x > 0 ? source.GetPixel(x - 1, y) : p;
                var d = y < source.Height - 1 ? source.GetPixel(x, y + 1) : p;

                var topLeft = c == a && c != d && a != b ? a : p;
                var topRight = a == b && a != c && b != d ? b : p;
                var bottomLeft = d == c && d != b && c != a ? c : p;
                var bottomRight = b == d && b != a && d != c ? d : p;

                result.SetPixel(x * 2, y * 2, topLeft);
                result.SetPixel(x * 2 + 1, y * 2, topRight);
                result.SetPixel(x * 2, y * 2 + 1, bottomLeft);
                result.SetPixel(x * 2 + 1, y * 2 + 1, bottomRight);
            }
        }

        return result;
    }

    /// <summary>
    /// Backs both <see cref="ScaleMode.Dominant"/> and <see cref="ScaleMode.PreserveDetail"/>:
    /// each destination pixel looks at every source pixel inside its footprint and picks one of
    /// them outright — no blending, so the output can only ever contain colours the source
    /// already had. <paramref name="preserveDetail"/> flips the tie-break from "most of this
    /// footprint" to "rarest in the whole design".
    /// </summary>
    private static void ScaleByVote(DesignDocument source, DesignDocument result, bool preserveDetail)
    {
        var globalCounts = preserveDetail ? CountPixelsPerIndex(source) : null;
        var footprintCounts = new int[PaletteSlots];
        var seen = new List<byte>();

        for (var ty = 0; ty < result.Height; ty++)
        {
            var sy0 = ty * source.Height / result.Height;
            var sy1 = Math.Max(sy0 + 1, (ty + 1) * source.Height / result.Height);

            for (var tx = 0; tx < result.Width; tx++)
            {
                var sx0 = tx * source.Width / result.Width;
                var sx1 = Math.Max(sx0 + 1, (tx + 1) * source.Width / result.Width);

                seen.Clear();
                for (var sy = sy0; sy < sy1 && sy < source.Height; sy++)
                {
                    for (var sx = sx0; sx < sx1 && sx < source.Width; sx++)
                    {
                        var index = source.GetPixel(sx, sy);
                        if (footprintCounts[index] == 0)
                            seen.Add(index);
                        footprintCounts[index]++;
                    }
                }

                var winner = seen[0];
                foreach (var candidate in seen)
                {
                    var better = preserveDetail
                        // Rarer in the design as a whole wins; same rarity falls back to whichever
                        // covers more of this footprint.
                        ? globalCounts![candidate] < globalCounts[winner] ||
                          (globalCounts[candidate] == globalCounts[winner] && footprintCounts[candidate] > footprintCounts[winner])
                        : footprintCounts[candidate] > footprintCounts[winner];

                    if (better)
                        winner = candidate;
                }

                result.SetPixel(tx, ty, winner);

                foreach (var index in seen)
                    footprintCounts[index] = 0;
            }
        }
    }

    private static int[] CountPixelsPerIndex(DesignDocument source)
    {
        var counts = new int[PaletteSlots];
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                counts[source.GetPixel(x, y)]++;
        return counts;
    }

    /// <summary>A palette index is a byte, so there are always exactly this many possible slots regardless of how many the palette defines.</summary>
    private const int PaletteSlots = 256;

    private static void ScaleNearestNeighbor(DesignDocument source, DesignDocument result)
    {
        for (var ty = 0; ty < result.Height; ty++)
        {
            var sy = Math.Min(source.Height - 1, ty * source.Height / result.Height);
            for (var tx = 0; tx < result.Width; tx++)
            {
                var sx = Math.Min(source.Width - 1, tx * source.Width / result.Width);
                result.SetPixel(tx, ty, source.GetPixel(sx, sy));
            }
        }
    }

    private static void ScaleSmooth(DesignDocument source, DesignDocument result)
    {
        var candidates = UsedColors(source);

        for (var ty = 0; ty < result.Height; ty++)
        {
            var srcY = (ty + 0.5) * source.Height / result.Height - 0.5;
            var y0 = (int)Math.Floor(srcY);
            var fy = srcY - y0;

            for (var tx = 0; tx < result.Width; tx++)
            {
                var srcX = (tx + 0.5) * source.Width / result.Width - 0.5;
                var x0 = (int)Math.Floor(srcX);
                var fx = srcX - x0;

                var c00 = source.Palette[source.GetPixel(Clamp(x0, source.Width), Clamp(y0, source.Height))];
                var c10 = source.Palette[source.GetPixel(Clamp(x0 + 1, source.Width), Clamp(y0, source.Height))];
                var c01 = source.Palette[source.GetPixel(Clamp(x0, source.Width), Clamp(y0 + 1, source.Height))];
                var c11 = source.Palette[source.GetPixel(Clamp(x0 + 1, source.Width), Clamp(y0 + 1, source.Height))];

                var r = Bilerp(c00.R, c10.R, c01.R, c11.R, fx, fy);
                var g = Bilerp(c00.G, c10.G, c01.G, c11.G, fx, fy);
                var b = Bilerp(c00.B, c10.B, c01.B, c11.B, fx, fy);

                result.SetPixel(tx, ty, NearestUsedIndex(candidates, r, g, b));
            }
        }
    }

    private static void ScaleAreaAverage(DesignDocument source, DesignDocument result)
    {
        var candidates = UsedColors(source);

        for (var ty = 0; ty < result.Height; ty++)
        {
            var sy0 = ty * source.Height / result.Height;
            var sy1 = Math.Max(sy0 + 1, (ty + 1) * source.Height / result.Height);

            for (var tx = 0; tx < result.Width; tx++)
            {
                var sx0 = tx * source.Width / result.Width;
                var sx1 = Math.Max(sx0 + 1, (tx + 1) * source.Width / result.Width);

                double sumR = 0, sumG = 0, sumB = 0;
                var count = 0;
                for (var sy = sy0; sy < sy1 && sy < source.Height; sy++)
                {
                    for (var sx = sx0; sx < sx1 && sx < source.Width; sx++)
                    {
                        var c = source.Palette[source.GetPixel(sx, sy)];
                        sumR += c.R;
                        sumG += c.G;
                        sumB += c.B;
                        count++;
                    }
                }

                var avgR = count > 0 ? sumR / count : 0;
                var avgG = count > 0 ? sumG / count : 0;
                var avgB = count > 0 ? sumB / count : 0;
                result.SetPixel(tx, ty, NearestUsedIndex(candidates, avgR, avgG, avgB));
            }
        }
    }

    private static int Clamp(int value, int length) => Math.Clamp(value, 0, length - 1);

    private static double Bilerp(double c00, double c10, double c01, double c11, double fx, double fy)
    {
        var top = c00 + (c10 - c00) * fx;
        var bottom = c01 + (c11 - c01) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>
    /// Every distinct palette index that actually appears in the design, with its colour. The
    /// blending modes snap to one of THESE rather than to the nearest entry anywhere in the
    /// palette: a palette may define 256 colours while the design paints with a handful, and
    /// snapping across all of them let resizing introduce colours the design never contained,
    /// visibly rewriting its colour scheme.
    /// </summary>
    private static List<(byte Index, RugColor Colour)> UsedColors(DesignDocument source)
    {
        var seen = new bool[PaletteSlots];
        var used = new List<(byte, RugColor)>();

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = source.GetPixel(x, y);
                if (seen[index] || index >= source.Palette.Count)
                    continue;
                seen[index] = true;
                used.Add((index, source.Palette[index]));
            }
        }

        return used;
    }

    /// <summary>Picks whichever of the design's own colours is closest to (r, g, b) by squared Euclidean distance.</summary>
    private static byte NearestUsedIndex(List<(byte Index, RugColor Colour)> candidates, double r, double g, double b)
    {
        var bestIndex = candidates.Count > 0 ? candidates[0].Index : (byte)0;
        var bestDistance = double.MaxValue;

        foreach (var (index, colour) in candidates)
        {
            var dr = colour.R - r;
            var dg = colour.G - g;
            var db = colour.B - b;
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Where the original's top-left corner lands for a given anchor — public so the Resize
    /// dialog can pre-fill its per-edge distribution boxes from the anchor the user clicked
    /// (the two are the same thing expressed differently, not two separate behaviors).
    /// </summary>
    public static (int OffsetX, int OffsetY) AnchorOffset(ResizeAnchor anchor, int sourceWidth, int sourceHeight, int newWidth, int newHeight)
    {
        var offsetX = anchor switch
        {
            ResizeAnchor.TopLeft or ResizeAnchor.MiddleLeft or ResizeAnchor.BottomLeft => 0,
            ResizeAnchor.TopRight or ResizeAnchor.MiddleRight or ResizeAnchor.BottomRight => newWidth - sourceWidth,
            _ => (newWidth - sourceWidth) / 2,
        };
        var offsetY = anchor switch
        {
            ResizeAnchor.TopLeft or ResizeAnchor.TopCenter or ResizeAnchor.TopRight => 0,
            ResizeAnchor.BottomLeft or ResizeAnchor.BottomCenter or ResizeAnchor.BottomRight => newHeight - sourceHeight,
            _ => (newHeight - sourceHeight) / 2,
        };
        return (offsetX, offsetY);
    }
}
