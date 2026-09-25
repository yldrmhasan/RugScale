using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonCompoundFitterTests
{
    [Fact]
    public void Fit_MirrorFusedHookedRibbon_RemovesRasterWobbleWithoutFlatteningCompoundCurve()
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

                    // Long designer sweep plus a second curl. Small deterministic offsets model
                    // residual thick-raster skeleton phase after left/right source fusion.
                    var x =
                        10d +
                        56d *
                            t +
                        7.0 *
                            Math.Sin(
                                Math.PI *
                                t);
                    var y =
                        78d -
                        60d *
                            t +
                        10.5 *
                            Math.Sin(
                                Math.PI *
                                2d *
                                t);
                    var phase =
                        index %
                        4;
                    var wobbleX =
                        phase == 0
                            ? 0.28
                            : phase == 2
                                ? -0.24
                                : 0d;
                    var wobbleY =
                        phase == 1
                            ? 0.30
                            : phase == 3
                                ? -0.26
                                : 0d;

                    return new LeafPetalAxisSample(
                        x +
                        wobbleX,
                        y +
                        wobbleY,
                        3.0 +
                        0.18 *
                            Math.Sin(
                                Math.PI *
                                t),
                        t);
                })
                .ToArray();

        var region =
            new LeafPetalRegion(
                4,
                Array.Empty<int>(),
                Array.Empty<int>(),
                0,
                0,
                80,
                90);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                40d,
                45d,
                1d,
                0d,
                0d,
                1d,
                80d,
                12d,
                3d,
                0.25);
        var model =
            new LeafPetalArcModel(
                candidate,
                samples,
                ReversedForApex: false,
                BaseWidth: samples[0].HalfWidth,
                ApexWidth: samples[^1].HalfWidth,
                SkeletonCoverage: 1d);

        var accepted =
            CurveFillRibbonCompoundFitter.TryFit(
                model,
                out var fit,
                out var diagnostics);

        Assert.True(
            accepted,
            $"Hooked ribbon should have a safe low-frequency compound fit: " +
            $"reason={diagnostics.Reason}, p95={diagnostics.Percentile95Deviation:0.000}, " +
            $"max={diagnostics.MaximumDeviation:0.000}, flips={diagnostics.CurvatureSignFlips}.");
        Assert.True(
            fit.IsSafe);
        Assert.InRange(
            fit.CurvatureSignFlips,
            0,
            2);
        Assert.InRange(
            diagnostics.Percentile95Deviation,
            0d,
            3.80);
        Assert.InRange(
            diagnostics.MaximumDeviation,
            0d,
            6.10);
        Assert.True(
            fit.Points.Count >=
            20);
    }
}
