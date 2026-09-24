namespace RugScale.Core.Drawing;

/// <summary>
/// Recovers a filled ribbon's underlying 5-point RugScale Through-Points curve from its noisy
/// medial skeleton using continuous geometric distance rather than raster pixel F1.
///
/// A thick categorical band is not a Curve-tool raster: thinning/dilation shifts some medial
/// pixels and creates staircase phase. Feeding that path to the ordinary 1x1 inverse learner can
/// therefore reject the correct family. This fitter searches ordered source-path control indices,
/// roundness and a tightly-bounded +/-1 control-point adjustment, while scoring the CONTINUOUS
/// RugScale curve against all source medial samples.
/// </summary>
internal static class CurveFillRibbonThroughPointsFitter
{
    private const int ControlCount = 5;
    private const int ContinuousSamples = 144;
    private const double MaximumP95Deviation = 2.80;
    private const double MaximumDeviation = 3.80;

    private static readonly double[] RoundnessCandidates =
    [
        0.25,
        0.35,
        0.42,
        0.50,
        0.60,
        0.70,
        0.78,
        0.85,
        0.92,
        1.00,
    ];

    private static readonly double[][] SeedFractions =
    [
        [0.00, 0.25, 0.50, 0.75, 1.00],
        [0.00, 0.18, 0.43, 0.72, 1.00],
        [0.00, 0.28, 0.57, 0.82, 1.00],
        [0.00, 0.14, 0.40, 0.76, 1.00],
    ];

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out RibbonThroughPointsFitDiagnostics diagnostics)
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

        var source =
            model.Samples;

        if (source.Count < 18)
        {
            diagnostics =
                new RibbonThroughPointsFitDiagnostics(
                    "too-few-samples",
                    0d,
                    0d,
                    0d,
                    0,
                    0d);
            return false;
        }

        Candidate? best = null;

        foreach (var fractions in SeedFractions)
        {
            var indices =
                BuildIndices(
                    source.Count,
                    fractions);

            OptimizeIndices(
                source,
                indices);

            var controls =
                ControlsFromIndices(
                    source,
                    indices);
            var scored =
                FindBestRoundness(
                    source,
                    controls);

            if (best is null ||
                scored.Objective <
                best.Value.Objective)
            {
                best =
                    scored with
                    {
                        Indices =
                            indices.ToArray(),
                    };
            }
        }

        if (best is null)
        {
            diagnostics =
                new RibbonThroughPointsFitDiagnostics(
                    "no-candidate",
                    0d,
                    0d,
                    0d,
                    0,
                    0d);
            return false;
        }

        var refinedControls =
            best.Value.Controls
                .ToArray();
        var refined =
            RefineControlCoordinates(
                source,
                refinedControls,
                best.Value.Roundness);

        if (refined.Objective <
            best.Value.Objective)
        {
            best =
                refined with
                {
                    Indices =
                        best.Value.Indices,
                };
        }

        var selected =
            best.Value;
        var safe =
            selected.CurvatureSignFlips == 0 &&
            selected.P95Deviation <=
                MaximumP95Deviation &&
            selected.MaximumDeviation <=
                MaximumDeviation;

        diagnostics =
            new RibbonThroughPointsFitDiagnostics(
                safe
                    ? "ok"
                    : selected.CurvatureSignFlips != 0
                        ? "curvature-flip"
                        : selected.P95Deviation >
                          MaximumP95Deviation
                            ? "typical-deviation"
                            : "maximum-deviation",
                selected.MaximumDeviation,
                selected.P95Deviation,
                selected.MeanDeviation,
                selected.CurvatureSignFlips,
                selected.Roundness);

        var points =
            BuildFitPoints(
                selected.Controls,
                selected.Roundness,
                source);

        fit =
            new ElegantArcFit(
                points,
                safe,
                selected.CurvatureSignFlips == 0,
                selected.CurvatureSignFlips,
                selected.MaximumDeviation);

        return safe;
    }

    private static int[] BuildIndices(
        int count,
        IReadOnlyList<double> fractions)
    {
        var indices =
            new int[ControlCount];

        for (var index = 0;
             index < ControlCount;
             index++)
        {
            indices[index] =
                (int)Math.Round(
                    fractions[index] *
                    (count - 1d));
        }

        indices[0] = 0;
        indices[^1] =
            count - 1;

        for (var index = 1;
             index < indices.Length - 1;
             index++)
        {
            indices[index] =
                Math.Clamp(
                    indices[index],
                    indices[index - 1] + 1,
                    count -
                    (indices.Length - index));
        }

        return indices;
    }

    private static void OptimizeIndices(
        IReadOnlyList<LeafPetalAxisSample> source,
        int[] indices)
    {
        var steps =
            new[]
            {
                Math.Max(
                    1,
                    source.Count / 14),
                Math.Max(
                    1,
                    source.Count / 28),
                2,
                1,
            }
            .Distinct()
            .OrderByDescending(value =>
                value)
            .ToArray();

        var controls =
            ControlsFromIndices(
                source,
                indices);
        var current =
            FindBestRoundness(
                source,
                controls);

        foreach (var step in steps)
        {
            for (var pass = 0;
                 pass < 2;
                 pass++)
            {
                var changed = false;

                for (var controlIndex = 1;
                     controlIndex <
                     indices.Length - 1;
                     controlIndex++)
                {
                    var original =
                        indices[controlIndex];
                    var minimum =
                        indices[controlIndex - 1] +
                        1;
                    var maximum =
                        indices[controlIndex + 1] -
                        1;
                    var bestIndex =
                        original;
                    var bestCandidate =
                        current;

                    foreach (var candidateIndex in
                             new[]
                             {
                                 Math.Clamp(
                                     original -
                                     step,
                                     minimum,
                                     maximum),
                                 Math.Clamp(
                                     original +
                                     step,
                                     minimum,
                                     maximum),
                             }
                             .Distinct())
                    {
                        indices[controlIndex] =
                            candidateIndex;
                        var candidateControls =
                            ControlsFromIndices(
                                source,
                                indices);
                        var candidate =
                            FindBestRoundness(
                                source,
                                candidateControls);

                        if (candidate.Objective <
                            bestCandidate.Objective)
                        {
                            bestCandidate =
                                candidate;
                            bestIndex =
                                candidateIndex;
                        }
                    }

                    indices[controlIndex] =
                        bestIndex;

                    if (bestIndex !=
                        original)
                    {
                        changed = true;
                        current =
                            bestCandidate;
                    }
                }

                if (!changed)
                    break;
            }
        }
    }

    private static (int X, int Y)[] ControlsFromIndices(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<int> indices) =>
        indices
            .Select(index =>
                (
                    X: (int)Math.Round(
                        source[index].X),
                    Y: (int)Math.Round(
                        source[index].Y)
                ))
            .ToArray();

    private static Candidate FindBestRoundness(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<(int X, int Y)> controls)
    {
        Candidate? best = null;

        foreach (var roundness in RoundnessCandidates)
        {
            var candidate =
                ScoreCandidate(
                    source,
                    controls,
                    roundness);

            if (best is null ||
                candidate.Objective <
                best.Value.Objective)
            {
                best =
                    candidate;
            }
        }

        return best!.Value;
    }

    private static Candidate RefineControlCoordinates(
        IReadOnlyList<LeafPetalAxisSample> source,
        (int X, int Y)[] controls,
        double initialRoundness)
    {
        var current =
            ScoreCandidate(
                source,
                controls,
                initialRoundness);

        // One-pixel coordinate freedom compensates for medial-skeleton phase without letting the
        // inverse model drift away from source evidence. Endpoints are included because square
        // dilation can move the thinned cap centre by one cell.
        for (var pass = 0;
             pass < 2;
             pass++)
        {
            var changed = false;

            for (var controlIndex = 0;
                 controlIndex < controls.Length;
                 controlIndex++)
            {
                var origin =
                    controls[controlIndex];
                var bestPoint =
                    origin;
                var bestCandidate =
                    current;

                for (var dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (var dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        if (dx == 0 &&
                            dy == 0)
                        {
                            continue;
                        }

                        controls[controlIndex] =
                            (
                                origin.X +
                                dx,
                                origin.Y +
                                dy
                            );

                        var candidate =
                            FindBestRoundness(
                                source,
                                controls);

                        if (candidate.Objective <
                            bestCandidate.Objective)
                        {
                            bestCandidate =
                                candidate;
                            bestPoint =
                                controls[controlIndex];
                        }
                    }
                }

                controls[controlIndex] =
                    bestPoint;

                if (bestPoint !=
                    origin)
                {
                    changed = true;
                    current =
                        bestCandidate with
                        {
                            Controls =
                                controls.ToArray(),
                        };
                }
            }

            if (!changed)
                break;
        }

        return FindBestRoundness(
            source,
            controls);
    }

    private static Candidate ScoreCandidate(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<(int X, int Y)> controls,
        double roundness)
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
                    curve.Length - 1);

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

        // Typical geometry dominates. Maximum error is only a light tie-break because thick-band
        // thinning can leave isolated cap/shoulder outliers. Any real S-bend is heavily penalized.
        var objective =
            p95 +
            mean *
                0.35 +
            maximum *
                0.06 +
            flips *
                4.0;

        return new Candidate(
            controls.ToArray(),
            roundness,
            objective,
            maximum,
            p95,
            mean,
            flips,
            Array.Empty<int>());
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
        double Objective,
        double MaximumDeviation,
        double P95Deviation,
        double MeanDeviation,
        int CurvatureSignFlips,
        IReadOnlyList<int> Indices);
}

internal readonly record struct RibbonThroughPointsFitDiagnostics(
    string Reason,
    double MaximumDeviation,
    double Percentile95Deviation,
    double MeanDeviation,
    int CurvatureSignFlips,
    double Roundness);
