namespace RugScale.Core.Drawing;

/// <summary>
/// Straight fill boundaries stay exactly straight.
///
/// The neutral fill layer samples a signed distance field built on the source grid. Along a long
/// straight diagonal edge (the sides of a diamond, a chevron, a thick ruled line) that field
/// wobbles with the sampling phase, so a 1.6x target edge gets an irregular stair cadence
/// (3-1-3-2 ...) although the source edge is a perfectly regular digital line.
///
/// Every boundary between two fill colours is followed on the source as a chain of crack
/// midpoints (the mid-points of the cell edges separating the two colours; for a digital straight
/// edge they lie on the true line). Long runs that fit one line within
/// <see cref="Tolerance"/> source px are mapped to the target, and target pixels of those two
/// colours close to the line are reassigned by the side of the line their centre lies on. The
/// result is a digital straight line with a perfectly periodic cadence. Curved boundaries do not
/// fit a long line and are left to the distance field.
/// </summary>
internal static class StraightFillEdges
{
    /// <summary>Largest distance (source px) of a crack midpoint from the fitted line.</summary>
    internal const double Tolerance = 0.55;

    /// <summary>Shortest straight run (source px along the line) that is snapped.</summary>
    internal const double MinimumLength = 10;

    /// <summary>Crack points tried beyond a misfit before a straight run ends.</summary>
    internal const int LookAhead = 60;

    /// <summary>Target px on each side of the line whose colour is decided by the line.</summary>
    internal const double Reach = 1.5;

    private readonly record struct Crack(
        int CornerA,
        int CornerB,
        double X,
        double Y);

    internal static int Snap(
        byte[] source,
        int w,
        int h,
        bool[] isCord,
        byte[] target,
        int W,
        int H)
    {
        var cracks =
            new Dictionary<int, List<Crack>>();
        var corners =
            w + 1;

        void Add(
            byte a,
            byte b,
            Crack crack)
        {
            if (a == b ||
                isCord[a] ||
                isCord[b])
            {
                return;
            }

            var key =
                Math.Min(a, b) * 256 +
                Math.Max(a, b);

            if (!cracks.TryGetValue(
                    key,
                    out var list))
            {
                list = new List<Crack>();
                cracks[key] = list;
            }

            list.Add(crack);
        }

        for (var y = 0;
             y < h;
             y++)
        {
            for (var x = 0;
                 x < w;
                 x++)
            {
                var here =
                    source[y * w + x];

                if (x + 1 < w)
                {
                    // Vertical crack between (x,y) and (x+1,y).
                    Add(
                        here,
                        source[y * w + x + 1],
                        new Crack(
                            y * corners + x + 1,
                            (y + 1) * corners + x + 1,
                            x + 1,
                            y + 0.5));
                }

                if (y + 1 < h)
                {
                    // Horizontal crack between (x,y) and (x,y+1).
                    Add(
                        here,
                        source[(y + 1) * w + x],
                        new Crack(
                            (y + 1) * corners + x,
                            (y + 1) * corners + x + 1,
                            x + 0.5,
                            y + 1));
                }
            }
        }

        var snapped = 0;
        var scaleX =
            W /
            (double)w;
        var scaleY =
            H /
            (double)h;

        foreach (var (key, list) in cracks)
        {
            var a =
                (byte)(key / 256);
            var b =
                (byte)(key % 256);

            foreach (var chain in Chains(list))
            {
                foreach (var (start, end) in StraightRuns(chain))
                {
                    snapped +=
                        Apply(
                            chain,
                            start,
                            end,
                            a,
                            b,
                            source,
                            w,
                            h,
                            target,
                            W,
                            H,
                            scaleX,
                            scaleY);
                }
            }
        }

        return snapped;
    }

    /// <summary>Orders the cracks of one colour pair into chains of crack midpoints.</summary>
    private static List<List<(double X, double Y)>> Chains(
        List<Crack> cracks)
    {
        var byCorner =
            new Dictionary<int, List<int>>();

        for (var index = 0;
             index < cracks.Count;
             index++)
        {
            foreach (var corner in new[] { cracks[index].CornerA, cracks[index].CornerB })
            {
                if (!byCorner.TryGetValue(
                        corner,
                        out var list))
                {
                    list = new List<int>(2);
                    byCorner[corner] = list;
                }

                list.Add(index);
            }
        }

        var used =
            new bool[cracks.Count];
        var chains =
            new List<List<(double X, double Y)>>();

        void Walk(
            int first,
            int fromCorner)
        {
            var chain =
                new List<(double X, double Y)>();
            var current = first;
            var corner = fromCorner;

            while (current >= 0 &&
                   !used[current])
            {
                used[current] = true;
                chain.Add(
                    (cracks[current].X,
                     cracks[current].Y));
                var crack =
                    cracks[current];
                corner =
                    crack.CornerA == corner
                        ? crack.CornerB
                        : crack.CornerA;
                var around =
                    byCorner[corner];

                // Continue only through a plain corner (exactly two cracks of this pair meet).
                current =
                    around.Count == 2
                        ? (around[0] == current ? around[1] : around[0])
                        : -1;
            }

            if (chain.Count > 1)
                chains.Add(chain);
        }

        // Open chains start at corners where the boundary branches or ends.
        foreach (var (corner, around) in byCorner)
        {
            if (around.Count == 2)
                continue;

            foreach (var index in around)
            {
                if (!used[index])
                    Walk(index, corner);
            }
        }

        // Closed loops.
        for (var index = 0;
             index < cracks.Count;
             index++)
        {
            if (!used[index])
                Walk(index, cracks[index].CornerA);
        }

        return chains;
    }

    /// <summary>Greedy maximal runs of the chain that fit one line within the tolerance.</summary>
    private static List<(int Start, int End)> StraightRuns(
        List<(double X, double Y)> chain)
    {
        var runs =
            new List<(int Start, int End)>();
        var start = 0;

        while (start < chain.Count - 1)
        {
            // Grow the run as far as one line still fits. A digital line's jogs make a single
            // extra point misfit a run fitted so far, while the longer run fits again, so keep
            // looking ahead past a misfit before giving up.
            var end =
                start + 1;
            var probe =
                end;

            while (probe + 1 < chain.Count &&
                   probe + 1 - end <= LookAhead)
            {
                probe++;

                if (Fits(chain, start, probe))
                    end = probe;
            }

            var dx =
                chain[end].X -
                chain[start].X;
            var dy =
                chain[end].Y -
                chain[start].Y;

            if (Math.Sqrt(dx * dx + dy * dy) >= MinimumLength)
            {
                runs.Add((start, end));
                start = end;
            }
            else
            {
                start++;
            }
        }

        return runs;
    }

    private static bool Fits(
        List<(double X, double Y)> chain,
        int start,
        int end)
    {
        var (mx, my, ux, uy) =
            Line(
                chain,
                start,
                end);

        for (var index = start;
             index <= end;
             index++)
        {
            var distance =
                Math.Abs(
                    (chain[index].X - mx) * uy -
                    (chain[index].Y - my) * ux);

            if (distance > Tolerance)
                return false;
        }

        return true;
    }

    /// <summary>Total-least-squares line: centroid and unit direction.</summary>
    private static (double Mx, double My, double Ux, double Uy) Line(
        List<(double X, double Y)> chain,
        int start,
        int end)
    {
        double mx = 0, my = 0;
        var count =
            end - start + 1;

        for (var index = start;
             index <= end;
             index++)
        {
            mx += chain[index].X;
            my += chain[index].Y;
        }

        mx /= count;
        my /= count;
        double sxx = 0, syy = 0, sxy = 0;

        for (var index = start;
             index <= end;
             index++)
        {
            var dx =
                chain[index].X -
                mx;
            var dy =
                chain[index].Y -
                my;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }

        var angle =
            0.5 *
            Math.Atan2(
                2 * sxy,
                sxx - syy);

        return (mx, my, Math.Cos(angle), Math.Sin(angle));
    }

    private static int Apply(
        List<(double X, double Y)> chain,
        int start,
        int end,
        byte a,
        byte b,
        byte[] source,
        int w,
        int h,
        byte[] target,
        int W,
        int H,
        double scaleX,
        double scaleY)
    {
        var (mx, my, ux, uy) =
            Line(
                chain,
                start,
                end);
        var t0 =
            (chain[start].X - mx) * ux +
            (chain[start].Y - my) * uy;
        var t1 =
            (chain[end].X - mx) * ux +
            (chain[end].Y - my) * uy;

        // Which colour lies on the +normal side, read from the source next to the line middle.
        var nx = -uy;
        var ny = ux;
        var mid =
            (t0 + t1) / 2;
        var probeX =
            (int)Math.Floor(
                mx + mid * ux + nx * 0.75);
        var probeY =
            (int)Math.Floor(
                my + mid * uy + ny * 0.75);

        if (probeX < 0 ||
            probeY < 0 ||
            probeX >= w ||
            probeY >= h)
        {
            return 0;
        }

        var positive =
            source[probeY * w + probeX];

        if (positive != a &&
            positive != b)
        {
            return 0;
        }

        var negative =
            positive == a
                ? b
                : a;

        // Line in target pixel space.
        var p0 =
            (X: (mx + t0 * ux) * scaleX,
             Y: (my + t0 * uy) * scaleY);
        var p1 =
            (X: (mx + t1 * ux) * scaleX,
             Y: (my + t1 * uy) * scaleY);
        var vx =
            p1.X -
            p0.X;
        var vy =
            p1.Y -
            p0.Y;
        var length =
            Math.Sqrt(
                vx * vx +
                vy * vy);

        if (length < 2)
            return 0;

        var dirX =
            vx / length;
        var dirY =
            vy / length;

        // Target normal pointing to the same side as the source +normal.
        var tnx = -dirY;
        var tny = dirX;

        if (tnx * nx * scaleX + tny * ny * scaleY < 0)
        {
            tnx = -tnx;
            tny = -tny;
        }

        var left =
            Math.Max(
                0,
                (int)Math.Floor(Math.Min(p0.X, p1.X) - Reach - 1));
        var right =
            Math.Min(
                W - 1,
                (int)Math.Ceiling(Math.Max(p0.X, p1.X) + Reach + 1));
        var top =
            Math.Max(
                0,
                (int)Math.Floor(Math.Min(p0.Y, p1.Y) - Reach - 1));
        var bottom =
            Math.Min(
                H - 1,
                (int)Math.Ceiling(Math.Max(p0.Y, p1.Y) + Reach + 1));
        var changed = 0;

        for (var y = top;
             y <= bottom;
             y++)
        {
            for (var x = left;
                 x <= right;
                 x++)
            {
                var index =
                    y * W + x;
                var color =
                    target[index];

                if (color != a &&
                    color != b)
                {
                    continue;
                }

                var cx =
                    x + 0.5 -
                    p0.X;
                var cy =
                    y + 0.5 -
                    p0.Y;
                var along =
                    cx * dirX +
                    cy * dirY;

                // Leave one target pixel at each end to the corners.
                if (along < 1 ||
                    along > length - 1)
                {
                    continue;
                }

                var side =
                    cx * tnx +
                    cy * tny;

                if (Math.Abs(side) > Reach)
                    continue;

                var wanted =
                    side >= 0
                        ? positive
                        : negative;

                if (wanted == color ||
                    !OnlyPairAround(target, W, H, x, y, a, b))
                {
                    continue;
                }

                target[index] =
                    wanted;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>True when every 4-neighbour is one of the two colours (no third colour contact).</summary>
    private static bool OnlyPairAround(
        byte[] target,
        int W,
        int H,
        int x,
        int y,
        byte a,
        byte b)
    {
        bool Ok(
            int nx,
            int ny)
        {
            if (nx < 0 ||
                ny < 0 ||
                nx >= W ||
                ny >= H)
            {
                return true;
            }

            var color =
                target[ny * W + nx];

            return color == a ||
                   color == b;
        }

        return Ok(x - 1, y) &&
               Ok(x + 1, y) &&
               Ok(x, y - 1) &&
               Ok(x, y + 1);
    }
}
