using RugScale.Core.Drawing;
using RugScale.Core.Drawing.WallToWall;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class WallToWallRepeatTests
{
    private static Palette Palette() =>
        new(
            new[]
            {
                new RugColor(214, 208, 203),
                new RugColor(255, 255, 255),
                new RugColor(84, 64, 53),
                new RugColor(157, 148, 137),
                new RugColor(0, 0, 255),
            });

    /// <summary>A random 40x30 rapport repeated as a half-drop, plus 2+2 blue marker columns.</summary>
    private static DesignDocument Periodic(
        int drop = 15,
        bool markers = true)
    {
        var random =
            new Random(3);
        var unit =
            new byte[40, 30];

        for (var y = 0; y < 30; y++)
            for (var x = 0; x < 40; x++)
                unit[x, y] = (byte)random.Next(0, 4);

        var design =
            new DesignDocument(
                164,
                100,
                Palette());

        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 164; x++)
            {
                var cx =
                    x - 2;
                var column =
                    (int)Math.Floor(cx / 40.0);
                var uy =
                    ((y - column * drop) % 30 + 30) % 30;
                design.SetPixel(x, y, unit[((cx % 40) + 40) % 40, uy]);
            }

            if (markers)
            {
                design.SetPixel(0, y, 4);
                design.SetPixel(1, y, 4);
                design.SetPixel(162, y, 4);
                design.SetPixel(163, y, 4);
            }
        }

        return design;
    }

    [Fact]
    public void Detect_FindsPeriodsDropAndMarkers()
    {
        var detection =
            RapportDetector.Detect(
                Periodic());

        Assert.True(detection.HorizontalFound);
        Assert.True(detection.VerticalFound);
        Assert.Equal(40, detection.Tile.Width);
        Assert.Equal(30, detection.Tile.Height);
        Assert.Equal(15, detection.Tile.Drop);
        Assert.Equal(2, detection.Tile.X);
        Assert.NotNull(detection.Markers);
        Assert.Equal(2, detection.Markers!.Left);
        Assert.Equal(2, detection.Markers.Right);
    }

    [Fact]
    public void RepeatBoth_ContinuesThePatternExactlyAndKeepsMarkersOnTheEdges()
    {
        var source =
            Periodic();
        var detection =
            RapportDetector.Detect(source);
        var result =
            WallToWallRepeat.Render(
                source,
                detection.Tile,
                300,
                170,
                new RapportOptions(RapportDirection.Both, Seamless: true));

        // Inside the source extent the repeat reproduces the source exactly (it is periodic).
        for (var y = 0; y < 100; y++)
            for (var x = 4; x < 160; x++)
                Assert.Equal(source.GetPixel(x, y), result.Design.GetPixel(x - 2, y));

        // Beyond it the pattern keeps its period and drop: one rapport across is 15 rows lower.
        for (var y = 15; y < 170; y++)
            for (var x = 2; x + 40 < 298; x++)
                Assert.Equal(result.Design.GetPixel(x, y - 15), result.Design.GetPixel(x + 40, y));

        for (var y = 0; y < 170; y++)
        {
            Assert.Equal(4, result.Design.GetPixel(0, y));
            Assert.Equal(4, result.Design.GetPixel(1, y));
            Assert.Equal(4, result.Design.GetPixel(298, y));
            Assert.Equal(4, result.Design.GetPixel(299, y));
        }

        Assert.InRange(result.SeamAcross, 0, 1.5);
        Assert.InRange(result.SeamAlong, 0, 1.5);
    }

    [Fact]
    public void RepeatWidth_RepeatsAcrossOnlyAndReadsRowsFromTheSource()
    {
        var source =
            Periodic(drop: 0, markers: false);
        var tile =
            new RapportTile(10, 5, 40, 20);
        var result =
            WallToWallRepeat.Repeat(
                source,
                tile,
                130,
                20,
                new RapportOptions(RapportDirection.Width, KeepEdgeMarkers: false));

        for (var y = 0; y < 20; y++)
            for (var x = 0; x < 130; x++)
                Assert.Equal(source.GetPixel(10 + x % 40, 5 + y), result.GetPixel(x, y));
    }

    [Fact]
    public void RepeatLength_RepeatsAlongOnly()
    {
        var source =
            Periodic(drop: 0, markers: false);
        var tile =
            new RapportTile(0, 10, 60, 25);
        var result =
            WallToWallRepeat.Repeat(
                source,
                tile,
                60,
                90,
                new RapportOptions(RapportDirection.Length, KeepEdgeMarkers: false));

        for (var y = 0; y < 90; y++)
            for (var x = 0; x < 60; x++)
                Assert.Equal(source.GetPixel(x, 10 + y % 25), result.GetPixel(x, y));
    }

    [Fact]
    public void Seamless_JoinsAWholeDesignRapportWhoseEdgesDoNotMeet()
    {
        // Vertical stripes whose phase does not close over the width: a plain repeat leaves a
        // visible join, the seamless join overlaps the ends and the seam disappears.
        var source =
            new DesignDocument(
                97,
                60,
                Palette());

        // 37-column stripes over a 97-column design: the last column is dark, the first light.
        for (var y = 0; y < 60; y++)
            for (var x = 0; x < 97; x++)
                source.SetPixel(x, y, x % 37 < 18 ? (byte)0 : (byte)2);

        var tile =
            new RapportTile(0, 0, 97, 60);
        var plain =
            WallToWallRepeat.Render(
                source,
                tile,
                300,
                60,
                new RapportOptions(RapportDirection.Width, KeepEdgeMarkers: false));
        var seamless =
            WallToWallRepeat.Render(
                source,
                tile,
                300,
                60,
                new RapportOptions(RapportDirection.Width, Seamless: true, KeepEdgeMarkers: false));

        Assert.True(
            seamless.SeamAcross < plain.SeamAcross,
            $"seam plain {plain.SeamAcross:F2}, seamless {seamless.SeamAcross:F2}");
        Assert.True(seamless.PeriodWidth < 97);
    }

    [Fact]
    public void WallToWallMode_RepeatsInsteadOfScaling()
    {
        var source =
            Periodic();
        var result =
            DesignResizer.Scale(
                source,
                250,
                160,
                ScaleMode.WallToWall);

        // Pattern size is unchanged: one rapport further along is the same pixel.
        for (var y = 0; y + 30 < 160; y++)
            for (var x = 2; x < 240; x++)
                Assert.Equal(result.GetPixel(x, y), result.GetPixel(x, y + 30));
    }

    /// <summary>Blobby two-colour texture: a few large discs on a speckled ground.</summary>
    private static DesignDocument Blobs()
    {
        var random =
            new Random(5);
        var design =
            new DesignDocument(
                120,
                110,
                Palette());

        for (var y = 0; y < 110; y++)
            for (var x = 0; x < 120; x++)
                design.SetPixel(x, y, random.NextDouble() < 0.15 ? (byte)3 : (byte)0);

        for (var k = 0; k < 9; k++)
        {
            var cx = random.Next(0, 120);
            var cy = random.Next(0, 110);
            var r = random.Next(6, 14);

            for (var y = Math.Max(0, cy - r); y < Math.Min(110, cy + r); y++)
                for (var x = Math.Max(0, cx - r); x < Math.Min(120, cx + r); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                        design.SetPixel(x, y, 2);
        }

        return design;
    }

    [Fact]
    public void OpenRapport_KeepsTheOriginalAndGrowsWithItsOwnTexture()
    {
        var source =
            Blobs();
        var tile =
            new RapportTile(0, 0, 120, 110);
        var opened =
            RapportExpander.Expand(
                source,
                tile,
                190,
                170);

        Assert.Equal(190, opened.Width);
        Assert.Equal(170, opened.Height);

        // The original rapport is untouched away from the seam bands along its edges (a sixth of
        // its width / height).
        for (var y = 110 / 6; y < 110 - 110 / 6; y++)
            for (var x = 120 / 6; x < 120 - 120 / 6; x++)
                Assert.Equal(source.GetPixel(x, y), opened.GetPixel(x, y));

        // Only the design's own colours, in similar proportions.
        double Share(DesignDocument d, byte c)
        {
            var n = 0;

            for (var y = 0; y < d.Height; y++)
                for (var x = 0; x < d.Width; x++)
                    if (d.GetPixel(x, y) == c)
                        n++;

            return n / (double)(d.Width * d.Height);
        }

        for (var y = 0; y < 170; y++)
            for (var x = 0; x < 190; x++)
                Assert.Contains(opened.GetPixel(x, y), new byte[] { 0, 2, 3 });

        Assert.InRange(Share(opened, 2), Share(source, 2) - 0.12, Share(source, 2) + 0.12);
    }

    [Fact]
    public void OpenRapport_RepeatsWithoutVisibleJoins()
    {
        var source =
            Blobs();
        var opened =
            RapportExpander.Expand(
                source,
                new RapportTile(0, 0, 120, 110),
                190,
                170);
        var tiled =
            WallToWallRepeat.Render(
                opened,
                new RapportTile(0, 0, 190, 170),
                400,
                360,
                new RapportOptions(RapportDirection.Both, KeepEdgeMarkers: false));

        var plain =
            WallToWallRepeat.Render(
                source,
                new RapportTile(0, 0, 120, 110),
                400,
                360,
                new RapportOptions(RapportDirection.Both, KeepEdgeMarkers: false));

        // The opened rapport wraps around: its joins read far less than repeating the design
        // as it is, and stay near the texture itself.
        Assert.True(
            tiled.SeamAcross < plain.SeamAcross &&
            tiled.SeamAlong < plain.SeamAlong &&
            tiled.SeamAcross < 2 &&
            tiled.SeamAlong < 2,
            $"opened {tiled.SeamAcross:F2} / {tiled.SeamAlong:F2}, plain {plain.SeamAcross:F2} / {plain.SeamAlong:F2}");
    }

    /// <summary>
    /// Dithered horizontal brush strokes (B390A-like): bands of rows whose darkness drifts slowly
    /// across, seamless across.
    /// </summary>
    private static DesignDocument Strokes(bool seamless = true)
    {
        var design =
            new DesignDocument(160, 120, Palette());
        var random =
            new Random(5);
        var y0 = 0;

        while (y0 < 120)
        {
            var height =
                random.Next(5, 15);
            var level =
                random.NextDouble();
            var swing =
                random.NextDouble() * 0.4;
            var phase =
                random.NextDouble() * 2 * Math.PI;

            for (var y = y0; y < Math.Min(120, y0 + height); y++)
            {
                for (var x = 0; x < 160; x++)
                {
                    var tone =
                        Math.Clamp(
                            seamless
                                ? level + swing * Math.Sin(2 * Math.PI * x / 160 + phase)
                                : level + swing * (x / 80.0 - 1) * (phase < Math.PI ? 1 : -1),
                            0,
                            1);
                    design.SetPixel(x, y, random.NextDouble() < tone ? (byte)2 : (byte)0);
                }
            }

            y0 += height;
        }

        return design;
    }

    private static byte[] Pixels(DesignDocument design)
    {
        var grid =
            new byte[design.Width * design.Height];

        for (var y = 0; y < design.Height; y++)
            for (var x = 0; x < design.Width; x++)
                grid[y * design.Width + x] = design.GetPixel(x, y);

        return grid;
    }

    [Fact]
    public void OpenRapport_LengthensHorizontalStrokesInsteadOfCuttingThem()
    {
        var source =
            Strokes();
        var grid =
            Pixels(source);
        var rgb =
            new int[256 * 3];

        for (var c = 0; c < source.Palette.Count; c++)
        {
            rgb[c * 3] = source.Palette[c].R;
            rgb[c * 3 + 1] = source.Palette[c].G;
            rgb[c * 3 + 2] = source.Palette[c].B;
        }

        Assert.Equal(RapportExpander.Grain.Across, RapportExpander.GrainOf(grid, 160, 120, rgb));
        Assert.Equal(RapportExpander.Grain.None, RapportExpander.GrainOf(Pixels(Blobs()), 120, 110, rgb));

        var opened =
            RapportExpander.Expand(
                source,
                new RapportTile(0, 0, 160, 120),
                240,
                120);

        Assert.Equal(240, opened.Width);
        Assert.Equal(120, opened.Height);

        double Dark(DesignDocument d, int y)
        {
            var n = 0;

            for (var x = 0; x < d.Width; x++)
                if (d.GetPixel(x, y) == 2)
                    n++;

            return n / (double)d.Width;
        }

        // Every row stays a continuation of its own stroke: same darkness, no other row's tone
        // spliced in.
        for (var y = 0; y < 120; y++)
            Assert.InRange(Dark(opened, y), Dark(source, y) - 0.08, Dark(source, y) + 0.08);

        var tiled =
            WallToWallRepeat.Render(
                opened,
                new RapportTile(0, 0, 240, 120),
                720,
                240,
                new RapportOptions(RapportDirection.Both, KeepEdgeMarkers: false));

        Assert.True(
            tiled.SeamAcross < WallToWallRepeat.VisibleSeam,
            $"seam across {tiled.SeamAcross:F2}");
    }

    [Fact]
    public void OpenRapport_ClosesAVisibleJoinWhenLengtheningStrokes()
    {
        // Strokes whose darkness ramps across: the rapport's own join across is a straight line.
        var source =
            Strokes(seamless: false);
        var plain =
            WallToWallRepeat.Render(
                source,
                new RapportTile(0, 0, 160, 120),
                640,
                240,
                new RapportOptions(RapportDirection.Both, KeepEdgeMarkers: false));
        var opened =
            RapportExpander.Expand(
                source,
                new RapportTile(0, 0, 160, 120),
                240,
                120);
        var tiled =
            WallToWallRepeat.Render(
                opened,
                new RapportTile(0, 0, 240, 120),
                720,
                240,
                new RapportOptions(RapportDirection.Both, KeepEdgeMarkers: false));

        Assert.Equal(240, opened.Width);
        Assert.True(plain.SeamAcross > WallToWallRepeat.VisibleSeam, $"plain {plain.SeamAcross:F2}");
        Assert.True(
            tiled.SeamAcross < plain.SeamAcross &&
            tiled.SeamAcross < WallToWallRepeat.VisibleSeam,
            $"opened {tiled.SeamAcross:F2}, plain {plain.SeamAcross:F2}");
    }

    [Fact]
    public void OpenRapport_RejectsASmallerSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RapportExpander.Expand(
                Blobs(),
                new RapportTile(0, 0, 120, 110),
                100,
                170));
    }
}
