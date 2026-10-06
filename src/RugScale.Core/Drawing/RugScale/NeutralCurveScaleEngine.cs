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
/// 2. <b>Pixel-Cord line work.</b> A one-pixel source cord becomes an alternating one/two pixel
///    cord with an irregular step cadence under a 1.6x neutral resize. Protected cord colours are
///    therefore not resampled: every source cord chain is redrawn by
///    <see cref="PixelCordCurveRedraw"/> as a one-pixel, 4-connected line along the smooth curve
///    its staircase represents (each point within 0.5 source px of the source, corners and
///    junctions pinned), at the target pen size.
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
        int CordChainsRedrawn,
        int TargetCordPixels,
        int BarrierRepairs,
        int UnresolvedContacts,
        int CordCleanups);

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

        // Classic designs draw outlines and tracery one cell wide in colours that are also used
        // as fills, so no palette colour qualifies as a cord. Such line work is found per pixel,
        // the fill underneath it is completed, and the lines are drawn on top one pixel wide.
        var lines =
            LineLayer.Build(
                src,
                w,
                h,
                isCord);
        var fillSrc =
            lines?.Under ?? src;

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
                    fillSrc,
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
                    fillSrc[nearestIndex];
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
                            fillSrc[cy * w + cx];

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
                        seen[fillSrc[cy * w + Math.Clamp(sx, 0, w - 1)]] = false;
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

        // ---- 1b. straight fill edges stay exactly straight ----------------------------------
        StraightFillEdges.Snap(
            fillSrc,
            w,
            h,
            isCord,
            target,
            W,
            H);

        // ---- 2. cords: redraw each source cord chain as a smooth Pixel-Cord curve -----------
        var penX =
            PenSize(
                sourceWarpDensity,
                targetWarpDensity);
        var penY =
            PenSize(
                sourceWeftDensity,
                targetWeftDensity);
        var replayed =
            PixelCordCurveRedraw.Draw(
                src,
                w,
                h,
                isCord,
                target,
                W,
                H,
                penX,
                penY);

        // Classic line work keeps its exact cell layout (small motifs must not be reshaped);
        // only its width is made a constant one pixel.
        lines?.Render(
            target,
            W,
            H);

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

        // Where two redrawn chains meet (junctions, sharp tips, L corners) their pens overlap
        // into small 2x2 cord knots the source does not have. Peel them back to one cell.
        var thinned =
            ThinCordKnots(
                src,
                w,
                h,
                target,
                W,
                H,
                isCord,
                SolidCordAreaMask(
                    src,
                    w,
                    h,
                    isCord),
                contacts);

        // Knots of the source (two cord cells wide where a branch leaves a line) leave one- or
        // two-pixel spikes on the redrawn line. Prune spikes the source has no tip for.
        thinned +=
            PruneCordSpurs(
                src,
                w,
                h,
                target,
                W,
                H,
                isCord,
                contacts);

        // ---- 4. exact designer symmetry -----------------------------------------------------
        PreserveExactSymmetry(
            src,
            w,
            h,
            target,
            W,
            H);

        // A cord crossing the mirror seam can lose the cells that joined it on the discarded
        // side. Re-join stranded cord pieces (only while the target has more cord parts than the
        // source) and mirror again so the joins are symmetric too.
        if (BridgeStrandedCordPieces(
                src,
                w,
                h,
                target,
                W,
                H,
                isCord) > 0)
        {
            PreserveExactSymmetry(
                src,
                w,
                h,
                target,
                W,
                H);
        }

        Commit(
            target,
            destination);

        return new NeutralCurveReport(
            cords.Count,
            replayed,
            target.Count(value => isCord[value]),
            repairs,
            unresolved,
            thinned);
    }

    /// <summary>
    /// Peels cord cells out of solid 2x2 cord blocks in the target, one side at a time so the
    /// line stays centred, keeping the cord's 4-connected topology and its endpoints. Blocks over
    /// genuinely wide source cord are kept. A peeled cell takes the commonest neighbouring fill
    /// colour that is a legal contact with all of its 4-neighbours; without one it stays cord.
    /// </summary>
    private static int ThinCordKnots(
        byte[] source,
        int w,
        int h,
        byte[] target,
        int W,
        int H,
        bool[] isCord,
        bool[] wideCord,
        CurveFillRibbonFidelityGuard.SourceContacts contacts)
    {
        ReadOnlySpan<(int Dx, int Dy)> sides =
        [
            (0, -1),
            (0, 1),
            (-1, 0),
            (1, 0),
        ];
        var thinned = 0;

        bool Cord(
            int x,
            int y,
            byte color) =>
            x >= 0 &&
            y >= 0 &&
            x < W &&
            y < H &&
            target[y * W + x] == color;

        bool OverWideSourceCord(
            int x,
            int y)
        {
            var sx =
                (int)((long)x * w / W);
            var sy =
                (int)((long)y * h / H);

            for (var dy = -1;
                 dy <= 1;
                 dy++)
            {
                for (var dx = -1;
                     dx <= 1;
                     dx++)
                {
                    var nx =
                        sx + dx;
                    var ny =
                        sy + dy;

                    if (nx >= 0 &&
                        ny >= 0 &&
                        nx < w &&
                        ny < h &&
                        wideCord[ny * w + nx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        bool SourcePinholeNear(
            int x,
            int y)
        {
            var sx =
                (int)((long)x * w / W);
            var sy =
                (int)((long)y * h / H);

            for (var dy = -1;
                 dy <= 1;
                 dy++)
            {
                for (var dx = -1;
                     dx <= 1;
                     dx++)
                {
                    var nx =
                        sx + dx;
                    var ny =
                        sy + dy;

                    if (nx < 1 ||
                        ny < 1 ||
                        nx >= w - 1 ||
                        ny >= h - 1)
                    {
                        continue;
                    }

                    var at =
                        ny * w + nx;

                    if (!isCord[source[at]] &&
                        isCord[source[at - 1]] &&
                        isCord[source[at + 1]] &&
                        isCord[source[at - w]] &&
                        isCord[source[at + w]])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        int Replacement(
            int index)
        {
            Span<int> around =
                stackalloc int[4];
            var count =
                Neighbours(
                    index,
                    W,
                    H,
                    around);
            var best = -1;
            var bestVotes = 0;

            for (var n = 0;
                 n < count;
                 n++)
            {
                var candidate =
                    target[around[n]];

                if (isCord[candidate])
                    continue;

                var votes = 0;
                var legal = true;

                for (var m = 0;
                     m < count;
                     m++)
                {
                    var other =
                        target[around[m]];

                    if (other == candidate)
                    {
                        votes++;
                    }
                    else if (!isCord[other] &&
                             !contacts.Allowed(
                                 candidate,
                                 other,
                                 index))
                    {
                        legal = false;
                        break;
                    }
                }

                if (legal &&
                    votes > bestVotes)
                {
                    best = candidate;
                    bestVotes = votes;
                }
            }

            return best;
        }

        bool Removable(
            int x,
            int y,
            byte color) =>
            PixelCordCurveRedraw.InSolidBlock(
                (cx, cy) => Cord(cx, cy, color),
                x,
                y) &&
            PixelCordCurveRedraw.IsSimplePoint(
                (cx, cy) => Cord(cx, cy, color),
                x,
                y) &&
            !OverWideSourceCord(
                x,
                y);

        // A knot often encloses a one-pixel fill pinhole (an "o" in the line), or two chains of a
        // wide source cord run side by side around a fill sliver. Unless the source has that fill
        // cell, it joins the knot and is peeled with it.
        for (var index = 0;
             index < target.Length;
             index++)
        {
            if (isCord[target[index]])
                continue;

            var x =
                index % W;
            var y =
                index / W;

            if (x == 0 ||
                y == 0 ||
                x == W - 1 ||
                y == H - 1)
            {
                continue;
            }

            var left =
                target[index - 1];
            var up =
                target[index - W];
            var pinhole =
                isCord[left] &&
                target[index + 1] == left &&
                up == left &&
                target[index + W] == left &&
                !SourcePinholeNear(
                    x,
                    y);

            // A one-pixel fill sliver squeezed between two parallel cords where the source cell
            // is solid cord: the two chains of a wide source cord drawn side by side.
            var sourceColor =
                source[
                    (int)((y + 0.5) * h / H) * w +
                    (int)((x + 0.5) * w / W)];
            var sliver =
                isCord[sourceColor] &&
                ((left == sourceColor &&
                  target[index + 1] == sourceColor) ||
                 (up == sourceColor &&
                  target[index + W] == sourceColor));

            if (!pinhole &&
                !sliver)
            {
                continue;
            }

            target[index] =
                pinhole
                    ? left
                    : sourceColor;
            thinned++;
        }

        for (var iteration = 0;
             iteration < 4;
             iteration++)
        {
            var changed = 0;

            foreach (var (sx, sy) in sides)
            {
                var candidates =
                    new List<int>();

                for (var y = 0;
                     y < H;
                     y++)
                {
                    for (var x = 0;
                         x < W;
                         x++)
                    {
                        var color =
                            target[y * W + x];

                        if (isCord[color] &&
                            !Cord(x + sx, y + sy, color) &&
                            Removable(x, y, color))
                        {
                            candidates.Add(y * W + x);
                        }
                    }
                }

                foreach (var index in candidates)
                {
                    var x =
                        index % W;
                    var y =
                        index / W;
                    var color =
                        target[index];

                    if (!Removable(x, y, color))
                        continue;

                    var fill =
                        Replacement(
                            index);

                    if (fill < 0)
                        continue;

                    target[index] =
                        (byte)fill;
                    changed++;
                }
            }

            thinned += changed;

            if (changed == 0)
                break;
        }

        return thinned;
    }

    /// <summary>Source cord cells covered by a solid 3x3 block of one cord colour.</summary>
    private static bool[] SolidCordAreaMask(
        byte[] src,
        int w,
        int h,
        bool[] isCord)
    {
        var mask =
            new bool[src.Length];

        for (var y = 1;
             y + 1 < h;
             y++)
        {
            for (var x = 1;
                 x + 1 < w;
                 x++)
            {
                var color =
                    src[y * w + x];

                if (!isCord[color])
                    continue;

                var solid = true;

                for (var dy = -1;
                     dy <= 1 && solid;
                     dy++)
                {
                    for (var dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        if (src[(y + dy) * w + x + dx] != color)
                        {
                            solid = false;
                            break;
                        }
                    }
                }

                if (!solid)
                    continue;

                for (var dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (var dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        mask[(y + dy) * w + x + dx] = true;
                    }
                }
            }
        }

        return mask;
    }

    /// <summary>Longest spike (cells up to the junction it leaves from) that may be pruned.</summary>
    private const int MaximumSpurLength = 2;

    /// <summary>
    /// Removes short cord spikes: walking the 4-connected cord graph from an end cell reaches a
    /// junction (three or more cord 4-neighbours) within <see cref="MaximumSpurLength"/> cells.
    /// Spikes are kept where the source has a cord end near the mapped location, and a cell is
    /// only removed when a fill colour can legally take its place.
    /// </summary>
    private static int PruneCordSpurs(
        byte[] source,
        int w,
        int h,
        byte[] target,
        int W,
        int H,
        bool[] isCord,
        CurveFillRibbonFidelityGuard.SourceContacts contacts)
    {
        var pruned = 0;
        Span<int> around =
            stackalloc int[4];

        int CordDegree(
            byte[] pixels,
            int width,
            int height,
            int index,
            byte color,
            Span<int> buffer)
        {
            Span<int> all =
                stackalloc int[4];
            var count =
                Neighbours(
                    index,
                    width,
                    height,
                    all);
            var degree = 0;

            for (var n = 0;
                 n < count;
                 n++)
            {
                if (pixels[all[n]] == color)
                    buffer[degree++] = all[n];
            }

            return degree;
        }

        bool SourceEndNear(
            int x,
            int y,
            byte color)
        {
            Span<int> buffer =
                stackalloc int[4];
            var sx =
                (int)((long)x * w / W);
            var sy =
                (int)((long)y * h / H);

            for (var dy = -2;
                 dy <= 2;
                 dy++)
            {
                for (var dx = -2;
                     dx <= 2;
                     dx++)
                {
                    var nx =
                        sx + dx;
                    var ny =
                        sy + dy;

                    if (nx < 0 ||
                        ny < 0 ||
                        nx >= w ||
                        ny >= h)
                    {
                        continue;
                    }

                    var at =
                        ny * w + nx;

                    if (source[at] == color &&
                        CordDegree(
                            source,
                            w,
                            h,
                            at,
                            color,
                            buffer) <= 1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        int LegalFill(
            int index)
        {
            Span<int> ring =
                stackalloc int[4];
            var count =
                Neighbours(
                    index,
                    W,
                    H,
                    ring);
            var best = -1;
            var bestVotes = 0;

            for (var n = 0;
                 n < count;
                 n++)
            {
                var candidate =
                    target[ring[n]];

                if (isCord[candidate])
                    continue;

                var votes = 0;
                var legal = true;

                for (var m = 0;
                     m < count;
                     m++)
                {
                    var other =
                        target[ring[m]];

                    if (other == candidate)
                    {
                        votes++;
                    }
                    else if (!isCord[other] &&
                             !contacts.Allowed(
                                 candidate,
                                 other,
                                 index))
                    {
                        legal = false;
                        break;
                    }
                }

                if (legal &&
                    votes > bestVotes)
                {
                    best = candidate;
                    bestVotes = votes;
                }
            }

            return best;
        }

        var spur =
            new List<int>(MaximumSpurLength);

        for (var start = 0;
             start < target.Length;
             start++)
        {
            var color =
                target[start];

            if (!isCord[color] ||
                CordDegree(
                    target,
                    W,
                    H,
                    start,
                    color,
                    around) != 1)
            {
                continue;
            }

            // Walk the line until it meets a junction.
            spur.Clear();
            var previous = -1;
            var current = start;
            var reachedJunction = false;

            while (spur.Count <= MaximumSpurLength)
            {
                var degree =
                    CordDegree(
                        target,
                        W,
                        H,
                        current,
                        color,
                        around);

                if (degree >= 3)
                {
                    reachedJunction = true;
                    break;
                }

                if (degree == 0 ||
                    (degree == 1 && previous >= 0))
                {
                    break;
                }

                spur.Add(current);
                var next =
                    around[0] == previous && degree > 1
                        ? around[1]
                        : around[0];
                previous = current;
                current = next;
            }

            if (!reachedJunction ||
                spur.Count == 0 ||
                spur.Count > MaximumSpurLength ||
                SourceEndNear(
                    start % W,
                    start / W,
                    color))
            {
                continue;
            }

            // Remove from the tip inwards; stop at the first cell no fill can legally replace.
            foreach (var index in spur)
            {
                var fill =
                    LegalFill(
                        index);

                if (fill < 0)
                    break;

                target[index] =
                    (byte)fill;
                pruned++;
            }
        }

        return pruned;
    }

    /// <summary>Largest cord piece that may be re-joined, and the widest gap (cells) it may jump.</summary>
    private const int MaximumStrandedPiece = 40;
    private const int MaximumBridgeGap = 2;

    private static int BridgeStrandedCordPieces(
        byte[] src,
        int w,
        int h,
        byte[] target,
        int W,
        int H,
        bool[] isCord)
    {
        var bridged = 0;

        for (var color = 0;
             color < 256;
             color++)
        {
            if (!isCord[color])
                continue;

            var cord =
                (byte)color;
            var sourceParts =
                CountParts(
                    src,
                    w,
                    h,
                    cord);
            var labels =
                Label(
                    target,
                    W,
                    H,
                    cord,
                    out var sizes);
            var targetParts =
                sizes.Count - 1;

            if (targetParts <= sourceParts)
                continue;

            for (var label = 1;
                 label < sizes.Count &&
                 targetParts > sourceParts;
                 label++)
            {
                if (sizes[label] > MaximumStrandedPiece)
                    continue;

                // Nearest cell of another piece within the gap limit.
                var bestFrom = -1;
                var bestTo = -1;
                var bestDistance = int.MaxValue;

                for (var index = 0;
                     index < labels.Length;
                     index++)
                {
                    if (labels[index] != label)
                        continue;

                    var x =
                        index % W;
                    var y =
                        index / W;

                    for (var dy = -MaximumBridgeGap - 1;
                         dy <= MaximumBridgeGap + 1;
                         dy++)
                    {
                        for (var dx = -MaximumBridgeGap - 1;
                             dx <= MaximumBridgeGap + 1;
                             dx++)
                        {
                            var nx =
                                x + dx;
                            var ny =
                                y + dy;

                            if (nx < 0 ||
                                ny < 0 ||
                                nx >= W ||
                                ny >= H)
                            {
                                continue;
                            }

                            var other =
                                labels[ny * W + nx];

                            if (other == 0 ||
                                other == label)
                            {
                                continue;
                            }

                            var distance =
                                Math.Abs(dx) +
                                Math.Abs(dy);

                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestFrom = index;
                                bestTo = ny * W + nx;
                            }
                        }
                    }
                }

                if (bestFrom < 0 ||
                    bestDistance - 1 > MaximumBridgeGap)
                {
                    continue;
                }

                // 4-connected L path, horizontal first.
                var cx =
                    bestFrom % W;
                var cy =
                    bestFrom / W;
                var tx =
                    bestTo % W;
                var ty =
                    bestTo / W;

                while (cx != tx)
                {
                    cx += Math.Sign(tx - cx);
                    target[cy * W + cx] = cord;
                }

                while (cy != ty)
                {
                    cy += Math.Sign(ty - cy);
                    target[cy * W + cx] = cord;
                }

                bridged++;
                targetParts--;
            }
        }

        return bridged;
    }

    /// <summary>8-connected labels of one colour; sizes[0] is unused.</summary>
    private static int[] Label(
        byte[] pixels,
        int width,
        int height,
        byte color,
        out List<int> sizes)
    {
        var labels =
            new int[pixels.Length];
        sizes =
            new List<int> { 0 };
        var queue =
            new Queue<int>();

        for (var start = 0;
             start < pixels.Length;
             start++)
        {
            if (pixels[start] != color ||
                labels[start] != 0)
            {
                continue;
            }

            var label =
                sizes.Count;
            var size = 0;
            labels[start] = label;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current =
                    queue.Dequeue();
                size++;
                var x =
                    current % width;
                var y =
                    current / width;

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

                        if (nx < 0 ||
                            ny < 0 ||
                            nx >= width ||
                            ny >= height)
                        {
                            continue;
                        }

                        var next =
                            ny * width +
                            nx;

                        if (pixels[next] != color ||
                            labels[next] != 0)
                        {
                            continue;
                        }

                        labels[next] = label;
                        queue.Enqueue(next);
                    }
                }
            }

            sizes.Add(size);
        }

        return labels;
    }

    private static int CountParts(
        byte[] pixels,
        int width,
        int height,
        byte color)
    {
        Label(
            pixels,
            width,
            height,
            color,
            out var sizes);

        return sizes.Count - 1;
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

    /// <summary>Cord cells that are at least three cells thick in every direction: real cord areas.</summary>
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

                // A genuine cord AREA is at least three cells thick in every direction. Two-cell
                // spots (where two cords touch at a junction or a tip) are line work and are
                // handled by the cord redraw, not grown as an area.
                var thick = true;

                foreach (var (dx, dy) in axes)
                {
                    var run = 1;

                    for (var k = 1;
                         k <= 2;
                         k++)
                    {
                        var nx =
                            x + dx * k;
                        var ny =
                            y + dy * k;

                        if (nx < 0 || nx >= w || ny < 0 || ny >= h ||
                            src[ny * w + nx] != color)
                        {
                            break;
                        }

                        run++;
                    }

                    for (var k = 1;
                         k <= 2;
                         k++)
                    {
                        var nx =
                            x - dx * k;
                        var ny =
                            y - dy * k;

                        if (nx < 0 || nx >= w || ny < 0 || ny >= h ||
                            src[ny * w + nx] != color)
                        {
                            break;
                        }

                        run++;
                    }

                    if (run < 3)
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
