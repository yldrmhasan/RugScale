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
}
