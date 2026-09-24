using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public class DesignResizerTests
{
    private static DesignDocument MakeSource()
    {
        // 2x2, four distinct colors, one per corner.
        var palette = new Palette(new[]
        {
            new RugColor(255, 0, 0),
            new RugColor(0, 255, 0),
            new RugColor(0, 0, 255),
            new RugColor(255, 255, 0),
        });
        var doc = new DesignDocument(2, 2, palette);
        doc.SetPixel(0, 0, 0);
        doc.SetPixel(1, 0, 1);
        doc.SetPixel(0, 1, 2);
        doc.SetPixel(1, 1, 3);
        return doc;
    }

    [Fact]
    public void CropOrPad_TopLeftAnchor_KeepsOriginalAtOrigin()
    {
        var source = MakeSource();
        var result = DesignResizer.CropOrPad(source, 4, 4, ResizeAnchor.TopLeft, fillIndex: 0);

        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        Assert.Equal(0, result.GetPixel(0, 0));
        Assert.Equal(1, result.GetPixel(1, 0));
        Assert.Equal(2, result.GetPixel(0, 1));
        Assert.Equal(3, result.GetPixel(1, 1));
        // Padded area uses the fill index.
        Assert.Equal(0, result.GetPixel(3, 3));
    }

    [Fact]
    public void CropOrPad_CenterAnchor_CentersOriginal()
    {
        var source = MakeSource();
        var result = DesignResizer.CropOrPad(source, 4, 4, ResizeAnchor.Center, fillIndex: 0);

        // (4-2)/2 = 1, so the original 2x2 now sits at (1,1)-(2,2).
        Assert.Equal(0, result.GetPixel(1, 1));
        Assert.Equal(1, result.GetPixel(2, 1));
        Assert.Equal(2, result.GetPixel(1, 2));
        Assert.Equal(3, result.GetPixel(2, 2));
    }

    [Fact]
    public void CropOrPad_Shrinking_CropsToFit()
    {
        var source = MakeSource();
        var result = DesignResizer.CropOrPad(source, 1, 1, ResizeAnchor.TopLeft, fillIndex: 0);

        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(0, result.GetPixel(0, 0)); // only the top-left source pixel survives
    }

    [Fact]
    public void Scale_NearestNeighbor_EnlargingDuplicatesPixelsBlockwise()
    {
        var source = MakeSource();
        var result = DesignResizer.Scale(source, 4, 4, ScaleMode.NearestNeighbor);

        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        // Each 2x2 source pixel becomes a 2x2 block.
        Assert.Equal(0, result.GetPixel(0, 0));
        Assert.Equal(0, result.GetPixel(1, 0));
        Assert.Equal(0, result.GetPixel(0, 1));
        Assert.Equal(0, result.GetPixel(1, 1));
        Assert.Equal(3, result.GetPixel(3, 3));
    }

    [Fact]
    public void Scale_NearestNeighbor_ShrinkingProducesRequestedDimensions()
    {
        var source = MakeSource();
        var result = DesignResizer.Scale(source, 1, 1, ScaleMode.NearestNeighbor);

        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    /// <summary>
    /// The design's own colors must survive a resize untouched: a palette can define far more
    /// entries than the design paints with (a new design ships with 256, an imported indexed BMP
    /// brings its whole table), and blending modes used to snap to the nearest entry across the
    /// WHOLE palette, which pulled in colors the design never contained.
    /// </summary>
    [Theory]
    [InlineData(ScaleMode.NearestNeighbor)]
    [InlineData(ScaleMode.EdgeSmooth)]
    [InlineData(ScaleMode.Dominant)]
    [InlineData(ScaleMode.PreserveDetail)]
    [InlineData(ScaleMode.RugScale)]
    [InlineData(ScaleMode.CurveFill)]
    [InlineData(ScaleMode.LeafPetalArcs)]
    [InlineData(ScaleMode.Smooth)]
    [InlineData(ScaleMode.AreaAverage)]
    public void Scale_NeverIntroducesAColorTheDesignDidNotUse(ScaleMode mode)
    {
        // Two-color design living inside a big palette full of other colors.
        var palette = Palette.CreateDefault256();
        var source = new DesignDocument(9, 9, palette);
        for (var y = 0; y < 9; y++)
            for (var x = 0; x < 9; x++)
                source.SetPixel(x, y, (byte)((x + y) % 2 == 0 ? 0 : 1));

        var result = DesignResizer.Scale(source, 4, 4, mode);

        for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++)
                Assert.True(result.GetPixel(x, y) is 0 or 1,
                    $"{mode} produced index {result.GetPixel(x, y)}, which the source never used.");
    }

    /// <summary>A diagonal edge is exactly what EPX is for — enlarging it must NOT come out as the plain nearest-neighbor staircase.</summary>
    [Fact]
    public void Scale_EdgeSmooth_DiffersFromNearestNeighborWhenEnlargingADiagonal()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(8, 8, palette);
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                source.SetPixel(x, y, (byte)(x <= y ? 1 : 0)); // hard diagonal edge

        var nearest = DesignResizer.Scale(source, 16, 16, ScaleMode.NearestNeighbor);
        var smoothed = DesignResizer.Scale(source, 16, 16, ScaleMode.EdgeSmooth);

        var differences = 0;
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                if (nearest.GetPixel(x, y) != smoothed.GetPixel(x, y))
                    differences++;

        Assert.True(differences > 0, "EdgeSmooth produced the same staircase as NearestNeighbor.");
    }

    /// <summary>
    /// Documents the behavior the user ran into: on a pure enlargement every footprint is one
    /// source pixel, so the downscaling strategies genuinely have nothing to decide and all agree
    /// with NearestNeighbor. EdgeSmooth is the mode that exists to be different here.
    /// </summary>
    [Theory]
    [InlineData(ScaleMode.Dominant)]
    [InlineData(ScaleMode.PreserveDetail)]
    [InlineData(ScaleMode.AreaAverage)]
    public void Scale_DownscalingModes_MatchNearestNeighborWhenEnlarging(ScaleMode mode)
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(5, 5, palette);
        for (var y = 0; y < 5; y++)
            for (var x = 0; x < 5; x++)
                source.SetPixel(x, y, (byte)(x <= y ? 1 : 0));

        var nearest = DesignResizer.Scale(source, 13, 13, ScaleMode.NearestNeighbor);
        var other = DesignResizer.Scale(source, 13, 13, mode);

        for (var y = 0; y < 13; y++)
            for (var x = 0; x < 13; x++)
                Assert.Equal(nearest.GetPixel(x, y), other.GetPixel(x, y));
    }

    [Fact]
    public void Scale_Dominant_PicksTheMajorityColorOfEachFootprint()
    {
        // 2x2 -> 1x1 where three of the four source pixels share a color.
        var palette = new Palette(new[] { new RugColor(0, 0, 0), new RugColor(255, 255, 255) });
        var source = new DesignDocument(2, 2, palette);
        source.SetPixel(0, 0, 1);
        source.SetPixel(1, 0, 1);
        source.SetPixel(0, 1, 1);
        source.SetPixel(1, 1, 0);

        var result = DesignResizer.Scale(source, 1, 1, ScaleMode.Dominant);

        Assert.Equal(1, result.GetPixel(0, 0));
    }

    [Fact]
    public void Scale_PreserveDetail_KeepsTheRareColorTheMajorityWouldDiscard()
    {
        // A single detail pixel in an otherwise uniform 2x2 block: Dominant votes it away,
        // PreserveDetail keeps it because it's the rarest color in the design.
        var palette = new Palette(new[] { new RugColor(0, 0, 0), new RugColor(255, 255, 255) });
        var source = new DesignDocument(2, 2, palette);
        source.SetPixel(0, 0, 0);
        source.SetPixel(1, 0, 0);
        source.SetPixel(0, 1, 0);
        source.SetPixel(1, 1, 1); // the lone detail

        Assert.Equal(0, DesignResizer.Scale(source, 1, 1, ScaleMode.Dominant).GetPixel(0, 0));
        Assert.Equal(1, DesignResizer.Scale(source, 1, 1, ScaleMode.PreserveDetail).GetPixel(0, 0));
    }

    [Fact]
    public void Scale_LeafPetalArcs_RefinesCurvedTaperedFilledRegion()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(255, 255, 255),
            new RugColor(82, 132, 86),
        });
        var source = new DesignDocument(84, 64, palette);

        // Build one curved, filled, tapered leaf body.
        for (var step = 0; step <= 56; step++)
        {
            var t = step / 56d;
            var centerX = 13d + 56d * t;
            var centerY =
                45d -
                23d * t +
                7d * Math.Sin(t * Math.PI);
            var radius =
                7.0 * Math.Pow(
                    1d - t,
                    0.65) +
                1.0;

            var minX = Math.Max(0, (int)Math.Floor(centerX - radius - 1));
            var maxX = Math.Min(source.Width - 1, (int)Math.Ceiling(centerX + radius + 1));
            var minY = Math.Max(0, (int)Math.Floor(centerY - radius - 1));
            var maxY = Math.Min(source.Height - 1, (int)Math.Ceiling(centerY + radius + 1));

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;

                    if (dx * dx + dy * dy <= radius * radius)
                        source.SetPixel(x, y, 2);
                }
            }
        }

        var result = new DesignDocument(126, 96, palette);
        var diagnostics = LeafPetalArcScaleEngine.ResizeWithDiagnostics(
            source,
            result,
            40,
            50,
            40,
            50);

        Assert.True(
            diagnostics.Candidates >= 1,
            "The curved tapered fill should be classified as a Leaf / Petal Arc candidate.");
        Assert.True(
            diagnostics.Refined >= 1,
            "At least one eligible leaf region should receive elegant-arc refinement.");
        Assert.True(
            diagnostics.BoundaryPixelsChanged > 0,
            "The specialist mode should actually refine the target boundary, not collapse to Curve & Fill unchanged.");
        Assert.True(
            CountColor(result, 2) > 0,
            "The original indexed leaf fill must survive refinement.");
    }

    [Fact]
    public void Scale_LeafPetalArcs_PreservesOnePixelIndexedSeparator()
    {
        var palette = new Palette(new[]
        {
            new RugColor(45, 125, 170),
            new RugColor(255, 255, 255),
            new RugColor(196, 145, 112),
        });
        var source = new DesignDocument(36, 24, palette);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                source.SetPixel(
                    x,
                    y,
                    x < 17
                        ? (byte)0
                        : x == 17
                            ? (byte)1
                            : (byte)2);
            }
        }

        var result = DesignResizer.Scale(
            source,
            59,
            39,
            ScaleMode.LeafPetalArcs,
            40,
            50,
            40,
            50);

        for (var y = 0; y < result.Height; y++)
        {
            var lastLeft = -1;
            var firstRight = int.MaxValue;

            for (var x = 0; x < result.Width; x++)
            {
                var color = result.GetPixel(x, y);

                if (color == 0)
                    lastLeft = x;
                else if (color == 2)
                    firstRight = Math.Min(firstRight, x);
            }

            Assert.True(lastLeft >= 0 && firstRight < int.MaxValue);
            Assert.Contains(
                Enumerable.Range(
                        lastLeft + 1,
                        Math.Max(0, firstRight - lastLeft - 1))
                    .Select(x => result.GetPixel(x, y)),
                color => color == 1);
        }
    }

    [Fact]
    public void Scale_CurveFill_EnlargeCurve_RemainsOneContinuousIndexedRegion()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 225),
            new RugColor(40, 40, 40),
        });
        var source = new DesignDocument(32, 32, palette);

        var points = new (int X, int Y)[]
        {
            (5, 22), (5, 21), (5, 20), (6, 19), (6, 18), (7, 17),
            (8, 16), (9, 15), (10, 14), (11, 13), (12, 12), (13, 11),
            (14, 10), (15, 9), (16, 8), (17, 8), (18, 7), (19, 7),
            (20, 7), (21, 8), (22, 9), (22, 10), (22, 11),
        };
        foreach (var point in points)
            source.SetPixel(point.X, point.Y, 1);

        var result = DesignResizer.Scale(
            source,
            64,
            64,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        Assert.Equal(1, CountComponents(result, 1));
        Assert.True(
            CountColor(result, 1) > points.Length,
            "The enlarged curve collapsed instead of following the target geometry.");
    }

    [Fact]
    public void Scale_CurveFill_OneByOnePixelCord_ReplaysWithRugCADCurveToolRules()
    {
        var palette = new Palette(new[]
        {
            new RugColor(215, 208, 186),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(42, 42, palette);

        var sourceControls = new (int X, int Y)[]
        {
            (5, 31),
            (10, 12),
            (22, 7),
            (33, 14),
            (36, 31),
        };

        var sourceCord = Rasterizer.ConnectDiagonalSteps(
            CurveRasterizer.Draw(
                sourceControls,
                CurveType.SplineThroughPoints,
                0.25));

        foreach (var point in sourceCord)
            source.SetPixel(point.X, point.Y, 1);

        var result = DesignResizer.Scale(
            source,
            84,
            84,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        var mappedControls = sourceControls
            .Select(point =>
                (
                    X: (int)Math.Round((point.X + 0.5) * 2d - 0.5),
                    Y: (int)Math.Round((point.Y + 0.5) * 2d - 0.5)))
            .ToArray();

        var expected = Rasterizer.ConnectDiagonalSteps(
                CurveRasterizer.Draw(
                    mappedControls,
                    CurveType.SplineThroughPoints,
                    0.25))
            .Where(point =>
                point.X >= 0 &&
                point.X < result.Width &&
                point.Y >= 0 &&
                point.Y < result.Height)
            .Distinct()
            .ToArray();

        var matched = expected.Count(point =>
            HasColorNear(
                result,
                point.X,
                point.Y,
                1,
                radius: 1));

        Assert.True(
            matched >= expected.Length * 0.92,
            $"Tool-faithful curve recall is too low: {matched}/{expected.Length}.");

        Assert.Equal(
            1,
            CountFourConnectedComponents(
                result,
                1));

        // Same quality must not turn a 1x1 Pixel-Cord pen into a 2x2 bitmap block.
        var actualPixels =
            CountColor(
                result,
                1);

        Assert.True(
            actualPixels <= expected.Length * 1.35,
            $"Same-quality 1x1 Pixel-Cord redraw became too thick: actual={actualPixels}, tool-path={expected.Length}.");
    }

    [Fact]
    public void Scale_CurveFill_LearnsHighRoundnessThroughPointsCharacter()
    {
        var palette = new Palette(new[]
        {
            new RugColor(214, 205, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(64, 56, palette);
        var controls = new (int X, int Y)[]
        {
            (5, 43),
            (12, 13),
            (30, 6),
            (50, 16),
            (57, 43),
        };

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.SplineThroughPoints,
                         0.85)))
        {
            source.SetPixel(point.X, point.Y, 1);
        }

        var result = DesignResizer.Scale(
            source,
            103,
            91,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        (int X, int Y)[] MapControls() =>
            controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            result.Width /
                            source.Width -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            result.Height /
                            source.Height -
                            0.5)))
                .ToArray();

        var mapped = MapControls();
        var expectedHigh =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mapped,
                        CurveType.SplineThroughPoints,
                        0.85))
                .ToHashSet();
        var wrongLow =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mapped,
                        CurveType.SplineThroughPoints,
                        0.10))
                .ToHashSet();
        var actual =
            GetColorPixels(
                result,
                1);

        var correctScore =
            PixelNearF1(
                actual,
                expectedHigh,
                radius: 1);
        var wrongScore =
            PixelNearF1(
                actual,
                wrongLow,
                radius: 1);

        Assert.True(
            correctScore >= 0.94,
            $"High-roundness tool replay lost the expected Curve geometry: {correctScore:P2}.");
        Assert.True(
            correctScore >= wrongScore + 0.08,
            $"Resize lost the source roundness character: expected={correctScore:P2}, low-roundness={wrongScore:P2}.");
    }

    [Fact]
    public void Scale_CurveFill_WideOvalArc_PreservesOvalGeometryAtTargetScale()
    {
        var palette = new Palette(new[]
        {
            new RugColor(214, 205, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(68, 48, palette);
        var controls = new (int X, int Y)[]
        {
            (4, 34),
            (10, 16),
            (29, 7),
            (50, 15),
            (60, 34),
        };

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.SplineThroughPoints,
                         0.85)))
        {
            source.SetPixel(
                point.X,
                point.Y,
                1);
        }

        var result =
            new DesignDocument(
                109,
                77,
                palette);
        var diagnostics =
            CurveFillScaleEngine.ResizeWithDiagnostics(
                source,
                result,
                40,
                50,
                40,
                50);

        var mappedControls =
            controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            result.Width /
                            source.Width -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            result.Height /
                            source.Height -
                            0.5)))
                .ToArray();

        var expected =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mappedControls,
                        CurveType.SplineThroughPoints,
                        0.85))
                .ToHashSet();
        var actual =
            GetColorPixels(
                result,
                1);

        Assert.True(
            PixelNearF1(
                actual,
                expected,
                radius: 1) >= 0.98,
            "Wide oval redraw lost the source Curve-tool shoulders.");
        var exactScore =
            PixelF1(
                actual,
                expected);

        Assert.True(
            exactScore >= 0.80,
            $"Wide oval redraw drifted too far from the target Curve-tool raster: exact={exactScore:P2}, " +
            $"learned={diagnostics.LearnedCurves}, fallback={diagnostics.GraphFallbacks}, " +
            $"safetyFallback={diagnostics.CurveSafetyFallbacks}, clipped={diagnostics.CorridorClippedPixels}, " +
            $"accepted={diagnostics.AcceptedComponents}, pixelCord={diagnostics.PixelCordComponents}, " +
            $"completePath={diagnostics.CompletePathRecoveries}.");
        Assert.True(
            diagnostics.CompletePathRecoveries >= 1,
            "The Pixel-Cord oval should be recovered as one complete source drawing path.");
        Assert.True(
            diagnostics.LearnedCurves >= 1,
            "The recovered oval path should reach the Curve inverse learner.");

        Assert.Equal(
            1,
            CountFourConnectedComponents(
                result,
                1));
    }

    [Fact]
    public void Scale_CurveFill_TallSideOval_PreservesOvalGeometryAtTargetScale()
    {
        var palette = new Palette(new[]
        {
            new RugColor(214, 205, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(52, 66, palette);
        var controls = new (int X, int Y)[]
        {
            (42, 5),
            (22, 8),
            (8, 24),
            (15, 46),
            (38, 59),
        };

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.SplineThroughPoints,
                         0.85)))
        {
            source.SetPixel(point.X, point.Y, 1);
        }

        var result =
            new DesignDocument(
                83,
                106,
                palette);
        var diagnostics =
            CurveFillScaleEngine.ResizeWithDiagnostics(
                source,
                result,
                40,
                50,
                40,
                50);

        var mappedControls =
            controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            result.Width /
                            source.Width -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            result.Height /
                            source.Height -
                            0.5)))
                .ToArray();

        var expected =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mappedControls,
                        CurveType.SplineThroughPoints,
                        0.85))
                .ToHashSet();
        var actual =
            GetColorPixels(
                result,
                1);

        Assert.True(
            PixelNearF1(
                actual,
                expected,
                radius: 1) >= 0.98,
            "Tall side oval lost its smooth target geometry.");
        Assert.True(
            PixelF1(
                actual,
                expected) >= 0.80,
            "Tall side oval drifted too far from the target Curve-tool raster.");
        Assert.True(
            diagnostics.CompletePathRecoveries >= 1,
            "Tall Pixel-Cord oval should be recovered as one complete source drawing path.");
        Assert.True(
            diagnostics.LearnedCurves >= 1,
            "Tall recovered oval should reach the Curve inverse learner.");
        Assert.Equal(
            1,
            CountFourConnectedComponents(
                result,
                1));
    }

    [Fact]
    public void Scale_CurveFill_SoftOval_PreservesOvalGeometryAtTargetScale()
    {
        var palette = new Palette(new[]
        {
            new RugColor(214, 205, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(68, 50, palette);
        var controls = new (int X, int Y)[]
        {
            (6, 39),
            (13, 17),
            (28, 8),
            (46, 12),
            (59, 32),
        };

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.SplineThroughPoints,
                         0.85)))
        {
            source.SetPixel(point.X, point.Y, 1);
        }

        var result =
            new DesignDocument(
                109,
                80,
                palette);
        var diagnostics =
            CurveFillScaleEngine.ResizeWithDiagnostics(
                source,
                result,
                40,
                50,
                40,
                50);

        var mappedControls =
            controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            result.Width /
                            source.Width -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            result.Height /
                            source.Height -
                            0.5)))
                .ToArray();

        var expected =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mappedControls,
                        CurveType.SplineThroughPoints,
                        0.85))
                .ToHashSet();
        var actual =
            GetColorPixels(
                result,
                1);

        Assert.True(
            PixelNearF1(
                actual,
                expected,
                radius: 1) >= 0.98,
            "Soft oval lost its smooth target geometry.");
        var exactScore =
            PixelF1(
                actual,
                expected);

        Assert.True(
            exactScore >= 0.80,
            $"Soft oval drifted too far from the target Curve-tool raster: exact={exactScore:P2}, " +
            $"learned={diagnostics.LearnedCurves}, through={diagnostics.LearnedThroughPoints}, " +
            $"fallback={diagnostics.GraphFallbacks}, safetyFallback={diagnostics.CurveSafetyFallbacks}, " +
            $"completePath={diagnostics.CompletePathRecoveries}, clipped={diagnostics.CorridorClippedPixels}, " +
            $"roundness={diagnostics.MeanLearnedRoundness:0.000}.");
        Assert.True(
            diagnostics.CompletePathRecoveries >= 1,
            "Soft Pixel-Cord oval should be recovered as one complete source drawing path.");
        Assert.True(
            diagnostics.LearnedCurves >= 1,
            "Soft recovered oval should reach the Curve inverse learner.");
        Assert.Equal(
            1,
            CountFourConnectedComponents(
                result,
                1));
    }

    [Fact]
    public void Scale_CurveFill_LearnsBezierCharacterInsteadOfForcingThroughPoints()
    {
        var palette = new Palette(new[]
        {
            new RugColor(214, 205, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(64, 56, palette);
        var controls = new (int X, int Y)[]
        {
            (6, 44),
            (8, 8),
            (49, 4),
            (57, 43),
        };

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.Bezier,
                         0.90)))
        {
            source.SetPixel(point.X, point.Y, 1);
        }

        var result = DesignResizer.Scale(
            source,
            101,
            89,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        var mapped =
            controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            result.Width /
                            source.Width -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            result.Height /
                            source.Height -
                            0.5)))
                .ToArray();

        var expectedBezier =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mapped,
                        CurveType.Bezier,
                        0.90))
                .ToHashSet();
        var forcedThroughPoints =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        mapped,
                        CurveType.SplineThroughPoints,
                        0.25))
                .ToHashSet();
        var actual =
            GetColorPixels(
                result,
                1);

        var bezierScore =
            PixelNearF1(
                actual,
                expectedBezier,
                radius: 1);
        var forcedScore =
            PixelNearF1(
                actual,
                forcedThroughPoints,
                radius: 1);

        Assert.True(
            bezierScore >= 0.96,
            $"Bezier-trained replay lost the expected tool geometry: {bezierScore:P2}.");
        Assert.True(
            bezierScore >= forcedScore + 0.08,
            $"Bezier source was flattened into generic Through Points: bezier={bezierScore:P2}, through={forcedScore:P2}.");
    }

    [Fact]
    public void Scale_CurveFill_NativePixelCordEllipse_IsRecoveredAsEllipseTool()
    {
        var palette = new Palette(new[]
        {
            new RugColor(214, 205, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(48, 40, palette);

        const int left = 7;
        const int top = 5;
        const int right = 38;
        const int bottom = 31;

        foreach (var point in Rasterizer.EllipseOutlineConnected(
                     left,
                     top,
                     right,
                     bottom))
        {
            source.SetPixel(
                point.X,
                point.Y,
                1);
        }

        var result =
            new DesignDocument(
                84,
                72,
                palette);

        var diagnostics =
            CurveFillScaleEngine.ResizeWithDiagnostics(
                source,
                result,
                40,
                50,
                40,
                50);

        Assert.True(
            diagnostics.LearnedEllipses >= 1,
            "A native Pixel-Cord ellipse should be recognized as the Ellipse tool family.");

        int MapX(int x) =>
            (int)Math.Round(
                (x + 0.5) *
                result.Width /
                source.Width -
                0.5);
        int MapY(int y) =>
            (int)Math.Round(
                (y + 0.5) *
                result.Height /
                source.Height -
                0.5);

        var expected =
            Rasterizer.EllipseOutlineConnected(
                    MapX(left),
                    MapY(top),
                    MapX(right),
                    MapY(bottom))
                .ToHashSet();
        var actual =
            GetColorPixels(
                result,
                1);

        Assert.True(
            PixelNearF1(
                actual,
                expected,
                radius: 1) >= 0.98,
            "Recovered native ellipse lost its target oval geometry.");
        Assert.True(
            PixelF1(
                actual,
                expected) >= 0.82,
            "Recovered native ellipse differs too much from RugCAD's own Ellipse rasterizer.");
    }

    [Fact]
    public void Scale_CurveFill_EnlargeFilledRibbon_PreservesFilledAreaInsteadOfSkeletonizingIt()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 225),
            new RugColor(35, 35, 35),
        });
        var source = new DesignDocument(48, 48, palette);

        for (var x = 6; x <= 40; x++)
        {
            var centerY =
                23 +
                (int)Math.Round(
                    9 *
                    Math.Sin(
                        (x - 6) /
                        34d *
                        Math.PI));

            for (var dy = -1; dy <= 1; dy++)
                source.SetPixel(x, centerY + dy, 1);
        }

        var result = DesignResizer.Scale(
            source,
            96,
            96,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        var sourceArea = CountColor(source, 1);
        var targetArea = CountColor(result, 1);

        Assert.Equal(1, CountComponents(result, 1));

        // A 2x-by-2x geometric enlargement of a FILLED ribbon should remain a filled ribbon.
        // The previous skeleton-based path hollowed/rewrote such regions; contour+fill should stay
        // close to the expected 4x area while still smoothing the categorical boundary.
        Assert.InRange(
            targetArea / (double)sourceArea,
            3.4,
            4.6);
    }

    [Fact]
    public void CurveFillRibbonArcRefiner_RebuildsStableWidthOvalRibbonFromGeometry()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(134, 162, 125),
        });
        // Match the real C069 top-centre green ornament: one long half-oval ribbon rather than a
        // complete U-shaped arch. This is the filled geometry that motivated the regression.
        var source = new DesignDocument(56, 92, palette);
        var controls = new (int X, int Y)[]
        {
            (45, 6),
            (33, 12),
            (19, 30),
            (12, 54),
            (11, 82),
        };

        var sourceCenterline =
            CurveRasterizer.Draw(
                    controls,
                    CurveType.SplineThroughPoints,
                    0.85)
                .ToArray();

        foreach (var point in Rasterizer.Dilate(
                     sourceCenterline,
                     5,
                     5))
        {
            if (point.X >= 0 &&
                point.X < source.Width &&
                point.Y >= 0 &&
                point.Y < source.Height)
            {
                source.SetPixel(
                    point.X,
                    point.Y,
                    1);
            }
        }

        var targetWidth = 90;
        var targetHeight = 147;
        var nearest =
            DesignResizer.Scale(
                source,
                targetWidth,
                targetHeight,
                ScaleMode.NearestNeighbor,
                40,
                60,
                40,
                60);

        // Judge the redraw against the CONTINUOUS source Curve geometry, not against a
        // block-expanded source raster. This is the visual target for carpet design: the source
        // staircase is evidence for an underlying curve, not geometry that should be magnified.
        var idealSourceArc =
            Enumerable.Range(
                    0,
                    161)
                .Select(index =>
                {
                    var t =
                        index /
                        160d;
                    var point =
                        CurveRasterizer.Evaluate(
                            controls,
                            CurveType.SplineThroughPoints,
                            0.85,
                            t);

                    return new ElegantArcPoint(
                        point.X,
                        point.Y,
                        2.5);
                })
                .ToArray();
        var idealPolygon =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                idealSourceArc,
                targetWidth /
                    (double)source.Width,
                targetHeight /
                    (double)source.Height);
        var expected =
            LeafPetalArcRasterizer.RasterizePolygon(
                    idealPolygon,
                    targetWidth,
                    targetHeight)
                .Select(key =>
                    (
                        X: key %
                           targetWidth,
                        Y: key /
                           targetWidth))
                .ToHashSet();

        var before =
            GetColorPixels(
                nearest,
                1);
        var beforeScore =
            PixelF1(
                before,
                expected);

        var ribbonDiagnostics =
            CurveFillRibbonArcRefiner.ApplyWithDiagnostics(
                source,
                nearest);
        var changed =
            ribbonDiagnostics.BoundaryPixelsChanged;
        var after =
            GetColorPixels(
                nearest,
                1);
        var afterScore =
            PixelF1(
                after,
                expected);

        Assert.True(
            changed > 0,
            $"A stable-width, strongly curved filled ribbon should be geometrically refined. " +
            $"regions={ribbonDiagnostics.Regions}, classified={ribbonDiagnostics.Classified}, " +
            $"axis={ribbonDiagnostics.AxisBuilt}, skeleton={ribbonDiagnostics.MaxSkeletonPixels}, " +
            $"endpoints={ribbonDiagnostics.MaxEndpoints}, path={ribbonDiagnostics.MaxPrincipalPathPixels}, " +
            $"coverage={ribbonDiagnostics.MaxPrincipalPathCoverage:0.000}, reason={ribbonDiagnostics.LastCenterlineReason}, " +
            $"ribbon={ribbonDiagnostics.RibbonGeometryAccepted}, fitSafe={ribbonDiagnostics.FitSafe}, " +
            $"toolFit={ribbonDiagnostics.CurveToolFits}, fitDev={ribbonDiagnostics.MaxFitDeviation:0.000}, " +
            $"flips={ribbonDiagnostics.MaxFitCurvatureFlips}, " +
            $"refined={ribbonDiagnostics.Refined}.");
        Assert.True(
            afterScore >= beforeScore + 0.005,
            $"Ribbon refiner did not improve exact target oval geometry: before={beforeScore:P2}, after={afterScore:P2}; " +
            $"toolFit={ribbonDiagnostics.CurveToolFits}, fitDev={ribbonDiagnostics.MaxFitDeviation:0.000}, " +
            $"flips={ribbonDiagnostics.MaxFitCurvatureFlips}, changed={ribbonDiagnostics.BoundaryPixelsChanged}.");
        Assert.Equal(
            1,
            CountComponents(
                nearest,
                1));
    }

    [Fact]
    public void CurveFillRibbonArcRefiner_RebuildsBroadSparseOvalArchDespiteLowPcaElongation()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(216, 185, 124),
        });
        var source = new DesignDocument(96, 64, palette);
        var controls = new (int X, int Y)[]
        {
            (9, 54),
            (18, 19),
            (48, 8),
            (78, 19),
            (87, 54),
        };

        var sourceCenterline =
            CurveRasterizer.Draw(
                    controls,
                    CurveType.SplineThroughPoints,
                    0.85)
                .ToArray();

        foreach (var point in Rasterizer.Dilate(
                     sourceCenterline,
                     5,
                     5))
        {
            if (point.X >= 0 &&
                point.X < source.Width &&
                point.Y >= 0 &&
                point.Y < source.Height)
            {
                source.SetPixel(
                    point.X,
                    point.Y,
                    1);
            }
        }

        var regions =
            LeafPetalRegionExtractor.Extract(
                source);
        var ribbonRegion =
            Assert.Single(
                regions.Where(region =>
                    region.Color == 1));

        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                ribbonRegion,
                source.Width,
                out var candidate),
            "The broad synthetic arch should reach ribbon candidate analysis.");
        Assert.InRange(
            candidate.Elongation,
            1.25,
            1.99);

        const int targetWidth = 154;
        const int targetHeight = 103;
        var nearest =
            DesignResizer.Scale(
                source,
                targetWidth,
                targetHeight,
                ScaleMode.NearestNeighbor,
                40,
                60,
                40,
                60);

        var idealSourceArc =
            Enumerable.Range(
                    0,
                    193)
                .Select(index =>
                {
                    var t =
                        index /
                        192d;
                    var point =
                        CurveRasterizer.Evaluate(
                            controls,
                            CurveType.SplineThroughPoints,
                            0.85,
                            t);

                    return new ElegantArcPoint(
                        point.X,
                        point.Y,
                        2.5);
                })
                .ToArray();
        var idealPolygon =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                idealSourceArc,
                targetWidth /
                    (double)source.Width,
                targetHeight /
                    (double)source.Height);
        var expected =
            LeafPetalArcRasterizer.RasterizePolygon(
                    idealPolygon,
                    targetWidth,
                    targetHeight)
                .Select(key =>
                    (
                        X: key %
                           targetWidth,
                        Y: key /
                           targetWidth))
                .ToHashSet();

        var beforeScore =
            PixelF1(
                GetColorPixels(
                    nearest,
                    1),
                expected);
        var diagnostics =
            CurveFillRibbonArcRefiner.ApplyWithDiagnostics(
                source,
                nearest);
        var after =
            GetColorPixels(
                nearest,
                1);
        var afterScore =
            PixelF1(
                after,
                expected);

        Assert.True(
            diagnostics.RibbonGeometryAccepted >= 1,
            $"Broad sparse oval arch was rejected before geometric fitting: " +
            $"classified={diagnostics.Classified}, axis={diagnostics.AxisBuilt}, " +
            $"coverage={diagnostics.MaxPrincipalPathCoverage:0.000}, reason={diagnostics.LastCenterlineReason}.");
        Assert.True(
            diagnostics.Refined >= 1 &&
            diagnostics.BoundaryPixelsChanged > 0,
            $"Broad sparse oval arch was not redrawn: refined={diagnostics.Refined}, " +
            $"changed={diagnostics.BoundaryPixelsChanged}, tool={diagnostics.CurveToolFits}, " +
            $"cubic={diagnostics.CubicBezierFits}, dev={diagnostics.MaxFitDeviation:0.000}, " +
            $"flips={diagnostics.MaxFitCurvatureFlips}.");
        Assert.True(
            afterScore >=
                beforeScore +
                0.003,
            $"Broad arch did not move toward continuous target geometry: " +
            $"before={beforeScore:P2}, after={afterScore:P2}, " +
            $"tool={diagnostics.CurveToolFits}, cubic={diagnostics.CubicBezierFits}.");
        Assert.Equal(
            1,
            CountComponents(
                nearest,
                1));
    }

    [Fact]
    public void Scale_CurveFill_DoesNotEraseNestedFillWhenOutlineAndInteriorShareCurvature()
    {
        var palette = new Palette(new[]
        {
            new RugColor(220, 214, 190),
            new RugColor(255, 255, 255),
            new RugColor(44, 128, 166),
        });
        var source = new DesignDocument(42, 42, palette);

        for (var y = 2; y < 40; y++)
        {
            for (var x = 2; x < 40; x++)
            {
                var dx = (x - 21) / 15.0;
                var dy = (y - 21) / 10.0;
                var r = dx * dx + dy * dy;

                if (r <= 1.0)
                    source.SetPixel(x, y, 1);

                if (r <= 0.72)
                    source.SetPixel(x, y, 2);
            }
        }

        var result = DesignResizer.Scale(
            source,
            73,
            89,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        Assert.True(CountColor(result, 1) > 0, "Outer curved outline disappeared.");
        Assert.True(CountColor(result, 2) > 0, "Interior indexed fill disappeared.");
        Assert.Equal(
            (byte)2,
            result.GetPixel(
                result.Width / 2,
                result.Height / 2));
    }

    [Fact]
    public void Scale_CurveFill_OnePixelSeparator_BlocksFillBleedAcrossRegions()
    {
        var palette = new Palette(new[]
        {
            new RugColor(35, 115, 165),   // left fill
            new RugColor(255, 255, 255), // 1px separator
            new RugColor(195, 145, 110), // right fill
        });
        var source = new DesignDocument(31, 23, palette);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                source.SetPixel(
                    x,
                    y,
                    x < 15
                        ? (byte)0
                        : x == 15
                            ? (byte)1
                            : (byte)2);
            }
        }

        var result = DesignResizer.Scale(
            source,
            47,
            37,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        for (var y = 0; y < result.Height; y++)
        {
            var lastLeft = -1;
            var firstRight = int.MaxValue;

            for (var x = 0; x < result.Width; x++)
            {
                var color = result.GetPixel(x, y);

                if (color == 0)
                    lastLeft = x;
                else if (color == 2)
                    firstRight = Math.Min(firstRight, x);
            }

            Assert.True(
                lastLeft >= 0 &&
                firstRight < int.MaxValue,
                "Both indexed fill regions must survive.");

            Assert.Contains(
                Enumerable.Range(
                        lastLeft + 1,
                        Math.Max(
                            0,
                            firstRight - lastLeft - 1))
                    .Select(x => result.GetPixel(x, y)),
                color => color == 1);
        }
    }

    [Fact]
    public void Scale_CurveFill_ToolStrokeNeverEscapesSourceGeometryCorridor()
    {
        var palette = new Palette(new[]
        {
            new RugColor(215, 207, 182),
            new RugColor(255, 255, 255),
        });
        var source = new DesignDocument(54, 46, palette);
        var controls = new (int X, int Y)[]
        {
            (5, 36),
            (11, 13),
            (25, 7),
            (40, 14),
            (48, 37),
        };

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.SplineThroughPoints,
                         0.70)))
        {
            source.SetPixel(
                point.X,
                point.Y,
                1);
        }

        var sourceStroke =
            GetColorPixels(
                source,
                1);

        var result = DesignResizer.Scale(
            source,
            86,
            74,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        var scaleX =
            result.Width /
            (double)source.Width;
        var scaleY =
            result.Height /
            (double)source.Height;

        foreach (var (x, y) in GetColorPixels(result, 1))
        {
            var sourceX =
                (x + 0.5) /
                scaleX -
                0.5;
            var sourceY =
                (y + 0.5) /
                scaleY -
                0.5;

            var nearestSquared =
                sourceStroke.Min(point =>
                {
                    var dx =
                        point.X -
                        sourceX;
                    var dy =
                        point.Y -
                        sourceY;

                    return dx * dx +
                           dy * dy;
                });

            Assert.True(
                nearestSquared <=
                1.35 * 1.35 +
                1e-9,
                $"Redrawn Curve pixel ({x},{y}) escaped source geometry corridor; source-distance={Math.Sqrt(nearestSquared):0.000}.");
        }
    }

    [Fact]
    public void Scale_CurveFill_EnlargeCurve_NeverIntroducesPaletteIndex()
    {
        var palette = Palette.CreateDefault256();
        var source = new DesignDocument(24, 24, palette);

        for (var i = 3; i < 20; i++)
        {
            source.SetPixel(i, i / 2 + 4, 7);
            if (i % 3 == 0)
                source.SetPixel(i, i / 2 + 5, 7);
        }

        var result = DesignResizer.Scale(
            source,
            48,
            60,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
            Assert.True(
                result.GetPixel(x, y) is 0 or 7,
                $"Curve enlargement introduced index {result.GetPixel(x, y)}.");
    }

    [Fact]
    public void Scale_CurveFill_PreservesNestedIndexedFillsInsideCurvedOutline()
    {
        var palette = new Palette(new[]
        {
            new RugColor(220, 214, 190), // field
            new RugColor(255, 255, 255), // outline
            new RugColor(40, 130, 170),  // outer fill
            new RugColor(195, 145, 110), // inner fill
        });
        var source = new DesignDocument(48, 48, palette);

        // Raster ellipse with a white separator and a differently-coloured inner ellipse.
        for (var y = 3; y < 45; y++)
        {
            for (var x = 3; x < 45; x++)
            {
                var dx = (x - 24) / 17.0;
                var dy = (y - 24) / 12.0;
                var r = dx * dx + dy * dy;

                if (r <= 1.0)
                    source.SetPixel(x, y, 2);
                if (r >= 0.72 && r <= 1.0)
                    source.SetPixel(x, y, 1);

                var ix = (x - 24) / 8.0;
                var iy = (y - 24) / 5.0;
                var inner = ix * ix + iy * iy;
                if (inner <= 1.0)
                    source.SetPixel(x, y, 3);
                if (inner >= 0.72 && inner <= 1.0)
                    source.SetPixel(x, y, 1);
            }
        }

        var result = DesignResizer.Scale(
            source,
            83,
            77,
            ScaleMode.CurveFill,
            40,
            50,
            40,
            50);

        Assert.True(CountColor(result, 1) > 0, "Curve outline disappeared.");
        Assert.True(CountColor(result, 2) > 0, "Outer indexed fill disappeared.");
        Assert.True(CountColor(result, 3) > 0, "Inner indexed fill disappeared.");
        Assert.Equal(1, CountComponents(result, 3));

        // The centre must still belong to the original inner colour rather than being hollowed or
        // overwritten by an outline redraw.
        Assert.Equal(
            (byte)3,
            result.GetPixel(result.Width / 2, result.Height / 2));
    }

    [Fact]
    public void Scale_RugScale_RealisticProgressiveRaster_DoesNotExhaustThreadStack()
    {
        // Regression for the real 48x50 carpet workflow. Gap/symmetry helpers previously created
        // stack-backed buffers inside the destination-pixel loop; a realistic progressive resize
        // could therefore terminate the entire process with StackOverflowException.
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(480, 720, palette);

        // Give the analyser real edges/regions without making the fixture itself huge.
        for (var y = 40; y < source.Height - 40; y++)
        {
            source.SetPixel(120, y, 1);
            source.SetPixel(source.Width - 121, y, 1);
        }
        for (var x = 80; x < source.Width - 80; x++)
        {
            source.SetPixel(x, 180, 1);
            source.SetPixel(x, source.Height - 181, 1);
        }

        var result = DesignResizer.Scale(source, 384, 552, ScaleMode.RugScale);

        Assert.Equal(384, result.Width);
        Assert.Equal(552, result.Height);
    }

    [Fact]
    public void Scale_RugScale_ProgressiveShrink_PreservesMappedBranchEndpoint()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(25, 25, palette);

        // Trunk + one-pixel twig. A moderate progressive resize must not shorten the twig merely
        // because its endpoint lands beside another surviving pixel of the same colour.
        for (var y = 3; y <= 21; y++)
            source.SetPixel(10, y, 1);
        for (var x = 10; x <= 20; x++)
            source.SetPixel(x, 10, 1);

        var result = DesignResizer.Scale(source, 20, 20, ScaleMode.RugScale);

        var endpointX = Math.Clamp(
            (int)((20 + 0.5) * result.Width / source.Width),
            0,
            result.Width - 1);
        var endpointY = Math.Clamp(
            (int)((10 + 0.5) * result.Height / source.Height),
            0,
            result.Height - 1);

        Assert.Equal(1, result.GetPixel(endpointX, endpointY));
        Assert.Equal(1, CountComponents(result, 1));
    }

    [Fact]
    public void Scale_RugScale_V4_PreservesMultiBendBranchAndEndpoint()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(40, 40, palette);

        // A long one-pixel twig with several bends. This is closer to the floral branches that
        // were visibly clipped in the real 200 -> 160 carpet resize.
        for (var y = 5; y <= 18; y++)
            source.SetPixel(10, y, 1);
        for (var x = 10; x <= 20; x++)
            source.SetPixel(x, 18, 1);
        for (var y = 18; y <= 27; y++)
            source.SetPixel(20, y, 1);
        for (var x = 20; x <= 31; x++)
            source.SetPixel(x, 27, 1);

        var result = DesignResizer.Scale(source, 32, 32, ScaleMode.RugScale);

        var endpointX = Math.Clamp(
            (int)((31 + 0.5) * result.Width / source.Width),
            0,
            result.Width - 1);
        var endpointY = Math.Clamp(
            (int)((27 + 0.5) * result.Height / source.Height),
            0,
            result.Height - 1);

        Assert.Equal(1, result.GetPixel(endpointX, endpointY));
        Assert.Equal(1, CountComponents(result, 1));
        Assert.True(CountColor(result, 1) >= 24,
            "The projected multi-bend branch lost too much of its centreline.");
    }

    [Fact]
    public void Scale_RugScale_V4_PreservesJunctionBranchesAsOneComponent()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(40, 40, palette);

        // T-shaped floral junction with three long branches.
        for (var y = 6; y <= 31; y++)
            source.SetPixel(20, y, 1);
        for (var x = 7; x <= 33; x++)
            source.SetPixel(x, 16, 1);

        var result = DesignResizer.Scale(source, 32, 32, ScaleMode.RugScale);

        var endpoints = new[]
        {
            (20, 6),
            (20, 31),
            (7, 16),
            (33, 16),
        };

        foreach (var (sx, sy) in endpoints)
        {
            var tx = Math.Clamp(
                (int)((sx + 0.5) * result.Width / source.Width),
                0,
                result.Width - 1);
            var ty = Math.Clamp(
                (int)((sy + 0.5) * result.Height / source.Height),
                0,
                result.Height - 1);

            Assert.Equal(1, result.GetPixel(tx, ty));
        }

        Assert.Equal(1, CountComponents(result, 1));
    }

    [Fact]
    public void Scale_RugScale_DoesNotMergeIndependentArmsIntoUnsupportedJunction()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(40, 70, 100),
        });
        var source = new DesignDocument(40, 40, palette);

        // Three independent one-pixel motif arms approach the same area but remain separated.
        // A shrink may shorten/shift them, but must not invent a central T/X junction that merges
        // all three motifs into one connected component.
        for (var y = 3; y <= 15; y++)
            source.SetPixel(20, y, 1);
        for (var x = 3; x <= 15; x++)
            source.SetPixel(x, 20, 1);
        for (var x = 24; x <= 36; x++)
            source.SetPixel(x, 20, 1);

        var result = DesignResizer.Scale(
            source,
            32,
            32,
            ScaleMode.RugScale);

        Assert.True(
            CountComponents(result, 1) >= 3,
            "Independent source motif arms were merged into an unsupported target junction.");
    }

    [Fact]
    public void Scale_RugScale_PhaseShiftedLeftRightSymmetry_IsHardConstraint()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(40, 70, 100),
            new RugColor(180, 135, 95),
        });

        // Last column is a technical sentinel. Design content is x=0..31 and mirrors around
        // constant 31, i.e. source mirror shift -1 relative to the full 33px bitmap.
        var source = new DesignDocument(33, 31, palette);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var color = ((x * 3 + y * 5) % 11) < 4 ? (byte)1 : (byte)0;
                source.SetPixel(x, y, color);
                source.SetPixel(31 - x, y, color);
            }

            source.SetPixel(32, y, 2);
        }

        var result = DesignResizer.Scale(source, 27, 25, ScaleMode.RugScale);

        // Target last column remains outside the mirrored carpet content.
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x <= 25; x++)
            {
                Assert.Equal(
                    result.GetPixel(x, y),
                    result.GetPixel(25 - x, y));
            }
        }
    }

    [Fact]
    public void Scale_RugScale_PhaseShiftedTopBottomSymmetry_ReCentersTarget()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(40, 70, 100),
            new RugColor(180, 135, 95),
        });

        // Rows 1..31 mirror around constant 32 (source shift +1); row 0 is outside that pairing.
        // It is deliberately non-uniform so it cannot be mistaken for a sentinel scan-line.
        var source = new DesignDocument(29, 32, palette);

        for (var x = 0; x < source.Width; x++)
            source.SetPixel(x, 0, (byte)((x % 3) == 0 ? 2 : 0));

        for (var y = 1; y < source.Height; y++)
        {
            var mirrorY = 32 - y;
            if (mirrorY < 1 || mirrorY >= source.Height || y > mirrorY)
                continue;

            for (var x = 0; x < source.Width; x++)
            {
                var color = ((x * 7 + y * 3) % 13) < 5 ? (byte)1 : (byte)0;
                source.SetPixel(x, y, color);
                source.SetPixel(x, mirrorY, color);
            }
        }

        var result = DesignResizer.Scale(source, 23, 25, ScaleMode.RugScale);

        // Designer rule: target is re-centred on its geometric axis rather than preserving the
        // source's one-pixel phase offset.
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                Assert.Equal(
                    result.GetPixel(x, y),
                    result.GetPixel(x, result.Height - 1 - y));
            }
        }
    }

    [Fact]
    public void Scale_RugScale_ProgressiveSymmetryRepair_DoesNotRescoreUntouchedBaseline()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(31, 31, palette);

        // Symmetric broad geometry plus thin symmetric twigs.
        for (var y = 4; y <= 26; y++)
        {
            source.SetPixel(7, y, 1);
            source.SetPixel(23, y, 1);
        }
        for (var x = 7; x <= 12; x++)
        {
            source.SetPixel(x, 12, 1);
            source.SetPixel(source.Width - 1 - x, 12, 1);
            source.SetPixel(x, 18, 1);
            source.SetPixel(source.Width - 1 - x, 18, 1);
        }

        var result = DesignResizer.Scale(source, 25, 25, ScaleMode.RugScale);

        var differences = 0;
        for (var y = 0; y < result.Height; y++)
        {
            var sy = Math.Clamp(
                (int)Math.Floor((y + 0.5) * source.Height / result.Height),
                0,
                source.Height - 1);

            for (var x = 0; x < result.Width; x++)
            {
                var sx = Math.Clamp(
                    (int)Math.Floor((x + 0.5) * source.Width / result.Width),
                    0,
                    source.Width - 1);

                if (result.GetPixel(x, y) != source.GetPixel(sx, sy))
                    differences++;

                Assert.Equal(
                    result.GetPixel(x, y),
                    result.GetPixel(result.Width - 1 - x, y));
            }
        }

        Assert.True(
            differences <= result.Width * result.Height / 5,
            $"Progressive RugScale repainted {differences} pixels; expected structural repair rather than a wholesale redraw.");
    }

    [Fact]
    public void Scale_RugScale_ProgressiveShrinkStaysCloseToCenterNearest()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(20, 24, palette);

        // Dense but broad symmetric ornament: moderate 20x24 -> 16x18 should be fidelity-first,
        // not a wholesale feature-map repaint.
        for (var y = 2; y < source.Height - 2; y++)
        {
            source.SetPixel(4, y, 1);
            source.SetPixel(source.Width - 5, y, 1);
        }
        for (var x = 3; x < source.Width - 3; x++)
        {
            source.SetPixel(x, 6, 1);
            source.SetPixel(x, source.Height - 7, 1);
        }

        var result = DesignResizer.Scale(source, 16, 18, ScaleMode.RugScale);
        var differences = 0;

        for (var y = 0; y < result.Height; y++)
        {
            var sy = Math.Clamp(
                (int)Math.Floor((y + 0.5) * source.Height / result.Height),
                0,
                source.Height - 1);

            for (var x = 0; x < result.Width; x++)
            {
                var sx = Math.Clamp(
                    (int)Math.Floor((x + 0.5) * source.Width / result.Width),
                    0,
                    source.Width - 1);

                if (result.GetPixel(x, y) != source.GetPixel(sx, sy))
                    differences++;
            }
        }

        Assert.True(
            differences <= result.Width * result.Height / 5,
            $"Progressive RugScale changed {differences} pixels; expected a bounded structural repair rather than a wholesale redraw.");
    }

    [Fact]
    public void Scale_RugScale_EnlargementPreservesMirrorSymmetry()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(7, 9, palette);

        // Build an explicitly mirror-symmetric motif.
        for (var y = 0; y < source.Height; y++)
        {
            source.SetPixel(1, y, 1);
            source.SetPixel(source.Width - 2, y, 1);
        }
        for (var x = 0; x < source.Width; x++)
        {
            source.SetPixel(x, 2, 1);
            source.SetPixel(x, source.Height - 3, 1);
        }

        var result = DesignResizer.Scale(source, 12, 14, ScaleMode.RugScale);

        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                Assert.Equal(result.GetPixel(x, y),
                    result.GetPixel(result.Width - 1 - x, y));
                Assert.Equal(result.GetPixel(x, y),
                    result.GetPixel(x, result.Height - 1 - y));
            }
        }
    }

    [Fact]
    public void Scale_RugScale_PreservesAOnePixelContourThatDominantLoses()
    {
        // Three source columns collapse into one target column. Majority voting removes the
        // one-pixel motif line; RugScale's thin-line importance must keep it continuous.
        var palette = new Palette(new[] { new RugColor(245, 245, 245), new RugColor(20, 20, 20) });
        var source = new DesignDocument(9, 9, palette);

        for (var y = 0; y < source.Height; y++)
            source.SetPixel(4, y, 1);

        var dominant = DesignResizer.Scale(source, 3, 3, ScaleMode.Dominant);
        var rugScale = DesignResizer.Scale(source, 3, 3, ScaleMode.RugScale);

        Assert.Equal(0, CountColor(dominant, 1));
        Assert.Equal(3, CountColor(rugScale, 1));
        for (var y = 0; y < 3; y++)
            Assert.Equal(1, rugScale.GetPixel(1, y));
    }

    [Fact]
    public void Scale_RugScale_KeepsATinyCriticalMotifRepresented()
    {
        // A single indexed motif pixel occupies only 1/9 of its destination footprint and would
        // normally vanish. RugScale marks it as a tiny critical region/corner and reserves a cell.
        var palette = new Palette(new[] { new RugColor(240, 240, 240), new RugColor(10, 10, 10) });
        var source = new DesignDocument(9, 9, palette);
        source.SetPixel(4, 4, 1);

        var dominant = DesignResizer.Scale(source, 3, 3, ScaleMode.Dominant);
        var rugScale = DesignResizer.Scale(source, 3, 3, ScaleMode.RugScale);

        Assert.Equal(0, CountColor(dominant, 1));
        Assert.True(CountColor(rugScale, 1) >= 1, "RugScale dropped the tiny motif completely.");
    }

    [Fact]
    public void Scale_RugScale_PreservesConnectedThinLCorner()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(12, 12, palette);

        // One-pixel-wide L motif.
        for (var y = 2; y <= 9; y++)
            source.SetPixel(3, y, 1);
        for (var x = 3; x <= 9; x++)
            source.SetPixel(x, 9, 1);

        var rugScale = DesignResizer.Scale(source, 6, 6, ScaleMode.RugScale);

        Assert.True(CountColor(rugScale, 1) >= 4, "RugScale over-collapsed the L motif.");
        Assert.True(
            CountComponents(rugScale, 1) == 1,
            "The downscaled L motif should remain one connected component.");
    }

    [Fact]
    public void Scale_RugScale_MotifAtlas_ReusesGeometryAcrossDifferentColors()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(40, 70, 105),
            new RugColor(165, 115, 85),
        });
        var source = new DesignDocument(64, 32, palette);

        static void DrawLeaf(DesignDocument document, int ox, int oy, byte color)
        {
            // Connected asymmetric leaf/branch: enough geometry to make family reuse observable.
            for (var y = 0; y < 9; y++)
            {
                document.SetPixel(ox + 3, oy + y, color);

                if (y is >= 2 and <= 6)
                    document.SetPixel(ox + 2, oy + y, color);

                if (y is >= 4 and <= 7)
                    document.SetPixel(ox + 4, oy + y, color);
            }

            document.SetPixel(ox + 1, oy + 5, color);
            document.SetPixel(ox + 5, oy + 6, color);
        }

        DrawLeaf(source, 6, 8, 1);
        DrawLeaf(source, 42, 8, 2);

        var result = DesignResizer.Scale(
            source,
            48,
            24,
            ScaleMode.RugScale);

        static HashSet<(int X, int Y)> RelativeMask(
            DesignDocument document,
            byte color,
            out int width,
            out int height)
        {
            var points = new List<(int X, int Y)>();

            for (var y = 0; y < document.Height; y++)
            {
                for (var x = 0; x < document.Width; x++)
                {
                    if (document.GetPixel(x, y) == color)
                        points.Add((x, y));
                }
            }

            Assert.NotEmpty(points);

            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxX = points.Max(p => p.X);
            var maxY = points.Max(p => p.Y);

            width = maxX - minX + 1;
            height = maxY - minY + 1;

            return points
                .Select(p => (p.X - minX, p.Y - minY))
                .ToHashSet();
        }

        var first = RelativeMask(result, 1, out var firstWidth, out var firstHeight);
        var second = RelativeMask(result, 2, out var secondWidth, out var secondHeight);

        Assert.Equal(firstWidth, secondWidth);
        Assert.Equal(firstHeight, secondHeight);
        Assert.True(
            first.SetEquals(second),
            "Same motif geometry with different palette indexes should reuse one target shape.");
    }

    [Fact]
    public void Scale_RugScale_MotifAtlas_KeepsSeparatedTinyMotifsRepresented()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(35, 65, 95),
        });
        var source = new DesignDocument(80, 40, palette);

        var seeds = new[]
        {
            (8, 8),
            (24, 8),
            (40, 8),
            (56, 8),
            (16, 27),
            (48, 27),
        };

        foreach (var (x, y) in seeds)
        {
            source.SetPixel(x, y, 1);
            source.SetPixel(x + 1, y, 1);
            source.SetPixel(x, y + 1, 1);
        }

        var result = DesignResizer.Scale(
            source,
            64,
            32,
            ScaleMode.RugScale);

        Assert.True(
            CountComponents(result, 1) >= seeds.Length,
            "Separated source motifs should not vanish or merge during motif-first shrink.");
    }

    [Fact]
    public void Scale_RugScale_V6_PreservesInnerHorizontalRepeatZone()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(35, 65, 95),
            new RugColor(165, 125, 90),
        });
        var source = new DesignDocument(160, 120, palette);

        // Repeat exists only in an inner horizontal strip; outer borders are plain.
        for (var y = 40; y < 56; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var phase = x % 10;
                source.SetPixel(
                    x,
                    y,
                    phase < 4 ? (byte)1 : (byte)2);
            }
        }

        var result = DesignResizer.Scale(
            source,
            128,
            96,
            ScaleMode.RugScale);

        // 10 source pixels map to exactly 8 target pixels at 0.8 scale.
        var sampleY = 38;
        for (var x = 16; x + 8 < 112; x++)
        {
            Assert.Equal(
                result.GetPixel(x, sampleY),
                result.GetPixel(x + 8, sampleY));
        }
    }

    [Fact]
    public void Scale_RugScale_V6_PreservesInnerVerticalRepeatZone()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(35, 65, 95),
            new RugColor(165, 125, 90),
        });
        var source = new DesignDocument(120, 160, palette);

        // Repeat exists only in an inner vertical strip; outer borders are plain.
        for (var x = 40; x < 56; x++)
        {
            for (var y = 0; y < source.Height; y++)
            {
                var phase = y % 10;
                source.SetPixel(
                    x,
                    y,
                    phase < 4 ? (byte)1 : (byte)2);
            }
        }

        var result = DesignResizer.Scale(
            source,
            96,
            128,
            ScaleMode.RugScale);

        var sampleX = 38;
        for (var y = 16; y + 8 < 112; y++)
        {
            Assert.Equal(
                result.GetPixel(sampleX, y),
                result.GetPixel(sampleX, y + 8));
        }
    }

    [Fact]
    public void Scale_RugScale_V5_PreservesHorizontalBorderRepeatPhase()
    {
        var palette = new Palette(new[]
        {
            new RugColor(240, 235, 225),
            new RugColor(35, 65, 95),
            new RugColor(145, 105, 75),
        });
        var source = new DesignDocument(160, 80, palette);

        // Leave 24px corner blocks untouched; central top strip is seven exact 16px repeats.
        for (var y = 0; y < 20; y++)
        {
            for (var x = 24; x < 136; x++)
            {
                var phase = (x - 24) % 16;
                var color = phase switch
                {
                    < 4 => (byte)1,
                    < 8 => (byte)2,
                    < 12 => (byte)1,
                    _ => (byte)0,
                };
                source.SetPixel(x, y, color);
            }
        }

        // Distinct corner blocks make it easy to detect accidental repeat painting into corners.
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                source.SetPixel(x, y, 2);
                source.SetPixel(source.Width - 1 - x, y, 2);
            }
        }

        var result = DesignResizer.Scale(source, 128, 64, ScaleMode.RugScale);

        // Detector should resolve 7 repeats over the resized central span.
        var targetStart = (int)Math.Round(24 * (128d / 160d));
        var targetEnd = 128 - targetStart;
        var targetSpan = targetEnd - targetStart;

        for (var repeat = 1; repeat < 7; repeat++)
        {
            for (var phaseSample = 0; phaseSample < 4; phaseSample++)
            {
                var a = targetStart +
                        (int)Math.Floor((phaseSample + 0.5) * targetSpan / (7d * 4d));
                var b = targetStart +
                        (int)Math.Floor((repeat * 4 + phaseSample + 0.5) *
                                       targetSpan / (7d * 4d));

                Assert.Equal(
                    result.GetPixel(a, 4),
                    result.GetPixel(b, 4));
            }
        }

        // Corners are not part of the repeat strip and must keep their source identity.
        Assert.Equal(2, result.GetPixel(2, 4));
        Assert.Equal(2, result.GetPixel(result.Width - 3, 4));
    }

    [Fact]
    public void Scale_RugScale_V5_CanonicalRepeatUsesMajorityOccurrence()
    {
        var palette = new Palette(new[]
        {
            new RugColor(240, 235, 225),
            new RugColor(35, 65, 95),
            new RugColor(145, 105, 75),
        });
        var source = new DesignDocument(160, 80, palette);

        // Eight exact 16px repeats between 16px corner blocks. One middle occurrence is
        // deliberately corrupted; V5.1 must build the canonical tile from the majority of all
        // occurrences instead of copying that one arbitrary middle tile everywhere.
        for (var y = 0; y < 20; y++)
        {
            for (var x = 16; x < 144; x++)
            {
                var phase = (x - 16) % 16;
                var color = phase < 8 ? (byte)1 : (byte)0;
                source.SetPixel(x, y, color);
            }

            for (var x = 0; x < 16; x++)
            {
                source.SetPixel(x, y, 2);
                source.SetPixel(source.Width - 1 - x, y, 2);
            }
        }

        // Corrupt repeat #4 only, in the middle of the pattern.
        for (var y = 0; y < 20; y++)
            for (var x = 16 + 4 * 16; x < 16 + 4 * 16 + 8; x++)
                source.SetPixel(x, y, 2);

        var result = DesignResizer.Scale(source, 128, 64, ScaleMode.RugScale);

        var targetStart = (int)Math.Round(16 * (128d / 160d));
        var targetEnd = 128 - targetStart;
        var targetSpan = targetEnd - targetStart;

        // Sample the centre of the first half of every target repeat. The majority source motif
        // is colour 1 there; the one corrupted source occurrence must not turn the whole border 2.
        for (var repeat = 0; repeat < 8; repeat++)
        {
            var position =
                (repeat + 0.25) * targetSpan / 8d;
            var x = targetStart + Math.Clamp(
                (int)Math.Floor(position),
                0,
                targetSpan - 1);

            Assert.Equal(1, result.GetPixel(x, 4));
        }
    }

    [Fact]
    public void Scale_RugScale_V5_PreservesVerticalBorderRepeatPhase()
    {
        var palette = new Palette(new[]
        {
            new RugColor(240, 235, 225),
            new RugColor(35, 65, 95),
            new RugColor(145, 105, 75),
        });
        var source = new DesignDocument(80, 160, palette);

        for (var x = 0; x < 20; x++)
        {
            for (var y = 24; y < 136; y++)
            {
                var phase = (y - 24) % 16;
                var color = phase switch
                {
                    < 4 => (byte)1,
                    < 8 => (byte)2,
                    < 12 => (byte)1,
                    _ => (byte)0,
                };
                source.SetPixel(x, y, color);
            }
        }

        for (var x = 0; x < 20; x++)
        {
            for (var y = 0; y < 24; y++)
            {
                source.SetPixel(x, y, 2);
                source.SetPixel(x, source.Height - 1 - y, 2);
            }
        }

        var result = DesignResizer.Scale(source, 64, 128, ScaleMode.RugScale);

        var targetStart = (int)Math.Round(24 * (128d / 160d));
        var targetEnd = 128 - targetStart;
        var targetSpan = targetEnd - targetStart;

        for (var repeat = 1; repeat < 7; repeat++)
        {
            for (var phaseSample = 0; phaseSample < 4; phaseSample++)
            {
                var a = targetStart +
                        (int)Math.Floor((phaseSample + 0.5) * targetSpan / (7d * 4d));
                var b = targetStart +
                        (int)Math.Floor((repeat * 4 + phaseSample + 0.5) *
                                       targetSpan / (7d * 4d));

                Assert.Equal(
                    result.GetPixel(4, a),
                    result.GetPixel(4, b));
            }
        }

        Assert.Equal(2, result.GetPixel(4, 2));
        Assert.Equal(2, result.GetPixel(4, result.Height - 3));
    }

    [Fact]
    public void Scale_RugScale_IsRepeatAwareAcrossOppositeBorders()
    {
        var palette = new Palette(new[] { new RugColor(255, 255, 255), new RugColor(0, 0, 0) });
        var source = new DesignDocument(9, 9, palette);

        // These are the two halves of one two-pixel-wide vertical stripe in repeat space.
        for (var y = 0; y < source.Height; y++)
        {
            source.SetPixel(0, y, 1);
            source.SetPixel(8, y, 1);
        }

        var rugScale = DesignResizer.Scale(source, 3, 3, ScaleMode.RugScale);

        for (var y = 0; y < rugScale.Height; y++)
        {
            Assert.Equal(1, rugScale.GetPixel(0, y));
            Assert.Equal(1, rugScale.GetPixel(rugScale.Width - 1, y));
        }
    }

    private static HashSet<(int X, int Y)> GetColorPixels(
        DesignDocument document,
        byte color)
    {
        var result =
            new HashSet<(int X, int Y)>();

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                if (document.GetPixel(
                        x,
                        y) == color)
                {
                    result.Add(
                        (x, y));
                }
            }
        }

        return result;
    }

    private static double PixelNearF1(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected,
        int radius)
    {
        if (actual.Count == 0 ||
            expected.Count == 0)
        {
            return 0d;
        }

        static bool Near(
            IReadOnlySet<(int X, int Y)> points,
            (int X, int Y) point,
            int searchRadius)
        {
            for (var dy = -searchRadius;
                 dy <= searchRadius;
                 dy++)
            {
                for (var dx = -searchRadius;
                     dx <= searchRadius;
                     dx++)
                {
                    if (points.Contains(
                            (
                                point.X + dx,
                                point.Y + dy)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        var actualSupported =
            actual.Count(point =>
                Near(
                    expected,
                    point,
                    radius));
        var expectedSupported =
            expected.Count(point =>
                Near(
                    actual,
                    point,
                    radius));

        var precision =
            actualSupported /
            (double)actual.Count;
        var recall =
            expectedSupported /
            (double)expected.Count;
        var sum =
            precision +
            recall;

        return sum <= 0d
            ? 0d
            : 2d *
              precision *
              recall /
              sum;
    }

    private static double PixelF1(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected)
    {
        if (actual.Count == 0 ||
            expected.Count == 0)
        {
            return 0d;
        }

        var intersection =
            actual.Count(
                expected.Contains);
        var precision =
            intersection /
            (double)actual.Count;
        var recall =
            intersection /
            (double)expected.Count;
        var sum =
            precision +
            recall;

        return sum <= 0d
            ? 0d
            : 2d *
              precision *
              recall /
              sum;
    }

    private static int CountColor(DesignDocument document, byte color)
    {
        var count = 0;
        for (var y = 0; y < document.Height; y++)
            for (var x = 0; x < document.Width; x++)
                if (document.GetPixel(x, y) == color)
                    count++;
        return count;
    }

    private static bool HasColorNear(
        DesignDocument document,
        int x,
        int y,
        byte color,
        int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var py = y + dy;
            if (py < 0 || py >= document.Height)
                continue;

            for (var dx = -radius; dx <= radius; dx++)
            {
                var px = x + dx;
                if (px < 0 || px >= document.Width)
                    continue;

                if (document.GetPixel(px, py) == color)
                    return true;
            }
        }

        return false;
    }

    private static int CountFourConnectedComponents(
        DesignDocument document,
        byte color)
    {
        var seen = new bool[document.Width * document.Height];
        var queue = new Queue<(int X, int Y)>();
        var components = 0;
        (int X, int Y)[] directions =
        [
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1),
        ];

        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var start = y * document.Width + x;
                if (seen[start] ||
                    document.GetPixel(x, y) != color)
                {
                    continue;
                }

                components++;
                seen[start] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();

                    foreach (var (dx, dy) in directions)
                    {
                        var nx = cx + dx;
                        var ny = cy + dy;

                        if (nx < 0 ||
                            nx >= document.Width ||
                            ny < 0 ||
                            ny >= document.Height)
                        {
                            continue;
                        }

                        var next = ny * document.Width + nx;
                        if (seen[next] ||
                            document.GetPixel(nx, ny) != color)
                        {
                            continue;
                        }

                        seen[next] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        return components;
    }

    private static int CountSolidTwoByTwoBlocks(
        DesignDocument document,
        byte color)
    {
        var count = 0;

        for (var y = 0; y + 1 < document.Height; y++)
        {
            for (var x = 0; x + 1 < document.Width; x++)
            {
                if (document.GetPixel(x, y) == color &&
                    document.GetPixel(x + 1, y) == color &&
                    document.GetPixel(x, y + 1) == color &&
                    document.GetPixel(x + 1, y + 1) == color)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountComponents(DesignDocument document, byte color)
    {
        var seen = new bool[document.Width * document.Height];
        var queue = new Queue<(int X, int Y)>();
        var components = 0;

        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var start = y * document.Width + x;
                if (seen[start] || document.GetPixel(x, y) != color)
                    continue;

                components++;
                seen[start] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;

                            var nx = cx + dx;
                            var ny = cy + dy;
                            if (nx < 0 || nx >= document.Width || ny < 0 || ny >= document.Height)
                                continue;

                            var next = ny * document.Width + nx;
                            if (seen[next] || document.GetPixel(nx, ny) != color)
                                continue;

                            seen[next] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }
        }

        return components;
    }

    [Fact]
    public void Scale_AreaAverage_UniformRegionStaysExact()
    {
        // A uniform 4x4 image of a single color must average back to that exact color.
        var palette = new Palette(new[] { new RugColor(10, 20, 30) });
        var source = new DesignDocument(4, 4, palette);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                source.SetPixel(x, y, 0);

        var result = DesignResizer.Scale(source, 2, 2, ScaleMode.AreaAverage);

        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 2; x++)
                Assert.Equal(0, result.GetPixel(x, y));
    }
}
