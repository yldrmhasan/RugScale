using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveOvalTrainingTests
{
    [Fact]
    public void BroadOvalFit_RasterShoulderOutliersDoNotFlattenDesignerOval()
    {
        var samples =
            Enumerable.Range(
                    0,
                    101)
                .Select(index =>
                {
                    var t =
                        index /
                        100d;
                    var theta =
                        Math.PI *
                        (1d -
                         t);
                    var phase =
                        index %
                        4;
                    var rasterWobble =
                        phase switch
                        {
                            0 => 0.18,
                            2 => -0.16,
                            _ => 0d,
                        };
                    var shoulderOutlier =
                        index is 20 or 24 or 76 or 80
                            ? 2.2
                            : 0d;

                    return new LeafPetalAxisSample(
                        70d +
                        55d *
                            Math.Cos(
                                theta),
                        70d +
                        31d *
                            Math.Sin(
                                theta) +
                        rasterWobble +
                        shoulderOutlier,
                        3.2,
                        t);
                })
                .ToArray();
        var model =
            Model(
                samples,
                width: 120,
                height: 80);

        var accepted =
            CurveFillBroadOvalArcFitter.TryFit(
                model,
                out var fit,
                out var diagnostics);

        Assert.True(
            accepted,
            $"Broad oval should stay source-bounded after robust raster de-phasing: " +
            $"reason={diagnostics.Reason}, p95={diagnostics.Percentile95Deviation:0.000}, " +
            $"max={diagnostics.MaximumDeviation:0.000}, ratio={diagnostics.HeightToHalfChordRatio:0.000}.");
        Assert.True(
            fit.IsSafe);
        Assert.InRange(
            diagnostics.HeightToHalfChordRatio,
            0.53,
            0.63);

        // Long production arches need denser sub-pixel sampling than the old fixed 128 points so
        // the polygon rasterizer does not visibly facet the oval after enlargement.
        Assert.True(
            fit.Points.Count >
            128,
            $"Expected adaptive oval sampling, got {fit.Points.Count} points.");

        var apex =
            fit.Points.Max(point =>
                point.Y);

        Assert.InRange(
            apex,
            99d,
            102.5d);
    }

    [Fact]
    public void WidthRegularizer_BoundsLocalBreathingWithoutChangingBandWeight()
    {
        var sourceSamples =
            Enumerable.Range(
                    0,
                    81)
                .Select(index =>
                {
                    var t =
                        index /
                        80d;
                    var lowFrequency =
                        0.18 *
                        Math.Sin(
                            Math.PI *
                            t);
                    var phase =
                        index %
                        2 == 0
                            ? 0.20
                            : -0.20;

                    return new LeafPetalAxisSample(
                        index,
                        15d +
                        6d *
                            Math.Sin(
                                Math.PI *
                                t),
                        3.0 +
                        lowFrequency +
                        phase,
                        t);
                })
                .ToArray();
        var model =
            Model(
                sourceSamples,
                width: 90,
                height: 30);

        var rawPoints =
            Enumerable.Range(
                    0,
                    161)
                .Select(index =>
                {
                    var t =
                        index /
                        160d;
                    var phase =
                        index %
                        2 == 0
                            ? 0.55
                            : -0.55;
                    var spike =
                        index is 78 or 79 or 80
                            ? 0.85
                            : 0d;

                    return new ElegantArcPoint(
                        index *
                            0.5,
                        15d +
                        6d *
                            Math.Sin(
                                Math.PI *
                                t),
                        3.0 +
                        0.18 *
                            Math.Sin(
                                Math.PI *
                                t) +
                        phase +
                        spike);
                })
                .ToArray();
        var fit =
            new ElegantArcFit(
                rawPoints,
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 0,
                MaximumCenterlineDeviation: 0.5);

        var regularized =
            CurveFillRibbonWidthProfileRegularizer.Regularize(
                model,
                fit,
                out var diagnostics);

        Assert.True(
            diagnostics.Applied,
            "Alternating half-width phase should be regularized.");

        var maximumAdjacentDelta =
            regularized.Points
                .Zip(
                    regularized.Points.Skip(1),
                    (left, right) =>
                        Math.Abs(
                            right.HalfWidth -
                            left.HalfWidth))
                .DefaultIfEmpty()
                .Max();

        Assert.InRange(
            maximumAdjacentDelta,
            0d,
            0.27);
        Assert.True(
            diagnostics.AfterAdjacentVariation <
            diagnostics.BeforeAdjacentVariation *
                0.45,
            $"Width breathing should drop substantially: before={diagnostics.BeforeAdjacentVariation:0.000}, " +
            $"after={diagnostics.AfterAdjacentVariation:0.000}.");

        var rawMean =
            rawPoints.Average(point =>
                point.HalfWidth);
        var regularizedMean =
            regularized.Points.Average(point =>
                point.HalfWidth);

        Assert.InRange(
            Math.Abs(
                regularizedMean -
                rawMean),
            0d,
            0.20);
    }

    [Fact]
    public void OneSidedSweepExtractor_TrimsOppositeTerminalHookAndKeepsDominantArc()
    {
        var samples =
            new List<LeafPetalAxisSample>();

        const int mainCount = 72;

        for (var index = 0;
             index < mainCount;
             index++)
        {
            var t =
                index /
                (double)(mainCount - 1);
            var x =
                10d +
                70d *
                    t;
            var y =
                48d -
                22d *
                    (t - 0.5) *
                    (t - 0.5);

            samples.Add(
                new LeafPetalAxisSample(
                    x,
                    y,
                    3.2,
                    samples.Count));
        }

        var tailStart =
            samples[^1];

        const int hookCount = 24;

        for (var index = 1;
             index <= hookCount;
             index++)
        {
            var u =
                index /
                (double)hookCount;

            samples.Add(
                new LeafPetalAxisSample(
                    tailStart.X +
                    18d *
                        u,
                    tailStart.Y -
                    4d *
                        u +
                    15d *
                        u *
                        u,
                    3.2 -
                    1.8 *
                        u,
                    samples.Count));
        }

        var normalized =
            samples
                .Select((sample, index) =>
                    sample with
                    {
                        AxisPosition =
                            index /
                            (double)Math.Max(
                                1,
                                samples.Count - 1),
                    })
                .ToArray();
        var model =
            Model(
                normalized,
                width: 110,
                height: 70);

        var extracted =
            CurveFillRibbonMainArcExtractor.TryExtractOneSidedSweep(
                model,
                out var sweep,
                out var diagnostics);

        Assert.True(
            extracted,
            $"Expected dominant sweep extraction: reason={diagnostics.Reason}, " +
            $"range={diagnostics.StartIndex}-{diagnostics.EndIndex}, " +
            $"kept={diagnostics.KeptFraction:0.000}, radius={diagnostics.Radius}.");
        Assert.Equal(
            "ok-one-sided",
            diagnostics.Reason);
        Assert.InRange(
            diagnostics.KeptFraction,
            0.48,
            0.95);
        Assert.True(
            sweep.Samples.Count <
            model.Samples.Count,
            "The opposite-curvature terminal hook must be excluded from the dominant sweep.");
        Assert.True(
            diagnostics.EndIndex <
            model.Samples.Count - 1,
            "The one-sided extractor should trim the terminal hook, not keep the complete path.");
    }

    [Fact]
    public void TrueRibbonRasterizer_ReplacesNearestStaircaseWithFittedFillAndOutline()
    {
        var palette =
            new Palette(
                new[]
                {
                    new RugColor(218, 210, 184),
                    new RugColor(255, 255, 255),
                    new RugColor(88, 132, 92),
                });
        var source =
            new DesignDocument(
                42,
                32,
                palette);
        var regionPixels =
            new HashSet<int>();

        for (var x = 5;
             x <= 36;
             x++)
        {
            var t =
                (x - 5) /
                31d;
            var centerY =
                16 +
                (int)Math.Round(
                    4d *
                    Math.Sin(
                        Math.PI *
                        t));

            for (var y = centerY - 2;
                 y <= centerY + 2;
                 y++)
            {
                source.SetPixel(
                    x,
                    y,
                    2);
                regionPixels.Add(
                    y *
                    source.Width +
                    x);
            }
        }

        var boundary =
            regionPixels
                .Where(key =>
                {
                    var x =
                        key %
                        source.Width;
                    var y =
                        key /
                        source.Width;

                    return
                        x == 0 ||
                        x + 1 >= source.Width ||
                        y == 0 ||
                        y + 1 >= source.Height ||
                        !regionPixels.Contains(
                            y *
                                source.Width +
                            x -
                            1) ||
                        !regionPixels.Contains(
                            y *
                                source.Width +
                            x +
                            1) ||
                        !regionPixels.Contains(
                            (y - 1) *
                                source.Width +
                            x) ||
                        !regionPixels.Contains(
                            (y + 1) *
                                source.Width +
                            x);
                })
                .ToArray();

        // Paint a dedicated 1x1 white outline around the source region.
        foreach (var key in boundary)
        {
            var x =
                key %
                source.Width;
            var y =
                key /
                source.Width;

            for (var dy = -1;
                 dy <= 1;
                 dy++)
            {
                for (var dx = -1;
                     dx <= 1;
                     dx++)
                {
                    if (dx == 0 &&
                        dy == 0)
                    {
                        continue;
                    }

                    var nx =
                        x +
                        dx;
                    var ny =
                        y +
                        dy;

                    if (nx < 0 ||
                        nx >= source.Width ||
                        ny < 0 ||
                        ny >= source.Height ||
                        regionPixels.Contains(
                            ny *
                                source.Width +
                            nx))
                    {
                        continue;
                    }

                    source.SetPixel(
                        nx,
                        ny,
                        1);
                }
            }
        }

        var region =
            new LeafPetalRegion(
                2,
                regionPixels.ToArray(),
                boundary,
                5,
                10,
                36,
                22);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                20.5,
                16,
                1,
                0,
                0,
                1,
                31,
                12,
                3.0,
                0.30);
        var sourceSamples =
            Enumerable.Range(
                    0,
                    65)
                .Select(index =>
                {
                    var t =
                        index /
                        64d;

                    return new LeafPetalAxisSample(
                        5d +
                        31d *
                            t,
                        16d +
                        4d *
                            Math.Sin(
                                Math.PI *
                                t),
                        2.45,
                        t);
                })
                .ToArray();
        var model =
            new LeafPetalArcModel(
                candidate,
                sourceSamples,
                ReversedForApex: false,
                BaseWidth: 2.45,
                ApexWidth: 2.45,
                SkeletonCoverage: 1d);
        var fitPoints =
            Enumerable.Range(
                    0,
                    193)
                .Select(index =>
                {
                    var t =
                        index /
                        192d;

                    return new ElegantArcPoint(
                        5d +
                        31d *
                            t,
                        16d +
                        4d *
                            Math.Sin(
                                Math.PI *
                                t),
                        2.45);
                })
                .ToArray();
        var fit =
            new ElegantArcFit(
                fitPoints,
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 0,
                MaximumCenterlineDeviation: 0.35);
        var destination =
            DesignResizer.Scale(
                source,
                84,
                64,
                ScaleMode.NearestNeighbor);

        var beforeFill =
            0;
        var beforeOutline =
            0;
        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var fillMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                LeafPetalArcRasterizer.BuildTargetPolygon(
                    fit.Points,
                    scaleX,
                    scaleY),
                destination.Width,
                destination.Height);
        var outerMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                LeafPetalArcRasterizer.BuildTargetExpandedPolygon(
                    fit.Points,
                    scaleX,
                    scaleY,
                    additionalTargetPixels: 1d),
                destination.Width,
                destination.Height);

        foreach (var key in fillMask)
        {
            var x =
                key %
                destination.Width;
            var y =
                key /
                destination.Width;

            if (destination.GetPixel(
                    x,
                    y) !=
                2)
            {
                beforeFill++;
            }
        }

        foreach (var key in outerMask)
        {
            if (fillMask.Contains(
                    key))
            {
                continue;
            }

            var x =
                key %
                destination.Width;
            var y =
                key /
                destination.Width;

            if (destination.GetPixel(
                    x,
                    y) !=
                1)
            {
                beforeOutline++;
            }
        }

        var applied =
            CurveFillTrueRibbonRasterizer.TryApply(
                source,
                destination,
                model,
                fit,
                new HashSet<byte>
                {
                    1,
                },
                out var changed);

        Assert.True(
            applied);
        Assert.True(
            changed > 0);
        Assert.True(
            beforeFill > 0 ||
            beforeOutline > 0,
            "The nearest-neighbour target must contain staircase phase for this regression to be meaningful.");

        var correctedFill = 0;
        var correctedOutline = 0;

        foreach (var key in fillMask)
        {
            var x =
                key %
                destination.Width;
            var y =
                key /
                destination.Width;

            if (destination.GetPixel(
                    x,
                    y) ==
                2)
            {
                correctedFill++;
            }
        }

        foreach (var key in outerMask)
        {
            if (fillMask.Contains(
                    key))
            {
                continue;
            }

            var x =
                key %
                destination.Width;
            var y =
                key /
                destination.Width;

            if (destination.GetPixel(
                    x,
                    y) ==
                1)
            {
                correctedOutline++;
            }
        }

        Assert.True(
            correctedFill >
            fillMask.Count *
                0.90,
            $"Expected the fitted fill mask to become authoritative, got {correctedFill}/{fillMask.Count}.");
        Assert.True(
            correctedOutline >
            (outerMask.Count -
             fillMask.Count) *
                0.75,
            $"Expected the fitted outline ring to be materially rebuilt, got {correctedOutline}/{outerMask.Count - fillMask.Count}.");
    }

    [Fact]
    public void TargetExpandedPolygon_KeepsDedicatedOutlineInTargetPixelUnits()
    {
        var points =
            Enumerable.Range(
                    0,
                    81)
                .Select(index =>
                {
                    var t =
                        index /
                        80d;

                    return new ElegantArcPoint(
                        10d +
                        40d *
                            t,
                        20d +
                        6d *
                            Math.Sin(
                                Math.PI *
                                t),
                        3d);
                })
                .ToArray();

        var fill =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                points,
                scaleX: 2d,
                scaleY: 2d);
        var expanded =
            LeafPetalArcRasterizer.BuildTargetExpandedPolygon(
                points,
                scaleX: 2d,
                scaleY: 2d,
                additionalTargetPixels: 1d);

        Assert.Equal(
            fill.Count,
            expanded.Count);

        // At the middle of this nearly horizontal arch the left side is approximately the first
        // half of the polygon and the extra outline must be one TARGET pixel, not two pixels
        // merely because the design was enlarged 2x.
        var mid =
            points.Length /
            2;
        var dx =
            expanded[mid].X -
            fill[mid].X;
        var dy =
            expanded[mid].Y -
            fill[mid].Y;
        var distance =
            Math.Sqrt(
                dx *
                    dx +
                dy *
                    dy);

        Assert.InRange(
            distance,
            0.95,
            1.05);
    }

    [Fact]
    public void PixelCadence_IdenticalOrderedStepsScoreOne()
    {
        var path =
            new (int X, int Y)[]
            {
                (0, 0),
                (1, 0),
                (1, 1),
                (2, 1),
                (2, 2),
                (3, 2),
                (3, 3),
            };

        var score =
            CurvePixelCadence.Measure(
                path,
                path);

        Assert.Equal(
            1d,
            score,
            precision: 12);
    }

    [Fact]
    public void PixelCadence_PrefersPixelCordAlternationOverSameEndpointWrongRhythm()
    {
        var expected =
            new (int X, int Y)[]
            {
                (0, 0),
                (1, 0),
                (1, 1),
                (2, 1),
                (2, 2),
                (3, 2),
                (3, 3),
                (4, 3),
                (4, 4),
            };
        var faithful =
            new (int X, int Y)[]
            {
                (0, 0),
                (1, 0),
                (1, 1),
                (2, 1),
                (2, 2),
                (3, 2),
                (3, 3),
                (4, 3),
                (4, 4),
            };
        var wrongCadence =
            new (int X, int Y)[]
            {
                (0, 0),
                (1, 0),
                (2, 0),
                (3, 0),
                (4, 0),
                (4, 1),
                (4, 2),
                (4, 3),
                (4, 4),
            };

        var faithfulScore =
            CurvePixelCadence.Measure(
                faithful,
                expected);
        var wrongScore =
            CurvePixelCadence.Measure(
                wrongCadence,
                expected);

        Assert.True(
            faithfulScore >
            wrongScore +
                0.25,
            $"Expected ordered Pixel-Cord cadence to matter: faithful={faithfulScore:0.000}, wrong={wrongScore:0.000}.");
    }

    [Fact]
    public void VariableWidthProfileMapper_PreservesTaperWithoutRasterBreathing()
    {
        var samples =
            Enumerable.Range(
                    0,
                    81)
                .Select(index =>
                {
                    var t =
                        index /
                        80d;
                    var designerWidth =
                        2.0 +
                        4.8 *
                        t +
                        0.6 *
                        Math.Sin(
                            Math.PI *
                            t);
                    var rasterPhase =
                        index %
                        2 == 0
                            ? 0.32
                            : -0.32;

                    return new LeafPetalAxisSample(
                        8d +
                        70d *
                            t,
                        30d +
                        9d *
                            Math.Sin(
                                Math.PI *
                                t),
                        designerWidth +
                        rasterPhase,
                        t);
                })
                .ToArray();
        var model =
            Model(
                samples,
                width: 90,
                height: 55);
        var points =
            Enumerable.Range(
                    0,
                    241)
                .Select(index =>
                {
                    var t =
                        index /
                        240d;

                    return new ElegantArcPoint(
                        8d +
                        70d *
                            t,
                        30d +
                        9d *
                            Math.Sin(
                                Math.PI *
                                t),
                        4.0);
                })
                .ToArray();
        var fit =
            new ElegantArcFit(
                points,
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 0,
                MaximumCenterlineDeviation: 0.5);

        var mapped =
            CurveFillVariableWidthProfileMapper.Apply(
                model,
                fit,
                out var diagnostics);

        Assert.True(
            diagnostics.Applied);
        Assert.True(
            diagnostics.SourceCoefficientVariation >
            0.30);
        Assert.True(
            diagnostics.MappedCoefficientVariation >
            0.25,
            "A real designer taper must not collapse to the constant robust ribbon width.");
        Assert.True(
            mapped.Points[^1].HalfWidth -
            mapped.Points[0].HalfWidth >
            3.0,
            "The low-frequency source taper must survive target geometry mapping.");
        Assert.True(
            diagnostics.MaximumAdjacentVariation <
            0.12,
            $"Raster phase should be removed from the mapped profile, got adjacent variation {diagnostics.MaximumAdjacentVariation:0.000}.");
        Assert.InRange(
            diagnostics.MappedMinimum,
            1.7,
            2.6);
        Assert.InRange(
            diagnostics.MappedMaximum,
            6.0,
            7.0);
    }

    [Fact]
    public void SweptTubeRaster_TightHookHasNoEnclosedMaskHoles()
    {
        var points =
            new List<ElegantArcPoint>();

        // Dense U/hairpin with a radius close to the ribbon half-width: this is exactly where
        // paired normal-offset polygons can cross themselves.
        for (var index = 0;
             index <= 24;
             index++)
        {
            var t =
                index /
                24d;

            points.Add(
                new ElegantArcPoint(
                    8d +
                    12d *
                        t,
                    8d,
                    3.8));
        }

        for (var index = 1;
             index <= 24;
             index++)
        {
            var angle =
                -Math.PI /
                    2d +
                Math.PI *
                    index /
                    24d;

            points.Add(
                new ElegantArcPoint(
                    20d +
                    5d *
                        Math.Cos(
                            angle),
                    13d +
                    5d *
                        Math.Sin(
                            angle),
                    3.8));
        }

        for (var index = 1;
             index <= 24;
             index++)
        {
            var t =
                index /
                24d;

            points.Add(
                new ElegantArcPoint(
                    20d -
                    12d *
                        t,
                    18d,
                    3.8));
        }

        const int width = 80;
        const int height = 64;
        var mask =
            CurveFillTrueRibbonRasterizer.BuildTargetSweptTubeMask(
                points,
                scaleX: 2d,
                scaleY: 2d,
                width,
                height,
                additionalTargetPixels: 0d);

        Assert.NotEmpty(
            mask);

        var minX =
            mask.Min(key =>
                key %
                width);
        var maxX =
            mask.Max(key =>
                key %
                width);
        var minY =
            mask.Min(key =>
                key /
                width);
        var maxY =
            mask.Max(key =>
                key /
                width);
        var reachable =
            new HashSet<int>();
        var queue =
            new Queue<int>();

        void EnqueueBackground(
            int x,
            int y)
        {
            if (x < minX ||
                x > maxX ||
                y < minY ||
                y > maxY)
            {
                return;
            }

            var key =
                y *
                    width +
                x;

            if (mask.Contains(
                    key) ||
                !reachable.Add(
                    key))
            {
                return;
            }

            queue.Enqueue(
                key);
        }

        for (var x = minX;
             x <= maxX;
             x++)
        {
            EnqueueBackground(
                x,
                minY);
            EnqueueBackground(
                x,
                maxY);
        }

        for (var y = minY;
             y <= maxY;
             y++)
        {
            EnqueueBackground(
                minX,
                y);
            EnqueueBackground(
                maxX,
                y);
        }

        while (queue.Count > 0)
        {
            var key =
                queue.Dequeue();
            var x =
                key %
                width;
            var y =
                key /
                width;

            EnqueueBackground(
                x - 1,
                y);
            EnqueueBackground(
                x + 1,
                y);
            EnqueueBackground(
                x,
                y - 1);
            EnqueueBackground(
                x,
                y + 1);
        }

        var enclosedBackground = 0;

        for (var y = minY;
             y <= maxY;
             y++)
        {
            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                var key =
                    y *
                        width +
                    x;

                if (!mask.Contains(
                        key) &&
                    !reachable.Contains(
                        key))
                {
                    enclosedBackground++;
                }
            }
        }

        Assert.Equal(
            0,
            enclosedBackground);
    }

    [Fact]
    public void PiecewiseSCurveFitter_PreservesInflectionAndBuildsContinuousJoin()
    {
        var samples =
            Enumerable.Range(
                    0,
                    161)
                .Select(index =>
                {
                    var t =
                        index /
                        160d;
                    var u =
                        2d *
                            t -
                        1d;
                    var rasterPhase =
                        (index %
                         4) switch
                        {
                            0 => 0.14,
                            2 => -0.14,
                            _ => 0d,
                        };

                    return new LeafPetalAxisSample(
                        12d +
                        112d *
                            t,
                        54d +
                        24d *
                            u *
                            u *
                            u +
                        rasterPhase,
                        3.2 +
                        0.15 *
                            Math.Sin(
                                Math.PI *
                                t),
                        t);
                })
                .ToArray();
        var model =
            Model(
                samples,
                width: 140,
                height: 110);

        var safe =
            CurveFillRibbonPiecewiseSCurveFitter.TryFit(
                model,
                out var fit,
                out var diagnostics);

        Assert.True(
            safe,
            $"Expected a safe piecewise S fit: reason={diagnostics.Reason}, " +
            $"split={diagnostics.SplitIndex}, p95={diagnostics.Percentile95Deviation:0.000}, " +
            $"max={diagnostics.MaximumDeviation:0.000}, rough={diagnostics.Roughness:0.000}, " +
            $"flips={diagnostics.CurvatureSignFlips}, join={diagnostics.JoinAngleDegrees:0.000}.");
        Assert.True(
            fit.IsSafe);
        Assert.InRange(
            diagnostics.SplitIndex,
            55,
            105);
        Assert.InRange(
            diagnostics.CurvatureSignFlips,
            1,
            2);
        Assert.InRange(
            diagnostics.JoinAngleDegrees,
            0d,
            8d);
        Assert.InRange(
            diagnostics.Percentile95Deviation,
            0d,
            3.80);
        Assert.InRange(
            diagnostics.MaximumDeviation,
            0d,
            6.10);
    }

    [Fact]
    public void ConstrainedFairing_ReducesRasterPhaseWithoutLeavingSourceCorridor()
    {
        var samples =
            Enumerable.Range(
                    0,
                    181)
                .Select(index =>
                {
                    var t =
                        index /
                        180d;
                    var u =
                        2d *
                            t -
                        1d;

                    return new LeafPetalAxisSample(
                        10d +
                        126d *
                            t,
                        58d +
                        26d *
                            u *
                            u *
                            u,
                        3.0,
                        t);
                })
                .ToArray();
        var model =
            Model(
                samples,
                width: 150,
                height: 120);
        var noisy =
            samples
                .Select((sample, index) =>
                {
                    var phase =
                        (index %
                         4) switch
                        {
                            0 => 0.34,
                            2 => -0.34,
                            _ => 0d,
                        };

                    return new ElegantArcPoint(
                        sample.X,
                        sample.Y +
                        phase,
                        sample.HalfWidth);
                })
                .ToArray();
        var acceptedFit =
            new ElegantArcFit(
                noisy,
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 1,
                MaximumCenterlineDeviation: 0.34);

        var safe =
            CurveFillRibbonConstrainedFairing.TryFair(
                model,
                acceptedFit,
                out var faired,
                out var diagnostics);

        Assert.True(
            safe,
            $"Expected source-bounded fairing: reason={diagnostics.Reason}, " +
            $"rough={diagnostics.BeforeRoughness:0.000}->{diagnostics.AfterRoughness:0.000}, " +
            $"p95={diagnostics.Percentile95Deviation:0.000}, max={diagnostics.MaximumDeviation:0.000}, " +
            $"shift={diagnostics.MaximumShift:0.000}, flips={diagnostics.BeforeCurvatureSignFlips}->{diagnostics.AfterCurvatureSignFlips}.");
        Assert.True(
            faired.IsSafe);
        Assert.True(
            diagnostics.AfterRoughness <
            diagnostics.BeforeRoughness *
                0.90);
        Assert.InRange(
            diagnostics.MaximumShift,
            0d,
            0.90);
        Assert.InRange(
            diagnostics.AfterCurvatureSignFlips,
            1,
            2);
        Assert.InRange(
            diagnostics.Percentile95Deviation,
            0d,
            3.80);
        Assert.InRange(
            diagnostics.MaximumDeviation,
            0d,
            6.10);
    }

    [Fact]
    public void CurvatureFairness_IgnoresLegitimateSInflectionButPenalizesLobeJitter()
    {
        var clean =
            Enumerable.Range(
                    0,
                    241)
                .Select(index =>
                {
                    var t =
                        index /
                        240d;
                    var u =
                        2d *
                            t -
                        1d;

                    return new ElegantArcPoint(
                        10d +
                        140d *
                            t,
                        64d +
                        30d *
                            u *
                            u *
                            u,
                        3d);
                })
                .ToArray();
        var noisy =
            clean
                .Select((point, index) =>
                {
                    var phase =
                        (index %
                         6) switch
                        {
                            0 => 0.55,
                            3 => -0.55,
                            _ => 0d,
                        };

                    return point with
                    {
                        Y =
                            point.Y +
                            phase,
                    };
                })
                .ToArray();

        var cleanDiagnostics =
            CurveFillRibbonCurvatureFairness.Measure(
                clean);
        var noisyDiagnostics =
            CurveFillRibbonCurvatureFairness.Measure(
                noisy);

        Assert.InRange(
            cleanDiagnostics.InflectionCount,
            1,
            2);
        Assert.InRange(
            noisyDiagnostics.InflectionCount,
            1,
            4);
        Assert.True(
            noisyDiagnostics.Score >
            cleanDiagnostics.Score *
                1.35,
            $"Expected lobe jitter to worsen fairness materially: clean={cleanDiagnostics.Score:0.000000}, " +
            $"noisy={noisyDiagnostics.Score:0.000000}, inflections={cleanDiagnostics.InflectionCount}/{noisyDiagnostics.InflectionCount}.");
        Assert.True(
            noisyDiagnostics.MaximumVariation >
            cleanDiagnostics.MaximumVariation);
    }

    [Fact]
    public void LayeredRibbonAnalyzer_DetectsStableProtectedBandSequence()
    {
        var palette =
            new Palette(
                new[]
                {
                    new RugColor(0, 0, 255),
                    new RugColor(255, 255, 255),
                    new RugColor(230, 224, 218),
                    new RugColor(215, 208, 186),
                    new RugColor(0, 0, 102),
                    new RugColor(216, 185, 124),
                    new RugColor(44, 128, 166),
                });
        var source =
            new DesignDocument(
                84,
                64,
                palette);

        // Background = 2. Horizontal fill ribbon = 6. On its positive-normal side build
        // white(1) -> navy(4) -> white(1) -> background(2).
        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                source.SetPixel(
                    x,
                    y,
                    2);
            }
        }

        for (var x = 8;
             x <= 75;
             x++)
        {
            source.SetPixel(x, 29, 6);
            source.SetPixel(x, 30, 6);
            source.SetPixel(x, 31, 6);
            source.SetPixel(x, 32, 6);
            source.SetPixel(x, 33, 6);

            source.SetPixel(x, 34, 1);
            source.SetPixel(x, 35, 4);
            source.SetPixel(x, 36, 4);
            source.SetPixel(x, 37, 1);
        }

        var regionPixels =
            Enumerable.Range(
                    8,
                    68)
                .SelectMany(x =>
                    Enumerable.Range(
                            29,
                            5)
                        .Select(y =>
                            y *
                                source.Width +
                            x))
                .ToArray();
        var region =
            new LeafPetalRegion(
                6,
                regionPixels,
                regionPixels,
                8,
                29,
                75,
                33);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                41.5,
                31,
                1,
                0,
                0,
                1,
                67,
                4,
                12,
                0.15);
        var samples =
            Enumerable.Range(
                    0,
                    68)
                .Select(index =>
                    new LeafPetalAxisSample(
                        8d +
                        index,
                        31d,
                        2.25,
                        index /
                        67d))
                .ToArray();
        var model =
            new LeafPetalArcModel(
                candidate,
                samples,
                ReversedForApex: false,
                BaseWidth: 2.25,
                ApexWidth: 2.25,
                SkeletonCoverage: 1d);

        var diagnostics =
            CurveFillLayeredRibbonAnalyzer.Analyze(
                source,
                model,
                new HashSet<byte>
                {
                    1,
                });

        Assert.True(
            diagnostics.Detected,
            $"Expected stable layered ribbon, got side={diagnostics.Side}, " +
            $"seq={diagnostics.Sequence}, cov={diagnostics.Coverage:0.000}.");
        Assert.Equal(
            "positive",
            diagnostics.Side);
        Assert.StartsWith(
            "1>4>1>2",
            diagnostics.Sequence,
            StringComparison.Ordinal);
        Assert.True(
            diagnostics.Coverage >=
            0.90);
        Assert.True(
            diagnostics.BracketedCoverage >=
            0.90,
            $"Expected white/navy/white bracketing, got {diagnostics.BracketedCoverage:0.000}.");
        Assert.Equal(
            "2",
            diagnostics.ExteriorColor);
        Assert.True(
            diagnostics.ExteriorCoverage >=
            0.90,
            $"Expected source-proven background ownership after the outer white cord, got {diagnostics.ExteriorCoverage:0.000}.");
        Assert.True(
            diagnostics.MeanRunWidths.Count >=
            4);
    }

    [Fact]
    public void LayeredRibbonBandMask_UsesTargetCordWidthAndScaledBroadBand()
    {
        var points =
            Enumerable.Range(
                    0,
                    41)
                .Select(index =>
                    new ElegantArcPoint(
                        10d +
                        index,
                        20d,
                        2d))
                .ToArray();

        var whiteCord =
            CurveFillLayeredRibbonRasterizer.BuildOneSidedBandMask(
                points,
                side: 1d,
                scaleX: 2d,
                scaleY: 2d,
                targetWidth: 128,
                targetHeight: 96,
                innerAdditionalTargetPixels: 0d,
                sourceBandWidth: 0d,
                fixedBandWidthTargetPixels: 1d);
        var navyBand =
            CurveFillLayeredRibbonRasterizer.BuildOneSidedBandMask(
                points,
                side: 1d,
                scaleX: 2d,
                scaleY: 2d,
                targetWidth: 128,
                targetHeight: 96,
                innerAdditionalTargetPixels: 1d,
                sourceBandWidth: 3d);

        Assert.NotEmpty(
            whiteCord);
        Assert.NotEmpty(
            navyBand);

        var middleX = 80;
        var whiteYs =
            whiteCord
                .Where(key =>
                    key %
                        128 ==
                    middleX)
                .Select(key =>
                    key /
                    128)
                .Distinct()
                .Order()
                .ToArray();
        var navyYs =
            navyBand
                .Where(key =>
                    key %
                        128 ==
                    middleX)
                .Select(key =>
                    key /
                    128)
                .Distinct()
                .Order()
                .ToArray();

        Assert.InRange(
            whiteYs.Length,
            1,
            2);
        Assert.InRange(
            navyYs.Length,
            5,
            7);
        Assert.True(
            navyYs.Min() >=
            whiteYs.Max(),
            "The broad protected band must be outside the 1-target-pixel cord on the selected side.");
    }

    [Fact]
    public void LayeredRibbonPreview_OverlayOnlyWorksWithoutBracketedExterior()
    {
        var palette =
            new Palette(
                new[]
                {
                    new RugColor(0, 0, 0),
                    new RugColor(255, 255, 255),
                    new RugColor(230, 224, 218),
                    new RugColor(120, 120, 120),
                    new RugColor(0, 0, 102),
                });
        var source =
            new DesignDocument(
                64,
                48,
                palette);
        var destination =
            new DesignDocument(
                128,
                96,
                palette);

        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                source.SetPixel(
                    x,
                    y,
                    2);
            }
        }

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
                    2);
            }
        }

        var samples =
            Enumerable.Range(
                    0,
                    41)
                .Select(index =>
                    new LeafPetalAxisSample(
                        10d +
                        index,
                        20d,
                        2d,
                        index /
                        40d))
                .ToArray();
        var model =
            Model(
                samples,
                width: 64,
                height: 48);
        var fit =
            new ElegantArcFit(
                samples
                    .Select(sample =>
                        new ElegantArcPoint(
                            sample.X,
                            sample.Y,
                            sample.HalfWidth))
                    .ToArray(),
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 0,
                MaximumCenterlineDeviation: 0d);
        var profile =
            new LayeredRibbonProfileDiagnostics(
                Detected: true,
                Side: "positive",
                Sequence: "1>4",
                Coverage: 0.95,
                BracketedCoverage: 0d,
                ExteriorColor: string.Empty,
                ExteriorCoverage: 0d,
                SampleCount: 30,
                MatchingSamples: 29,
                MeanRunWidths:
                    new[]
                    {
                        1.2,
                        3.0,
                    });

        var applied =
            CurveFillLayeredRibbonRasterizer.TryApplySecondProtectedBandPreview(
                source,
                destination,
                model,
                fit,
                profile,
                new HashSet<byte>
                {
                    1,
                },
                out var changed);

        Assert.True(
            applied);
        Assert.True(
            changed > 0);
        Assert.Contains(
            Enumerable.Range(
                    0,
                    destination.Width *
                    destination.Height),
            key =>
            {
                var x =
                    key %
                    destination.Width;
                var y =
                    key /
                    destination.Width;

                return destination.GetPixel(
                           x,
                           y) ==
                       1;
            });
        Assert.Contains(
            Enumerable.Range(
                    0,
                    destination.Width *
                    destination.Height),
            key =>
            {
                var x =
                    key %
                    destination.Width;
                var y =
                    key /
                    destination.Width;

                return destination.GetPixel(
                           x,
                           y) ==
                       4;
            });
    }

    [Fact]
    public void LayeredRibbonPreview_BracketedStackRedrawsOuterCordAndCleansStaleBandToExterior()
    {
        var palette =
            new Palette(
                new[]
                {
                    new RugColor(0, 0, 0),
                    new RugColor(255, 255, 255),
                    new RugColor(230, 224, 218),
                    new RugColor(120, 120, 120),
                    new RugColor(0, 0, 102),
                    new RugColor(216, 185, 124),
                    new RugColor(44, 128, 166),
                });
        var source =
            new DesignDocument(
                64,
                48,
                palette);

        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                source.SetPixel(
                    x,
                    y,
                    2);
            }
        }

        var regionPixels =
            new List<int>();

        for (var x = 8;
             x <= 55;
             x++)
        {
            for (var y = 18;
                 y <= 22;
                 y++)
            {
                source.SetPixel(
                    x,
                    y,
                    6);
                regionPixels.Add(
                    y *
                        source.Width +
                    x);
            }

            source.SetPixel(x, 23, 1);
            source.SetPixel(x, 24, 4);
            source.SetPixel(x, 25, 4);
            source.SetPixel(x, 26, 4);
            source.SetPixel(x, 27, 1);
        }

        var region =
            new LeafPetalRegion(
                6,
                regionPixels.ToArray(),
                regionPixels.ToArray(),
                8,
                18,
                55,
                22);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                31.5,
                20,
                1,
                0,
                0,
                1,
                47,
                4,
                10,
                0.15);
        var samples =
            Enumerable.Range(
                    0,
                    48)
                .Select(index =>
                    new LeafPetalAxisSample(
                        8d +
                        index,
                        20d,
                        2.25,
                        index /
                        47d))
                .ToArray();
        var model =
            new LeafPetalArcModel(
                candidate,
                samples,
                ReversedForApex: false,
                BaseWidth: 2.25,
                ApexWidth: 2.25,
                SkeletonCoverage: 1d);
        var fit =
            new ElegantArcFit(
                samples
                    .Select(sample =>
                        new ElegantArcPoint(
                            sample.X,
                            sample.Y -
                            0.55,
                            sample.HalfWidth))
                    .ToArray(),
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 0,
                MaximumCenterlineDeviation: 0.55);
        var destination =
            DesignResizer.Scale(
                source,
                128,
                96,
                ScaleMode.NearestNeighbor);
        var before =
            Enumerable.Range(
                    0,
                    destination.Width *
                    destination.Height)
                .Select(key =>
                {
                    var x =
                        key %
                        destination.Width;
                    var y =
                        key /
                        destination.Width;

                    return destination.GetPixel(
                        x,
                        y);
                })
                .ToArray();
        var profile =
            new LayeredRibbonProfileDiagnostics(
                Detected: true,
                Side: "positive",
                Sequence: "1>4>1>2",
                Coverage: 0.92,
                BracketedCoverage: 0.96,
                ExteriorColor: string.Empty,
                ExteriorCoverage: 0d,
                SampleCount: 40,
                MatchingSamples: 38,
                MeanRunWidths:
                    new[]
                    {
                        1.0,
                        3.0,
                        1.0,
                        8.0,
                    });

        var applied =
            CurveFillLayeredRibbonRasterizer.TryApplySecondProtectedBandPreview(
                source,
                destination,
                model,
                fit,
                profile,
                new HashSet<byte>
                {
                    1,
                },
                out var changed);

        Assert.True(
            applied);
        Assert.True(
            changed > 0);

        const double Scale = 2d;
        var innerSeparator =
            CurveFillLayeredRibbonRasterizer.BuildOneSidedBandMask(
                fit.Points,
                side: 1d,
                scaleX: Scale,
                scaleY: Scale,
                targetWidth: destination.Width,
                targetHeight: destination.Height,
                innerAdditionalTargetPixels: 0d,
                sourceBandWidth: 0d,
                fixedBandWidthTargetPixels: 1d);
        var band =
            CurveFillLayeredRibbonRasterizer.BuildOneSidedBandMask(
                fit.Points,
                side: 1d,
                scaleX: Scale,
                scaleY: Scale,
                targetWidth: destination.Width,
                targetHeight: destination.Height,
                innerAdditionalTargetPixels: 1d,
                sourceBandWidth: 3d);
        var outerSeparator =
            CurveFillLayeredRibbonRasterizer.BuildOneSidedBandMask(
                fit.Points,
                side: 1d,
                scaleX: Scale,
                scaleY: Scale,
                targetWidth: destination.Width,
                targetHeight: destination.Height,
                innerAdditionalTargetPixels: 1d,
                sourceBandWidth: 0d,
                fixedBandWidthTargetPixels: 1d,
                innerAdditionalSourceWidth: 3d);
        var authority =
            CurveFillLayeredRibbonRasterizer.BuildOneSidedBandMask(
                fit.Points,
                side: 1d,
                scaleX: Scale,
                scaleY: Scale,
                targetWidth: destination.Width,
                targetHeight: destination.Height,
                innerAdditionalTargetPixels: 0d,
                sourceBandWidth: 6d);

        Assert.NotEmpty(
            outerSeparator);
        Assert.True(
            outerSeparator.Count(key =>
            {
                var x =
                    key %
                    destination.Width;
                var y =
                    key /
                    destination.Width;

                return destination.GetPixel(
                           x,
                           y) ==
                       1;
            }) >
            outerSeparator.Count *
                0.75,
            "The outer Pixel-Cord separator must be redrawn around the broad band.");

        var staleCleaned =
            authority.Count(key =>
            {
                if (innerSeparator.Contains(key) ||
                    band.Contains(key) ||
                    outerSeparator.Contains(key) ||
                    before[key] != 4)
                {
                    return false;
                }

                var x =
                    key %
                    destination.Width;
                var y =
                    key /
                    destination.Width;

                return destination.GetPixel(
                           x,
                           y) ==
                       2;
            });

        Assert.True(
            staleCleaned > 0,
            "Local source-normal bracketing should restore at least one stale navy pixel even when the profile has no global exterior colour.");
    }

    [Fact]
    public void LayeredRibbonCleanup_ReachFollowsMeasuredCentrelineShiftAndStaysInsideOwnSourceStack()
    {
        // Source stack on the positive side of a straight cyan ribbon:
        //   fill(6) y=18..22 | white(1) y=23 | navy(4) y=24..26 | white(1) y=27 | background(2)
        // plus a FOREIGN white outline at y=32 that belongs to another motif.
        static (DesignDocument Source, LeafPetalArcModel Model) BuildSource()
        {
            var palette =
                new Palette(
                    new[]
                    {
                        new RugColor(0, 0, 0),
                        new RugColor(255, 255, 255),
                        new RugColor(230, 224, 218),
                        new RugColor(120, 120, 120),
                        new RugColor(0, 0, 102),
                        new RugColor(216, 185, 124),
                        new RugColor(44, 128, 166),
                    });
            var source =
                new DesignDocument(
                    64,
                    48,
                    palette);

            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, 2);

            var regionPixels =
                new List<int>();

            for (var x = 8; x <= 55; x++)
            {
                for (var y = 18; y <= 22; y++)
                {
                    source.SetPixel(x, y, 6);
                    regionPixels.Add(
                        y *
                            source.Width +
                        x);
                }

                source.SetPixel(x, 23, 1);
                source.SetPixel(x, 24, 4);
                source.SetPixel(x, 25, 4);
                source.SetPixel(x, 26, 4);
                source.SetPixel(x, 27, 1);
                source.SetPixel(x, 32, 1);
            }

            var region =
                new LeafPetalRegion(
                    6,
                    regionPixels.ToArray(),
                    regionPixels.ToArray(),
                    8,
                    18,
                    55,
                    22);
            var candidate =
                new LeafPetalArcCandidate(
                    region,
                    31.5,
                    20,
                    1,
                    0,
                    0,
                    1,
                    47,
                    4,
                    10,
                    0.15);
            var samples =
                Enumerable.Range(
                        0,
                        48)
                    .Select(index =>
                        new LeafPetalAxisSample(
                            8d +
                            index,
                            20d,
                            2.25,
                            index /
                            47d))
                    .ToArray();

            return (
                source,
                new LeafPetalArcModel(
                    candidate,
                    samples,
                    ReversedForApex: false,
                    BaseWidth: 2.25,
                    ApexWidth: 2.25,
                    SkeletonCoverage: 1d));
        }

        static ElegantArcFit ShiftedFit(
            LeafPetalArcModel model,
            double shift) =>
            new(
                model.Samples
                    .Select(sample =>
                        new ElegantArcPoint(
                            sample.X,
                            sample.Y +
                            shift,
                            sample.HalfWidth))
                    .ToArray(),
                IsSafe: true,
                IsMonotonic: true,
                CurvatureSignFlips: 0,
                MaximumCenterlineDeviation:
                    Math.Abs(
                        shift));

        var profile =
            new LayeredRibbonProfileDiagnostics(
                Detected: true,
                Side: "positive",
                Sequence: "1>4>1>2",
                Coverage: 0.92,
                BracketedCoverage: 0.96,
                ExteriorColor: "2",
                ExteriorCoverage: 0.92,
                SampleCount: 40,
                MatchingSamples: 38,
                MeanRunWidths:
                    new[]
                    {
                        1.0,
                        3.0,
                        1.0,
                        4.0,
                    });
        var protectedCord =
            new HashSet<byte>
            {
                1,
            };

        // Scenario A: the accepted centreline moved 3 source px AWAY from the stack. The old
        // navy/white rows (source y=26..27, target y=52..55) lie beyond the fixed reach but inside
        // the measured-shift reach and must be restored to the source-proven exterior.
        {
            var (source, model) =
                BuildSource();
            var destination =
                DesignResizer.Scale(
                    source,
                    128,
                    96,
                    ScaleMode.NearestNeighbor);

            Assert.True(
                CurveFillLayeredRibbonRasterizer.TryApplySecondProtectedBandPreview(
                    source,
                    destination,
                    model,
                    ShiftedFit(model, -3d),
                    profile,
                    protectedCord,
                    out _,
                    out var diagnostics));
            Assert.True(
                diagnostics.StaleCleanedPixels > 0);

            var staleRemaining =
                Enumerable.Range(
                        24,
                        80)
                    .Sum(x =>
                        Enumerable.Range(
                                52,
                                4)
                            .Count(y =>
                                destination.GetPixel(x, y) is 4 or 1));

            Assert.True(
                staleRemaining == 0,
                $"Stale old-stack pixels beyond the fixed reach must be cleaned when the fit measured a 3 px shift; {staleRemaining} remain.");
        }

        // Scenario B: the centreline moved 3 source px TOWARDS the stack, so target cleanup
        // authority now reaches the foreign white outline at source y=32 (target y=64..65). That
        // outline is 12 source px from THIS ribbon's source centreline, outside its own stack, and
        // must survive untouched.
        {
            var (source, model) =
                BuildSource();
            var destination =
                DesignResizer.Scale(
                    source,
                    128,
                    96,
                    ScaleMode.NearestNeighbor);

            Assert.True(
                CurveFillLayeredRibbonRasterizer.TryApplySecondProtectedBandPreview(
                    source,
                    destination,
                    model,
                    ShiftedFit(model, 3d),
                    profile,
                    protectedCord,
                    out _,
                    out _));

            // Nothing of this ribbon's own old stack is left beyond the new masks here, so the
            // only candidates inside the extended authority are the foreign outline pixels.
            var foreignSurvivors =
                Enumerable.Range(
                        24,
                        80)
                    .Sum(x =>
                        Enumerable.Range(
                                64,
                                2)
                            .Count(y =>
                                destination.GetPixel(x, y) == 1));

            Assert.Equal(
                160,
                foreignSurvivors);
        }
    }

    private static LeafPetalArcModel Model(
        IReadOnlyList<LeafPetalAxisSample> samples,
        int width,
        int height)
    {
        var region =
            new LeafPetalRegion(
                2,
                Array.Empty<int>(),
                Array.Empty<int>(),
                0,
                0,
                width - 1,
                height - 1);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                width *
                    0.5,
                height *
                    0.5,
                1d,
                0d,
                0d,
                1d,
                width,
                height,
                2.0,
                0.25);

        return new LeafPetalArcModel(
            candidate,
            samples,
            ReversedForApex: false,
            BaseWidth: samples[0].HalfWidth,
            ApexWidth: samples[^1].HalfWidth,
            SkeletonCoverage: 1d);
    }
}

public sealed class ProductionLayeredRibbonGateTests
{
    private static LeafPetalArcModel BuildLongSModel(
        int majorExtent = 246,
        double elongation = 4.75)
    {
        // 1994 fill pixels inside a 51 x 246 bounding box: bounding fill 0.159, matching the
        // real C069 long S-sweep statistics without embedding any C069 raster/coordinates.
        var pixels =
            Enumerable.Range(
                    0,
                    1994)
                .ToArray();
        var region =
            new LeafPetalRegion(
                6,
                pixels,
                pixels,
                0,
                0,
                50,
                majorExtent - 1);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                25,
                majorExtent / 2d,
                0,
                1,
                1,
                0,
                majorExtent,
                majorExtent / elongation,
                elongation,
                0.377);
        var samples =
            Enumerable.Range(
                    0,
                    40)
                .Select(index =>
                    new LeafPetalAxisSample(
                        25d,
                        index *
                        (majorExtent - 1) /
                        39d,
                        1.1,
                        index /
                        39d))
                .ToArray();

        return new LeafPetalArcModel(
            candidate,
            samples,
            ReversedForApex: false,
            BaseWidth: 2.2,
            ApexWidth: 2.2,
            SkeletonCoverage: 0.87);
    }

    private static ElegantArcFit BuildFit(
        LeafPetalArcModel model,
        int flips = 2,
        double deviation = 3.19,
        bool safe = true) =>
        new(
            model.Samples
                .Select(sample =>
                    new ElegantArcPoint(
                        sample.X,
                        sample.Y,
                        sample.HalfWidth))
                .ToArray(),
            IsSafe: safe,
            IsMonotonic: false,
            CurvatureSignFlips: flips,
            MaximumCenterlineDeviation: deviation);

    private static LayeredRibbonProfileDiagnostics BuildProfile(
        double bracketed = 0.885,
        string sequence = "1>4>1>2",
        double bandWidth = 4.07) =>
        new(
            Detected: true,
            Side: "negative",
            Sequence: sequence,
            Coverage: 0.59,
            BracketedCoverage: bracketed,
            ExteriorColor: "2",
            ExteriorCoverage: 0.59,
            SampleCount: 191,
            MatchingSamples: 113,
            MeanRunWidths:
                new[]
                {
                    1.28,
                    bandWidth,
                    1.30,
                    10.8,
                });

    private static readonly IReadOnlySet<byte> ProtectedCord =
        new HashSet<byte>
        {
            1,
        };

    [Fact]
    public void ProductionLayeredGate_AcceptsProvenBracketedTwoInflectionSweep()
    {
        var model =
            BuildLongSModel();

        Assert.True(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                BuildFit(model),
                BuildProfile(),
                ProtectedCord,
                redrawScale: 1.6));
    }

    [Fact]
    public void ProductionLayeredGate_RejectsWeakOrForeignEvidence()
    {
        var model =
            BuildLongSModel();
        var fit =
            BuildFit(model);
        var profile =
            BuildProfile();

        // Not an S: a single-bend or straight sweep is not the proven production class.
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                BuildFit(model, flips: 1),
                profile,
                ProtectedCord,
                1.6));

        // Unsafe or too far from the immutable source centreline.
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                BuildFit(model, safe: false),
                profile,
                ProtectedCord,
                1.6));
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                BuildFit(model, deviation: 3.5),
                profile,
                ProtectedCord,
                1.6));

        // Moderate enlargement keeps the conservative path.
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                fit,
                profile,
                ProtectedCord,
                1.25));

        // Bracket evidence below the production threshold.
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                fit,
                BuildProfile(bracketed: 0.70),
                ProtectedCord,
                1.6));

        // Separator must be a protected Pixel-Cord/outline role, and the bracket must close.
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                fit,
                profile,
                new HashSet<byte>(),
                1.6));
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                fit,
                BuildProfile(sequence: "1>4>2>3"),
                ProtectedCord,
                1.6));

        // Broad middle band outside the proven source width class.
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                model,
                fit,
                BuildProfile(bandWidth: 9.0),
                ProtectedCord,
                1.6));

        // Short or compact regions are not the long-S class.
        var shortModel =
            BuildLongSModel(
                majorExtent: 100);
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                shortModel,
                BuildFit(shortModel),
                profile,
                ProtectedCord,
                1.6));
        var compactModel =
            BuildLongSModel(
                elongation: 2.5);
        Assert.False(
            CurveFillRibbonArcRefiner.LooksLikeProductionLayeredRibbon(
                compactModel,
                BuildFit(compactModel),
                profile,
                ProtectedCord,
                1.6));
    }
}
