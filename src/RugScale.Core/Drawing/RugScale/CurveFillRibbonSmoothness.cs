namespace RugScale.Core.Drawing;

/// <summary>
/// Measures low-frequency aesthetic smoothness of a continuous ribbon centreline.
///
/// Points are first resampled by arc length so the score is independent of the fitter's parameter
/// spacing. The metric then measures variation in successive turning increments: a clean oval or
/// designer sweep changes curvature gradually, while an overfit control polygon produces abrupt
/// curvature acceleration even when its pixel/source deviation is small.
///
/// Lower is smoother. This metric is never used as a substitute for source-distance safety; it is
/// only a tie-break between already-safe geometric candidates.
/// </summary>
internal static class CurveFillRibbonSmoothness
{
    private const int ResampleCount = 64;

    public static double Measure(
        IReadOnlyList<ElegantArcPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 5)
            return double.PositiveInfinity;

        var sampled =
            ResampleByArcLength(
                points,
                ResampleCount);

        if (sampled.Count < 5)
            return double.PositiveInfinity;

        var turns =
            new List<double>(
                sampled.Count - 2);

        for (var index = 1;
             index < sampled.Count - 1;
             index++)
        {
            var a =
                sampled[index - 1];
            var b =
                sampled[index];
            var c =
                sampled[index + 1];
            var angleA =
                Math.Atan2(
                    b.Y -
                    a.Y,
                    b.X -
                    a.X);
            var angleB =
                Math.Atan2(
                    c.Y -
                    b.Y,
                    c.X -
                    b.X);
            var turn =
                NormalizeAngle(
                    angleB -
                    angleA);

            turns.Add(
                turn);
        }

        if (turns.Count < 3)
            return 0d;

        var sumSquaredVariation = 0d;
        var maximumVariation = 0d;
        var count = 0;

        for (var index = 1;
             index < turns.Count;
             index++)
        {
            var variation =
                Math.Abs(
                    turns[index] -
                    turns[index - 1]);

            sumSquaredVariation +=
                variation *
                variation;
            maximumVariation =
                Math.Max(
                    maximumVariation,
                    variation);
            count++;
        }

        if (count == 0)
            return 0d;

        var rms =
            Math.Sqrt(
                sumSquaredVariation /
                count);

        // A single visible kink matters even if the rest of a long arc is smooth.
        return rms +
               maximumVariation *
               0.20;
    }

    private static IReadOnlyList<(double X, double Y)> ResampleByArcLength(
        IReadOnlyList<ElegantArcPoint> points,
        int count)
    {
        var cumulative =
            new double[points.Count];

        for (var index = 1;
             index < points.Count;
             index++)
        {
            var dx =
                points[index].X -
                points[index - 1].X;
            var dy =
                points[index].Y -
                points[index - 1].Y;

            cumulative[index] =
                cumulative[index - 1] +
                Math.Sqrt(
                    dx *
                        dx +
                    dy *
                        dy);
        }

        var total =
            cumulative[^1];

        if (total <= 1e-9)
            return Array.Empty<(double X, double Y)>();

        var result =
            new (double X, double Y)[count];
        var segment = 1;

        for (var sampleIndex = 0;
             sampleIndex < count;
             sampleIndex++)
        {
            var target =
                total *
                sampleIndex /
                Math.Max(
                    1d,
                    count -
                    1d);

            while (segment <
                       cumulative.Length - 1 &&
                   cumulative[segment] <
                       target)
            {
                segment++;
            }

            var previous =
                Math.Max(
                    0,
                    segment - 1);
            var span =
                cumulative[segment] -
                cumulative[previous];
            var local =
                span <= 1e-9
                    ? 0d
                    : Math.Clamp(
                        (target -
                         cumulative[previous]) /
                        span,
                        0d,
                        1d);

            result[sampleIndex] =
                (
                    points[previous].X +
                    (points[segment].X -
                     points[previous].X) *
                    local,
                    points[previous].Y +
                    (points[segment].Y -
                     points[previous].Y) *
                    local
                );
        }

        return result;
    }

    private static double NormalizeAngle(
        double value)
    {
        while (value >
               Math.PI)
        {
            value -=
                Math.PI *
                2d;
        }

        while (value <
               -Math.PI)
        {
            value +=
                Math.PI *
                2d;
        }

        return value;
    }
}
