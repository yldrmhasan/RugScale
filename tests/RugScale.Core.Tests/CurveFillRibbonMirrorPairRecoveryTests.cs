using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonMirrorPairRecoveryTests
{
    [Fact]
    public void Recover_NearMirrorPair_FusesRasterPhaseBeforeCurveFit()
    {
        const int Width = 80;
        const int Height = 92;
        const int MirrorConstant = Width - 1;

        var palette =
            new Palette(
                new[]
                {
                    new RugColor(220, 214, 192),
                    new RugColor(132, 158, 121),
                });
        var source =
            new DesignDocument(
                Width,
                Height,
                palette);
        var controls =
            new (int X, int Y)[]
            {
                (9, 75),
                (10, 52),
                (17, 30),
                (27, 20),
                (31, 8),
            };
        var leftPixels =
            Rasterizer.Dilate(
                    CurveRasterizer.Draw(
                        controls,
                        CurveType.SplineThroughPoints,
                        0.80),
                    5,
                    5)
                .Where(point =>
                    point.X >= 0 &&
                    point.X < Width &&
                    point.Y >= 0 &&
                    point.Y < Height)
                .Distinct()
                .ToArray();

        foreach (var point in leftPixels)
        {
            source.SetPixel(
                point.X,
                point.Y,
                1);

            source.SetPixel(
                MirrorConstant -
                    point.X,
                point.Y,
                1);
        }

        var regions =
            LeafPetalRegionExtractor.Extract(
                    source)
                .Where(region =>
                    region.Color == 1 &&
                    region.Area >= 30)
                .OrderBy(region =>
                    region.MinX)
                .ToArray();

        Assert.Equal(
            2,
            regions.Length);

        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                regions[0],
                Width,
                out var leftCandidate));
        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                regions[1],
                Width,
                out var rightCandidate));
        Assert.True(
            CurveFillRibbonCenterlineBuilder.TryBuild(
                leftCandidate,
                Width,
                out var leftModel,
                out _));
        Assert.True(
            CurveFillRibbonCenterlineBuilder.TryBuild(
                rightCandidate,
                Width,
                out var rightModel,
                out _));

        var noisySamples =
            leftModel.Samples
                .Select((sample, index) =>
                {
                    if (index == 0 ||
                        index ==
                        leftModel.Samples.Count - 1)
                    {
                        return sample;
                    }

                    // Model the sort of phase wobble created by thick raster thinning. The
                    // underlying source regions remain exact mirrors; only one recovered spine is
                    // perturbed.
                    var phase =
                        index %
                        4;

                    return sample with
                    {
                        X =
                            sample.X +
                            (phase == 0
                                ? 1.2
                                : phase == 2
                                    ? -0.8
                                    : 0.35),
                        Y =
                            sample.Y +
                            (phase == 1
                                ? 0.9
                                : phase == 3
                                    ? -0.7
                                    : 0.20),
                    };
                })
                .ToArray();
        var noisyModel =
            leftModel with
            {
                Samples =
                    noisySamples,
            };
        var before =
            MeanMirrorDistance(
                noisyModel.Samples,
                rightModel.Samples,
                MirrorConstant);

        var recovered =
            CurveFillRibbonMirrorPairRecovery.TryRecover(
                noisyModel,
                regions,
                Width,
                out var fused,
                out var diagnostics);
        var after =
            MeanMirrorDistance(
                fused.Samples,
                rightModel.Samples,
                MirrorConstant);

        Assert.True(
            recovered,
            $"Near-mirror pair should fuse before fitting: reason={diagnostics.Reason}, " +
            $"agreement={diagnostics.MirrorAgreement:0.000}, " +
            $"shift={diagnostics.MeanFusionShift:0.000}/{diagnostics.MaximumFusionShift:0.000}.");
        Assert.InRange(
            diagnostics.MirrorAgreement,
            0.99,
            1.0);
        Assert.True(
            diagnostics.MeanFusionShift > 0d);
        Assert.True(
            after <
            before *
            0.75,
            $"Source-pair fusion should suppress medial-path phase noise: before={before:0.000}, after={after:0.000}.");
    }

    [Fact]
    public void Recover_ExactMirrorPair_WithUnevenSkeletonSampling_UsesArcLengthAlignment()
    {
        const int Width = 96;
        const int Height = 104;
        const int MirrorConstant = Width - 1;

        var palette =
            new Palette(
                new[]
                {
                    new RugColor(220, 214, 192),
                    new RugColor(0, 0, 102),
                });
        var source =
            new DesignDocument(
                Width,
                Height,
                palette);
        var controls =
            new (int X, int Y)[]
            {
                (10, 92),
                (12, 64),
                (20, 35),
                (34, 18),
                (39, 7),
            };

        foreach (var point in Rasterizer.Dilate(
                     CurveRasterizer.Draw(
                         controls,
                         CurveType.SplineThroughPoints,
                         0.72),
                     6,
                     6))
        {
            if (point.X < 0 ||
                point.X >= Width ||
                point.Y < 0 ||
                point.Y >= Height)
            {
                continue;
            }

            source.SetPixel(
                point.X,
                point.Y,
                1);
            source.SetPixel(
                MirrorConstant -
                    point.X,
                point.Y,
                1);
        }

        var regions =
            LeafPetalRegionExtractor.Extract(
                    source)
                .Where(region =>
                    region.Color == 1 &&
                    region.Area >= 30)
                .OrderBy(region =>
                    region.MinX)
                .ToArray();

        Assert.Equal(
            2,
            regions.Length);
        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                regions[0],
                Width,
                out var leftCandidate));
        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                regions[1],
                Width,
                out var rightCandidate));
        Assert.True(
            CurveFillRibbonCenterlineBuilder.TryBuild(
                leftCandidate,
                Width,
                out var leftModel,
                out _));
        Assert.True(
            CurveFillRibbonCenterlineBuilder.TryBuild(
                rightCandidate,
                Width,
                out var rightModel,
                out _));

        // Keep exactly the same left-hand polyline geometry, but make the upper half much more
        // densely sampled than the lower half. Matching samples by list index now pairs different
        // physical locations; normalized arc length should remain invariant.
        var uneven =
            new List<LeafPetalAxisSample>();

        for (var index = 0;
             index < leftModel.Samples.Count - 1;
             index++)
        {
            var a =
                leftModel.Samples[index];
            var b =
                leftModel.Samples[index + 1];
            var subdivisions =
                index <
                leftModel.Samples.Count /
                2
                    ? 5
                    : 1;

            for (var step = 0;
                 step < subdivisions;
                 step++)
            {
                var t =
                    step /
                    (double)subdivisions;

                uneven.Add(
                    new LeafPetalAxisSample(
                        a.X +
                        (b.X -
                         a.X) *
                        t,
                        a.Y +
                        (b.Y -
                         a.Y) *
                        t,
                        a.HalfWidth +
                        (b.HalfWidth -
                         a.HalfWidth) *
                        t,
                        0d));
            }
        }

        uneven.Add(
            leftModel.Samples[^1]);

        var unevenModel =
            leftModel with
            {
                Samples =
                    uneven,
            };

        var recovered =
            CurveFillRibbonMirrorPairRecovery.TryRecover(
                unevenModel,
                regions,
                Width,
                out var fused,
                out var diagnostics);

        Assert.True(
            recovered,
            $"Uneven skeleton sampling should not break exact mirror fusion: " +
            $"reason={diagnostics.Reason}, agreement={diagnostics.MirrorAgreement:0.000}, " +
            $"shift={diagnostics.MeanFusionShift:0.000}/{diagnostics.MaximumFusionShift:0.000}.");
        Assert.InRange(
            diagnostics.MirrorAgreement,
            0.99,
            1.0);
        Assert.InRange(
            diagnostics.MeanFusionShift,
            0d,
            1.25);
        Assert.InRange(
            diagnostics.MaximumFusionShift,
            0d,
            2.50);
        Assert.True(
            fused.Samples.Count >=
            rightModel.Samples.Count);
    }

    [Fact]
    public void Recover_UnrelatedOppositeRegion_DoesNotInventSharedGeometry()
    {
        const int Width = 80;
        var palette =
            new Palette(
                new[]
                {
                    new RugColor(220, 214, 192),
                    new RugColor(132, 158, 121),
                });
        var source =
            new DesignDocument(
                Width,
                92,
                palette);

        foreach (var point in Rasterizer.Dilate(
                     CurveRasterizer.Draw(
                         new[]
                         {
                             (8, 75),
                             (11, 48),
                             (20, 24),
                             (30, 10),
                         },
                         CurveType.SplineThroughPoints,
                         0.80),
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

        foreach (var point in Rasterizer.Dilate(
                     CurveRasterizer.Draw(
                         new[]
                         {
                             (54, 80),
                             (62, 62),
                             (67, 40),
                             (70, 17),
                         },
                         CurveType.SplineThroughPoints,
                         0.30),
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
                    source)
                .Where(region =>
                    region.Color == 1 &&
                    region.Area >= 30)
                .OrderBy(region =>
                    region.MinX)
                .ToArray();

        Assert.Equal(
            2,
            regions.Length);
        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                regions[0],
                Width,
                out var candidate));
        Assert.True(
            CurveFillRibbonCenterlineBuilder.TryBuild(
                candidate,
                Width,
                out var model,
                out _));

        var recovered =
            CurveFillRibbonMirrorPairRecovery.TryRecover(
                model,
                regions,
                Width,
                out var result,
                out var diagnostics);

        Assert.False(
            recovered);
        Assert.Same(
            model,
            result);
        Assert.NotEqual(
            "ok",
            diagnostics.Reason);
    }

    private static double MeanMirrorDistance(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<LeafPetalAxisSample> partner,
        double mirrorConstant)
    {
        var forward =
            MeanMirrorDistance(
                source,
                partner,
                mirrorConstant,
                reverse: false);
        var reversed =
            MeanMirrorDistance(
                source,
                partner,
                mirrorConstant,
                reverse: true);

        return Math.Min(
            forward,
            reversed);
    }

    private static double MeanMirrorDistance(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<LeafPetalAxisSample> partner,
        double mirrorConstant,
        bool reverse)
    {
        var count =
            Math.Max(
                source.Count,
                partner.Count);
        var sourceArcLength =
            BuildNormalizedArcLength(
                source);
        var partnerArcLength =
            BuildNormalizedArcLength(
                partner);
        var sum = 0d;

        for (var index = 0;
             index < count;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    count - 1);
            var a =
                SampleAtArcLength(
                    source,
                    sourceArcLength,
                    t);
            var b =
                SampleAtArcLength(
                    partner,
                    partnerArcLength,
                    reverse
                        ? 1d -
                          t
                        : t);
            var dx =
                a.X -
                (mirrorConstant -
                 b.X);
            var dy =
                a.Y -
                b.Y;

            sum +=
                Math.Sqrt(
                    dx *
                        dx +
                    dy *
                        dy);
        }

        return sum /
               Math.Max(
                   1,
                   count);
    }

    private static double[] BuildNormalizedArcLength(
        IReadOnlyList<LeafPetalAxisSample> samples)
    {
        var cumulative =
            new double[samples.Count];

        if (samples.Count <= 1)
            return cumulative;

        var total = 0d;

        for (var index = 1;
             index < samples.Count;
             index++)
        {
            var dx =
                samples[index].X -
                samples[index - 1].X;
            var dy =
                samples[index].Y -
                samples[index - 1].Y;

            total +=
                Math.Sqrt(
                    dx *
                        dx +
                    dy *
                        dy);
            cumulative[index] =
                total;
        }

        if (total <= 1e-9)
            return cumulative;

        for (var index = 1;
             index < cumulative.Length;
             index++)
        {
            cumulative[index] /=
                total;
        }

        return cumulative;
    }

    private static LeafPetalAxisSample SampleAtArcLength(
        IReadOnlyList<LeafPetalAxisSample> samples,
        IReadOnlyList<double> cumulative,
        double t)
    {
        t =
            Math.Clamp(
                t,
                0d,
                1d);

        var right = 1;

        while (right <
                   cumulative.Count - 1 &&
               cumulative[right] <
                   t)
        {
            right++;
        }

        var left =
            Math.Max(
                0,
                right - 1);
        var span =
            cumulative[right] -
            cumulative[left];
        var local =
            span <= 1e-9
                ? 0d
                : Math.Clamp(
                    (t -
                     cumulative[left]) /
                    span,
                    0d,
                    1d);
        var a =
            samples[left];
        var b =
            samples[right];

        return new LeafPetalAxisSample(
            a.X +
            (b.X -
             a.X) *
            local,
            a.Y +
            (b.Y -
             a.Y) *
            local,
            a.HalfWidth +
            (b.HalfWidth -
             a.HalfWidth) *
            local,
            t);
    }
}
