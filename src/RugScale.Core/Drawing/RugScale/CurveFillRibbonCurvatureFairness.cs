namespace RugScale.Core.Drawing;

/// <summary>
/// Inflection-aware fairness metric for S-shaped ribbon centrelines.
///
/// The generic smoothness score measures successive turn variation across the complete curve. That
/// is appropriate for one-flow ovals, but it can over-penalize a legitimate S inflection where
/// signed curvature must cross zero. This metric measures curvature variation *inside* each lobe
/// and excludes a short neighbourhood around stable sign changes.
///
/// Lower is fairer. It is diagnostic/tie-break evidence only and never replaces source-distance
/// safety.
/// </summary>
internal static class CurveFillRibbonCurvatureFairness
{
    private const int ResampleCount = 96;
    private const int InflectionExclusionRadius = 4;
    private const double SignificantTurnRadians = 0.0045;

    public static RibbonCurvatureFairnessDiagnostics Measure(
        IReadOnlyList<ElegantArcPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 8)
            return default;

        var sampled =
            ResampleByArcLength(
                points,
                ResampleCount);

        if (sampled.Count < 8)
            return default;

        var turns =
            new double[
                sampled.Count -
                2];

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

            turns[index - 1] =
                NormalizeAngle(
                    angleB -
                    angleA);
        }

        var filteredTurns =
            MedianSmooth(
                turns);
        var inflections =
            FindStableInflections(
                filteredTurns);
        var excluded =
            new bool[
                filteredTurns.Length];

        foreach (var inflection in inflections)
        {
            var start =
                Math.Max(
                    0,
                    inflection -
                    InflectionExclusionRadius);
            var end =
                Math.Min(
                    excluded.Length - 1,
                    inflection +
                    InflectionExclusionRadius);

            for (var index = start;
                 index <= end;
                 index++)
            {
                excluded[index] =
                    true;
            }
        }

        var squared = 0d;
        var maximum = 0d;
        var count = 0;

        for (var index = 1;
             index < filteredTurns.Length;
             index++)
        {
            if (excluded[index] ||
                excluded[index - 1])
            {
                continue;
            }

            var previous =
                filteredTurns[index - 1];
            var current =
                filteredTurns[index];

            // Do not bridge two curvature lobes even if the zero-crossing detector placed the
            // representative inflection one sample away.
            if (Math.Abs(
                    previous) >=
                    SignificantTurnRadians &&
                Math.Abs(
                    current) >=
                    SignificantTurnRadians &&
                Math.Sign(
                    previous) !=
                Math.Sign(
                    current))
            {
                continue;
            }

            var variation =
                Math.Abs(
                    current -
                    previous);

            squared +=
                variation *
                variation;
            maximum =
                Math.Max(
                    maximum,
                    variation);
            count++;
        }

        if (count == 0)
        {
            return new RibbonCurvatureFairnessDiagnostics(
                0d,
                inflections.Count,
                0d,
                0d,
                0);
        }

        var rms =
            Math.Sqrt(
                squared /
                count);
        var score =
            rms +
            maximum *
                0.20;

        return new RibbonCurvatureFairnessDiagnostics(
            score,
            inflections.Count,
            rms,
            maximum,
            count);
    }

    public static RibbonCurvatureFairnessDiagnostics Measure(
        IReadOnlyList<LeafPetalAxisSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        return Measure(
            samples
                .Select(sample =>
                    new ElegantArcPoint(
                        sample.X,
                        sample.Y,
                        sample.HalfWidth))
                .ToArray());
    }

    private static IReadOnlyList<int> FindStableInflections(
        IReadOnlyList<double> turns)
    {
        var result =
            new List<int>();
        var previousSign = 0;
        var previousIndex = -1;

        for (var index = 0;
             index < turns.Count;
             index++)
        {
            var value =
                turns[index];

            if (Math.Abs(
                    value) <
                SignificantTurnRadians)
            {
                continue;
            }

            var sign =
                Math.Sign(
                    value);

            if (previousSign != 0 &&
                sign !=
                previousSign)
            {
                var candidate =
                    previousIndex < 0
                        ? index
                        : (previousIndex +
                           index) /
                          2;

                if (result.Count == 0 ||
                    candidate -
                    result[^1] >
                    InflectionExclusionRadius *
                    2)
                {
                    result.Add(
                        candidate);
                }
            }

            previousSign =
                sign;
            previousIndex =
                index;
        }

        return result;
    }

    private static double[] MedianSmooth(
        IReadOnlyList<double> values)
    {
        var result =
            new double[
                values.Count];

        for (var index = 0;
             index < values.Count;
             index++)
        {
            var a =
                values[
                    Math.Max(
                        0,
                        index - 1)];
            var b =
                values[index];
            var c =
                values[
                    Math.Min(
                        values.Count - 1,
                        index + 1)];

            result[index] =
                Median3(
                    a,
                    b,
                    c);
        }

        return result;
    }

    private static double Median3(
        double a,
        double b,
        double c)
    {
        if (a > b)
            (a, b) =
                (b, a);

        if (b > c)
            (b, c) =
                (c, b);

        if (a > b)
            (a, b) =
                (b, a);

        return b;
    }

    private static IReadOnlyList<(double X, double Y)> ResampleByArcLength(
        IReadOnlyList<ElegantArcPoint> points,
        int count)
    {
        var cumulative =
            new double[
                points.Count];

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
            new (double X, double Y)[
                count];
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

internal readonly record struct RibbonCurvatureFairnessDiagnostics(
    double Score,
    int InflectionCount,
    double RmsVariation,
    double MaximumVariation,
    int SamplesUsed);
