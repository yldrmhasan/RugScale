using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class LeafPetalBoundaryPairOptimizerTests
{
    [Fact]
    public void Optimize_MismatchedRoundness_ImprovesTargetDesignerPair()
    {
        var leftControls = new (int X, int Y)[]
        {
            (23, 58),
            (21, 41),
            (28, 20),
            (42, 7),
        };
        var rightControls = new (int X, int Y)[]
        {
            (52, 58),
            (55, 41),
            (53, 21),
            (42, 7),
        };
        const double DesignerRoundness = 0.35;

        var leftSource = OrderedCurve(
            leftControls,
            DesignerRoundness);
        var rightSource = OrderedCurve(
            rightControls,
            DesignerRoundness);

        var region = new LeafPetalRegion(
            Color: 2,
            Pixels: Array.Empty<int>(),
            BoundaryPixels: Array.Empty<int>(),
            MinX: 0,
            MinY: 0,
            MaxX: 1,
            MaxY: 1);
        var candidate = new LeafPetalArcCandidate(
            region,
            CenterX: 38d,
            CenterY: 32d,
            AxisX: 0d,
            AxisY: -1d,
            NormalX: 1d,
            NormalY: 0d,
            MajorExtent: 52d,
            MinorExtent: 30d,
            Elongation: 1.9,
            BoundaryRatio: 0.2);
        var arc = new LeafPetalArcModel(
            candidate,
            Samples: Array.Empty<LeafPetalAxisSample>(),
            ReversedForApex: false,
            BaseWidth: 15d,
            ApexWidth: 1d,
            SkeletonCoverage: 1d);

        var wrongLeft = new ToolFaithfulCurveStyleFit(
            CurveType.SplineThroughPoints,
            Roundness: 0.85,
            Controls: leftControls,
            Score: 1d,
            ModelScore: 1d,
            PolylineBaselineScore: 0d,
            PolylineBaselineModelScore: 0d,
            SimplifyTolerance: 0d);
        var wrongRight = new ToolFaithfulCurveStyleFit(
            CurveType.SplineThroughPoints,
            Roundness: 0.10,
            Controls: rightControls,
            Score: 1d,
            ModelScore: 1d,
            PolylineBaselineScore: 0d,
            PolylineBaselineModelScore: 0d,
            SimplifyTolerance: 0d);

        var model = new LeafPetalBoundaryCurveModel(
            arc,
            OutlineColor: 1,
            OutlineCoverage: 1d,
            LeftSourcePath: leftSource,
            RightSourcePath: rightSource,
            SourceOuterPath: leftSource
                .Concat(rightSource)
                .ToHashSet(),
            LeftFit: wrongLeft,
            RightFit: wrongRight,
            DrawBaseCap: false,
            DrawApexCap: true);

        const double ScaleX = 1.25;
        const double ScaleY = 1.14782608695652;

        var optimized =
            LeafPetalBoundaryPairOptimizer.Optimize(
                model,
                ScaleX,
                ScaleY);

        var expected = RenderTarget(
                leftControls,
                DesignerRoundness,
                ScaleX,
                ScaleY)
            .Concat(
                RenderTarget(
                    rightControls,
                    DesignerRoundness,
                    ScaleX,
                    ScaleY))
            .ToHashSet();
        var before = RenderTarget(
                leftControls,
                wrongLeft.Roundness,
                ScaleX,
                ScaleY)
            .Concat(
                RenderTarget(
                    rightControls,
                    wrongRight.Roundness,
                    ScaleX,
                    ScaleY))
            .ToHashSet();
        var after = RenderTarget(
                leftControls,
                optimized.LeftFit.Roundness,
                ScaleX,
                ScaleY)
            .Concat(
                RenderTarget(
                    rightControls,
                    optimized.RightFit.Roundness,
                    ScaleX,
                    ScaleY))
            .ToHashSet();

        var beforeScore =
            NearF1(
                before,
                expected,
                radius: 1);
        var afterScore =
            NearF1(
                after,
                expected,
                radius: 1);

        Assert.True(
            afterScore >= beforeScore,
            $"Joint optimization made target geometry worse: before={beforeScore:P2}, after={afterScore:P2}.");
        Assert.True(
            optimized.LeftFit.Roundness != wrongLeft.Roundness ||
            optimized.RightFit.Roundness != wrongRight.Roundness,
            "A deliberately mismatched pair should not survive target-scale joint optimization unchanged.");
    }

    private static (int X, int Y)[] OrderedCurve(
        IReadOnlyList<(int X, int Y)> controls,
        double roundness) =>
        Rasterizer.ConnectDiagonalSteps(
                CurveRasterizer.Draw(
                    controls,
                    CurveType.SplineThroughPoints,
                    roundness))
            .Distinct()
            .ToArray();

    private static IEnumerable<(int X, int Y)> RenderTarget(
        IReadOnlyList<(int X, int Y)> controls,
        double roundness,
        double scaleX,
        double scaleY)
    {
        var mapped = controls
            .Select(point =>
                (
                    X: (int)Math.Round(
                        (point.X + 0.5) *
                        scaleX -
                        0.5),
                    Y: (int)Math.Round(
                        (point.Y + 0.5) *
                        scaleY -
                        0.5)))
            .ToArray();

        return Rasterizer.ConnectDiagonalSteps(
                CurveRasterizer.Draw(
                    mapped,
                    CurveType.SplineThroughPoints,
                    roundness))
            .Distinct();
    }

    private static double NearF1(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected,
        int radius)
    {
        if (actual.Count == 0 ||
            expected.Count == 0)
        {
            return 0d;
        }

        var actualSupported =
            actual.Count(point =>
                HasNear(
                    expected,
                    point,
                    radius));
        var expectedSupported =
            expected.Count(point =>
                HasNear(
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

    private static bool HasNear(
        IReadOnlySet<(int X, int Y)> points,
        (int X, int Y) point,
        int radius)
    {
        for (var dy = -radius;
             dy <= radius;
             dy++)
        {
            for (var dx = -radius;
                 dx <= radius;
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
}
