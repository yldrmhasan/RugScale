using RugScale.Core.Drawing;
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
