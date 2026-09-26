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
