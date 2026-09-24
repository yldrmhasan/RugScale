using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class LeafPetalBoundaryCurveFitterTests
{
    [Fact]
    public void Fit_DesignerThroughPointArc_PreservesTargetScaleGeometry()
    {
        var sourceControls = new (int X, int Y)[]
        {
            (5, 41),
            (11, 17),
            (27, 7),
            (47, 14),
            (59, 39),
        };

        var sourcePath =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            sourceControls,
                            CurveType.SplineThroughPoints,
                            0.50)))
                .ToArray();

        var fit =
            LeafPetalBoundaryCurveFitter.Fit(
                sourcePath);

        Assert.True(
            fit.HasValue,
            "A clean designer leaf-side arc should produce a specialist boundary fit.");
        Assert.Equal(
            CurveType.SplineThroughPoints,
            fit.Value.Type);
        Assert.InRange(
            fit.Value.Controls.Count,
            4,
            7);

        const double ScaleX = 1.60;
        const double ScaleY = 1.45;

        var expectedControls =
            sourceControls
                .Select(point =>
                    Map(
                        point,
                        ScaleX,
                        ScaleY))
                .ToArray();
        var learnedControls =
            fit.Value.Controls
                .Select(point =>
                    Map(
                        point,
                        ScaleX,
                        ScaleY))
                .ToArray();

        var expected =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        expectedControls,
                        CurveType.SplineThroughPoints,
                        0.50))
                .ToHashSet();
        var learned =
            Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        learnedControls,
                        fit.Value.Type,
                        fit.Value.Roundness))
                .ToHashSet();

        var exact =
            F1Exact(
                learned,
                expected);
        var near =
            F1Near(
                learned,
                expected,
                radius: 1);

        Assert.True(
            exact >= 0.72,
            $"Target-size designer arc exact F1 is too low: {exact:P2}.");
        Assert.True(
            near >= 0.97,
            $"Target-size designer arc left the expected one-pixel geometry corridor: {near:P2}.");
    }

    [Fact]
    public void Fit_DoesNotOverfitPixelStaircaseWithManyControlPoints()
    {
        var controls = new (int X, int Y)[]
        {
            (4, 35),
            (11, 16),
            (28, 9),
            (45, 15),
            (55, 34),
        };

        var sourcePath =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            controls,
                            CurveType.SplineThroughPoints,
                            0.35)))
                .ToArray();

        var fit =
            LeafPetalBoundaryCurveFitter.Fit(
                sourcePath);

        Assert.True(
            fit.HasValue);
        Assert.True(
            fit.Value.Controls.Count <= 7,
            $"Specialist arc fit used too many controls: {fit.Value.Controls.Count}.");

        var flips =
            CountCurvatureSignFlips(
                fit.Value.Controls);

        Assert.True(
            flips <= 2,
            $"Designer arc overfit digital staircase noise with {flips} macro curvature sign changes.");
    }

    private static (int X, int Y) Map(
        (int X, int Y) point,
        double scaleX,
        double scaleY) =>
        (
            X: (int)Math.Round(
                (point.X + 0.5) *
                scaleX -
                0.5),
            Y: (int)Math.Round(
                (point.Y + 0.5) *
                scaleY -
                0.5));

    private static IEnumerable<(int X, int Y)> ConsecutiveDistinct(
        IEnumerable<(int X, int Y)> points)
    {
        (int X, int Y)? previous =
            null;

        foreach (var point in points)
        {
            if (previous is not null &&
                previous.Value ==
                point)
            {
                continue;
            }

            yield return point;
            previous =
                point;
        }
    }

    private static double F1Exact(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected)
    {
        var intersection =
            actual.Count(
                expected.Contains);
        var precision =
            intersection /
            (double)Math.Max(
                1,
                actual.Count);
        var recall =
            intersection /
            (double)Math.Max(
                1,
                expected.Count);

        return F1(
            precision,
            recall);
    }

    private static double F1Near(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected,
        int radius)
    {
        var actualSupported =
            actual.Count(point =>
                HasNeighbor(
                    expected,
                    point,
                    radius));
        var expectedSupported =
            expected.Count(point =>
                HasNeighbor(
                    actual,
                    point,
                    radius));
        var precision =
            actualSupported /
            (double)Math.Max(
                1,
                actual.Count);
        var recall =
            expectedSupported /
            (double)Math.Max(
                1,
                expected.Count);

        return F1(
            precision,
            recall);
    }

    private static bool HasNeighbor(
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

    private static double F1(
        double precision,
        double recall)
    {
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

    private static int CountCurvatureSignFlips(
        IReadOnlyList<(int X, int Y)> controls)
    {
        var flips = 0;
        var previousSign = 0;

        for (var i = 1;
             i < controls.Count - 1;
             i++)
        {
            var ax =
                controls[i].X -
                controls[i - 1].X;
            var ay =
                controls[i].Y -
                controls[i - 1].Y;
            var bx =
                controls[i + 1].X -
                controls[i].X;
            var by =
                controls[i + 1].Y -
                controls[i].Y;
            var cross =
                ax *
                by -
                ay *
                bx;

            if (Math.Abs(cross) <= 1)
                continue;

            var sign =
                Math.Sign(
                    cross);

            if (previousSign != 0 &&
                sign !=
                previousSign)
            {
                flips++;
            }

            previousSign =
                sign;
        }

        return flips;
    }
}
