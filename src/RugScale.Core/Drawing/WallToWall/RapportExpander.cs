using RugScale.Core.Models;

namespace RugScale.Core.Drawing.WallToWall;

/// <summary>
/// "Rapor açma": grows a rapport to a larger width and / or length the way a designer opens one by
/// hand, from the rapport's own content, never rebuilt from small blocks (dithered, painterly
/// grounds like B390A came out as rectangles that way).
///
/// - <b>Along the strokes</b> (a design of horizontal brush strokes widened, see
///   <see cref="GrainOf"/>): the strokes are lengthened. Along a top-to-bottom path where every
///   row continues smoothly, each row gets a short piece of its own content once more; dither
///   and texture are copied, not stretched.
/// - <b>Otherwise</b>: the rapport is cut open at its edge (or, when it wraps seamlessly, where a
///   strip fits best) and a strip of its own content is spliced in. The strip is chosen by
///   <b>tone</b> (mean colour of <see cref="Cell"/> x <see cref="Cell"/> cells, so dither does not
///   count) and joined along a free-form minimum cut inside a band of up to <see cref="Band"/>
///   px; on a dithered design the cut is dissolved into a dithered transition.
///
/// Every path and cut closes on itself, so the opened rapport repeats like the original. Where the
/// original's own join is visible (B390A across: a straight line between the repeats), lengthening
/// grows a quarter wider and then overlaps both ends: every row switches from one end to the other
/// once, where its tones meet, the switch points staggered like stroke ends
/// (<see cref="StrokeEnds"/>). The original rapport keeps its content: only the bands along its
/// edges can change.
/// </summary>
public static class RapportExpander
{
    /// <summary>Widest seam band (px).</summary>
    internal const int Band = 64;


    /// <summary>Tone cells (px) on which strips are compared.</summary>
    internal const int Cell = 4;

    /// <summary>Strip start / shift positions are tried with this step (px).</summary>
    internal const int Step = 2;

    /// <summary>
    /// A rapport whose own wrap join reads below this ratio (see
    /// <see cref="WallToWallRepeat.VisibleSeam"/>) may be shifted across while splicing.
    /// </summary>
    internal const double SeamlessWrap = 1.15;

    public static DesignDocument Expand(
        DesignDocument source,
        RapportTile tile,
        int newWidth,
        int newHeight,
        int seed = 1,
        int band = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tile);

        var w =
            source.Width;
        var h =
            source.Height;
        var tx =
            Math.Clamp(tile.X, 0, w - 1);
        var ty =
            Math.Clamp(tile.Y, 0, h - 1);
        var tw =
            Math.Clamp(tile.Width, 1, w - tx);
        var th =
            Math.Clamp(tile.Height, 1, h - ty);

        if (newWidth < tw ||
            newHeight < th)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newWidth),
                $"The opened rapport ({newWidth}x{newHeight}) must be at least the rapport size ({tw}x{th}).");
        }

        var palette =
            source.Palette;
        var rgb =
            new int[256 * 3];

        for (var index = 0; index < palette.Count && index < 256; index++)
        {
            rgb[index * 3] = palette[index].R;
            rgb[index * 3 + 1] = palette[index].G;
            rgb[index * 3 + 2] = palette[index].B;
        }

        var grid =
            new byte[tw * th];

        for (var y = 0; y < th; y++)
            for (var x = 0; x < tw; x++)
                grid[y * tw + x] = source.GetPixel(tx + x, ty + y);

        var random =
            new Random(seed);
        var gw = tw;
        var gh = th;

        // Streaks: a design of horizontal brush strokes is widened by lengthening its strokes
        // (vertical strokes: lengthened along); across the strokes, strips are spliced in.
        var grain =
            GrainOf(grid, tw, th, rgb);

        void Widen()
        {
            (grid, gw) =
                Grow(grid, gw, gh, newWidth, grain, band, rgb, random);
        }

        // Length: the same on the transposed rapport.
        void Extend()
        {
            var turned =
                Transpose(grid, gw, gh);
            var tgw = gh;

            (turned, tgw) =
                Grow(turned, tgw, gw, newHeight, Turn(grain), band, rgb, random);

            gh = tgw;
            grid = Transpose(turned, gh, gw);
        }

        // A strip may be shifted across the splice only where the rapport wraps seamlessly that
        // way; every splice leaves its own axis seamless. So the axis whose cross-wrap is the
        // cleaner one goes first, and the second one is then free to shift.
        var wrapAlong =
            WrapRatio(grid, gw, gh, rgb);
        var wrapAcross =
            WrapRatio(Transpose(grid, gw, gh), gh, gw, rgb);

        // The stroke-lengthening axis first: it needs no shift, and leaves its join closed so
        // the splices across the strokes are free to shift.
        if (grain == Grain.Along ||
            (grain == Grain.None && wrapAcross < wrapAlong))
        {
            Extend();
            Widen();
        }
        else
        {
            Widen();
            Extend();
        }

        var result =
            new DesignDocument(
                gw,
                gh,
                palette);

        for (var y = 0; y < gh; y++)
            for (var x = 0; x < gw; x++)
                result.SetPixel(x, y, grid[y * gw + x]);

        return result;
    }

    internal enum Grain
    {
        None,

        /// <summary>Strokes run across (horizontal streaks).</summary>
        Across,

        /// <summary>Strokes run along (vertical streaks).</summary>
        Along,
    }

    /// <summary>Tone changes this much more across the strokes than along them: streaked.</summary>
    internal const double StreakRatio = 1.6;

    /// <summary>
    /// Direction of the strokes, from the tone (5 x 5 mean colour, so dither does not count)
    /// <see cref="StreakDistance"/> px apart across and along.
    /// </summary>
    internal static Grain GrainOf(
        byte[] grid,
        int gw,
        int gh,
        int[] rgb)
    {
        if (gw <= 2 * StreakDistance ||
            gh <= 2 * StreakDistance)
        {
            return Grain.None;
        }

        var tone =
            ToneMap(grid, gw, gh, rgb);
        double across = 0, along = 0;

        for (var y = 0; y + StreakDistance < gh; y += 2)
        {
            for (var x = 0; x + StreakDistance < gw; x += 2)
            {
                var at =
                    y * gw + x;
                across += Distance(tone, at, at + StreakDistance);
                along += Distance(tone, at, at + StreakDistance * gw);
            }
        }

        return along > across * StreakRatio
            ? Grain.Across
            : across > along * StreakRatio
                ? Grain.Along
                : Grain.None;
    }

    internal const int StreakDistance = 8;

    /// <summary>
    /// Grows a rapport across to <paramref name="target"/> columns: by lengthening its strokes, or
    /// by splicing strips. Splicing closes the rapport's own join across (the strip's tail is cut
    /// into its start); lengthening does not, so a visible join (B390A: a straight line between
    /// the repeats) is closed separately: grown a quarter of the target wider, both ends then
    /// overlap (a narrower overlap lined all stroke ends up in one strip).
    /// </summary>
    private static (byte[] Grid, int Width) Grow(
        byte[] grid,
        int gw,
        int gh,
        int target,
        Grain strokes,
        int bandWanted,
        int[] rgb,
        Random random)
    {
        var visible =
            WrapRatio(Transpose(grid, gw, gh), gh, gw, rgb) >= SeamlessWrap;

        if (gw >= target &&
            !visible)
        {
            return (grid, gw);
        }

        if (strokes != Grain.Across)
        {
            // A splice closes the join it is cut into. Nothing to add but a visible join: a strip
            // of the rapport's own content is spliced over the join alone (width unchanged; H312B
            // across, between vertical strokes).
            if (gw >= target)
                return Splice(grid, gw, gh, 0, bandWanted, strokes, rgb, random);

            while (gw < target)
                (grid, gw) = Splice(grid, gw, gh, target - gw, bandWanted, strokes, rgb, random);

            return (grid, gw);
        }

        var b =
            visible
                ? Math.Max(2, bandWanted > 0 ? Math.Min(bandWanted, (gw - 1) / 3) : Math.Max(target / 4, 2))
                : 0;

        (grid, gw) =
            Lengthen(grid, gw, gh, target - gw + b, rgb);

        return visible
            ? CloseWrap(grid, gw, gh, b, rgb, random)
            : (grid, gw);
    }

    /// <summary>
    /// Closes a rapport's join across by overlapping its last <paramref name="band"/> columns onto
    /// its first, one switch per row (<see cref="StrokeEnds"/>), tapered in dither on dithered
    /// designs; the rapport becomes <paramref name="band"/> columns narrower. Blending the ends
    /// over the whole overlap in random patches looked torn (user: B390A join "kötü duruyor").
    /// </summary>
    private static (byte[] Grid, int Width) CloseWrap(
        byte[] grid,
        int gw,
        int gh,
        int band,
        int[] rgb,
        Random random)
    {
        // Left of a row's switch point: the previous repeat's tail (A); right of it: this repeat's
        // start (B). One switch per row, like a stroke ending and the next one starting.
        var cut =
            StrokeEnds(
                (x, y) => grid[y * gw + gw - band + x],
                (x, y) => grid[y * gw + x],
                band,
                gh,
                rgb);
        var feather =
            FeatherWidth(grid, gw, gh, band) > 0
                ? StrokeTaper
                : 0;
        var salt =
            random.Next();
        var nw =
            gw - band;
        var result =
            new byte[nw * gh];

        for (var y = 0; y < gh; y++)
        {
            var c =
                cut[y];

            for (var x = 0; x < band; x++)
            {
                // A dithered stroke ends in thinning dither, not a hard edge.
                var start =
                    feather == 0
                        ? x >= c
                        : Noise(x, y, salt) < Math.Clamp((x - c + feather + 0.5) / (2.0 * feather), 0, 1);

                result[y * nw + x] =
                    start
                        ? grid[y * gw + x]
                        : grid[y * gw + gw - band + x];
            }

            for (var x = band; x < nw; x++)
                result[y * nw + x] = grid[y * gw + x];
        }

        return (result, nw);
    }

    /// <summary>
    /// Per row, where source A (left) hands over to source B (right) inside a band of horizontal
    /// strokes. A row hands over where the tone of A just before matches the tone of B just after
    /// (3 rows x <see cref="EndWindow"/> px). Neighbouring rows may hand over at different places,
    /// but the step between them shows as a short horizontal edge between A and B: it costs what
    /// that edge adds over the rows' own contrast, so it is free between strokes and expensive
    /// inside one (a stroke must not be torn into teeth). Exact minimum by dynamic programming over
    /// the rows, closing on itself along (row 0 follows the last row).
    /// </summary>
    private static int[] StrokeEnds(
        Func<int, int, byte> sourceA,
        Func<int, int, byte> sourceB,
        int band,
        int rows,
        int[] rgb)
    {
        var lo = 1;
        var hi = band - 1;

        if (hi <= lo)
            return Enumerable.Repeat(Math.Max(0, band / 2), rows).ToArray();

        // Per-pixel colour, and colour summed over 3 rows (wrapping) with prefix sums along x.
        var colorA =
            new int[rows * band * 3];
        var colorB =
            new int[rows * band * 3];

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < band; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    colorA[(y * band + x) * 3 + channel] = rgb[sourceA(x, y) * 3 + channel];
                    colorB[(y * band + x) * 3 + channel] = rgb[sourceB(x, y) * 3 + channel];
                }
            }
        }

        long[] RowSums(
            int[] color)
        {
            var sums =
                new long[rows * (band + 1) * 3];

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < band; x++)
                {
                    for (var channel = 0; channel < 3; channel++)
                    {
                        long v = 0;

                        for (var dy = -1; dy <= 1; dy++)
                            v += color[((((y + dy) % rows) + rows) % rows * band + x) * 3 + channel];

                        sums[(y * (band + 1) + x + 1) * 3 + channel] =
                            sums[(y * (band + 1) + x) * 3 + channel] + v;
                    }
                }
            }

            return sums;
        }

        var sumA =
            RowSums(colorA);
        var sumB =
            RowSums(colorB);

        // Handing over before column c: A's tone over [c - w, c) against B's over [c, c + w).
        var unary =
            new long[rows * band];

        for (var y = 0; y < rows; y++)
        {
            for (var c = lo; c <= hi; c++)
            {
                var wa =
                    Math.Min(EndWindow, c);
                var wb =
                    Math.Min(EndWindow, band - c);
                long cost = 0;

                for (var channel = 0; channel < 3; channel++)
                {
                    var a =
                        (sumA[(y * (band + 1) + c) * 3 + channel] - sumA[(y * (band + 1) + c - wa) * 3 + channel]) / (3.0 * wa);
                    var b =
                        (sumB[(y * (band + 1) + c + wb) * 3 + channel] - sumB[(y * (band + 1) + c) * 3 + channel]) / (3.0 * wb);
                    cost += (long)Math.Abs(a - b);
                }

                // Stroke ends spread over the band: a gentle pull towards a place that wanders
                // with the rows, so where no place matches better the ends do not line up.
                var wander =
                    band * (0.5 + 0.4 * Math.Sin(y / 23.0) * Math.Cos(y / 61.0 + 1.3));
                cost += (long)(WanderPull * Math.Abs(c - wander));

                unary[y * band + c] = cost;
            }
        }

        // Step between row y and y + 1: what the edge between A in one row and B in the other adds
        // over the rows' own contrast (colours averaged over 3 px across, so dither is not edge).
        int Tone(
            int[] color,
            int x,
            int y,
            int channel)
        {
            var sum = 0;

            for (var dx = -1; dx <= 1; dx++)
                sum += color[(y * band + Math.Clamp(x + dx, 0, band - 1)) * 3 + channel];

            return sum / 3;
        }

        int Contrast(
            int[] upper,
            int[] lower,
            int x,
            int y,
            int next)
        {
            var sum = 0;

            for (var channel = 0; channel < 3; channel++)
                sum += Math.Abs(Tone(upper, x, y, channel) - Tone(lower, x, next, channel));

            return sum;
        }

        // aboveA[y][x]: row y shows A and row y + 1 shows B at column x; aboveB the reverse.
        var aboveA =
            new long[rows * (band + 1)];
        var aboveB =
            new long[rows * (band + 1)];

        for (var y = 0; y < rows; y++)
        {
            var next =
                (y + 1) % rows;

            for (var x = 0; x < band; x++)
            {
                var own =
                    Math.Max(Contrast(colorA, colorA, x, y, next), Contrast(colorB, colorB, x, y, next));
                aboveA[y * (band + 1) + x + 1] =
                    aboveA[y * (band + 1) + x] + Math.Max(0, Contrast(colorA, colorB, x, y, next) - own) / 2;
                aboveB[y * (band + 1) + x + 1] =
                    aboveB[y * (band + 1) + x] + Math.Max(0, Contrast(colorB, colorA, x, y, next) - own) / 2;
            }
        }

        // Row y hands over at c, row y + 1 at d: between them one row shows A, the other B.
        long Step(
            int y,
            int c,
            int d) =>
            c > d
                ? aboveA[y * (band + 1) + c] - aboveA[y * (band + 1) + d]
                : aboveB[y * (band + 1) + d] - aboveB[y * (band + 1) + c];

        int[] Solve(
            int fixedStart,
            out long total)
        {
            var cost =
                new long[band];
            var from =
                new int[rows * band];
            const long never = long.MaxValue / 4;

            for (var c = 0; c < band; c++)
            {
                cost[c] =
                    c < lo || c > hi || (fixedStart >= 0 && c != fixedStart)
                        ? never
                        : unary[c];
            }

            for (var y = 1; y < rows; y++)
            {
                var next =
                    new long[band];
                var prev =
                    y - 1;

                // From c >= d: cost[c] + aboveA[c] - aboveA[d] -> suffix minimum of cost + aboveA.
                var suffix =
                    new long[band + 1];
                var suffixAt =
                    new int[band + 1];
                suffix[band] = never;

                for (var c = band - 1; c >= 0; c--)
                {
                    var v =
                        cost[c] >= never
                            ? never
                            : cost[c] + aboveA[prev * (band + 1) + c];
                    suffix[c] = v < suffix[c + 1] ? v : suffix[c + 1];
                    suffixAt[c] = v < suffix[c + 1] ? c : suffixAt[c + 1];
                }

                // From c <= d: cost[c] - aboveB[c] + aboveB[d] -> prefix minimum of cost - aboveB.
                var prefix =
                    never;
                var prefixAt = -1;

                for (var d = 0; d < band; d++)
                {
                    if (cost[d] < never)
                    {
                        var v =
                            cost[d] - aboveB[prev * (band + 1) + d];

                        if (prefixAt < 0 || v < prefix)
                        {
                            prefix = v;
                            prefixAt = d;
                        }
                    }

                    if (d < lo || d > hi)
                    {
                        next[d] = never;
                        continue;
                    }

                    var fromAbove =
                        suffix[d] >= never
                            ? never
                            : suffix[d] - aboveA[prev * (band + 1) + d];
                    var fromBelow =
                        prefixAt < 0
                            ? never
                            : prefix + aboveB[prev * (band + 1) + d];

                    if (fromAbove <= fromBelow)
                    {
                        next[d] = fromAbove >= never ? never : fromAbove + unary[y * band + d];
                        from[y * band + d] = suffixAt[d];
                    }
                    else
                    {
                        next[d] = fromBelow + unary[y * band + d];
                        from[y * band + d] = prefixAt;
                    }
                }

                cost = next;
            }

            // Close along: the last row steps into row 0.
            var end = -1;
            total = never;

            for (var c = lo; c <= hi; c++)
            {
                if (cost[c] >= never)
                    continue;

                var v =
                    cost[c] + (fixedStart >= 0 ? Step(rows - 1, c, fixedStart) : 0);

                if (v < total)
                {
                    total = v;
                    end = c;
                }
            }

            var cuts =
                new int[rows];

            for (var y = rows - 1; y >= 0; y--)
            {
                cuts[y] = end;

                if (y > 0)
                    end = from[y * band + end];
            }

            return cuts;
        }

        // Free solution first; then closed on itself from its own start column.
        var free =
            Solve(-1, out _);

        return Solve(free[0], out _);
    }

    /// <summary>Width (px) over which tones meet at a stroke end.</summary>
    internal const int EndWindow = 6;

    /// <summary>Half length (px) of a dithered stroke end.</summary>
    internal const int StrokeTaper = 12;

    /// <summary>Tone units per px away from the wandering place where stroke ends gather.</summary>
    internal const double WanderPull = 1.5;

    /// <summary>
    /// Widens a design of horizontal strokes by lengthening them: along a top-to-bottom path
    /// where every row continues smoothly <c>k</c> px further on, each row gets the k pixels just
    /// before the path once more. Dither and texture are copied, not stretched; a path never
    /// runs through a stroke end (that is where a row does not continue), and later paths keep
    /// away from earlier ones, so the added width is spread out. Every path closes on itself
    /// along, so the rapport still wraps.
    /// </summary>
    private static (byte[] Grid, int Width) Lengthen(
        byte[] grid,
        int gw,
        int gh,
        int wanted,
        int[] rgb)
    {
        var k =
            Math.Clamp(gw / 12, 4, 32);

        // The original column every pixel shows: a copy is fine once; content that already shows
        // twice nearby must not be copied again (three identical pieces side by side beat).
        var columns =
            gw;
        var origin =
            new int[gw * gh];

        for (var at = 0; at < origin.Length; at++)
            origin[at] = at % gw;

        var seen =
            new int[columns];

        while (wanted > 0)
        {
            var piece =
                Math.Min(k, wanted);

            if (gw <= piece + 2)
            {
                // Too narrow: plain repeat of the last columns.
                piece = Math.Min(piece, gw);
            }

            var tone =
                ToneMap(grid, gw, gh, rgb);

            // Inserting before column x: row continues from x - 1 into the copy starting at
            // x - piece; the copy's end joins x naturally.
            var energy =
                new long[gw * gh];
            double mean = 0;

            for (var y = 0; y < gh; y++)
            {
                for (var x = piece; x < gw; x++)
                {
                    var at =
                        y * gw + x;
                    var e =
                        (long)Distance(tone, at - 1, at - piece);
                    energy[at] = e;
                    mean += e;
                }
            }

            mean /= Math.Max(1, (gw - piece) * gh);

            // Keep away from earlier insertions: a pixel whose content shows again within
            // Repeat px is doubled already. Copying doubled content makes a beat; next to doubled
            // content the added width bunches up.
            var doubled =
                new bool[gw];
            var doubledBefore =
                new int[gw + 1];
            var reach =
                3 * piece;

            for (var y = 0; y < gh; y++)
            {
                Array.Fill(seen, int.MinValue / 2);
                Array.Clear(doubled);

                for (var x = 0; x < gw; x++)
                {
                    var o =
                        origin[y * gw + x];

                    if (x - seen[o] <= reach)
                    {
                        doubled[x] = true;
                        doubled[seen[o]] = true;
                    }

                    seen[o] = x;
                }

                for (var x = 0; x < gw; x++)
                    doubledBefore[x + 1] = doubledBefore[x] + (doubled[x] ? 1 : 0);

                int Doubled(
                    int a,
                    int b) =>
                    doubledBefore[Math.Clamp(b, 0, gw)] - doubledBefore[Math.Clamp(a, 0, gw)];

                for (var x = piece; x < gw; x++)
                {
                    if (Doubled(x - piece, x) > piece / 4)
                        energy[y * gw + x] += (long)(50 * mean) + 1;
                    else if (Doubled(x - 2 * piece, x + piece) > 0)
                        energy[y * gw + x] += (long)(4 * mean) + 1;
                }
            }

            var path =
                CyclicPath(energy, gw, gh, piece, gw - 1);
            var nw =
                gw + piece;
            var next =
                new byte[nw * gh];
            var nextOrigin =
                new int[nw * gh];

            // The copy joins the row where it continues smoothly in tone; on a dithered design the
            // dither itself still breaks there, along a near-vertical line. So the join is a
            // transition of stroke-shaped patches: left of it the row (A = row[x]), right of it
            // the row shifted by the copy (B = row[x - piece]).
            var feather =
                FeatherWidth(grid, gw, gh, piece);
            var salt =
                gw * 7919 + wanted;

            for (var y = 0; y < gh; y++)
            {
                var x0 =
                    path[y];
                var to =
                    y * nw;
                var f =
                    Math.Max(0, Math.Min(feather, Math.Min(x0 - piece, gw - x0)));

                for (var x = 0; x < nw; x++)
                {
                    bool shifted;

                    if (x < x0 - f)
                    {
                        shifted = false;
                    }
                    else if (x >= x0 + f)
                    {
                        shifted = true;
                    }
                    else
                    {
                        var ramp =
                            (x - (x0 - f) + 0.5) / (2.0 * f);
                        shifted = PatchNoise(x, y, salt, Grain.Across) < ramp;
                    }

                    next[to + x] =
                        shifted
                            ? grid[y * gw + x - piece]
                            : grid[y * gw + x];
                    nextOrigin[to + x] =
                        shifted
                            ? origin[y * gw + x - piece]
                            : origin[y * gw + x];
                }
            }

            grid = next;
            origin = nextOrigin;
            gw = nw;
            wanted -= piece;
        }

        return (grid, gw);
    }

    /// <summary>
    /// Minimum-energy top-to-bottom path (one column per row, moving at most one column per row)
    /// within [from, to], ending next to where it starts so it wraps along.
    /// </summary>
    private static int[] CyclicPath(
        long[] energy,
        int gw,
        int gh,
        int from,
        int to)
    {
        from = Math.Min(from, to);

        int[] Run(
            int start)
        {
            var span =
                to - from + 1;
            var cost =
                new long[span * gh];
            var back =
                new int[span * gh];

            for (var i = 0; i < span; i++)
            {
                cost[i] =
                    start < 0 || i == start
                        ? energy[from + i]
                        : long.MaxValue / 4;
            }

            for (var y = 1; y < gh; y++)
            {
                for (var i = 0; i < span; i++)
                {
                    var best = i;

                    for (var j = Math.Max(0, i - 1); j <= Math.Min(span - 1, i + 1); j++)
                    {
                        if (cost[(y - 1) * span + j] < cost[(y - 1) * span + best])
                            best = j;
                    }

                    cost[y * span + i] = cost[(y - 1) * span + best] + energy[y * gw + from + i];
                    back[y * span + i] = best;
                }
            }

            // End within one column of the start (the path continues into row 0).
            var end = -1;

            for (var i = 0; i < span; i++)
            {
                if (start >= 0 && Math.Abs(i - start) > 1)
                    continue;

                if (end < 0 ||
                    cost[(gh - 1) * span + i] < cost[(gh - 1) * span + end])
                {
                    end = i;
                }
            }

            var path =
                new int[gh];

            for (var y = gh - 1; y >= 0; y--)
            {
                path[y] = from + end;
                end = back[y * span + end];
            }

            return path;
        }

        var free =
            Run(-1);

        return Run(free[gh - 1] - from);
    }

    /// <summary>Mean colour over 5 x 5 (wrapping), per pixel, as R, G, B.</summary>
    private static int[] ToneMap(
        byte[] grid,
        int gw,
        int gh,
        int[] rgb)
    {
        const int radius = 2;
        var rows =
            new int[gw * gh * 3];

        for (var y = 0; y < gh; y++)
        {
            for (var x = 0; x < gw; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var sum = 0;

                    for (var dx = -radius; dx <= radius; dx++)
                        sum += rgb[grid[y * gw + ((x + dx) % gw + gw) % gw] * 3 + channel];

                    rows[(y * gw + x) * 3 + channel] = sum;
                }
            }
        }

        var tone =
            new int[gw * gh * 3];

        for (var y = 0; y < gh; y++)
        {
            for (var x = 0; x < gw; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var sum = 0;

                    for (var dy = -radius; dy <= radius; dy++)
                        sum += rows[((((y + dy) % gh + gh) % gh) * gw + x) * 3 + channel];

                    tone[(y * gw + x) * 3 + channel] = sum / 25;
                }
            }
        }

        return tone;
    }

    private static int Distance(
        int[] tone,
        int a,
        int b) =>
        Math.Abs(tone[a * 3] - tone[b * 3]) +
        Math.Abs(tone[a * 3 + 1] - tone[b * 3 + 1]) +
        Math.Abs(tone[a * 3 + 2] - tone[b * 3 + 2]);

    /// <summary>
    /// Inserts one vertical strip into a <paramref name="gw"/> x <paramref name="gh"/> rapport:
    /// at most <paramref name="wanted"/> columns wider. The rapport is cut open at its right edge,
    /// or, when it wraps seamlessly across, at the column where a strip fits best (the result is
    /// then the rapport rotated to start right after the strip, which repeats the same way).
    /// </summary>
    private static (byte[] Grid, int Width) Splice(
        byte[] grid,
        int gw,
        int gh,
        int wanted,
        int bandWanted,
        Grain strokes,
        int[] rgb,
        Random random)
    {
        if (gw < 4)
        {
            // Too narrow to splice: plain repeat.
            var nw0 =
                gw + wanted;
            var plain =
                new byte[nw0 * gh];

            for (var y = 0; y < gh; y++)
                for (var x = 0; x < nw0; x++)
                    plain[y * nw0 + x] = grid[y * gw + x % gw];

            return (plain, nw0);
        }

        var b =
            bandWanted > 0
                ? bandWanted
                : Math.Clamp(gw / 6, 2, Band);
        b = Math.Max(1, Math.Min(b, (gw - 1) / 3));

        var added =
            Math.Min(wanted, gw - 2 * b);
        var length =
            added + 2 * b;

        // A strip is shifted along, or the rapport opened elsewhere than at its edge, only where
        // the rapport wraps seamlessly that way: otherwise its own join would move inside.
        var freeAlong =
            WrapRatio(grid, gw, gh, rgb) < SeamlessWrap;
        var freeAcross =
            WrapRatio(Transpose(grid, gw, gh), gh, gw, rgb) < SeamlessWrap;
        var cell =
            freeAlong && freeAcross
                ? 2 * Cell
                : Cell;
        var step =
            freeAlong && freeAcross
                ? 2 * Step
                : Step;
        // At most MaxShifts x MaxOpenings combinations: a long rapport free both ways took a minute.
        int[] Positions(
            bool free,
            int size,
            int most)
        {
            if (!free)
                return [0];

            var spacing =
                Math.Max(step, (size + most - 1) / most);

            return Enumerable.Range(0, (size + spacing - 1) / spacing).Select(k => k * spacing).ToArray();
        }

        var shifts =
            Positions(freeAlong, gh, MaxShifts);
        var openings =
            Positions(freeAcross, gw, MaxOpenings);

        // Tone of cells via summed-area tables over the rapport tiled 2 x 2 (indices wrap).
        var stride =
            2 * gw + 1;
        var sums =
            new long[3][];

        for (var channel = 0; channel < 3; channel++)
        {
            var table =
                new long[stride * (2 * gh + 1)];

            for (var y = 0; y < 2 * gh; y++)
            {
                long row = 0;

                for (var x = 0; x < 2 * gw; x++)
                {
                    row += rgb[grid[(y % gh) * gw + x % gw] * 3 + channel];
                    table[(y + 1) * stride + x + 1] = table[y * stride + x + 1] + row;
                }
            }

            sums[channel] = table;
        }

        long Tone(
            int x,
            int y,
            int cw,
            int channel)
        {
            var table =
                sums[channel];

            return table[(y + cell) * stride + x + cw] -
                   table[y * stride + x + cw] -
                   table[(y + cell) * stride + x] +
                   table[y * stride + x];
        }

        // Band of b columns starting at rapport column bandX, against the strip columns from
        // stripX, shifted along by shift.
        long Cost(
            int bandX,
            int stripX,
            int shift,
            long bound)
        {
            long cost = 0;

            for (var cy = 0; cy + cell <= gh && cost <= bound; cy += cell)
            {
                for (var cx = 0; cx < b; cx += cell)
                {
                    var cw =
                        Math.Min(cell, b - cx);

                    for (var channel = 0; channel < 3; channel++)
                    {
                        cost += Math.Abs(
                            Tone(bandX + cx, cy, cw, channel) -
                            Tone(stripX + cx, cy + shift, cw, channel));
                    }
                }
            }

            return cost;
        }

        // Opened at column p (the rapport then reads from p on): the strip's first b columns
        // overlap the rapport's last b before p, its last b the first b from p. The strip is taken
        // from the rapport as read from p, so it never crosses p.
        var candidates =
            new List<(long Cost, int Open, int Start, int Shift)>();
        var bound =
            long.MaxValue;
        const int keep = 8;

        foreach (var open in openings)
        {
            foreach (var shift in shifts)
            {
                for (var s = 0; s + length <= gw; s += step)
                {
                    var cost =
                        Cost((open + gw - b) % gw, (open + s) % gw, shift, bound);

                    if (cost > bound)
                        continue;

                    cost += Cost(open, (open + s + added + b) % gw, shift, bound - cost);

                    if (cost > bound)
                        continue;

                    candidates.Add((cost, open, s, shift));

                    if (candidates.Count >= keep * 8)
                    {
                        candidates.Sort();
                        candidates.RemoveRange(keep, candidates.Count - keep);
                        bound = candidates[^1].Cost;
                    }
                }
            }
        }

        candidates.Sort();

        if (candidates.Count > keep)
            candidates.RemoveRange(keep, candidates.Count - keep);

        // Among the best strips by tone, the one whose actual seams cost least.
        var joined =
            new List<(long Cost, int Open, int Start, int Shift, bool[] Left, bool[] Right)>();

        foreach (var (_, open, start, shift) in candidates)
        {
            byte Rapport(
                int x,
                int y) =>
                grid[y * gw + (open + x) % gw];

            byte At(
                int x,
                int y) =>
                grid[((y + shift) % gh) * gw + (open + start + x) % gw];

            // Left band: rapport on the left side of the cut, strip on the right.
            var (leftCost, left) =
                SeamSide(
                    (x, y) => Difference(Rapport(gw - b + x, y), At(x, y), rgb),
                    b,
                    gh);

            // Right band (over the rapport's first columns): strip on the left side.
            var (rightCost, right) =
                SeamSide(
                    (x, y) => Difference(At(added + b + x, y), Rapport(x, y), rgb),
                    b,
                    gh);

            joined.Add((leftCost + rightCost, open, start, shift, left, right));
        }

        joined.Sort((p, q) =>
            p.Cost != q.Cost
                ? p.Cost.CompareTo(q.Cost)
                : (p.Open, p.Start, p.Shift).CompareTo((q.Open, q.Start, q.Shift)));
        // The cheapest seams; the seed only varies among strips practically as good.
        var close =
            joined.Count(j => j.Cost <= joined[0].Cost * 1.05);
        var pick =
            joined[random.Next(Math.Max(1, close))];

        byte Source(
            int x,
            int y) =>
            grid[y * gw + (pick.Open + x) % gw];

        byte Strip(
            int x,
            int y) =>
            grid[((y + pick.Shift) % gh) * gw + (pick.Open + pick.Start + x) % gw];

        // Dithered designs are themselves made of mixed-colour transitions: there the seam is
        // dissolved into one (a hard cut through brush strokes would read as a straight line).
        // A seam that runs along the strokes lies between strokes already: only its edge is
        // softened like a stroke edge (a few px, pixel noise); a wide transition of patches would
        // break the strokes into dashes.
        var feather =
            FeatherWidth(grid, gw, gh, b);

        if (strokes == Grain.Along)
            feather = Math.Min(feather, StrokeEdge);


        var leftMix =
            Feather(pick.Left, b, gh, feather);
        var rightMix =
            Feather(pick.Right, b, gh, feather);
        var salt =
            random.Next();

        var nw =
            gw + added;
        var result =
            new byte[nw * gh];

        for (var y = 0; y < gh; y++)
        {
            for (var x = 0; x < gw - b; x++)
                result[y * nw + x] = Source(x, y);

            // Left band: the strip lies right of the cut.
            for (var x = 0; x < b; x++)
            {
                result[y * nw + gw - b + x] =
                    Chance(x, y, salt, strokes) < leftMix[y * b + x]
                        ? Strip(x, y)
                        : Source(gw - b + x, y);
            }

            for (var x = b; x < added + b; x++)
                result[y * nw + gw - b + x] = Strip(x, y);

            // The strip's tail lies over the rapport's first columns (wrap), left of the cut.
            for (var x = 0; x < b; x++)
            {
                if (Chance(x, y, salt + 1, strokes) >= rightMix[y * b + x])
                    result[y * nw + x] = Strip(added + b + x, y);
            }
        }

        return (result, nw);
    }

    /// <summary>
    /// Minimum cut through a band of <paramref name="band"/> columns that wraps along: which
    /// pixels lie on its left side. The cut is the shortest top-to-bottom path between the pixels
    /// (the planar dual of a minimum graph cut), free to run sideways and back up along a streak.
    /// Cutting between two pixels costs the colour difference of both, measured over 3 x 3, so a
    /// dithered ground does not steer the path. The path ends in the column it starts in.
    /// </summary>
    private static (long Cost, bool[] Left) SeamSide(
        Func<int, int, int> rawError,
        int band,
        int rows)
    {
        var raw =
            new int[band * rows];

        for (var y = 0; y < rows; y++)
            for (var x = 0; x < band; x++)
                raw[y * band + x] = rawError(x, y);

        var error =
            new long[band * rows];

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < band; x++)
            {
                long sum = 0;
                var n = 0;

                for (var dy = -1; dy <= 1; dy++)
                {
                    var yy =
                        (y + dy + rows) % rows;

                    for (var xx = Math.Max(0, x - 1); xx <= Math.Min(band - 1, x + 1); xx++)
                    {
                        sum += raw[yy * band + xx];
                        n++;
                    }
                }

                // + 1: no free wandering through identical areas.
                error[y * band + x] = sum / n + 1;
            }
        }

        if (band < 3)
        {
            // Too narrow for a path: cut down the middle.
            var middle =
                new bool[band * rows];

            for (var y = 0; y < rows; y++)
                for (var x = 0; x < band / 2 + (band == 1 ? 1 : 0); x++)
                    middle[y * band + x] = true;

            return (0, middle);
        }

        // Path corners: column i in [1, band - 1] (between pixels i - 1 and i), row j in [0, rows].
        var corners =
            band + 1;

        long Pixel(
            int x,
            int y) =>
            error[((y % rows + rows) % rows) * band + x];

        (long[] Distance, int[] Previous) Run(
            int startColumn)
        {
            var distance =
                new long[corners * (rows + 1)];
            var previous =
                new int[corners * (rows + 1)];
            Array.Fill(distance, long.MaxValue);
            Array.Fill(previous, -1);
            var queue =
                new PriorityQueue<int, long>();

            for (var i = 1; i < band; i++)
            {
                if (startColumn >= 0 && i != startColumn)
                    continue;

                distance[i] = 0;
                queue.Enqueue(i, 0);
            }

            while (queue.TryDequeue(out var node, out var d))
            {
                if (d > distance[node])
                    continue;

                var i =
                    node % corners;
                var j =
                    node / corners;

                if (j == rows)
                    continue;

                void Relax(
                    int ni,
                    int nj,
                    long step)
                {
                    var next =
                        nj * corners + ni;

                    if (d + step < distance[next])
                    {
                        distance[next] = d + step;
                        previous[next] = node;
                        queue.Enqueue(next, d + step);
                    }
                }

                // Down / up: separates pixel (i - 1) from pixel i in that row.
                Relax(i, j + 1, Pixel(i - 1, j) + Pixel(i, j));

                if (j > 0)
                    Relax(i, j - 1, Pixel(i - 1, j - 1) + Pixel(i, j - 1));

                // Sideways: separates the pixel above from the pixel below (wraps along).
                if (i + 1 < band)
                    Relax(i + 1, j, Pixel(i, j - 1) + Pixel(i, j));

                if (i - 1 >= 1)
                    Relax(i - 1, j, Pixel(i - 1, j - 1) + Pixel(i - 1, j));
            }

            return (distance, previous);
        }

        // Free path first; then the path forced to start and end in its free end column.
        var (free, _) =
            Run(-1);
        var column = 1;

        for (var i = 2; i < band; i++)
        {
            if (free[rows * corners + i] < free[rows * corners + column])
                column = i;
        }

        var (forced, from) =
            Run(column);

        // Walls: the pixel links the path crosses.
        var wallRight =
            new bool[band * rows];
        var wallDown =
            new bool[band * rows];
        var at =
            rows * corners + column;

        while (from[at] >= 0)
        {
            var back =
                from[at];
            int i0 = back % corners, j0 = back / corners;
            int i1 = at % corners, j1 = at / corners;

            if (i0 == i1)
            {
                // Vertical step: between pixel (i - 1) and i of row min(j0, j1).
                wallRight[Math.Min(j0, j1) % rows * band + i0 - 1] = true;
            }
            else
            {
                // Sideways step in corner row j: between pixel rows j - 1 and j, column min(i).
                var row =
                    ((j0 - 1) % rows + rows) % rows;
                wallDown[row * band + Math.Min(i0, i1)] = true;
            }

            at = back;
        }

        // Left side: everything reached from the band's first column without crossing the path.
        var left =
            new bool[band * rows];
        var stack =
            new Stack<int>();

        for (var y = 0; y < rows; y++)
        {
            left[y * band] = true;
            stack.Push(y * band);
        }

        while (stack.Count > 0)
        {
            var p =
                stack.Pop();
            var x =
                p % band;
            var y =
                p / band;

            void Visit(
                int q)
            {
                if (!left[q])
                {
                    left[q] = true;
                    stack.Push(q);
                }
            }

            if (x + 1 < band && !wallRight[p])
                Visit(p + 1);

            if (x > 0 && !wallRight[p - 1])
                Visit(p - 1);

            var down =
                (y + 1) % rows;

            if (!wallDown[p])
                Visit(down * band + x);

            var up =
                (y - 1 + rows) % rows;

            if (!wallDown[up * band + x])
                Visit(up * band + x);
        }

        return (forced[rows * corners + column], left);
    }

    /// <summary>
    /// Half width (px) of the dithered transition across a seam: none for flat colour areas
    /// (B317B splotches with outlines change colour at 25 % of pixels; a transition breaks them
    /// into speckles), up to half the band for a fully dithered design (B390A: 59 %).
    /// </summary>
    internal static int FeatherWidth(
        byte[] grid,
        int gw,
        int gh,
        int band)
    {
        long changes = 0;

        for (var y = 0; y < gh; y++)
            for (var x = 1; x < gw; x++)
                if (grid[y * gw + x] != grid[y * gw + x - 1])
                    changes++;

        var share =
            changes / (double)Math.Max(1, (gw - 1) * gh);
        var strength =
            Math.Clamp((share - 0.3) / 0.25, 0, 1);

        return (int)Math.Round(strength * band / 2);
    }

    /// <summary>
    /// Per band pixel, the chance to take the source on the right side of the cut: 0 / 1 away
    /// from it, rising across <paramref name="feather"/> px on either side, and pure at the band
    /// edges.
    /// </summary>
    private static double[] Feather(
        bool[] left,
        int band,
        int rows,
        int feather)
    {
        var mix =
            new double[band * rows];

        if (feather <= 0)
        {
            for (var k = 0; k < mix.Length; k++)
                mix[k] = left[k] ? 0 : 1;

            return mix;
        }

        // Distance (4-connected, wrapping along) to the other side of the cut.
        var distance =
            new int[band * rows];
        Array.Fill(distance, int.MaxValue);
        var queue =
            new Queue<int>();

        IEnumerable<int> Neighbours(
            int p)
        {
            var x =
                p % band;
            var y =
                p / band;

            if (x > 0)
                yield return p - 1;

            if (x + 1 < band)
                yield return p + 1;

            yield return ((y + 1) % rows) * band + x;
            yield return ((y - 1 + rows) % rows) * band + x;
        }

        for (var p = 0; p < mix.Length; p++)
        {
            if (Neighbours(p).Any(q => left[q] != left[p]))
            {
                distance[p] = 1;
                queue.Enqueue(p);
            }
        }

        while (queue.Count > 0)
        {
            var p =
                queue.Dequeue();

            foreach (var q in Neighbours(p))
            {
                if (left[q] == left[p] &&
                    distance[q] > distance[p] + 1)
                {
                    distance[q] = distance[p] + 1;
                    queue.Enqueue(q);
                }
            }
        }

        for (var p = 0; p < mix.Length; p++)
        {
            var x =
                p % band;
            var reach =
                Math.Min(1.0, (distance[p] - 0.5) / feather);
            var value =
                left[p]
                    ? 0.5 - 0.5 * reach
                    : 0.5 + 0.5 * reach;

            value = Math.Min(value, x / (double)feather);
            value = Math.Max(value, 1 - (band - 1 - x) / (double)feather);
            mix[p] = Math.Clamp(value, 0, 1);
        }

        return mix;
    }

    internal const int MaxShifts = 64;

    internal const int MaxOpenings = 32;

    /// <summary>Half width (px) of the softened edge where a seam runs along the strokes.</summary>
    internal const int StrokeEdge = 4;

    /// <summary>Noise for a splice transition: pixel noise along the strokes, patches otherwise.</summary>
    private static double Chance(
        int x,
        int y,
        int salt,
        Grain strokes) =>
        strokes == Grain.Along
            ? Noise(x, y, salt)
            : PatchNoise(x, y, salt, strokes);

    /// <summary>The same strokes seen in the transposed rapport.</summary>
    private static Grain Turn(
        Grain grain) =>
        grain switch
        {
            Grain.Across => Grain.Along,
            Grain.Along => Grain.Across,
            _ => Grain.None,
        };

    /// <summary>
    /// Smooth noise in [0, 1) for choosing between two sources in a transition: patches drawn out
    /// along the strokes (40 x 3 px; 6 x 6 without strokes) with a little per-pixel grain, so a
    /// transition reads as stroke fragments instead of salt and pepper.
    /// </summary>
    private static double PatchNoise(
        int x,
        int y,
        int salt,
        Grain strokes)
    {
        var (across, along) =
            strokes switch
            {
                Grain.Across => (40.0, 3.0),
                Grain.Along => (3.0, 40.0),
                _ => (6.0, 6.0),
            };
        var fx =
            x / across;
        var fy =
            y / along;
        var ix =
            (int)Math.Floor(fx);
        var iy =
            (int)Math.Floor(fy);
        var tx =
            fx - ix;
        var ty =
            fy - iy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        var top =
            Noise(ix, iy, salt) * (1 - tx) + Noise(ix + 1, iy, salt) * tx;
        var bottom =
            Noise(ix, iy + 1, salt) * (1 - tx) + Noise(ix + 1, iy + 1, salt) * tx;
        var smooth =
            top * (1 - ty) + bottom * ty;

        // Smoothed values bunch around 0.5: spread them back over [0, 1).
        var spread =
            Math.Clamp((smooth - 0.5) * 2.2 + 0.5, 0, 1);

        return Math.Clamp(0.85 * spread + 0.15 * Noise(x, y, salt + 7), 0, 0.999999);
    }

    /// <summary>Deterministic per-pixel noise in [0, 1).</summary>
    private static double Noise(
        int x,
        int y,
        int salt)
    {
        unchecked
        {
            var hash =
                (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>How visible the rapport's own join along is (last row against the first).</summary>
    private static double WrapRatio(
        byte[] grid,
        int gw,
        int gh,
        int[] rgb)
    {
        if (gh < 3)
            return double.PositiveInfinity;

        double Rows(
            int a,
            int b)
        {
            long sum = 0;

            for (var x = 0; x < gw; x++)
                sum += Difference(grid[a * gw + x], grid[b * gw + x], rgb);

            return sum / (double)gw;
        }

        double inside = 0;
        var count = 0;

        for (var y = 0; y + 1 < gh; y += Math.Max(1, gh / 64))
        {
            inside += Rows(y, y + 1);
            count++;
        }

        return Rows(gh - 1, 0) / Math.Max(1e-6, inside / count);
    }

    private static int Difference(
        byte a,
        byte b,
        int[] rgb) =>
        Math.Abs(rgb[a * 3] - rgb[b * 3]) +
        Math.Abs(rgb[a * 3 + 1] - rgb[b * 3 + 1]) +
        Math.Abs(rgb[a * 3 + 2] - rgb[b * 3 + 2]);

    private static byte[] Transpose(
        byte[] grid,
        int gw,
        int gh)
    {
        var result =
            new byte[gw * gh];

        for (var y = 0; y < gh; y++)
            for (var x = 0; x < gw; x++)
                result[x * gh + y] = grid[y * gw + x];

        return result;
    }
}
