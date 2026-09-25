using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonMainArcExtractorTests
{
    [Fact]
    public void Extract_MirrorFusedHookedRibbon_IsolatesDominantSingleCurvatureSweep()
    {
        const int count = 101;
        var samples =
            Enumerable.Range(
                    0,
                    count)
                .Select(index =>
                {
                    var t =
                        index /
                        (double)(count - 1);

                    // Central designer sweep: one broad arch.
                    var x =
                        12d +
                        76d *
                            t;
                    var y =
                        58d -
                        24d *
                            Math.Sin(
                                Math.PI *
                                t);

                    // Symmetric terminal hooks deliberately introduce opposite local curvature
                    // near both ends. These should stay on the categorical baseline while the
                    // long central arch is extracted for specialist Curve redraw.
                    if (t < 0.18)
                    {
                        var u =
                            1d -
                            t /
                            0.18;
                        x +=
                            5.5 *
                            Math.Sin(
                                Math.PI *
                                u);
                        y +=
                            8.0 *
                            Math.Sin(
                                Math.PI *
                                u) *
                            u;
                    }
                    else if (t > 0.82)
                    {
                        var u =
                            (t -
                             0.82) /
                            0.18;
                        x -=
                            5.5 *
                            Math.Sin(
                                Math.PI *
                                u);
                        y +=
                            8.0 *
                            Math.Sin(
                                Math.PI *
                                u) *
                            u;
                    }

                    return new LeafPetalAxisSample(
                        x,
                        y,
                        3.0,
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
                100,
                80);
        var candidate =
            new LeafPetalArcCandidate(
                region,
                50d,
                40d,
                1d,
                0d,
                0d,
                1d,
                90d,
                18d,
                3d,
                0.20);
        var model =
            new LeafPetalArcModel(
                candidate,
                samples,
                ReversedForApex: false,
                BaseWidth: 3d,
                ApexWidth: 3d,
                SkeletonCoverage: 0.78);

        var extracted =
            CurveFillRibbonMainArcExtractor.TryExtract(
                model,
                out var mainArc,
                out var diagnostics);

        Assert.True(
            extracted,
            $"Dominant hooked-ribbon sweep was not extracted: " +
            $"reason={diagnostics.Reason}, start={diagnostics.StartIndex}, " +
            $"end={diagnostics.EndIndex}, kept={diagnostics.KeptFraction:0.000}.");
        Assert.InRange(
            diagnostics.KeptFraction,
            0.42,
            0.82);
        Assert.True(
            mainArc.Samples.Count <
            model.Samples.Count);
        Assert.Equal(
            mainArc.Samples.Count,
            diagnostics.EndIndex -
            diagnostics.StartIndex +
            1);
        Assert.InRange(
            diagnostics.StartIndex,
            8,
            35);
        Assert.InRange(
            diagnostics.EndIndex,
            65,
            92);
    }
}
