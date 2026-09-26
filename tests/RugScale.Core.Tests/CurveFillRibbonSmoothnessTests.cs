using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonSmoothnessTests
{
    [Fact]
    public void Measure_CleanOvalSweepScoresSmootherThanWobblySweep()
    {
        var clean =
            Enumerable.Range(
                    0,
                    101)
                .Select(index =>
                {
                    var t =
                        index /
                        100d;
                    var angle =
                        Math.PI *
                        (1d -
                         t);

                    return new ElegantArcPoint(
                        50d +
                        38d *
                            Math.Cos(
                                angle),
                        60d -
                        26d *
                            Math.Sin(
                                angle),
                        3d);
                })
                .ToArray();

        var wobbly =
            clean
                .Select((point, index) =>
                {
                    var t =
                        index /
                        (double)Math.Max(
                            1,
                            clean.Length - 1);
                    var wobble =
                        0.9 *
                        Math.Sin(
                            Math.PI *
                            10d *
                            t);

                    return point with
                    {
                        Y =
                            point.Y +
                            wobble,
                    };
                })
                .ToArray();

        var cleanScore =
            CurveFillRibbonSmoothness.Measure(
                clean);
        var wobblyScore =
            CurveFillRibbonSmoothness.Measure(
                wobbly);

        Assert.True(
            cleanScore <
            wobblyScore * 0.55,
            $"Clean oval should be substantially smoother: clean={cleanScore:0.000000}, wobbly={wobblyScore:0.000000}.");
    }
}
