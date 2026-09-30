using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonSymmetryRecoveryTests
{
    [Fact]
    public void Recover_ExactMirrorRegion_ReCentersAsymmetricPrincipalSpine()
    {
        const int SourceWidth = 32;
        var region =
            BuildRegion(
                SourceWidth,
                asymmetric: false);
        var model =
            BuildModel(
                region);

        var recovered =
            CurveFillRibbonSymmetryRecovery.TryRecover(
                model,
                SourceWidth,
                out var symmetric,
                out var diagnostics);

        Assert.True(
            recovered,
            $"Exact source symmetry should recover the visual spine: reason={diagnostics.Reason}, " +
            $"axis={diagnostics.Axis}, match={diagnostics.MirrorAgreement:0.000}, " +
            $"endpoint={diagnostics.EndpointMirrorError:0.000}, " +
            $"shift={diagnostics.MeanSampleShift:0.000}/{diagnostics.MaximumSampleShift:0.000}.");
        Assert.Equal(
            "LR",
            diagnostics.Axis);
        Assert.Equal(
            1d,
            diagnostics.MirrorAgreement,
            6);
        Assert.InRange(
            diagnostics.MaximumSampleShift,
            0.01,
            4.00);

        var samples =
            symmetric.Samples;

        for (var index = 0;
             index < samples.Count;
             index++)
        {
            var opposite =
                samples[
                    samples.Count -
                    1 -
                    index];

            Assert.InRange(
                Math.Abs(
                    samples[index].X +
                    opposite.X -
                    (region.MinX +
                     region.MaxX)),
                0d,
                1e-9);
            Assert.InRange(
                Math.Abs(
                    samples[index].Y -
                    opposite.Y),
                0d,
                1e-9);
            Assert.InRange(
                Math.Abs(
                    samples[index].HalfWidth -
                    opposite.HalfWidth),
                0d,
                1e-9);
        }
    }

    [Fact]
    public void Recover_AsymmetricSourceRegion_DoesNotInventMirrorGeometry()
    {
        const int SourceWidth = 32;
        var region =
            BuildRegion(
                SourceWidth,
                asymmetric: true);
        var model =
            BuildModel(
                region);

        var recovered =
            CurveFillRibbonSymmetryRecovery.TryRecover(
                model,
                SourceWidth,
                out var result,
                out var diagnostics);

        Assert.False(
            recovered);
        Assert.Same(
            model,
            result);
        Assert.Contains(
            diagnostics.Reason,
            new[]
            {
                "mirror-agreement",
                "endpoint-mirror",
                "sample-shift",
            });
        Assert.NotEqual(
            "ok",
            diagnostics.Reason);
    }

    private static LeafPetalRegion BuildRegion(
        int sourceWidth,
        bool asymmetric)
    {
        const int MinX = 4;
        const int MaxX = 27;
        const int MinY = 4;
        const int MaxY = 15;

        var pixels =
            new List<int>();
        var boundary =
            new List<int>();

        for (var y = MinY;
             y <= MaxY;
             y++)
        {
            for (var x = MinX;
                 x <= MaxX;
                 x++)
            {
                if (asymmetric &&
                    x is >= 24 and <= 26 &&
                    y is >= 6 and <= 12)
                {
                    continue;
                }

                var key =
                    y *
                    sourceWidth +
                    x;
                pixels.Add(
                    key);

                if (x == MinX ||
                    x == MaxX ||
                    y == MinY ||
                    y == MaxY)
                {
                    boundary.Add(
                        key);
                }
            }
        }

        return new LeafPetalRegion(
            Color: 1,
            Pixels: pixels,
            BoundaryPixels: boundary,
            MinX: MinX,
            MinY: MinY,
            MaxX: MaxX,
            MaxY: MaxY);
    }

    private static LeafPetalArcModel BuildModel(
        LeafPetalRegion region)
    {
        var candidate =
            new LeafPetalArcCandidate(
                region,
                CenterX: 15.5,
                CenterY: 9.5,
                AxisX: 1d,
                AxisY: 0d,
                NormalX: 0d,
                NormalY: 1d,
                MajorExtent: 24d,
                MinorExtent: 12d,
                Elongation: 2d,
                BoundaryRatio: 0.25);

        LeafPetalAxisSample[] samples =
        [
            new(6.0, 12.0, 2.5, 0.00),
            new(8.0, 10.4, 2.6, 0.11),
            new(10.0, 8.3, 2.4, 0.22),
            new(12.0, 6.6, 2.7, 0.33),
            new(14.0, 5.4, 2.5, 0.44),
            new(17.0, 6.1, 2.8, 0.56),
            new(19.0, 7.3, 2.4, 0.67),
            new(21.0, 9.5, 2.7, 0.78),
            new(23.0, 11.0, 2.5, 0.89),
            new(25.0, 12.0, 2.5, 1.00),
        ];

        return new LeafPetalArcModel(
            candidate,
            samples,
            ReversedForApex: false,
            BaseWidth: 2.5,
            ApexWidth: 2.5,
            SkeletonCoverage: 0.80);
    }
}
