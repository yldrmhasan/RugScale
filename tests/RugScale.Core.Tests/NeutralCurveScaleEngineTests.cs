using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class NeutralCurveScaleEngineTests
{
    // 0 field, 1 white Pixel-Cord, 2 tan fill, 3 navy fill.
    private static Palette Palette() =>
        new(
            new[]
            {
                new RugColor(230, 224, 218),
                new RugColor(255, 255, 255),
                new RugColor(216, 185, 124),
                new RugColor(0, 0, 102),
            });

    /// <summary>
    /// Tan disc and navy disc on a field, each outlined by a 1-cell white cord drawn as a
    /// 4-connected staircase, plus a long diagonal cord: the N69 drawing language in miniature.
    /// </summary>
    private static DesignDocument Source()
    {
        var source =
            new DesignDocument(
                80,
                80,
                Palette());

        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                var dTan =
                    Math.Sqrt((x - 22) * (x - 22) + (y - 22) * (y - 22));
                var dNavy =
                    Math.Sqrt((x - 56) * (x - 56) + (y - 56) * (y - 56));
                byte color = 0;

                if (dTan <= 14)
                    color = 2;
                else if (dNavy <= 14)
                    color = 3;

                source.SetPixel(x, y, color);
            }
        }

        // 4-connected outline: any fill cell with a 4-neighbour of another colour becomes cord.
        var copy =
            new byte[80, 80];

        for (var y = 0; y < 80; y++)
            for (var x = 0; x < 80; x++)
                copy[x, y] = source.GetPixel(x, y);

        for (var y = 1; y < 79; y++)
        {
            for (var x = 1; x < 79; x++)
            {
                var c = copy[x, y];

                if (c == 0)
                    continue;

                if (copy[x - 1, y] != c || copy[x + 1, y] != c ||
                    copy[x, y - 1] != c || copy[x, y + 1] != c)
                {
                    source.SetPixel(x, y, 1);
                }
            }
        }

        // Long diagonal 4-connected staircase cord across the field.
        for (var k = 0; k < 30; k++)
        {
            source.SetPixel(45 + k, 2 + k, 1);
            source.SetPixel(46 + k, 2 + k, 1);
        }

        return source;
    }

    private static DesignDocument Scale(
        DesignDocument source,
        int width,
        int height) =>
        DesignResizer.Scale(
            source,
            width,
            height,
            ScaleMode.CurveNeutral,
            40,
            60,
            40,
            60);

    [Fact]
    public void Output_UsesOnlySourceColours()
    {
        var source =
            Source();
        var target =
            Scale(
                source,
                128,
                128);
        var used =
            new HashSet<byte>();

        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                used.Add(source.GetPixel(x, y));

        for (var y = 0; y < target.Height; y++)
            for (var x = 0; x < target.Width; x++)
                Assert.Contains(target.GetPixel(x, y), used);
    }

    [Fact]
    public void Cords_StayOnePixelWideAtSameQuality()
    {
        var target =
            Scale(
                Source(),
                128,
                128);

        // No 2x2 cord block anywhere: a one-cell source cord must not become a two-cell band.
        var blocks = 0;

        for (var y = 0; y + 1 < target.Height; y++)
        {
            for (var x = 0; x + 1 < target.Width; x++)
            {
                if (target.GetPixel(x, y) == 1 &&
                    target.GetPixel(x + 1, y) == 1 &&
                    target.GetPixel(x, y + 1) == 1 &&
                    target.GetPixel(x + 1, y + 1) == 1)
                {
                    blocks++;
                }
            }
        }

        Assert.True(
            blocks <= 4,
            $"{blocks} solid 2x2 cord blocks; the cord was block-scaled.");
    }

    [Fact]
    public void Fills_NeverTouchWhereTheSourceSeparatesThem()
    {
        var target =
            Scale(
                Source(),
                128,
                128);

        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                var c = target.GetPixel(x, y);

                if (c is not (2 or 3))
                    continue;

                // Tan and navy fills, and either fill against the field, are always cord-separated
                // in the source.
                if (x + 1 < target.Width)
                {
                    var r = target.GetPixel(x + 1, y);
                    Assert.False(
                        r is 0 or 2 or 3 && r != c,
                        $"fill {c} touches {r} at ({x},{y})");
                }

                if (y + 1 < target.Height)
                {
                    var d = target.GetPixel(x, y + 1);
                    Assert.False(
                        d is 0 or 2 or 3 && d != c,
                        $"fill {c} touches {d} at ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public void Output_StaysCloseToNeutralResize()
    {
        var source =
            Source();
        var target =
            Scale(
                source,
                128,
                128);
        var nearest =
            DesignResizer.Scale(
                source,
                128,
                128,
                ScaleMode.NearestNeighbor);
        var same = 0;

        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                // Compare fills only: cords are intentionally redrawn thinner than nearest.
                var t = target.GetPixel(x, y);
                var n = nearest.GetPixel(x, y);

                if (t == n || t == 1 || n == 1)
                    same++;
            }
        }

        Assert.True(
            same >= target.Width * target.Height * 0.97,
            $"only {same} of {target.Width * target.Height} pixels agree with the neutral resize.");
    }

    [Fact]
    public void ExactSourceSymmetry_IsPreserved()
    {
        var source =
            new DesignDocument(
                40,
                40,
                Palette());

        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                var dx = Math.Min(x, 39 - x);
                source.SetPixel(x, y, dx < 6 ? (byte)3 : dx == 6 ? (byte)1 : (byte)0);
            }
        }

        var target =
            Scale(
                source,
                64,
                64);

        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 32; x++)
                Assert.Equal(target.GetPixel(x, y), target.GetPixel(63 - x, y));
    }
    [Fact]
    public void Cords_AreRedrawnWithRegularStepCadence()
    {
        // A straight 1-cell cord of slope 2/5, drawn as a 4-connected staircase whose source runs
        // alternate 2,3,2,3: a cell-by-cell 1.6x copy turns that into 3/5 jitter.
        var source =
            new DesignDocument(
                90,
                60,
                Palette());

        for (var y = 0; y < 60; y++)
            for (var x = 0; x < 90; x++)
                source.SetPixel(x, y, 0);

        static int F(int x) => 5 + x * 2 / 5;

        for (var x = 0; x < 90; x++)
        {
            source.SetPixel(x, F(x), 1);

            if (x > 0 && F(x) != F(x - 1))
                source.SetPixel(x, F(x - 1), 1);
        }

        var target =
            Scale(
                source,
                144,
                96);

        // Horizontal run lengths of the target cord along the line, away from the ends.
        var runs =
            new List<int>();

        for (var y = 14; y < 54; y++)
        {
            var count = 0;

            for (var x = 0; x < target.Width; x++)
            {
                if (target.GetPixel(x, y) == 1)
                    count++;
            }

            if (count > 0)
                runs.Add(count);
        }

        Assert.NotEmpty(runs);

        // A smooth line of constant slope has an (almost) constant cadence: every run is within
        // one cell of the others, never the 1-2-1-3 jitter of a cell-by-cell copy.
        Assert.True(
            runs.Max() - runs.Min() <= 1,
            $"irregular cadence: {string.Join(",", runs)}");
    }

    [Fact]
    public void Junctions_AndTips_HaveNoKnotsOrSpikes()
    {
        // Two 4-connected staircase cords converge into one horizontal cord (a Y junction).
        var source =
            new DesignDocument(
                90,
                60,
                Palette());

        for (var y = 0; y < 60; y++)
            for (var x = 0; x < 90; x++)
                source.SetPixel(x, y, 0);

        void Staircase(int x0, int y0, int x1, int y1)
        {
            var x = x0;
            var y = y0;
            source.SetPixel(x, y, 1);

            while (x != x1 || y != y1)
            {
                // Step along the axis that keeps closest to the straight segment.
                var stepX = x + Math.Sign(x1 - x);
                var stepY = y + Math.Sign(y1 - y);
                var errX = Math.Abs((stepX - x0) * (y1 - y0) - (y - y0) * (x1 - x0));
                var errY = Math.Abs((x - x0) * (y1 - y0) - (stepY - y0) * (x1 - x0));

                if (x != x1 && (y == y1 || errX <= errY))
                    x = stepX;
                else
                    y = stepY;

                source.SetPixel(x, y, 1);
            }
        }

        Staircase(4, 6, 40, 30);
        Staircase(4, 54, 40, 30);
        Staircase(40, 30, 86, 30);

        var target =
            Scale(
                source,
                144,
                96);

        bool Cord(int x, int y) =>
            x >= 0 && y >= 0 && x < target.Width && y < target.Height &&
            target.GetPixel(x, y) == 1;

        var blocks = 0;
        var ends = 0;

        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                if (!Cord(x, y))
                    continue;

                if (Cord(x + 1, y) && Cord(x, y + 1) && Cord(x + 1, y + 1))
                    blocks++;

                var degree =
                    (Cord(x - 1, y) ? 1 : 0) + (Cord(x + 1, y) ? 1 : 0) +
                    (Cord(x, y - 1) ? 1 : 0) + (Cord(x, y + 1) ? 1 : 0);

                if (degree <= 1)
                    ends++;
            }
        }

        // One cell wide at the junction, and exactly the three source ends: no spikes.
        Assert.Equal(0, blocks);
        Assert.Equal(3, ends);
    }

    [Fact]
    public void StraightDiagonalSides_KeepAPerfectlyRegularCadence()
    {
        // A navy diamond outlined by a 4-connected cord on a 34x41 quality grid: its sides are
        // physical 45-degree lines, i.e. pixel slope 41/34, drawn as uneven 1/2 staircases.
        const int warp = 34;
        const int weft = 41;
        var source =
            new DesignDocument(
                200,
                240,
                Palette());
        var inside =
            new bool[200, 240];

        for (var y = 0; y < 240; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var d =
                    Math.Abs((x + 0.5) / warp - 100.0 / warp) +
                    Math.Abs((y + 0.5) / weft - 120.0 / weft);
                inside[x, y] = d <= 2.6;
                source.SetPixel(x, y, inside[x, y] ? (byte)3 : (byte)0);
            }
        }

        for (var y = 1; y < 239; y++)
        {
            for (var x = 1; x < 199; x++)
            {
                if (!inside[x, y])
                    continue;

                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (!inside[x + dx, y + dy])
                            source.SetPixel(x, y, 1);
            }
        }

        var target =
            DesignResizer.Scale(
                source,
                320,
                384,
                ScaleMode.CurveNeutral,
                warp,
                weft,
                warp,
                weft);

        // Cord cells per row on the upper-right side, away from the corners.
        int minY = int.MaxValue, maxY = 0;

        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                if (target.GetPixel(x, y) == 1)
                {
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        var centreY =
            (minY + maxY) / 2;
        var runs =
            new List<int>();

        for (var y = minY + 6; y < centreY - 6; y++)
        {
            var count = 0;

            for (var x = target.Width / 2 + 2; x < target.Width; x++)
            {
                if (target.GetPixel(x, y) == 1)
                    count++;
            }

            runs.Add(count);
        }

        // A digital straight line is balanced: any k consecutive runs sum to within one cell.
        for (var k = 1; k <= 6; k++)
        {
            var sums =
                Enumerable
                    .Range(0, runs.Count - k + 1)
                    .Select(i => runs.Skip(i).Take(k).Sum())
                    .ToList();

            Assert.True(
                sums.Max() - sums.Min() <= 1,
                $"side is not straight (window {k}): {string.Join("", runs)}");
        }
    }

    [Fact]
    public void ThickStraightBand_EdgesStayDigitallyStraight()
    {
        // A six-cell tan band (a thick ruled line, not a Pixel-Cord) between the field and navy,
        // running at slope 25/26 like a hand-placed diagonal: a 45-degree staircase with one jog
        // every 25 rows. The distance field alone renders its 1.6x edges with phase wobble.
        var source =
            new DesignDocument(
                160,
                160,
                Palette());

        for (var y = 0; y < 160; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var u =
                    x - (int)Math.Floor(y * 26 / 25.0);
                source.SetPixel(
                    x,
                    y,
                    u < -30 ? (byte)0 : u < -24 ? (byte)2 : (byte)3);
            }
        }

        var target =
            Scale(
                source,
                256,
                256);

        // Edge position per row: first navy pixel. A digital straight line keeps all positions
        // within one pixel of a straight line.
        var rows =
            new List<(double Y, double X)>();

        for (var y = 50; y < 236; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                if (target.GetPixel(x, y) == 3)
                {
                    rows.Add((y, x));
                    break;
                }
            }
        }

        var n = rows.Count;
        var my = rows.Average(r => r.Y);
        var mx = rows.Average(r => r.X);
        var slope =
            rows.Sum(r => (r.Y - my) * (r.X - mx)) /
            rows.Sum(r => (r.Y - my) * (r.Y - my));
        var residuals =
            rows
                .Select(r => r.X - (mx + slope * (r.Y - my)))
                .ToList();

        Assert.True(
            n > 150 &&
            residuals.Max() - residuals.Min() <= 1.05,
            $"edge wobbles by {residuals.Max() - residuals.Min():F2} px");
    }

    [Fact]
    public void ClassicOutlines_InAFillColour_StayOnePixelAndConnected()
    {
        // Classic designs outline motifs one cell wide in a colour that is also a fill: a navy
        // outline around a tan disc, plus a solid navy square, so navy is not a cord colour.
        var source =
            new DesignDocument(
                100,
                80,
                Palette());

        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var d =
                    Math.Sqrt((x - 30) * (x - 30) + (y - 40) * (y - 40));
                source.SetPixel(x, y, d <= 20 ? (byte)2 : (byte)0);

                if (x >= 64 && x < 94 && y >= 25 && y < 55)
                    source.SetPixel(x, y, 3);
            }
        }

        for (var y = 1; y < 79; y++)
        {
            for (var x = 1; x < 60; x++)
            {
                if (source.GetPixel(x, y) != 2)
                    continue;

                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (source.GetPixel(x + dx, y + dy) == 0)
                            source.SetPixel(x, y, 3);
            }
        }

        var target =
            Scale(
                source,
                160,
                128);

        // Outline area: left of the square. No navy 2x2 block there (a one-cell outline that
        // became two pixels wide), and the outline stays one closed piece.
        var blocks = 0;
        var navy =
            new HashSet<(int X, int Y)>();

        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < 96; x++)
            {
                if (target.GetPixel(x, y) != 3)
                    continue;

                navy.Add((x, y));

                if (target.GetPixel(x + 1, y) == 3 &&
                    target.GetPixel(x, y + 1) == 3 &&
                    target.GetPixel(x + 1, y + 1) == 3)
                {
                    blocks++;
                }
            }
        }

        var seen =
            new HashSet<(int X, int Y)>();
        var parts = 0;

        foreach (var start in navy)
        {
            if (!seen.Add(start))
                continue;

            parts++;
            var stack =
                new Stack<(int X, int Y)>();
            stack.Push(start);

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();

                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (navy.Contains((cx + dx, cy + dy)) && seen.Add((cx + dx, cy + dy)))
                            stack.Push((cx + dx, cy + dy));
            }
        }

        Assert.True(
            navy.Count > 100 &&
            blocks == 0 &&
            parts == 1,
            $"{navy.Count} navy outline px, {blocks} 2x2 blocks, {parts} parts");
    }
}
