using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// RugScale Curve — neutral-first redraw for curve-heavy indexed carpet artwork.
///
/// Design rule: the output must read like a neutral (nearest-neighbour) enlargement of the
/// source. Nothing is re-fitted, re-shaped or "beautified". Only the two systematic defects of
/// a neutral non-integer resize are corrected:
///
/// 1. <b>Block staircases on fill boundaries.</b> Every used colour gets an exact signed
///    distance field on the source grid (anisotropic, from warp/weft quality). A target pixel takes
///    the colour whose field is highest at its sample point, but the neutral colour receives a
///    <see cref="NeutralBias"/> head start, so a boundary can only move by a fraction of a source
///    pixel towards the smooth contour. Shapes, tips, teeth and thin features stay where the
///    designer put them.
/// 2. <b>Uneven Pixel-Cord weight.</b> A one-pixel source cord becomes an alternating one/two
///    pixel cord under a 1.6x neutral resize. Protected cord colours are therefore not resampled;
///    the source cord graph is replayed in the target at the target pen size: every source cord
///    cell maps to its target centre and 4-adjacent source cells are joined by straight target runs,
///    giving the same 4-connected staircase drawing language as the source.
///
/// Afterwards two hard source facts are enforced: two fill colours may touch only where the
/// source lets them touch locally (otherwise a cord is missing), and exact designer symmetry of
/// the source is reproduced exactly. Output colours are always source colours.
/// </summary>
internal static class NeutralCurveScaleEngine
{
    /// <summary>Head start (source px of signed distance) for the neutral nearest colour.</summary>
    internal const float NeutralBias = 0.15f;

    internal readonly record struct NeutralCurveReport(
        int CordColours,
        int CordCellsReplayed,
        int TargetCordPixels,
        int BarrierRepairs,
        int UnresolvedContacts);

    public static void Resize(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity) =>
        _ = ResizeWithDiagnostics(
            source,
            destination,
            sourceWarpDensity,
            sourceWeftDensity,
            targetWarpDensity,
            targetWeftDensity);

    internal static NeutralCurveReport ResizeWithDiagnostics(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
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

        if (w == W &&
            h == H)
        {
            Commit(
                src,
                destination);
            return default;
        }

        var used =
            UsedColours(
                src);

        if (used.Count <= 1)
        {
            var only =
                new byte[W * H];
            Array.Fill(
                only,
                used.Count == 0
                    ? (byte)0
                    : used[0]);
            Commit(
                only,
                destination);
            return default;
        }

        var cords =
            ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                source);
        var isCord =
            new bool[256];

        foreach (var cord in cords)
            isCord[cord] = true;

        var thickCord =
            ThickCordMask(
                src,
                w,
                h,
                isCord);

        // ---- 1. fills: neutral-biased signed-distance contour interpolation ------------------
        var yStep =
            sourceWarpDensity > 0 &&
            sourceWeftDensity > 0
                ? sourceWarpDensity /
                  (float)sourceWeftDensity
                : 1f;
        var fields =
            new float[256][];

        foreach (var color in used)
        {
            fields[color] =
                SignedDistanceField(
                    src,
                    w,
                    h,
                    color,
                    yStep);
        }

        var target =
            new byte[W * H];
        var seen =
            new bool[256];
        Span<byte> candidates =
            stackalloc byte[16];

        for (var ty = 0;
             ty < H;
             ty++)
        {
            var sv =
                (ty + 0.5) *
                h /
                H;
            var v =
                sv -
                0.5;
            var nearestY =
                Math.Clamp(
                    (int)Math.Floor(
                        sv),
                    0,
                    h - 1);
            var y0 =
                (int)Math.Floor(
                    v);
            var fy =
                (float)(v - y0);

            for (var tx = 0;
                 tx < W;
                 tx++)
            {
                var su =
                    (tx + 0.5) *
                    w /
                    W;
                var u =
                    su -
                    0.5;
                var nearestX =
                    Math.Clamp(
                        (int)Math.Floor(
                            su),
                        0,
                        w - 1);
                var nearestIndex =
                    nearestY * w +
                    nearestX;
                var neutral =
                    src[nearestIndex];
                var x0 =
                    (int)Math.Floor(
                        u);
                var fx =
                    (float)(u - x0);

                // Colours that can own this sample: the 4x4 source neighbourhood.
                var count = 0;

                for (var sy = y0 - 1;
                     sy <= y0 + 2;
                     sy++)
                {
                    var cy =
                        Math.Clamp(
                            sy,
                            0,
                            h - 1);

                    for (var sx = x0 - 1;
                         sx <= x0 + 2;
                         sx++)
                    {
                        var cx =
                            Math.Clamp(
                                sx,
                                0,
                                w - 1);
                        var color =
                            src[cy * w + cx];

                        if (seen[color])
                            continue;

                        seen[color] = true;

                        // Thin cords are replayed as line work later; the fill layer only keeps
                        // genuinely wide cord areas.
                        if (isCord[color] &&
                            !(color == neutral &&
                              thickCord[nearestIndex]))
                        {
                            continue;
                        }

                        candidates[count++] =
                            color;
                    }
                }

                for (var sy = y0 - 1;
                     sy <= y0 + 2;
                     sy++)
                {
                    var cy =
                        Math.Clamp(
                            sy,
                            0,
                            h - 1);

                    for (var sx = x0 - 1;
                         sx <= x0 + 2;
                         sx++)
                    {
                        seen[src[cy * w + Math.Clamp(sx, 0, w - 1)]] = false;
                    }
                }

                var best =
                    neutral;
                var bestScore =
                    float.NegativeInfinity;

                for (var k = 0;
                     k < count;
                     k++)
                {
                    var color =
                        candidates[k];
                    var score =
                        SampleBilinear(
                            fields[color],
                            w,
                            h,
                            x0,
                            y0,
                            fx,
                            fy);

                    if (color == neutral)
                        score += NeutralBias;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = color;
                    }
                }

                target[ty * W + tx] =
                    best;
            }
        }

        // ---- 2. cords: replay the source cord graph at target pen size -----------------------
        var penX =
            PenSize(
                sourceWarpDensity,
                targetWarpDensity);
        var penY =
            PenSize(
                sourceWeftDensity,
                targetWeftDensity);
        var replayed = 0;

        int TargetX(
            int x) =>
            Math.Clamp(
                (int)Math.Round(
                    (x + 0.5) *
                    W /
                    w -
                    0.5,
                    MidpointRounding.AwayFromZero),
                0,
                W - 1);

        int TargetY(
            int y) =>
            Math.Clamp(
                (int)Math.Round(
                    (y + 0.5) *
                    H /
                    h -
                    0.5,
                    MidpointRounding.AwayFromZero),
                0,
                H - 1);

        void Plot(
            int x,
            int y,
            byte color)
        {
            var left =
                x -
                (penX - 1) / 2;
            var top =
                y -
                (penY - 1) / 2;

            for (var py = top;
                 py < top + penY;
                 py++)
            {
                if (py < 0 ||
                    py >= H)
                {
                    continue;
                }

                for (var px = left;
                     px < left + penX;
                     px++)
                {
                    if (px >= 0 &&
                        px < W)
                    {
                        target[py * W + px] =
                            color;
                    }
                }
            }
        }

        void Run(
            int x1,
            int y1,
            int x2,
            int y2,
            byte color)
        {
            // Axis-aligned run (callers only pass horizontal or vertical pairs).
            if (y1 == y2)
            {
                for (var x = Math.Min(x1, x2);
                     x <= Math.Max(x1, x2);
                     x++)
                {
                    Plot(
                        x,
                        y1,
                        color);
                }

                return;
            }

            for (var y = Math.Min(y1, y2);
                 y <= Math.Max(y1, y2);
                 y++)
            {
                Plot(
                    x1,
                    y,
                    color);
            }
        }

        for (var y = 0;
             y < h;
             y++)
        {
            for (var x = 0;
                 x < w;
                 x++)
            {
                var color =
                    src[y * w + x];

                if (!isCord[color])
                    continue;

                replayed++;

                var px =
                    TargetX(
                        x);
                var py =
                    TargetY(
                        y);

                Plot(
                    px,
                    py,
                    color);

                var right =
                    x + 1 < w &&
                    src[y * w + x + 1] == color;
                var down =
                    y + 1 < h &&
                    src[(y + 1) * w + x] == color;

                if (right)
                {
                    Run(
                        px,
                        py,
                        TargetX(
                            x + 1),
                        py,
                        color);
                }

                if (down)
                {
                    Run(
                        px,
                        py,
                        px,
                        TargetY(
                            y + 1),
                        color);
                }

                // Diagonal-only joints: join with an L so the target cord stays 4-connected.
                for (var dx = -1;
                     dx <= 1;
                     dx += 2)
                {
                    var nx =
                        x + dx;

                    if (nx < 0 ||
                        nx >= w ||
                        y + 1 >= h ||
                        src[(y + 1) * w + nx] != color ||
                        src[y * w + nx] == color ||
                        down)
                    {
                        continue;
                    }

                    var qx =
                        TargetX(
                            nx);
                    var qy =
                        TargetY(
                            y + 1);

                    Run(
                        px,
                        py,
                        qx,
                        py,
                        color);
                    Run(
                        qx,
                        py,
                        qx,
                        qy,
                        color);
                }
            }
        }

        // ---- 3. separators: fills may touch only where the source lets them touch locally ----
        var contacts =
            new CurveFillRibbonFidelityGuard.SourceContacts(
                source,
                CurveFillRibbonFidelityGuard.SourceAdjacencyCounts(
                    source),
                W,
                H,
                cords);
        var (repairs, unresolved) =
            RepairContacts(
                target,
                W,
                H,
                contacts,
                cords,
                isCord);

        // ---- 4. exact designer symmetry -----------------------------------------------------
        PreserveExactSymmetry(
            src,
            w,
            h,
            target,
            W,
            H);

        Commit(
            target,
            destination);

        return new NeutralCurveReport(
            cords.Count,
            replayed,
            target.Count(value => isCord[value]),
            repairs,
            unresolved);
    }

    private static int PenSize(
        int sourceDensity,
        int targetDensity) =>
        sourceDensity > 0 &&
        targetDensity > 0
            ? Math.Max(
                1,
                (int)Math.Round(
                    targetDensity /
                    (double)sourceDensity,
                    MidpointRounding.AwayFromZero))
            : 1;

    private static List<byte> UsedColours(
        byte[] pixels)
    {
        var present =
            new bool[256];

        foreach (var value in pixels)
            present[value] = true;

        var result =
            new List<byte>();

        for (var color = 0;
             color < 256;
             color++)
        {
            if (present[color])
                result.Add((byte)color);
        }

        return result;
    }

    /// <summary>Cord cells that are at least two cells thick in every direction: real cord areas.</summary>
    private static bool[] ThickCordMask(
        byte[] src,
        int w,
        int h,
        bool[] isCord)
    {
        var mask =
            new bool[src.Length];
        ReadOnlySpan<(int Dx, int Dy)> axes =
        [
            (1, 0),
            (0, 1),
            (1, 1),
            (1, -1),
        ];

        for (var y = 0;
             y < h;
             y++)
        {
            for (var x = 0;
                 x < w;
                 x++)
            {
                var color =
                    src[y * w + x];

                if (!isCord[color])
                    continue;

                var thick = true;

                foreach (var (dx, dy) in axes)
                {
                    var forward =
                        x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h &&
                        src[(y + dy) * w + x + dx] == color;
                    var backward =
                        x - dx >= 0 && x - dx < w && y - dy >= 0 && y - dy < h &&
                        src[(y - dy) * w + x - dx] == color;

                    if (!forward &&
                        !backward)
                    {
                        thick = false;
                        break;
                    }
                }

                mask[y * w + x] =
                    thick;
            }
        }

        return mask;
    }

    /// <summary>
    /// Signed distance (source px, anisotropic) from pixel centres to the colour's boundary:
    /// positive inside, negative outside, zero half-way between unlike neighbours.
    /// </summary>
    private static float[] SignedDistanceField(
        byte[] src,
        int w,
        int h,
        byte color,
        float yStep)
    {
        var inside =
            new bool[src.Length];

        for (var index = 0;
             index < src.Length;
             index++)
        {
            inside[index] =
                src[index] == color;
        }

        var toOutside =
            SquaredDistanceTo(
                inside,
                w,
                h,
                featureIsInside: false,
                yStep);
        var toInside =
            SquaredDistanceTo(
                inside,
                w,
                h,
                featureIsInside: true,
                yStep);
        var field =
            new float[src.Length];

        for (var index = 0;
             index < src.Length;
             index++)
        {
            field[index] =
                inside[index]
                    ? MathF.Sqrt(toOutside[index]) - 0.5f
                    : -(MathF.Sqrt(toInside[index]) - 0.5f);
        }

        return field;
    }

    /// <summary>Exact squared Euclidean distance transform (Felzenszwalb-Huttenlocher), anisotropic in y.</summary>
    private static float[] SquaredDistanceTo(
        bool[] inside,
        int w,
        int h,
        bool featureIsInside,
        float yStep)
    {
        var grid =
            new double[inside.Length];

        for (var index = 0;
             index < inside.Length;
             index++)
        {
            grid[index] =
                inside[index] == featureIsInside
                    ? 0d
                    : double.PositiveInfinity;
        }

        var length =
            Math.Max(
                w,
                h);
        var f =
            new double[length];
        var d =
            new double[length];
        var v =
            new int[length];
        var z =
            new double[length + 1];
        var columnWeight =
            (double)yStep *
            yStep;

        for (var x = 0;
             x < w;
             x++)
        {
            for (var y = 0;
                 y < h;
                 y++)
            {
                f[y] =
                    grid[y * w + x];
            }

            Transform1D(
                f,
                h,
                columnWeight,
                d,
                v,
                z);

            for (var y = 0;
                 y < h;
                 y++)
            {
                grid[y * w + x] =
                    d[y];
            }
        }

        for (var y = 0;
             y < h;
             y++)
        {
            Array.Copy(
                grid,
                y * w,
                f,
                0,
                w);

            Transform1D(
                f,
                w,
                1d,
                d,
                v,
                z);

            Array.Copy(
                d,
                0,
                grid,
                y * w,
                w);
        }

        var result =
            new float[grid.Length];

        for (var index = 0;
             index < grid.Length;
             index++)
        {
            result[index] =
                double.IsPositiveInfinity(grid[index])
                    ? 1e12f
                    : (float)grid[index];
        }

        return result;
    }

    /// <summary>Lower envelope of parabolas weight*(q-r)^2 + f[r]; infinite samples are skipped.</summary>
    private static void Transform1D(
        double[] f,
        int n,
        double weight,
        double[] d,
        int[] v,
        double[] z)
    {
        var k = -1;

        for (var q = 0;
             q < n;
             q++)
        {
            if (double.IsPositiveInfinity(f[q]))
                continue;

            if (k < 0)
            {
                k = 0;
                v[0] = q;
                z[0] = double.NegativeInfinity;
                z[1] = double.PositiveInfinity;
                continue;
            }

            double s;

            while (true)
            {
                var r =
                    v[k];
                s =
                    ((f[q] + weight * q * q) -
                     (f[r] + weight * r * r)) /
                    (2d * weight * (q - r));

                if (s > z[k] ||
                    k == 0)
                {
                    break;
                }

                k--;
            }

            if (s <= z[k])
            {
                // Parabola q dominates everything to the left as well.
                v[k] = q;
                z[k] = double.NegativeInfinity;
                z[k + 1] = double.PositiveInfinity;
                continue;
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        if (k < 0)
        {
            Array.Fill(
                d,
                double.PositiveInfinity,
                0,
                n);
            return;
        }

        var j = 0;

        for (var q = 0;
             q < n;
             q++)
        {
            while (z[j + 1] < q)
                j++;

            var r =
                v[j];
            d[q] =
                weight *
                (q - r) *
                (q - r) +
                f[r];
        }
    }

    private static float SampleBilinear(
        float[] field,
        int w,
        int h,
        int x0,
        int y0,
        float fx,
        float fy)
    {
        var xa =
            Math.Clamp(
                x0,
                0,
                w - 1);
        var xb =
            Math.Clamp(
                x0 + 1,
                0,
                w - 1);
        var ya =
            Math.Clamp(
                y0,
                0,
                h - 1);
        var yb =
            Math.Clamp(
                y0 + 1,
                0,
                h - 1);

        var top =
            field[ya * w + xa] +
            (field[ya * w + xb] - field[ya * w + xa]) *
            fx;
        var bottom =
            field[yb * w + xa] +
            (field[yb * w + xb] - field[yb * w + xa]) *
            fx;

        return top +
               (bottom - top) *
               fy;
    }

    /// <summary>
    /// Every 4-contact between two fill colours that the source does not allow locally means a
    /// cord or a third colour is missing there. Prefer moving one pixel to the other side (it
    /// landed on the wrong side of a replayed cord); otherwise draw the cord between them.
    /// </summary>
    private static (int Repairs, int Unresolved) RepairContacts(
        byte[] target,
        int W,
        int H,
        CurveFillRibbonFidelityGuard.SourceContacts contacts,
        IReadOnlySet<byte> cords,
        bool[] isCord)
    {
        var repairs = 0;
        var unresolved = 0;
        var attempts =
            new byte[target.Length];
        var queue =
            new Queue<int>(
                Enumerable.Range(
                    0,
                    target.Length));
        var orderedCords =
            cords
                .OrderBy(value => value)
                .ToArray();

        bool Illegal(
            byte a,
            byte b,
            int at) =>
            a != b &&
            !isCord[a] &&
            !isCord[b] &&
            !contacts.Allowed(
                a,
                b,
                at);

        bool LegalAt(
            int index,
            byte color)
        {
            Span<int> around =
                stackalloc int[4];
            var count =
                Neighbours(
                    index,
                    W,
                    H,
                    around);

            for (var n = 0;
                 n < count;
                 n++)
            {
                if (Illegal(
                        color,
                        target[around[n]],
                        index))
                {
                    return false;
                }
            }

            return true;
        }

        void Set(
            int index,
            byte color)
        {
            target[index] =
                color;
            attempts[index]++;
            repairs++;
            queue.Enqueue(
                index);

            Span<int> around =
                stackalloc int[4];
            var count =
                Neighbours(
                    index,
                    W,
                    H,
                    around);

            for (var n = 0;
                 n < count;
                 n++)
            {
                queue.Enqueue(
                    around[n]);
            }
        }

        Span<int> local =
            stackalloc int[4];

        while (queue.Count > 0)
        {
            var p =
                queue.Dequeue();
            var count =
                Neighbours(
                    p,
                    W,
                    H,
                    local);

            for (var n = 0;
                 n < count;
                 n++)
            {
                var q =
                    local[n];
                var a =
                    target[p];
                var b =
                    target[q];

                if (!Illegal(
                        a,
                        b,
                        p))
                {
                    continue;
                }

                if (attempts[p] < 3 &&
                    LegalAt(
                        p,
                        b))
                {
                    Set(
                        p,
                        b);
                    break;
                }

                if (attempts[q] < 3 &&
                    LegalAt(
                        q,
                        a))
                {
                    Set(
                        q,
                        a);
                    break;
                }

                var cord = -1;

                foreach (var candidate in orderedCords)
                {
                    if (contacts.Global[a, candidate] > 0 &&
                        contacts.Global[b, candidate] > 0)
                    {
                        cord = candidate;
                        break;
                    }
                }

                if (cord >= 0 &&
                    attempts[p] < 3)
                {
                    Set(
                        p,
                        (byte)cord);
                    break;
                }

                unresolved++;
            }
        }

        return (repairs, unresolved);
    }

    private static int Neighbours(
        int index,
        int width,
        int height,
        Span<int> result)
    {
        var x =
            index % width;
        var y =
            index / width;
        var count = 0;

        if (x > 0)
            result[count++] = index - 1;

        if (x + 1 < width)
            result[count++] = index + 1;

        if (y > 0)
            result[count++] = index - width;

        if (y + 1 < height)
            result[count++] = index + width;

        return count;
    }

    private static void PreserveExactSymmetry(
        byte[] src,
        int w,
        int h,
        byte[] target,
        int W,
        int H)
    {
        var leftRight = true;
        var topBottom = true;

        for (var y = 0;
             y < h && (leftRight || topBottom);
             y++)
        {
            for (var x = 0;
                 x < w;
                 x++)
            {
                var value =
                    src[y * w + x];

                if (value != src[y * w + (w - 1 - x)])
                    leftRight = false;

                if (value != src[(h - 1 - y) * w + x])
                    topBottom = false;
            }
        }

        if (leftRight)
        {
            for (var y = 0;
                 y < H;
                 y++)
            {
                for (var x = 0;
                     x < W / 2;
                     x++)
                {
                    target[y * W + (W - 1 - x)] =
                        target[y * W + x];
                }
            }
        }

        if (topBottom)
        {
            for (var y = 0;
                 y < H / 2;
                 y++)
            {
                Array.Copy(
                    target,
                    y * W,
                    target,
                    (H - 1 - y) * W,
                    W);
            }
        }
    }

    private static void Commit(
        byte[] pixels,
        DesignDocument destination)
    {
        for (var y = 0;
             y < destination.Height;
             y++)
        {
            for (var x = 0;
                 x < destination.Width;
                 x++)
            {
                destination.SetPixel(
                    x,
                    y,
                    pixels[y * destination.Width + x]);
            }
        }
    }
}
