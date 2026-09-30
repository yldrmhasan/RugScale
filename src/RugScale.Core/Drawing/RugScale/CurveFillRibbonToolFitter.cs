namespace RugScale.Core.Drawing;

/// <summary>
/// Fits a filled ribbon's recovered medial path back to RugScale's actual Curve-tool family and
/// converts that model to a sub-pixel centreline suitable for paired-boundary rasterization.
///
/// The inverse learner is source-only: no target bitmap or expected answer participates. This lets
/// a filled band inherit the same Spline/Through-Points/Bezier drawing language as a 1x1 Curve
/// stroke instead of smoothing the source staircase with an arbitrary generic spline.
/// </summary>
internal static class CurveFillRibbonToolFitter
{
    private const double MaximumCenterlineDeviation = 1.55;
    private const int MinimumContinuousSamples = 32;
    private const int MaximumContinuousSamples = 384;

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out ToolFaithfulCurveStyleFit style)
    {
        ArgumentNullException.ThrowIfNull(model);

        fit = new ElegantArcFit(
            Array.Empty<ElegantArcPoint>(),
            false,
            false,
            int.MaxValue,
            double.PositiveInfinity);
        style = default;

        var chain =
            ConsecutiveDistinct(
                model.Samples
                    .Select(sample =>
                        (
                            X: (int)Math.Round(
                                sample.X),
                            Y: (int)Math.Round(
                                sample.Y))))
                .ToArray();

        if (chain.Length < 12)
            return false;

        var learned =
            ToolFaithfulCurveStyleLearner.Fit(
                chain,
                pixelCord: false);

        if (!learned.HasValue ||
            learned.Value.Controls.Count < 2)
        {
            return false;
        }

        style =
            learned.Value;

        var sampleCount =
            Math.Clamp(
                model.Samples.Count * 3,
                MinimumContinuousSamples,
                MaximumContinuousSamples);
        var points =
            new List<ElegantArcPoint>(
                sampleCount);

        for (var index = 0;
             index < sampleCount;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    sampleCount - 1);
            var point =
                CurveRasterizer.Evaluate(
                    style.Controls,
                    style.Type,
                    style.Roundness,
                    t);
            var halfWidth =
                InterpolateHalfWidth(
                    model.Samples,
                    t);

            points.Add(
                new ElegantArcPoint(
                    point.X,
                    point.Y,
                    halfWidth));
        }

        var signFlips =
            CountCurvatureSignFlips(
                points);
        var maximumDeviation =
            MaximumDeviation(
                points,
                model.Samples);
        var monotonic =
            signFlips <= 1;
        var safe =
            monotonic &&
            maximumDeviation <=
                MaximumCenterlineDeviation;

        fit =
            new ElegantArcFit(
                points,
                safe,
                monotonic,
                signFlips,
                maximumDeviation);

        return safe;
    }

    private static IReadOnlyList<(int X, int Y)> ConsecutiveDistinct(
        IEnumerable<(int X, int Y)> source)
    {
        var result =
            new List<(int X, int Y)>();

        foreach (var point in source)
        {
            if (result.Count == 0 ||
                result[^1] != point)
            {
                result.Add(
                    point);
            }
        }

        return result;
    }

    private static double InterpolateHalfWidth(
        IReadOnlyList<LeafPetalAxisSample> samples,
        double t)
    {
        if (samples.Count == 0)
            return 0.5;

        if (samples.Count == 1)
        {
            return Math.Max(
                0.5,
                samples[0].HalfWidth);
        }

        var position =
            Math.Clamp(
                t,
                0d,
                1d) *
            (samples.Count - 1);
        var left =
            Math.Clamp(
                (int)Math.Floor(
                    position),
                0,
                samples.Count - 1);
        var right =
            Math.Min(
                samples.Count - 1,
                left + 1);
        var local =
            position -
            left;

        return Math.Max(
            0.5,
            samples[left].HalfWidth +
            (samples[right].HalfWidth -
             samples[left].HalfWidth) *
            local);
    }

    private static int CountCurvatureSignFlips(
        IReadOnlyList<ElegantArcPoint> points)
    {
        var flips = 0;
        var previousSign = 0;

        for (var index = 1;
             index < points.Count - 1;
             index++)
        {
            var ax =
                points[index].X -
                points[index - 1].X;
            var ay =
                points[index].Y -
                points[index - 1].Y;
            var bx =
                points[index + 1].X -
                points[index].X;
            var by =
                points[index + 1].Y -
                points[index].Y;
            var cross =
                ax *
                by -
                ay *
                bx;
            var scale =
                Math.Sqrt(
                    (ax * ax +
                     ay * ay) *
                    (bx * bx +
                     by * by));

            if (scale <= 1e-9 ||
                Math.Abs(
                    cross) <
                scale *
                0.04)
            {
                continue;
            }

            var sign =
                Math.Sign(
                    cross);

            if (previousSign != 0 &&
                sign != previousSign)
            {
                flips++;
            }

            previousSign =
                sign;
        }

        return flips;
    }

    private static double MaximumDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var maximum = 0d;

        foreach (var point in fit)
        {
            var nearestSquared =
                double.PositiveInfinity;

            foreach (var sample in source)
            {
                var dx =
                    point.X -
                    sample.X;
                var dy =
                    point.Y -
                    sample.Y;
                var distance =
                    dx *
                    dx +
                    dy *
                    dy;

                nearestSquared =
                    Math.Min(
                        nearestSquared,
                        distance);
            }

            maximum =
                Math.Max(
                    maximum,
                    Math.Sqrt(
                        nearestSquared));
        }

        return maximum;
    }
}
