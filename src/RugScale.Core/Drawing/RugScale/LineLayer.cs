namespace RugScale.Core.Drawing;

/// <summary>
/// One-cell line work of a classic design, separated from the fills it is drawn over.
///
/// Classic (Persian / floral) carpet designs outline petals, leaves and scrolls, and draw their
/// tracery, with one-cell lines in colours that are also used as fills (navy outline and navy
/// petal, brown stem and brown bud). No palette colour qualifies as a protected cord, so without
/// this layer every such line goes through the fill layer and comes out irregularly one or two
/// pixels wide after a 1.6x resize.
///
/// A line pixel is a pixel that belongs to no solid 2x2 block of its own colour, in an
/// 8-connected run of such pixels of at least <see cref="MinimumLinePixels"/> cells (dots and
/// tiny details stay with the fills). <see cref="Under"/> is the source with every line pixel
/// replaced by the fill around it; <see cref="Layer"/> holds only the line pixels, for the cord
/// redraw.
/// </summary>
internal sealed class LineLayer
{
    /// <summary>Smallest 8-connected run of one-cell pixels treated as line work.</summary>
    internal const int MinimumLinePixels = 6;

    private readonly int w;
    private readonly int h;
    private readonly byte[] source;

    private LineLayer(
        byte[] source,
        int w,
        int h,
        byte[] under,
        byte[] layer,
        bool[] isLine,
        int pixels)
    {
        this.source = source;
        this.w = w;
        this.h = h;
        Under = under;
        Layer = layer;
        IsLine = isLine;
        Pixels = pixels;
    }

    /// <summary>The source with line pixels replaced by the surrounding fill.</summary>
    internal byte[] Under { get; }

    /// <summary>Line pixels in their colour; every other pixel holds a colour that is not a line colour.</summary>
    internal byte[] Layer { get; }

    /// <summary>Colours that occur as line work.</summary>
    internal bool[] IsLine { get; }

    internal int Pixels { get; }

    /// <summary>Returns null when the design has no one-cell line work outside the cord colours.</summary>
    internal static LineLayer? Build(
        byte[] source,
        int w,
        int h,
        bool[] isCord)
    {
        var inBlock =
            new bool[source.Length];

        for (var y = 0;
             y + 1 < h;
             y++)
        {
            for (var x = 0;
                 x + 1 < w;
                 x++)
            {
                var index =
                    y * w + x;
                var color =
                    source[index];

                if (source[index + 1] == color &&
                    source[index + w] == color &&
                    source[index + w + 1] == color)
                {
                    inBlock[index] = true;
                    inBlock[index + 1] = true;
                    inBlock[index + w] = true;
                    inBlock[index + w + 1] = true;
                }
            }
        }

        // One-cell pixels, grouped in 8-connected runs of one colour.
        var line =
            new bool[source.Length];
        var visited =
            new bool[source.Length];
        var run =
            new List<int>();
        var stack =
            new Stack<int>();
        var total = 0;

        for (var start = 0;
             start < source.Length;
             start++)
        {
            var color =
                source[start];

            if (visited[start] ||
                inBlock[start] ||
                isCord[color])
            {
                continue;
            }

            run.Clear();
            visited[start] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var current =
                    stack.Pop();
                run.Add(current);
                var cx =
                    current % w;
                var cy =
                    current / w;

                for (var dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (var dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        var nx =
                            cx + dx;
                        var ny =
                            cy + dy;

                        if (nx < 0 ||
                            ny < 0 ||
                            nx >= w ||
                            ny >= h)
                        {
                            continue;
                        }

                        var next =
                            ny * w + nx;

                        if (visited[next] ||
                            inBlock[next] ||
                            source[next] != color)
                        {
                            continue;
                        }

                        visited[next] = true;
                        stack.Push(next);
                    }
                }
            }

            if (run.Count < MinimumLinePixels)
                continue;

            // A line is drawn over fills: most of its cells touch a solid area of another colour.
            // A one-cell strip squeezed between two lines (background showing between parallel
            // outlines) touches only line work and stays with the fills.
            var onFill = 0;

            foreach (var index in run)
            {
                if (TouchesOtherFill(
                        source,
                        inBlock,
                        w,
                        h,
                        index))
                {
                    onFill++;
                }
            }

            if (onFill * 2 < run.Count)
                continue;

            foreach (var index in run)
                line[index] = true;

            total += run.Count;
        }

        if (total == 0)
            return null;

        // A colour with no pixel in the source marks "no line here" in the layer.
        var present =
            new bool[256];

        foreach (var color in source)
            present[color] = true;

        var empty = -1;

        for (var color = 255;
             color >= 0;
             color--)
        {
            if (!present[color])
            {
                empty = color;
                break;
            }
        }

        if (empty < 0)
            return null;

        var layer =
            new byte[source.Length];
        var isLine =
            new bool[256];

        for (var index = 0;
             index < source.Length;
             index++)
        {
            if (line[index])
            {
                layer[index] = source[index];
                isLine[source[index]] = true;
            }
            else
            {
                layer[index] = (byte)empty;
            }
        }

        return new LineLayer(
            source,
            w,
            h,
            Underpaint(
                source,
                w,
                h,
                line),
            layer,
            isLine,
            total);
    }

    /// <summary>
    /// Draws the line work one pixel wide in its exact source layout: every line cell becomes the
    /// target pixel under its centre, joined to its line neighbours by a one-pixel run (straight
    /// for edge neighbours, 8-connected for a diagonal-only neighbour). Nothing is smoothed, so
    /// small motifs keep their shape; only the 1-or-2 pixel width of a neutral resize goes.
    /// </summary>
    internal void Render(
        byte[] target,
        int W,
        int H)
    {
        int Tx(
            int x) =>
            Math.Clamp(
                (int)Math.Floor(
                    (x + 0.5) * W / w),
                0,
                W - 1);

        int Ty(
            int y) =>
            Math.Clamp(
                (int)Math.Floor(
                    (y + 0.5) * H / h),
                0,
                H - 1);

        bool Same(
            int x,
            int y,
            byte color) =>
            x >= 0 &&
            y >= 0 &&
            x < w &&
            y < h &&
            Layer[y * w + x] == color;

        void Link(
            int ax,
            int ay,
            int bx,
            int by,
            byte color)
        {
            // 8-connected run from a to b (Bresenham).
            var dx =
                Math.Abs(bx - ax);
            var dy =
                -Math.Abs(by - ay);
            var sx =
                Math.Sign(bx - ax);
            var sy =
                Math.Sign(by - ay);
            var error =
                dx + dy;

            while (true)
            {
                target[ay * W + ax] = color;

                if (ax == bx &&
                    ay == by)
                {
                    break;
                }

                var twice =
                    2 * error;

                if (twice >= dy)
                {
                    error += dy;
                    ax += sx;
                }

                if (twice <= dx)
                {
                    error += dx;
                    ay += sy;
                }
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
                    Layer[y * w + x];

                if (!IsLine[color])
                    continue;

                var cx =
                    Tx(x);
                var cy =
                    Ty(y);
                target[cy * W + cx] = color;

                if (Same(x + 1, y, color))
                    Link(cx, cy, Tx(x + 1), cy, color);

                if (Same(x, y + 1, color))
                    Link(cx, cy, cx, Ty(y + 1), color);

                if (Same(x + 1, y + 1, color) &&
                    !Same(x + 1, y, color) &&
                    !Same(x, y + 1, color))
                {
                    Link(cx, cy, Tx(x + 1), Ty(y + 1), color);
                }

                if (Same(x - 1, y + 1, color) &&
                    !Same(x - 1, y, color) &&
                    !Same(x, y + 1, color))
                {
                    Link(cx, cy, Tx(x - 1), Ty(y + 1), color);
                }

                // A line that grows out of a solid area of its own colour stays attached to it:
                // run into the centre of every such neighbour (diagonals only without an edge one).
                var attachedByEdge = false;

                foreach (var (dx, dy) in Edges)
                {
                    if (!SameFill(x + dx, y + dy, color))
                        continue;

                    attachedByEdge = true;
                    Link(cx, cy, Tx(x + dx), Ty(y + dy), color);
                }

                if (attachedByEdge)
                    continue;

                foreach (var (dx, dy) in Diagonals)
                {
                    if (SameFill(x + dx, y + dy, color))
                        Link(cx, cy, Tx(x + dx), Ty(y + dy), color);
                }
            }
        }

        bool SameFill(
            int x,
            int y,
            byte color) =>
            x >= 0 &&
            y >= 0 &&
            x < w &&
            y < h &&
            source[y * w + x] == color &&
            Layer[y * w + x] != color;
    }

    private static readonly (int Dx, int Dy)[] Edges =
    [
        (1, 0),
        (-1, 0),
        (0, 1),
        (0, -1),
    ];

    private static readonly (int Dx, int Dy)[] Diagonals =
    [
        (1, 1),
        (1, -1),
        (-1, 1),
        (-1, -1),
    ];

    private static bool TouchesOtherFill(
        byte[] source,
        bool[] inBlock,
        int w,
        int h,
        int index)
    {
        var x =
            index % w;
        var y =
            index / w;
        var color =
            source[index];

        bool Fill(
            int nx,
            int ny)
        {
            if (nx < 0 ||
                ny < 0 ||
                nx >= w ||
                ny >= h)
            {
                return false;
            }

            var next =
                ny * w + nx;

            return inBlock[next] &&
                   source[next] != color;
        }

        return Fill(x - 1, y) ||
               Fill(x + 1, y) ||
               Fill(x, y - 1) ||
               Fill(x, y + 1);
    }

    /// <summary>
    /// Replaces every line pixel with the commonest colour among its non-line 8-neighbours (other
    /// than the line's own colour), growing inwards where lines cross or run side by side.
    /// </summary>
    private static byte[] Underpaint(
        byte[] source,
        int w,
        int h,
        bool[] line)
    {
        var under =
            source.ToArray();
        var open =
            line.ToArray();
        var votes =
            new int[256];
        var pending =
            new List<int>();

        for (var index = 0;
             index < line.Length;
             index++)
        {
            if (line[index])
                pending.Add(index);
        }

        for (var pass = 0;
             pass < 16 && pending.Count > 0;
             pass++)
        {
            var decided =
                new List<(int Index, byte Color)>();
            var still =
                new List<int>();

            foreach (var index in pending)
            {
                var x =
                    index % w;
                var y =
                    index / w;
                var own =
                    source[index];
                var best = -1;
                var bestVotes = 0;
                var fallback = -1;
                Array.Clear(votes);

                for (var dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (var dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        var nx =
                            x + dx;
                        var ny =
                            y + dy;

                        if ((dx == 0 && dy == 0) ||
                            nx < 0 ||
                            ny < 0 ||
                            nx >= w ||
                            ny >= h)
                        {
                            continue;
                        }

                        var next =
                            ny * w + nx;

                        if (open[next])
                            continue;

                        var color =
                            under[next];

                        if (color == own)
                        {
                            fallback = color;
                            continue;
                        }

                        // Orthogonal neighbours count double: they share an edge with the line.
                        votes[color] +=
                            dx == 0 || dy == 0
                                ? 2
                                : 1;

                        if (votes[color] > bestVotes ||
                            (votes[color] == bestVotes &&
                             color < best))
                        {
                            best = color;
                            bestVotes = votes[color];
                        }
                    }
                }

                if (best < 0)
                    best = fallback;

                if (best < 0)
                    still.Add(index);
                else
                    decided.Add((index, (byte)best));
            }

            foreach (var (index, color) in decided)
            {
                under[index] = color;
                open[index] = false;
            }

            if (decided.Count == 0)
                break;

            pending = still;
        }

        return under;
    }
}
