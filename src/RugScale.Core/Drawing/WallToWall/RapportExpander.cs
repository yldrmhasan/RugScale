using RugScale.Core.Models;

namespace RugScale.Core.Drawing.WallToWall;

/// <summary>
/// "Rapor açma": grows a rapport to a larger width and / or length with new content made from the
/// design's own texture, so a small rapport (B317B: 400 x 400) repeats less visibly on a roll.
///
/// The original rapport stays in the top-left corner (only the band of <see cref="Overlap"/> px
/// along its edges can be crossed by a seam). The added area is filled in raster
/// order with <see cref="BlockSize"/> blocks copied 1:1 from the source design (anywhere in its
/// content area). Every block is chosen among the candidates that best match the pixels already
/// placed in its overlap; the canvas is treated as a torus on the expanding axes, so the blocks at
/// the far edge must also match the rapport's opposite edge and the expanded rapport repeats
/// seamlessly. Inside the left and top overlaps the new block is cut in along the minimum-mismatch
/// path. To avoid stamping the same patch over and over, the block is drawn at random (fixed
/// seed, reproducible) from the <see cref="Shortlist"/> best candidates.
/// </summary>
public static class RapportExpander
{
    internal const int BlockSize = 32;

    internal const int Overlap = 8;

    /// <summary>Candidate positions are tried on a grid with this step (source px).</summary>
    internal const int CandidateStep = 3;

    internal const int Shortlist = 5;

    public static DesignDocument Expand(
        DesignDocument source,
        RapportTile tile,
        int newWidth,
        int newHeight,
        int seed = 1,
        int blockSize = 0)
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

        var src =
            new byte[w * h];

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                src[y * w + x] = source.GetPixel(x, y);

        // Candidate blocks come from the content area (technical marker columns excluded).
        var markers =
            RapportDetector.FindEdgeMarkers(source);
        var cx0 =
            markers?.Left ?? 0;
        var cx1 =
            w - (markers?.Right ?? 0);

        var W =
            newWidth;
        var H =
            newHeight;
        var canvas =
            new byte[W * H];
        var placed =
            new bool[W * H];

        for (var y = 0; y < th; y++)
        {
            for (var x = 0; x < tw; x++)
            {
                canvas[y * W + x] = src[(ty + y) * w + tx + x];
                placed[y * W + x] = true;
            }
        }

        if (W == tw &&
            H == th)
        {
            return ToDocument(canvas, W, H, source.Palette);
        }

        // Blocks scale with the rapport: large splotches need large blocks to keep their shape.
        var wanted =
            blockSize > 0
                ? blockSize
                : Math.Clamp(Math.Min(tw, th) / 6, BlockSize, 96);
        var block =
            Math.Min(wanted, Math.Min(Math.Min(cx1 - cx0, h), Math.Min(W, H)));
        var overlap =
            Math.Max(2, block / 4);
        var step =
            block - overlap;
        var random =
            new Random(seed);
        var candidates =
            new List<(int Cost, int X, int Y)>();
        var take =
            new bool[block * block];
        var error =
            new int[block * block];
        var mirrored =
            new int[block * block];
        var constraintAt =
            new int[block * block];
        var constraintOffset =
            new int[block * block];

        int Wrap(
            int value,
            int size) =>
            ((value % size) + size) % size;

        // Block grids anchored so that a block's first overlap band lies on the original rapport's
        // end; the last block reaches one band past the far edge, i.e. onto the rapport start.
        static List<int> Grid(
            int anchor,
            int size,
            int block,
            int overlap,
            int step)
        {
            var positions =
                new List<int>();
            var start =
                anchor - overlap;

            while (start > -block + overlap)
                start -= step;

            for (var p = start; p + block < size + overlap; p += step)
                positions.Add(p);

            positions.Add(size + overlap - block);
            return positions.Distinct().OrderBy(p => p).ToList();
        }

        var xs =
            Grid(tw, W, block, overlap, step);
        var ys =
            Grid(th, H, block, overlap, step);

        foreach (var by in ys)
        {
            foreach (var bx in xs)
            {
                var missing = false;

                for (var y = 0; y < block && !missing; y++)
                {
                    for (var x = 0; x < block; x++)
                    {
                        if (!placed[Wrap(by + y, H) * W + Wrap(bx + x, W)])
                        {
                            missing = true;
                            break;
                        }
                    }
                }

                if (!missing)
                    continue;

                // Only placed pixels constrain the choice: list them once per block.
                var constraintCount = 0;

                for (var y = 0; y < block; y++)
                {
                    for (var x = 0; x < block; x++)
                    {
                        var at =
                            Wrap(by + y, H) * W + Wrap(bx + x, W);

                        if (!placed[at])
                            continue;

                        constraintAt[constraintCount] = at;
                        constraintOffset[constraintCount] = y * w + x;
                        constraintCount++;
                    }
                }

                candidates.Clear();
                var bound =
                    int.MaxValue;

                for (var sy = 0; sy + block <= h; sy += CandidateStep)
                {
                    for (var sx = cx0; sx + block <= cx1; sx += CandidateStep)
                    {
                        var origin =
                            sy * w + sx;
                        var cost = 0;

                        for (var k = 0; k < constraintCount && cost <= bound; k++)
                        {
                            if (canvas[constraintAt[k]] != src[origin + constraintOffset[k]])
                                cost++;
                        }

                        if (cost > bound)
                            continue;

                        candidates.Add((cost, sx, sy));

                        if (candidates.Count >= Shortlist * 8)
                        {
                            candidates.Sort(Order);
                            candidates.RemoveRange(Shortlist, candidates.Count - Shortlist);
                            bound = candidates[^1].Cost;
                        }
                    }
                }

                candidates.Sort(Order);
                var pick =
                    candidates[random.Next(Math.Min(Shortlist, candidates.Count))];

                // Which edges of the block already hold pixels: each such band gets a seam.
                bool leftBand = false, rightBand = false, topBand = false, bottomBand = false;

                for (var y = 0; y < block; y++)
                {
                    for (var x = 0; x < block; x++)
                    {
                        var at =
                            Wrap(by + y, H) * W + Wrap(bx + x, W);
                        var isPlaced =
                            placed[at];
                        error[y * block + x] =
                            isPlaced &&
                            canvas[at] != src[(pick.Y + y) * w + pick.X + x]
                                ? 1
                                : 0;
                        mirrored[y * block + block - 1 - x] = error[y * block + x];

                        if (!isPlaced)
                            continue;

                        leftBand |= x < overlap;
                        rightBand |= x >= block - overlap;
                        topBand |= y < overlap;
                        bottomBand |= y >= block - overlap;
                    }
                }

                var cutLeft =
                    VerticalCut(error, block, overlap);
                var cutRight =
                    VerticalCut(mirrored, block, overlap);
                var cutTop =
                    HorizontalCut(error, block, overlap);
                var mirroredRows =
                    new int[block * block];

                for (var y = 0; y < block; y++)
                    for (var x = 0; x < block; x++)
                        mirroredRows[(block - 1 - y) * block + x] = error[y * block + x];

                var cutBottom =
                    HorizontalCut(mirroredRows, block, overlap);

                for (var y = 0; y < block; y++)
                {
                    for (var x = 0; x < block; x++)
                    {
                        var cx =
                            Wrap(bx + x, W);
                        var cy =
                            Wrap(by + y, H);
                        var at =
                            cy * W + cx;

                        if (!placed[at])
                        {
                            take[y * block + x] = true;
                            continue;
                        }

                        // The original rapport may only be crossed by a seam in the band along its
                        // own edges, never inside.
                        if (cx >= overlap &&
                            cx < tw - overlap &&
                            cy >= overlap &&
                            cy < th - overlap)
                        {
                            take[y * block + x] = false;
                            continue;
                        }

                        var inBand =
                            (leftBand && x < overlap) ||
                            (rightBand && x >= block - overlap) ||
                            (topBand && y < overlap) ||
                            (bottomBand && y >= block - overlap);

                        take[y * block + x] =
                            inBand &&
                            (!leftBand || x >= cutLeft[y]) &&
                            (!rightBand || block - 1 - x >= cutRight[y]) &&
                            (!topBand || y >= cutTop[x]) &&
                            (!bottomBand || block - 1 - y >= cutBottom[x]);
                    }
                }

                for (var y = 0; y < block; y++)
                {
                    for (var x = 0; x < block; x++)
                    {
                        if (!take[y * block + x])
                            continue;

                        var at =
                            Wrap(by + y, H) * W + Wrap(bx + x, W);
                        canvas[at] = src[(pick.Y + y) * w + pick.X + x];
                        placed[at] = true;
                    }
                }
            }
        }

        return ToDocument(canvas, W, H, source.Palette);
    }

    /// <summary>Per row, the first column (within the left overlap) where the new block takes over.</summary>
    private static int[] VerticalCut(
        int[] error,
        int block,
        int overlap)
    {
        var cost =
            new double[block * overlap];
        var from =
            new int[block * overlap];

        for (var x = 0; x < overlap; x++)
            cost[x] = error[x];

        for (var y = 1; y < block; y++)
        {
            for (var x = 0; x < overlap; x++)
            {
                var best = x;

                for (var px = Math.Max(0, x - 1); px <= Math.Min(overlap - 1, x + 1); px++)
                {
                    if (cost[(y - 1) * overlap + px] < cost[(y - 1) * overlap + best])
                        best = px;
                }

                cost[y * overlap + x] = cost[(y - 1) * overlap + best] + error[y * block + x];
                from[y * overlap + x] = best;
            }
        }

        var cut =
            new int[block];
        var end = 0;

        for (var x = 1; x < overlap; x++)
        {
            if (cost[(block - 1) * overlap + x] < cost[(block - 1) * overlap + end])
                end = x;
        }

        for (var y = block - 1; y >= 0; y--)
        {
            cut[y] = end;
            end = from[y * overlap + end];
        }

        return cut;
    }

    /// <summary>Per column, the first row (within the top overlap) where the new block takes over.</summary>
    private static int[] HorizontalCut(
        int[] error,
        int block,
        int overlap)
    {
        var cost =
            new double[block * overlap];
        var from =
            new int[block * overlap];

        for (var y = 0; y < overlap; y++)
            cost[y * block] = error[y * block];

        for (var x = 1; x < block; x++)
        {
            for (var y = 0; y < overlap; y++)
            {
                var best = y;

                for (var py = Math.Max(0, y - 1); py <= Math.Min(overlap - 1, y + 1); py++)
                {
                    if (cost[py * block + x - 1] < cost[best * block + x - 1])
                        best = py;
                }

                cost[y * block + x] = cost[best * block + x - 1] + error[y * block + x];
                from[y * block + x] = best;
            }
        }

        var cut =
            new int[block];
        var end = 0;

        for (var y = 1; y < overlap; y++)
        {
            if (cost[y * block + block - 1] < cost[end * block + block - 1])
                end = y;
        }

        for (var x = block - 1; x >= 0; x--)
        {
            cut[x] = end;
            end = from[end * block + x];
        }

        return cut;
    }

    private static int Order(
        (int Cost, int X, int Y) a,
        (int Cost, int X, int Y) b) =>
        a.Cost != b.Cost
            ? a.Cost.CompareTo(b.Cost)
            : a.Y != b.Y
                ? a.Y.CompareTo(b.Y)
                : a.X.CompareTo(b.X);

    private static DesignDocument ToDocument(
        byte[] canvas,
        int W,
        int H,
        Palette palette)
    {
        var result =
            new DesignDocument(
                W,
                H,
                palette);

        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                result.SetPixel(x, y, canvas[y * W + x]);

        return result;
    }
}
