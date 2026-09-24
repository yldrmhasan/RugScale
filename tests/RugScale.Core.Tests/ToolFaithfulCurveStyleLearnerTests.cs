using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class ToolFaithfulCurveStyleLearnerTests
{
    [Fact]
    public void Learner_RecoversHighRoundnessThroughPointsFamily()
    {
        (int X, int Y)[] controls =
        [
            (5, 43),
            (12, 13),
            (30, 6),
            (50, 16),
            (57, 43),
        ];

        var source =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        controls,
                        CurveType.SplineThroughPoints,
                        0.85))
                .Distinct()
                .ToArray();

        var fit =
            ToolFaithfulCurveStyleLearner.Fit(
                source,
                pixelCord: true);

        Assert.True(
            fit.HasValue,
            "A clean Curve-tool raster should produce a style fit.");
        Assert.Equal(
            CurveType.SplineThroughPoints,
            fit.Value.Type);
        Assert.InRange(
            fit.Value.Roundness,
            0.65,
            1.00);
        Assert.True(
            fit.Value.ModelScore >
            fit.Value.PolylineBaselineModelScore,
            "The compact learned curve model should beat the complexity-regularized polyline baseline.");
    }

    [Fact]
    public void Learner_RecoversBezierFamilyWhenBezierExplainsRasterBetter()
    {
        (int X, int Y)[] controls =
        [
            (6, 44),
            (8, 8),
            (49, 4),
            (57, 43),
        ];

        var source =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        controls,
                        CurveType.Bezier,
                        0.90))
                .Distinct()
                .ToArray();

        var fit =
            ToolFaithfulCurveStyleLearner.Fit(
                source,
                pixelCord: true);

        Assert.True(
            fit.HasValue,
            "A clean cubic Bezier raster should produce a style fit.");
        Assert.Equal(
            CurveType.Bezier,
            fit.Value.Type);
        Assert.InRange(
            fit.Value.Roundness,
            0.65,
            1.00);
    }

    [Fact]
    public void Learner_DoesNotForceClosedOutlineIntoOpenCurveFamily()
    {
        var loop =
            new List<(int X, int Y)>();

        foreach (var point in Rasterizer.RectangleOutline(
                     5,
                     5,
                     25,
                     18))
        {
            if (loop.Count == 0 ||
                loop[^1] != point)
            {
                loop.Add(point);
            }
        }

        loop.Add(loop[0]);

        var fit =
            ToolFaithfulCurveStyleLearner.Fit(
                loop,
                pixelCord: true);

        Assert.False(
            fit.HasValue,
            "Closed outline loops must stay on exact graph replay until a closed-curve model exists.");
    }

    [Fact]
    public void Learner_DoesNotSmoothDeliberateLCorner()
    {
        var chain =
            new List<(int X, int Y)>();

        for (var y = 4;
             y <= 22;
             y++)
        {
            chain.Add(
                (8, y));
        }

        for (var x = 9;
             x <= 27;
             x++)
        {
            chain.Add(
                (x, 22));
        }

        var fit =
            ToolFaithfulCurveStyleLearner.Fit(
                chain,
                pixelCord: true);

        Assert.False(
            fit.HasValue,
            "A deliberate L corner must remain edge-for-edge / polyline, not be rounded by training.");
    }
}
