namespace RugScale.Core.Drawing;

/// <summary>
/// Recovers a mirror-symmetric carpet arch as one five-control Through-Points curve.
///
/// This is intentionally narrower than the general ribbon inverse fitter. It is meant for source
/// regions that have already passed exact/near-exact self-symmetry recovery and main-arc
/// extraction. The controls are constrained to mirrored endpoint/shoulder pairs plus one centre
/// control on the symmetry axis, which removes residual one-sided raster phase without leaving
/// RugScale's own SplineThroughPoints drawing language.
/// </summary>
internal static class CurveFillRibbonSymmetricThroughPointsFitter
{
    private const int ContinuousSamples = 160;
    private const double MaximumP95Deviation = 1.15;
    private const double MaximumDeviation = 1.85;
    private const double MaximumMeanMirrorError = 0.80;

    private static readonly double[] ShoulderFractions =
    [
        0.14,
        0.18,
        0.22,
        0.26,
        0.30,
        0.34,
        0.38,
    ];

    private static readonly double[] RoundnessCandidates =
    [
        0.20,
        0.25,
        0.30,
        0.35,
        0.40,
        0.45,
        0.50,
        0.60,
        0.70,
        0.80,
        0.90,
        1.00,
    ];

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out RibbonSymmetricThroughFitDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        fit =
            new ElegantArcFit(
                Array.Empty<ElegantArcPoint>(),
                false,
                false,
                int.MaxValue,
                double.PositiveInfinity);
        diagnostics = default;

        var samples =
            model.Samples;

        if (samples.Count < 18)
        {
            diagnostics =
                new RibbonSymmetricThroughFitDiagnostics(
                    "too-few-samples",
                    "none",
                    0d,
                    0d,
                    0d,
                    0d,
                    0d);
            return false;
        }

        var region =
            model.Candidate.Region;
        var lrMirrorError =
            MeanMirrorError(
                samples,
                region,
                leftRight: true);
        var tbMirrorError =
            MeanMirrorError(
                samples,
                region,
                leftRight: false);
        var useLeftRight =
            lrMirrorError <=
            tbMirrorError;
        var mirrorError =
            useLeftRight
                ? lrMirrorError
                : tbMirrorError;
        var axis =
            useLeftRight
                ? "LR"
                : "TB";

        if (mirrorError >
            MaximumMeanMirrorError)
        {
            diagnostics =
                new RibbonSymmetricThroughFitDiagnostics(
                    "mirror-error",
                    axis,
                    mirrorError,
                    0d,
                    0d,
                    0d,
                    0d);
            return false;
        }

        Candidate? best = null;

        foreach (var shoulderFraction in ShoulderFractions)
        {
            var shoulderIndex =
                Math.Clamp(
                    (int)Math.Round(
                        shoulderFraction *
                        (samples.Count -
                         1)),
                    1,
                    samples.Count /
                        2 -
                    1);
            var controls =
                BuildSymmetricControls(
                    samples,
                    shoulderIndex,
                    region,
                    useLeftRight);

            if (controls is null)
                continue;

            foreach (var roundness in RoundnessCandidates)
            {
                var candidate =
                    Score(
                        samples,
                        controls,
                        roundness,
                        shoulderFraction);

                if (best is null ||
                    candidate.Objective <
                    best.Value.Objective)
                {
                    best =
                        candidate;
                }
            }
        }

        if (best is null)
        {
            diagnostics =
                new RibbonSymmetricThroughFitDiagnostics(
                    "no-candidate",
                    axis,
                    mirrorError,
                    0d,
                    0d,
                    0d,
                    0d);
            return false;
        }

        var selected =
            best.Value;
        var points =
            BuildFitPoints(
                selected.Controls,
                selected.Roundness,
                samples);
        var smoothness =
            CurveFillRibbonSmoothness.Measure(
                points);
        var safe =
            selected.CurvatureSignFlips == 0 &&
            selected.P95Deviation <=
                MaximumP95Deviation &&
            selected.MaximumDeviation <=
                MaximumDeviation;

        diagnostics =
            new RibbonSymmetricThroughFitDiagnostics(
                safe
                    ? "ok"
                    : selected.CurvatureSignFlips != 0
                        ? "curvature-flip"
                        : selected.P95Deviation >
                          MaximumP95Deviation
                            ? "typical-deviation"
                            : "maximum-deviation",
                axis,
                mirrorError,
                selected.MaximumDeviation,
                selected.P95Deviation,
                selected.Roundness,
                smoothness);

        fit =
            new ElegantArcFit(
                points,
                safe,
                selected.CurvatureSignFlips == 0,
                selected.CurvatureSignFlips,
                selected.MaximumDeviation);

        return safe;
    }

    private static (int X, int Y)[]? BuildSymmetricControls(
        IReadOnlyList<LeafPetalAxisSample> samples,
        int shoulderIndex,
        LeafPetalRegion region,
        bool leftRight)
    {
        if (shoulderIndex <= 0 ||
            shoulderIndex >=
                samples.Count /
                2)
        {
            return null;
        }

        var endpoint =
            AverageWithMirror(
                samples[0],
                samples[^1],
                region,
                leftRight);
        var shoulder =
            AverageWithMirror(
                samples[shoulderIndex],
                samples[
                    samples.Count -
                    1 -
                    shoulderIndex],
                region,
                leftRight);
        var endpointMirror =
            Mirror(
                endpoint.X,
                endpoint.Y,
                region,
                leftRight);
        var shoulderMirror =
            Mirror(
                shoulder.X,
                shoulder.Y,
                region,
                leftRight);
        var centerSample =
            samples[
                samples.Count /
                2];

        double centerX;
        double centerY;

        if (leftRight)
        {
            centerX =
                (region.MinX +
                 region.MaxX) *
                0.5;
            centerY =
                centerSample.Y;
        }
        else
        {
            centerX =
                centerSample.X;
            centerY =
                (region.MinY +
                 region.MaxY) *
                0.5;
        }

        return
        [
            (
                (int)Math.Round(
                    endpoint.X),
                (int)Math.Round(
                    endpoint.Y)
            ),
            (
                (int)Math.Round(
                    shoulder.X),
                (int)Math.Round(
                    shoulder.Y)
            ),
            (
                (int)Math.Round(
                    centerX),
                (int)Math.Round(
                    centerY)
            ),
            (
                (int)Math.Round(
                    shoulderMirror.X),
                (int)Math.Round(
                    shoulderMirror.Y)
            ),
            (
                (int)Math.Round(
                    endpointMirror.X),
                (int)Math.Round(
                    endpointMirror.Y)
            ),
        ];
    }

    private static (double X, double Y) AverageWithMirror(
        LeafPetalAxisSample current,
        LeafPetalAxisSample opposite,
        LeafPetalRegion region,
        bool leftRight)
    {
        var mirrored =
            Mirror(
                opposite.X,
                opposite.Y,
                region,
                leftRight);

        return
            (
                (current.X +
                 mirrored.X) *
                0.5,
                (current.Y +
                 mirrored.Y) *
                0.5
            );
    }

    private static (double X, double Y) Mirror(
        double x,
        double y,
        LeafPetalRegion region,
        bool leftRight) =>
        leftRight
            ? (
                region.MinX +
                region.MaxX -
                x,
                y
            )
            : (
                x,
                region.MinY +
                region.MaxY -
                y
            );

    private static double MeanMirrorError(
        IReadOnlyList<LeafPetalAxisSample> samples,
        LeafPetalRegion region,
        bool leftRight)
    {
        var sum = 0d;

        for (var index = 0;
             index < samples.Count;
             index++)
        {
            var current =
                samples[index];
            var opposite =
                samples[
                    samples.Count -
                    1 -
                    index];
            var mirrored =
                Mirror(
                    opposite.X,
                    opposite.Y,
                    region,
                    leftRight);
            var dx =
                current.X -
                mirrored.X;
            var dy =
                current.Y -
                mirrored.Y;

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
                   samples.Count);
    }

    private static Candidate Score(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<(int X, int Y)> controls,
        double roundness,
        double shoulderFraction)
    {
        var curve =
            new (double X, double Y)[ContinuousSamples];

        for (var index = 0;
             index < curve.Length;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    curve.Length -
                    1);

            curve[index] =
                CurveRasterizer.Evaluate(
                    controls,
                    CurveType.SplineThroughPoints,
                    roundness,
                    t);
        }

        var distances =
            new List<double>(
                source.Count +
                curve.Length);
        var sum = 0d;

        foreach (var sample in source)
        {
            var nearestSquared =
                curve.Min(point =>
                {
                    var dx =
                        point.X -
                        sample.X;
                    var dy =
                        point.Y -
                        sample.Y;

                    return dx *
                               dx +
                           dy *
                               dy;
                });
            var distance =
                Math.Sqrt(
                    nearestSquared);

            distances.Add(
                distance);
            sum +=
                distance;
        }

        foreach (var point in curve)
        {
            var nearestSquared =
                source.Min(sample =>
                {
                    var dx =
                        point.X -
                        sample.X;
                    var dy =
                        point.Y -
                        sample.Y;

                    return dx *
                               dx +
                           dy *
                               dy;
                });
            var distance =
                Math.Sqrt(
                    nearestSquared);

            distances.Add(
                distance);
            sum +=
                distance;
        }

        distances.Sort();

        var p95Index =
            Math.Clamp(
                (int)Math.Ceiling(
                    distances.Count *
                    0.95) -
                1,
                0,
                distances.Count - 1);
        var p95 =
            distances[p95Index];
        var maximum =
            distances[^1];
        var mean =
            sum /
            Math.Max(
                1,
                distances.Count);
        var flips =
            CountCurvatureSignFlips(
                curve);
        var points =
            BuildFitPoints(
                controls,
                roundness,
                source);
        var smoothness =
            CurveFillRibbonSmoothness.Measure(
                points);

        var objective =
            p95 +
            mean *
                0.35 +
            maximum *
                0.05 +
            smoothness *
                1.50 +
            flips *
                4d;

        return new Candidate(
            controls.ToArray(),
            roundness,
            shoulderFraction,
            objective,
            maximum,
            p95,
            mean,
            flips);
    }

    private static int CountCurvatureSignFlips(
        IReadOnlyList<(double X, double Y)> points)
    {
        var previousSign = 0;
        var flips = 0;

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
                    (ax *
                         ax +
                     ay *
                         ay) *
                    (bx *
                         bx +
                     by *
                         by));

            if (scale <= 1e-9 ||
                Math.Abs(
                    cross) <
                scale *
                0.035)
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

    private static IReadOnlyList<ElegantArcPoint> BuildFitPoints(
        IReadOnlyList<(int X, int Y)> controls,
        double roundness,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var result =
            new List<ElegantArcPoint>(
                ContinuousSamples);

        for (var index = 0;
             index < ContinuousSamples;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    ContinuousSamples - 1);
            var point =
                CurveRasterizer.Evaluate(
                    controls,
                    CurveType.SplineThroughPoints,
                    roundness,
                    t);

            result.Add(
                new ElegantArcPoint(
                    point.X,
                    point.Y,
                    InterpolateHalfWidth(
                        source,
                        t)));
        }

        return result;
    }

    private static double InterpolateHalfWidth(
        IReadOnlyList<LeafPetalAxisSample> source,
        double t)
    {
        var position =
            Math.Clamp(
                t,
                0d,
                1d) *
            (source.Count -
             1);
        var left =
            Math.Clamp(
                (int)Math.Floor(
                    position),
                0,
                source.Count - 1);
        var right =
            Math.Min(
                source.Count - 1,
                left + 1);
        var local =
            position -
            left;

        return Math.Max(
            0.5,
            source[left].HalfWidth +
            (source[right].HalfWidth -
             source[left].HalfWidth) *
            local);
    }

    private readonly record struct Candidate(
        IReadOnlyList<(int X, int Y)> Controls,
        double Roundness,
        double ShoulderFraction,
        double Objective,
        double MaximumDeviation,
        double P95Deviation,
        double MeanDeviation,
        int CurvatureSignFlips);
}

internal readonly record struct RibbonSymmetricThroughFitDiagnostics(
    string Reason,
    string Axis,
    double MeanMirrorError,
    double MaximumDeviation,
    double Percentile95Deviation,
    double Roundness,
    double Smoothness);
