namespace RugScale.Core.Drawing;

/// <summary>
/// Redraws protected Pixel-Cord line work at a new size the way a designer would with the Curve /
/// Pixel-Cord tool: a one-pixel, 4-connected line whose step cadence follows the curve smoothly
/// (1-1-2-1-2-2 ... changing gradually with the slope).
///
/// Copying the source staircase cell by cell onto a 1.6x grid cannot do that: each source step
/// maps to one or two target cells depending on rounding phase, so the target cadence becomes
/// irregular (1, 2, 1, 3, 1 ...) and the line reads as broken even though it is continuous.
///
/// Method, all from immutable source evidence:
/// 1. the source cord network is split into chains between junctions/endpoints (closed loops kept
///    closed); diagonal-only joints count as links;
/// 2. the cell centres of each chain are smoothed with a [1 2 1] kernel while every point stays
///    within <see cref="MaximumShift"/> source px of its source cell centre; chain ends and genuine
///    corners (direction change above <see cref="CornerAngleDegrees"/>) never move, so junctions
///    stay shared and tips stay sharp;
/// 3. the smoothed polyline is mapped to the target grid and rasterized as a 4-connected line,
///    choosing at every step the axis move that keeps the cell centre closest to the curve.
/// </summary>
internal static class PixelCordCurveRedraw
{
    /// <summary>Largest distance (source px) a smoothed point may move from its source cell centre.</summary>
    internal const double MaximumShift = 0.5;

    /// <summary>Turn (degrees, measured over +/- <see cref="CornerWindow"/> cells) that marks a designer corner.</summary>
    internal const double CornerAngleDegrees = 65;

    internal const int CornerWindow = 3;

    internal const int SmoothingIterations = 10;

    internal readonly record struct Chain(
        IReadOnlyList<int> Cells,
        bool Closed);

    /// <summary>Draws every cord chain into <paramref name="target"/>; returns the number of chains.</summary>
    internal static int Draw(
        byte[] source,
        int w,
        int h,
        bool[] isCord,
        byte[] target,
        int W,
        int H,
        int penX,
        int penY)
    {
        // Junction and tip spots where two source cords touch are two cells thick. Traced as they
        // are, every cell becomes a graph node and the spot is redrawn as a 3x3 blob or as two
        // parallel lines with a gap. Thin those spots to a one-cell line first.
        var skeleton =
            SkeletonizeTouchingCords(
                source,
                w,
                h,
                isCord);
        var chains =
            TraceChains(
                skeleton,
                w,
                h,
                isCord);
        var scaleX =
            W /
            (double)w;
        var scaleY =
            H /
            (double)h;

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

        foreach (var chain in chains)
        {
            var color =
                skeleton[chain.Cells[0]];
            var points =
                Smooth(
                    chain,
                    w);

            var mapped =
                points
                    .Select(point =>
                        (X: point.X * scaleX,
                         Y: point.Y * scaleY))
                    .ToArray();

            if (chain.Closed &&
                mapped.Length > 2)
            {
                mapped =
                    mapped
                        .Append(mapped[0])
                        .ToArray();
            }

            var cx =
                Cell(
                    mapped[0].X,
                    W);
            var cy =
                Cell(
                    mapped[0].Y,
                    H);

            Plot(
                cx,
                cy,
                color);

            for (var k = 1;
                 k < mapped.Length;
                 k++)
            {
                var a =
                    mapped[k - 1];
                var b =
                    mapped[k];
                var bx =
                    Cell(
                        b.X,
                        W);
                var by =
                    Cell(
                        b.Y,
                        H);

                // 4-connected walk to the next cell, always taking the axis step whose cell
                // centre stays closest to the true segment a-b.
                var guard = 0;

                while ((cx != bx ||
                        cy != by) &&
                       guard++ < 4 * (W + H))
                {
                    var stepX =
                        Math.Sign(
                            bx - cx);
                    var stepY =
                        Math.Sign(
                            by - cy);

                    if (stepX != 0 &&
                        stepY != 0)
                    {
                        var dX =
                            DistanceToSegment(
                                cx + stepX + 0.5,
                                cy + 0.5,
                                a,
                                b);
                        var dY =
                            DistanceToSegment(
                                cx + 0.5,
                                cy + stepY + 0.5,
                                a,
                                b);

                        if (dX <= dY)
                            cx += stepX;
                        else
                            cy += stepY;
                    }
                    else if (stepX != 0)
                    {
                        cx += stepX;
                    }
                    else
                    {
                        cy += stepY;
                    }

                    Plot(
                        cx,
                        cy,
                        color);
                }
            }
        }

        return chains.Count;
    }

    private static int Cell(
        double value,
        int size) =>
        Math.Clamp(
            (int)Math.Floor(
                value),
            0,
            size - 1);

    private static double DistanceToSegment(
        double px,
        double py,
        (double X, double Y) a,
        (double X, double Y) b)
    {
        var vx =
            b.X -
            a.X;
        var vy =
            b.Y -
            a.Y;
        var length2 =
            vx * vx +
            vy * vy;
        var t =
            length2 <= 1e-12
                ? 0d
                : Math.Clamp(
                    ((px - a.X) * vx + (py - a.Y) * vy) /
                    length2,
                    0d,
                    1d);
        var dx =
            a.X +
            t * vx -
            px;
        var dy =
            a.Y +
            t * vy -
            py;

        return Math.Sqrt(
            dx * dx +
            dy * dy);
    }

    /// <summary>Smoothed chain points in continuous source coordinates (cell centre = x + 0.5).</summary>
    internal static (double X, double Y)[] Smooth(
        Chain chain,
        int w)
    {
        // The corner cell of every 4-connected stair step belongs to the drawing language, not to
        // the curve: the curve runs diagonally past it. Smooth the 8-connected centre line only;
        // the target rasterizer re-creates 4-connected corners on its own grid.
        var centreLine =
            CentreLineCells(
                chain,
                w);
        var n =
            centreLine.Count;
        var original =
            centreLine
                .Select(cell =>
                    (X: cell % w + 0.5,
                     Y: cell / w + 0.5))
                .ToArray();

        if (n < 3)
            return original;

        var pinned =
            new bool[n];

        if (!chain.Closed)
        {
            pinned[0] = true;
            pinned[n - 1] = true;
        }

        var cornerCos =
            Math.Cos(
                CornerAngleDegrees *
                Math.PI /
                180d);

        for (var i = 0;
             i < n;
             i++)
        {
            if (pinned[i])
                continue;

            int Wrap(
                int index) =>
                chain.Closed
                    ? (index % n + n) % n
                    : Math.Clamp(
                        index,
                        0,
                        n - 1);

            var back =
                original[Wrap(i - CornerWindow)];
            var here =
                original[i];
            var ahead =
                original[Wrap(i + CornerWindow)];
            var ux =
                here.X -
                back.X;
            var uy =
                here.Y -
                back.Y;
            var vx =
                ahead.X -
                here.X;
            var vy =
                ahead.Y -
                here.Y;
            var lu =
                Math.Sqrt(
                    ux * ux +
                    uy * uy);
            var lv =
                Math.Sqrt(
                    vx * vx +
                    vy * vy);

            if (lu < 1e-9 ||
                lv < 1e-9)
            {
                continue;
            }

            if ((ux * vx + uy * vy) /
                (lu * lv) <
                cornerCos)
            {
                pinned[i] = true;
            }
        }

        var current =
            original.ToArray();
        var next =
            new (double X, double Y)[n];

        for (var iteration = 0;
             iteration < SmoothingIterations;
             iteration++)
        {
            for (var i = 0;
                 i < n;
                 i++)
            {
                if (pinned[i] ||
                    (!chain.Closed &&
                     (i == 0 ||
                      i == n - 1)))
                {
                    next[i] =
                        current[i];
                    continue;
                }

                var previous =
                    current[(i - 1 + n) % n];
                var following =
                    current[(i + 1) % n];
                var x =
                    (previous.X + 2 * current[i].X + following.X) /
                    4d;
                var y =
                    (previous.Y + 2 * current[i].Y + following.Y) /
                    4d;
                var dx =
                    x -
                    original[i].X;
                var dy =
                    y -
                    original[i].Y;
                var shift =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy);

                if (shift >
                    MaximumShift)
                {
                    x =
                        original[i].X +
                        dx * MaximumShift / shift;
                    y =
                        original[i].Y +
                        dy * MaximumShift / shift;
                }

                next[i] =
                    (x, y);
            }

            (current, next) =
                (next, current);
        }

        return current;
    }

    /// <summary>
    /// Drops stair-corner cells: a cell whose chain neighbours touch each other diagonally is only
    /// there to make the source line 4-connected. Chain ends are always kept.
    /// </summary>
    private static List<int> CentreLineCells(
        Chain chain,
        int w)
    {
        var cells =
            chain.Cells;
        var n =
            cells.Count;

        if (n < 3)
            return cells.ToList();

        var result =
            new List<int>(n);

        for (var i = 0;
             i < n;
             i++)
        {
            var isEnd =
                !chain.Closed &&
                (i == 0 ||
                 i == n - 1);

            if (!isEnd)
            {
                var previous =
                    cells[(i - 1 + n) % n];
                var next =
                    cells[(i + 1) % n];
                var dx =
                    Math.Abs(
                        previous % w -
                        next % w);
                var dy =
                    Math.Abs(
                        previous / w -
                        next / w);

                if (dx == 1 &&
                    dy == 1 &&
                    (result.Count == 0 ||
                     result[^1] == previous))
                {
                    continue;
                }
            }

            result.Add(
                cells[i]);
        }

        return result;
    }

    /// <summary>
    /// Copy of the source in which cord cells belonging to a solid 2x2 cord block are peeled away
    /// while the cord keeps its 4-connected topology and its endpoints. Wide cord areas (no
    /// non-cord 4-neighbour at all) are left alone; they are painted by the fill layer.
    /// </summary>
    internal static byte[] SkeletonizeTouchingCords(
        byte[] source,
        int w,
        int h,
        bool[] isCord)
    {
        var cells =
            source.ToArray();
        ReadOnlySpan<(int Dx, int Dy)> sides =
        [
            (0, -1),
            (0, 1),
            (-1, 0),
            (1, 0),
        ];
        var removed =
            new bool[cells.Length];

        bool Cord(
            int x,
            int y,
            byte color) =>
            x >= 0 &&
            y >= 0 &&
            x < w &&
            y < h &&
            !removed[y * w + x] &&
            cells[y * w + x] == color;

        for (var iteration = 0;
             iteration < 4;
             iteration++)
        {
            var changed = 0;

            foreach (var (sx, sy) in sides)
            {
                var remove =
                    new List<int>();

                for (var y = 0;
                     y < h;
                     y++)
                {
                    for (var x = 0;
                         x < w;
                         x++)
                    {
                        var color =
                            cells[y * w + x];

                        if (removed[y * w + x] ||
                            !isCord[color] ||
                            Cord(x + sx, y + sy, color) ||
                            !InSolidBlock(x, y, color))
                        {
                            continue;
                        }

                        if (IsSimple(x, y, color))
                            remove.Add(y * w + x);
                    }
                }

                // Apply one at a time, re-checking: removing two neighbours of the same block in
                // one sweep could split the cord.
                foreach (var index in remove)
                {
                    var x =
                        index % w;
                    var y =
                        index / w;
                    var color =
                        cells[index];

                    if (!InSolidBlock(x, y, color) ||
                        !IsSimple(x, y, color))
                    {
                        continue;
                    }

                    removed[index] = true;
                    changed++;
                }
            }

            if (changed == 0)
                break;
        }

        // Removed cells must not read as cord to the chain tracer.
        for (var index = 0;
             index < cells.Length;
             index++)
        {
            if (removed[index])
                cells[index] = FirstNonCord(isCord);
        }

        return cells;

        bool InSolidBlock(
            int x,
            int y,
            byte color) =>
            PixelCordCurveRedraw.InSolidBlock(
                (cx, cy) => Cord(cx, cy, color),
                x,
                y);

        bool IsSimple(
            int x,
            int y,
            byte color) =>
            IsSimplePoint(
                (cx, cy) => Cord(cx, cy, color),
                x,
                y);
    }

    internal static bool InSolidBlock(
        Func<int, int, bool> cord,
        int x,
        int y)
    {
        for (var oy = -1;
             oy <= 0;
             oy++)
        {
            for (var ox = -1;
                 ox <= 0;
                 ox++)
            {
                if (cord(x + ox, y + oy) &&
                    cord(x + ox + 1, y + oy) &&
                    cord(x + ox, y + oy + 1) &&
                    cord(x + ox + 1, y + oy + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// (4,8) simple point: the 4-adjacent cord neighbours stay one 4-connected group through
    /// the ring, all ring cord cells belong to it, the background ring stays one 8-connected
    /// group, and the cell is not an endpoint.
    /// </summary>
internal static bool IsSimplePoint(
    Func<int, int, bool> cord,
    int x,
    int y)
{
        ReadOnlySpan<(int Dx, int Dy)> ring =
        [
            (0, -1),
            (1, -1),
            (1, 0),
            (1, 1),
            (0, 1),
            (-1, 1),
            (-1, 0),
            (-1, -1),
        ];
        Span<bool> on =
            stackalloc bool[8];
        var count = 0;

        for (var k = 0;
             k < 8;
             k++)
        {
            on[k] =
                cord(
                    x + ring[k].Dx,
                    y + ring[k].Dy);

            if (on[k] &&
                k % 2 == 0)
            {
                count++;
            }
        }

        if (count < 2)
            return false;

        // Foreground: consecutive ring cells joined (4-adjacent); diagonal cells only
        // through an orthogonal neighbour.
        Span<int> group =
            stackalloc int[8];

        for (var k = 0;
             k < 8;
             k++)
        {
            group[k] = k;
        }

        int Find(
            Span<int> g,
            int v)
        {
            while (g[v] != v)
                v = g[v];

            return v;
        }

        for (var k = 0;
             k < 8;
             k++)
        {
            var next =
                (k + 1) % 8;

            if (on[k] &&
                on[next])
            {
                group[Find(group, k)] =
                    Find(group, next);
            }
        }

        var root = -1;

        for (var k = 0;
             k < 8;
             k++)
        {
            if (!on[k])
                continue;

            var r =
                Find(group, k);

            if (root < 0)
                root = r;
            else if (r != root)
                return false;
        }

        // Background: 8-connected around the ring.
        for (var k = 0;
             k < 8;
             k++)
        {
            group[k] = k;
        }

        for (var k = 0;
             k < 8;
             k++)
        {
            var next =
                (k + 1) % 8;

            if (!on[k] &&
                !on[next])
            {
                group[Find(group, k)] =
                    Find(group, next);
            }

            if (k % 2 == 0 &&
                !on[k] &&
                !on[(k + 2) % 8])
            {
                group[Find(group, k)] =
                    Find(group, (k + 2) % 8);
            }
        }

        var background = -1;

        for (var k = 0;
             k < 8;
             k++)
        {
            if (on[k])
                continue;

            var r =
                Find(group, k);

            if (background < 0)
                background = r;
            else if (r != background)
                return false;
        }

        return background >= 0;
    }

    private static byte FirstNonCord(
        bool[] isCord)
    {
        for (var color = 0;
             color < 256;
             color++)
        {
            if (!isCord[color])
                return (byte)color;
        }

        return 0;
    }

    /// <summary>
    /// Splits the cord network into chains. Links: 4-neighbours of the same colour, plus a
    /// diagonal neighbour when neither orthogonal cell between them carries that colour.
    /// </summary>
    internal static List<Chain> TraceChains(
        byte[] source,
        int w,
        int h,
        bool[] isCord)
    {
        var neighbours =
            new List<int>[source.Length];
        var degree =
            new int[source.Length];

        for (var y = 0;
             y < h;
             y++)
        {
            for (var x = 0;
                 x < w;
                 x++)
            {
                var index =
                    y * w +
                    x;
                var color =
                    source[index];

                if (!isCord[color])
                    continue;

                var list =
                    new List<int>(4);

                bool Same(
                    int nx,
                    int ny) =>
                    nx >= 0 &&
                    ny >= 0 &&
                    nx < w &&
                    ny < h &&
                    source[ny * w + nx] == color;

                if (Same(x - 1, y))
                    list.Add(index - 1);

                if (Same(x + 1, y))
                    list.Add(index + 1);

                if (Same(x, y - 1))
                    list.Add(index - w);

                if (Same(x, y + 1))
                    list.Add(index + w);

                foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    if (Same(x + dx, y + dy) &&
                        !Same(x + dx, y) &&
                        !Same(x, y + dy))
                    {
                        list.Add(
                            (y + dy) * w +
                            x + dx);
                    }
                }

                neighbours[index] =
                    list;
                degree[index] =
                    list.Count;
            }
        }

        var chains =
            new List<Chain>();
        var usedEdges =
            new HashSet<long>();

        long Edge(
            int a,
            int b) =>
            a < b
                ? (long)a * source.Length + b
                : (long)b * source.Length + a;

        bool IsNode(
            int index) =>
            degree[index] != 2;

        for (var start = 0;
             start < source.Length;
             start++)
        {
            if (neighbours[start] is null ||
                !IsNode(start))
            {
                continue;
            }

            if (degree[start] == 0)
            {
                chains.Add(
                    new Chain(
                        new[] { start },
                        false));
                continue;
            }

            foreach (var first in neighbours[start])
            {
                if (!usedEdges.Add(
                        Edge(
                            start,
                            first)))
                {
                    continue;
                }

                var cells =
                    new List<int> { start, first };
                var previous =
                    start;
                var current =
                    first;

                while (!IsNode(current))
                {
                    var advanced = false;

                    foreach (var candidate in neighbours[current])
                    {
                        if (candidate == previous ||
                            !usedEdges.Add(
                                Edge(
                                    current,
                                    candidate)))
                        {
                            continue;
                        }

                        cells.Add(
                            candidate);
                        previous =
                            current;
                        current =
                            candidate;
                        advanced = true;
                        break;
                    }

                    if (!advanced)
                        break;
                }

                chains.Add(
                    new Chain(
                        cells,
                        false));
            }
        }

        // Pure loops (every cell has exactly two links).
        for (var start = 0;
             start < source.Length;
             start++)
        {
            if (neighbours[start] is null ||
                IsNode(start))
            {
                continue;
            }

            var open =
                neighbours[start]
                    .FirstOrDefault(candidate =>
                        !usedEdges.Contains(
                            Edge(
                                start,
                                candidate)),
                        -1);

            if (open < 0)
                continue;

            usedEdges.Add(
                Edge(
                    start,
                    open));

            var cells =
                new List<int> { start, open };
            var previous =
                start;
            var current =
                open;

            while (current != start)
            {
                var advanced = false;

                foreach (var candidate in neighbours[current])
                {
                    if (candidate == previous ||
                        !usedEdges.Add(
                            Edge(
                                current,
                                candidate)))
                    {
                        continue;
                    }

                    if (candidate != start)
                        cells.Add(candidate);

                    previous =
                        current;
                    current =
                        candidate;
                    advanced = true;
                    break;
                }

                if (!advanced)
                    break;
            }

            chains.Add(
                new Chain(
                    cells,
                    true));
        }

        return chains;
    }
}
