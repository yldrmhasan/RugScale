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
    private const int MinimumStableSignRun = 5;
    private const int TurnSmoothingPasses = 2;

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
            SmoothTurns(
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
        var runs =
            new List<(int Sign, int Start, int End, int Count)>();
        var currentSign = 0;
        var start = -1;
        var end = -1;
        var count = 0;

        void Flush()
        {
            if (currentSign != 0 &&
                count >=
                    MinimumStableSignRun)
            {
                runs.Add(
                    (
                        currentSign,
                        start,
                        end,
                        count
                    ));
            }

            currentSign = 0;
            start = -1;
            end = -1;
            count = 0;
        }

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

            if (currentSign == 0)
            {
                currentSign =
                    sign;
                start =
                    index;
                end =
                    index;
                count = 1;
                continue;
            }

            if (sign ==
                currentSign)
            {
                end =
                    index;
                count++;
                continue;
            }

            // A short opposite burst is raster/high-frequency phase, not a real curvature lobe.
            // Look ahead for persistence before ending the current stable run.
            var persistent = 1;

            for (var probe = index + 1;
                 probe < turns.Count &&
                 persistent <
                     MinimumStableSignRun;
                 probe++)
            {
                var probeValue =
                    turns[probe];

                if (Math.Abs(
                        probeValue) <
                    SignificantTurnRadians)
                {
                    continue;
                }

                if (Math.Sign(
                        probeValue) !=
                    sign)
                {
                    break;
                }

                persistent++;
            }

            if (persistent <
                MinimumStableSignRun)
            {
                continue;
            }

            Flush();
            currentSign =
                sign;
            start =
                index;
            end =
                index;
            count = 1;
        }

        Flush();

        var result =
            new List<int>();

        for (var index = 1;
             index < runs.Count;
             index++)
        {
            var previous =
                runs[index - 1];
            var current =
                runs[index];

            if (previous.Sign ==
                current.Sign)
            {
                continue;
            }

            result.Add(
                (previous.End +
                 current.Start) /
                2);
        }

        return result;
    }

    private static double[] SmoothTurns(
        IReadOnlyList<double> values)
    {
        var current =
            values.ToArray();

        for (var pass = 0;
             pass < TurnSmoothingPasses;
             pass++)
        {
            var result =
                current.ToArray();

            for (var index = 0;
                 index < current.Length;
                 index++)
            {
                var i0 =
                    Math.Max(
                        0,
                        index - 2);
                var i1 =
                    Math.Max(
                        0,
                        index - 1);
                var i3 =
                    Math.Min(
                        current.Length - 1,
                        index + 1);
                var i4 =
                    Math.Min(
                        current.Length - 1,
                        index + 2);

                result[index] =
                    (current[i0] +
                     current[i1] *
                         2d +
                     current[index] *
                         4d +
                     current[i3] *
                         2d +
                     current[i4]) /
                    10d;
            }

            current =
                result;
        }

        return current;
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
