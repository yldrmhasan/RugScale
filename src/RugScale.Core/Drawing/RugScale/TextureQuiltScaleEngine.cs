using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// RugScale Texture — resizing for abstract / distressed designs (washed grounds, scratch streaks,
/// one-knot speckle, ragged splotches).
///
/// In such designs the grain is the drawing: one-knot speckle and one-row scratch lines. Any
/// pixel-mapping resize scales the grain with the design (1.6x enlargement turns one-knot speckle
/// into 1-2 knot blobs, a 0.8x shrink drops every fifth row of streaks), so the carpet reads coarser
/// or patchier than the original. Here the large structure is scaled and the grain is not:
///
/// 1. structure = a 7x7 majority filter of the source (splotches, bands, frames); it is scaled
///    like a neutral resize, so bands keep their proportional width and edges stay in place;
/// 2. a quilt map: the target is covered by overlapping <see cref="BlockSize"/> blocks, each copied
///    1:1 from the source within <see cref="SearchRadius"/> px of where its centre falls after
///    scaling; candidates are ranked by how well their structure layout (four quadrants) matches
///    the scaled structure, then by how well they continue the pixels already placed, and the
///    seam in each overlap follows the minimum-mismatch path;
/// 3. grain (source pixels that differ from their structure) is carried 1:1 through the quilt map
///    onto target pixels with the same structure colour; where the map lands on another structure
///    (near a splotch edge) the neutral pixel is used.
/// Output colours are always source colours. A one-pixel technical marker column or row on the
/// design edge is kept on the target edge.
/// </summary>
internal static class TextureQuiltScaleEngine
{
    /// <summary>Target block edge (px). Smaller blocks follow structure more closely, larger ones keep longer streaks intact.</summary>
    internal const int BlockSize = 24;

    internal const int Overlap = 6;

    /// <summary>Source px around the exact scaled position searched for a block.</summary>
    internal const int SearchRadius = 16;

    /// <summary>Cost per mismatched structure pixel between a candidate's quadrants and the scaled structure.</summary>
    internal const double LayoutWeight = 1.0;

    /// <summary>Candidates (best layout) whose seam fit is evaluated.</summary>
    internal const int Shortlist = 12;

    /// <summary>Cost per source px of distance from the exact scaled position.</summary>
    internal const double PositionWeight = 0.5;

    /// <summary>Half-size of the majority window that separates structure from grain.</summary>
    internal const int StructureRadius = 3;

    public static void Resize(
        DesignDocument source,
        DesignDocument destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var w =
            source.Width;
        var h =
            source.Height;
        var W =
            destination.Width;
        var H =
            destination.Height;
        var src =
            CurveFillRibbonFidelityGuard.Snapshot(
                source);

        var markers =
            StripEdgeMarkers(
                src,
                w,
                h);
        byte[] target;

        if (w < BlockSize ||
            h < BlockSize ||
            W < BlockSize ||
            H < BlockSize)
        {
            target =
                Nearest(
                    src,
                    w,
                    h,
                    W,
                    H);
        }
        else
        {
            target =
                Compose(
                    src,
                    w,
                    h,
                    W,
                    H);
        }

        foreach (var marker in markers)
            marker.Paint(target, W, H);

        for (var y = 0;
             y < H;
             y++)
        {
            for (var x = 0;
                 x < W;
                 x++)
            {
                destination.SetPixel(
                    x,
                    y,
                    target[y * W + x]);
            }
        }
    }

    private static int[] Quilt(
        byte[] src,
        byte[] layoutSource,
        byte[] guide,
        int w,
        int h,
        int W,
        int H)
    {
        var target =
            new byte[W * H];
        var map =
            new int[W * H];
        var placed =
            new bool[W * H];
        var scaleX =
            w /
            (double)W;
        var scaleY =
            h /
            (double)H;
        var step =
            BlockSize - Overlap;

        // Colour index -> dense slot, and per-slot summed-area tables of the source.
        var slotOf =
            Enumerable.Repeat(-1, 256).ToArray();
        var slots = 0;

        foreach (var color in src)
        {
            if (slotOf[color] < 0)
                slotOf[color] = slots++;
        }

        var stride =
            w + 1;
        var tables =
            new int[slots][];

        for (var k = 0;
             k < slots;
             k++)
        {
            tables[k] = new int[stride * (h + 1)];
        }

        for (var y = 0;
             y < h;
             y++)
        {
            for (var k = 0;
                 k < slots;
                 k++)
            {
                var table =
                    tables[k];
                var rowSum = 0;

                for (var x = 0;
                     x < w;
                     x++)
                {
                    if (slotOf[layoutSource[y * w + x]] == k)
                        rowSum++;

                    table[(y + 1) * stride + x + 1] =
                        table[y * stride + x + 1] +
                        rowSum;
                }
            }
        }

        int Count(
            int k,
            int x0,
            int y0,
            int x1,
            int y1) =>
            tables[k][y1 * stride + x1] -
            tables[k][y0 * stride + x1] -
            tables[k][y1 * stride + x0] +
            tables[k][y0 * stride + x0];

        var take =
            new bool[BlockSize * BlockSize];
        var error =
            new int[BlockSize * BlockSize];
        var guideFeature =
            new int[4 * slots];
        var shortlist =
            new List<(double Cost, int X, int Y)>();

        for (var ty0 = 0;
             ty0 < H;
             ty0 += step)
        {
            for (var tx0 = 0;
                 tx0 < W;
                 tx0 += step)
            {
                var bw =
                    Math.Min(
                        BlockSize,
                        W - tx0);
                var bh =
                    Math.Min(
                        BlockSize,
                        H - ty0);
                var hw =
                    bw / 2;
                var hh =
                    bh / 2;

                // Guide colour counts in the four quadrants of the block.
                Array.Clear(guideFeature);

                for (var y = 0;
                     y < bh;
                     y++)
                {
                    for (var x = 0;
                         x < bw;
                         x++)
                    {
                        var quadrant =
                            (y < hh ? 0 : 2) +
                            (x < hw ? 0 : 1);
                        guideFeature[
                            quadrant * slots +
                            slotOf[guide[(ty0 + y) * W + tx0 + x]]]++;
                    }
                }

                var idealX =
                    (int)Math.Round(
                        (tx0 + bw / 2.0) * scaleX -
                        bw / 2.0);
                var idealY =
                    (int)Math.Round(
                        (ty0 + bh / 2.0) * scaleY -
                        bh / 2.0);

                // Stage 1: every position within the search window, scored by how well its
                // colour layout matches the guide (plus a small pull to the exact position).
                shortlist.Clear();

                for (var sy = Math.Max(0, idealY - SearchRadius);
                     sy <= Math.Min(h - bh, idealY + SearchRadius);
                     sy++)
                {
                    for (var sx = Math.Max(0, idealX - SearchRadius);
                         sx <= Math.Min(w - bw, idealX + SearchRadius);
                         sx++)
                    {
                        var layout = 0;

                        for (var k = 0;
                             k < slots;
                             k++)
                        {
                            layout +=
                                Math.Abs(Count(k, sx, sy, sx + hw, sy + hh) - guideFeature[k]) +
                                Math.Abs(Count(k, sx + hw, sy, sx + bw, sy + hh) - guideFeature[slots + k]) +
                                Math.Abs(Count(k, sx, sy + hh, sx + hw, sy + bh) - guideFeature[2 * slots + k]) +
                                Math.Abs(Count(k, sx + hw, sy + hh, sx + bw, sy + bh) - guideFeature[3 * slots + k]);
                        }

                        var cost =
                            LayoutWeight * layout / 2.0 +
                            PositionWeight *
                            (Math.Abs(sx - idealX) + Math.Abs(sy - idealY));

                        if (shortlist.Count < Shortlist)
                        {
                            shortlist.Add((cost, sx, sy));
                        }
                        else
                        {
                            var worst = 0;

                            for (var k = 1;
                                 k < shortlist.Count;
                                 k++)
                            {
                                if (shortlist[k].Cost > shortlist[worst].Cost)
                                    worst = k;
                            }

                            if (cost < shortlist[worst].Cost)
                                shortlist[worst] = (cost, sx, sy);
                        }
                    }
                }

                // Stage 2: of those, the one that best continues what is already placed.
                var bestX =
                    Math.Clamp(idealX, 0, w - bw);
                var bestY =
                    Math.Clamp(idealY, 0, h - bh);
                var bestCost =
                    double.MaxValue;

                foreach (var (layoutCost, sx, sy) in shortlist.OrderBy(item => item.Cost))
                {
                    if (layoutCost >= bestCost)
                        break;

                    var cost =
                        layoutCost +
                        OverlapMismatch(
                            src,
                            w,
                            target,
                            placed,
                            W,
                            tx0,
                            ty0,
                            bw,
                            bh,
                            sx,
                            sy,
                            bestCost - layoutCost);

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestX = sx;
                        bestY = sy;
                    }
                }

                // Seams: minimum-mismatch cuts through the left and top overlaps.
                for (var y = 0;
                     y < bh;
                     y++)
                {
                    for (var x = 0;
                         x < bw;
                         x++)
                    {
                        var index =
                            (ty0 + y) * W + tx0 + x;
                        take[y * BlockSize + x] = true;
                        error[y * BlockSize + x] =
                            placed[index] &&
                            target[index] != src[(bestY + y) * w + bestX + x]
                                ? 1
                                : 0;
                    }
                }

                if (tx0 > 0)
                {
                    CutVertical(
                        error,
                        take,
                        Math.Min(Overlap, bw),
                        bh);
                }

                if (ty0 > 0)
                {
                    CutHorizontal(
                        error,
                        take,
                        bw,
                        Math.Min(Overlap, bh));
                }

                for (var y = 0;
                     y < bh;
                     y++)
                {
                    for (var x = 0;
                         x < bw;
                         x++)
                    {
                        var index =
                            (ty0 + y) * W + tx0 + x;

                        if (placed[index] &&
                            !take[y * BlockSize + x])
                        {
                            continue;
                        }

                        map[index] =
                            (bestY + y) * w + bestX + x;
                        target[index] =
                            src[map[index]];
                        placed[index] = true;
                    }
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Structure (splotches, bands, frames: anything wider than the majority window) is scaled
    /// like any neutral resize; grain (speckle and streaks: the pixels that differ from their
    /// structure) is transplanted 1:1 through the quilt map, and only onto target pixels whose
    /// structure colour matches the background the grain sat on in the source.
    /// </summary>
    private static byte[] Compose(
        byte[] src,
        int w,
        int h,
        int W,
        int H)
    {
        var structure =
            Majority(
                src,
                w,
                h,
                StructureRadius);
        var scaled =
            Nearest(
                structure,
                w,
                h,
                W,
                H);
        // Blocks are chosen by how well their structure matches the scaled structure, so the grain
        // they carry sat on the same background in the source.
        var map =
            Quilt(
                src,
                structure,
                scaled,
                w,
                h,
                W,
                H);
        var neutral =
            Nearest(
                src,
                w,
                h,
                W,
                H);
        var target =
            new byte[W * H];

        for (var index = 0;
             index < target.Length;
             index++)
        {
            var at =
                map[index];
            var grain =
                src[at] != structure[at];

            if (structure[at] == scaled[index])
            {
                target[index] =
                    grain
                        ? src[at]
                        : scaled[index];
            }
            else
            {
                // The quilt landed on another structure (near a splotch edge): fall back to the
                // neutral pixel, which still carries the grain of this place.
                target[index] =
                    neutral[index];
            }
        }

        return target;
    }

    /// <summary>Most frequent colour in the (2r+1)^2 window; ties keep the pixel's own colour.</summary>
    private static byte[] Majority(
        byte[] src,
        int w,
        int h,
        int radius)
    {
        var slotOf =
            Enumerable.Repeat(-1, 256).ToArray();
        var colors =
            new List<byte>();

        foreach (var color in src)
        {
            if (slotOf[color] < 0)
            {
                slotOf[color] = colors.Count;
                colors.Add(color);
            }
        }

        var k =
            colors.Count;
        var stride =
            w + 1;
        var tables =
            new int[k][];

        for (var slot = 0;
             slot < k;
             slot++)
        {
            tables[slot] = new int[stride * (h + 1)];
        }

        for (var y = 0;
             y < h;
             y++)
        {
            var rowSums =
                new int[k];

            for (var x = 0;
                 x < w;
                 x++)
            {
                rowSums[slotOf[src[y * w + x]]]++;

                for (var slot = 0;
                     slot < k;
                     slot++)
                {
                    tables[slot][(y + 1) * stride + x + 1] =
                        tables[slot][y * stride + x + 1] +
                        rowSums[slot];
                }
            }
        }

        var result =
            new byte[src.Length];

        for (var y = 0;
             y < h;
             y++)
        {
            var y0 =
                Math.Max(0, y - radius);
            var y1 =
                Math.Min(h, y + radius + 1);

            for (var x = 0;
                 x < w;
                 x++)
            {
                var x0 =
                    Math.Max(0, x - radius);
                var x1 =
                    Math.Min(w, x + radius + 1);
                var own =
                    src[y * w + x];
                var best =
                    own;
                var bestCount = -1;

                for (var slot = 0;
                     slot < k;
                     slot++)
                {
                    var table =
                        tables[slot];
                    var count =
                        table[y1 * stride + x1] -
                        table[y0 * stride + x1] -
                        table[y1 * stride + x0] +
                        table[y0 * stride + x0];

                    if (count > bestCount ||
                        (count == bestCount &&
                         colors[slot] == own))
                    {
                        bestCount = count;
                        best = colors[slot];
                    }
                }

                result[y * w + x] = best;
            }
        }

        return result;
    }

    /// <summary>Mismatching pixels between a candidate block and what is already placed.</summary>
    private static int OverlapMismatch(
        byte[] src,
        int w,
        byte[] target,
        bool[] placed,
        int W,
        int tx0,
        int ty0,
        int bw,
        int bh,
        int sx,
        int sy,
        double budget)
    {
        var mismatch = 0;
        var columns =
            tx0 > 0
                ? Math.Min(Overlap, bw)
                : 0;
        var rows =
            ty0 > 0
                ? Math.Min(Overlap, bh)
                : 0;

        for (var y = 0;
             y < bh;
             y++)
        {
            var row =
                (ty0 + y) * W + tx0;
            var sourceRow =
                (sy + y) * w + sx;
            var width =
                y < rows
                    ? bw
                    : columns;

            for (var x = 0;
                 x < width;
                 x++)
            {
                if (placed[row + x] &&
                    target[row + x] != src[sourceRow + x])
                {
                    mismatch++;
                }
            }

            if (mismatch > budget)
                return mismatch;
        }

        return mismatch;
    }

    /// <summary>Vertical minimum-error path through the left overlap; pixels left of it keep the old content.</summary>
    private static void CutVertical(
        int[] error,
        bool[] take,
        int columns,
        int rows)
    {
        var cost =
            new double[rows * columns];
        var from =
            new int[rows * columns];

        double Bias(
            int x) =>
            0.01 *
            Math.Abs(
                x -
                (columns - 1) / 2.0);

        for (var x = 0;
             x < columns;
             x++)
        {
            cost[x] =
                error[x] +
                Bias(x);
        }

        for (var y = 1;
             y < rows;
             y++)
        {
            for (var x = 0;
                 x < columns;
                 x++)
            {
                var best = x;

                for (var px = Math.Max(0, x - 1);
                     px <= Math.Min(columns - 1, x + 1);
                     px++)
                {
                    if (cost[(y - 1) * columns + px] <
                        cost[(y - 1) * columns + best])
                    {
                        best = px;
                    }
                }

                cost[y * columns + x] =
                    cost[(y - 1) * columns + best] +
                    error[y * BlockSize + x] +
                    Bias(x);
                from[y * columns + x] = best;
            }
        }

        var cut = 0;

        for (var x = 1;
             x < columns;
             x++)
        {
            if (cost[(rows - 1) * columns + x] <
                cost[(rows - 1) * columns + cut])
            {
                cut = x;
            }
        }

        for (var y = rows - 1;
             y >= 0;
             y--)
        {
            for (var x = 0;
                 x < cut;
                 x++)
            {
                take[y * BlockSize + x] = false;
            }

            cut =
                from[y * columns + cut];
        }
    }

    /// <summary>Horizontal minimum-error path through the top overlap; pixels above it keep the old content.</summary>
    private static void CutHorizontal(
        int[] error,
        bool[] take,
        int columns,
        int rows)
    {
        var cost =
            new double[rows * columns];
        var from =
            new int[rows * columns];

        double Bias(
            int y) =>
            0.01 *
            Math.Abs(
                y -
                (rows - 1) / 2.0);

        for (var y = 0;
             y < rows;
             y++)
        {
            cost[y * columns] =
                error[y * BlockSize] +
                Bias(y);
        }

        for (var x = 1;
             x < columns;
             x++)
        {
            for (var y = 0;
                 y < rows;
                 y++)
            {
                var best = y;

                for (var py = Math.Max(0, y - 1);
                     py <= Math.Min(rows - 1, y + 1);
                     py++)
                {
                    if (cost[py * columns + x - 1] <
                        cost[best * columns + x - 1])
                    {
                        best = py;
                    }
                }

                cost[y * columns + x] =
                    cost[best * columns + x - 1] +
                    error[y * BlockSize + x] +
                    Bias(y);
                from[y * columns + x] = best;
            }
        }

        var cut = 0;

        for (var y = 1;
             y < rows;
             y++)
        {
            if (cost[y * columns + columns - 1] <
                cost[cut * columns + columns - 1])
            {
                cut = y;
            }
        }

        for (var x = columns - 1;
             x >= 0;
             x--)
        {
            for (var y = 0;
                 y < cut;
                 y++)
            {
                take[y * BlockSize + x] = false;
            }

            cut =
                from[cut * columns + x];
        }
    }

    private static byte[] Nearest(
        byte[] src,
        int w,
        int h,
        int W,
        int H)
    {
        var target =
            new byte[W * H];

        for (var y = 0;
             y < H;
             y++)
        {
            var sy =
                Math.Min(
                    h - 1,
                    (int)((y + 0.5) * h / H));

            for (var x = 0;
                 x < W;
                 x++)
            {
                target[y * W + x] =
                    src[sy * w + Math.Min(w - 1, (int)((x + 0.5) * w / W))];
            }
        }

        return target;
    }

    private readonly record struct EdgeMarker(
        bool Column,
        bool Last,
        byte Color)
    {
        internal void Paint(
            byte[] target,
            int W,
            int H)
        {
            if (Column)
            {
                var x =
                    Last
                        ? W - 1
                        : 0;

                for (var y = 0;
                     y < H;
                     y++)
                {
                    target[y * W + x] = Color;
                }
            }
            else
            {
                var y =
                    Last
                        ? H - 1
                        : 0;

                for (var x = 0;
                     x < W;
                     x++)
                {
                    target[y * W + x] = Color;
                }
            }
        }
    }

    /// <summary>
    /// A colour that fills exactly one whole edge column (or row) and appears nowhere else is a
    /// technical marker, not texture: it is replaced by its neighbour for the quilt and painted
    /// back on the target edge.
    /// </summary>
    private static List<EdgeMarker> StripEdgeMarkers(
        byte[] src,
        int w,
        int h)
    {
        var markers =
            new List<EdgeMarker>();
        var counts =
            new int[256];

        foreach (var color in src)
            counts[color]++;

        bool Uniform(
            Func<int, int> at,
            int length,
            out byte color)
        {
            color = src[at(0)];

            for (var k = 1;
                 k < length;
                 k++)
            {
                if (src[at(k)] != color)
                    return false;
            }

            return counts[color] == length;
        }

        if (w > 1)
        {
            foreach (var last in new[] { false, true })
            {
                var x =
                    last
                        ? w - 1
                        : 0;
                var neighbour =
                    last
                        ? w - 2
                        : 1;

                if (Uniform(k => k * w + x, h, out var color))
                {
                    markers.Add(new EdgeMarker(true, last, color));

                    for (var y = 0;
                         y < h;
                         y++)
                    {
                        src[y * w + x] = src[y * w + neighbour];
                    }
                }
            }
        }

        if (h > 1)
        {
            foreach (var last in new[] { false, true })
            {
                var y =
                    last
                        ? h - 1
                        : 0;
                var neighbour =
                    last
                        ? h - 2
                        : 1;

                if (Uniform(k => y * w + k, w, out var color))
                {
                    markers.Add(new EdgeMarker(false, last, color));

                    for (var x = 0;
                         x < w;
                         x++)
                    {
                        src[y * w + x] = src[neighbour * w + x];
                    }
                }
            }
        }

        return markers;
    }
}
