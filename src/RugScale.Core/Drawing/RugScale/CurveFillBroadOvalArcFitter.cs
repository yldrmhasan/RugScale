namespace RugScale.Core.Drawing;

/// <summary>
/// Fits a broad, sparse filled arch to one rotated half-ellipse.
///
/// Thick raster arches are a poor input for generic skeleton smoothing: thinning can shift the
/// medial path by several cells around wide shoulders. For a carpet-design oval/arch, the stable
/// geometric evidence is instead the endpoint chord plus the coherent one-sided bulge. This fitter
/// learns the half-ellipse height from all recovered centreline samples and then emits continuous
/// sub-pixel geometry independent of source staircase phase.
///
/// It is intentionally NOT a general curve fitter. The caller uses it only for broad sparse ribbon
/// candidates, and this class additionally rejects chord backtracking, weak/degenerate arches and
/// source paths that are not adequately explained by one ellipse.
/// </summary>
internal static class CurveFillBroadOvalArcFitter
{
    private const int MinimumContinuousSamples = 128;
    private const int MaximumContinuousSamples = 320;
    private const double ContinuousSamplesPerChordPixel = 2.0;
    private const double MinimumHeightToHalfChord = 0.18;
    private const double MaximumHeightToHalfChord = 2.40;
    private const double MaximumBacktrackFraction = 0.08;
    private const double SignificantBacktrack = 0.35;
    private const double MaximumP95Deviation = 2.60;
    private const double MaximumDeviation = 4.75;

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out BroadOvalArcFitDiagnostics diagnostics)
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

        if (source.Count < 10)
        {
            diagnostics =
                new BroadOvalArcFitDiagnostics(
                    "too-few-samples",
                    0d,
                    0d,
                    0d,
                    0d);
            return false;
        }

        var first =
            source[0];
        var last =
            source[^1];
        var chordX =
            last.X -
            first.X;
        var chordY =
            last.Y -
            first.Y;
        var chordLength =
            Math.Sqrt(
                chordX *
                    chordX +
                chordY *
                    chordY);

        if (chordLength < 8d)
        {
            diagnostics =
                new BroadOvalArcFitDiagnostics(
                    "chord-too-short",
                    0d,
                    0d,
                    0d,
                    0d);
            return false;
        }

        var axisX =
            chordX /
            chordLength;
        var axisY =
            chordY /
            chordLength;
        var normalX =
            -axisY;
        var normalY =
            axisX;
        var centerX =
            (first.X +
             last.X) *
            0.5;
        var centerY =
            (first.Y +
             last.Y) *
            0.5;
        var halfChord =
            chordLength *
            0.5;

        // Pick the normal orientation that points toward the bulk of the source arch.
        var signedMean =
            source.Average(sample =>
                (sample.X -
                 centerX) *
                    normalX +
                (sample.Y -
                 centerY) *
                    normalY);

        if (signedMean < 0d)
        {
            normalX =
                -normalX;
            normalY =
                -normalY;
        }

        var projected =
            new (double U, double V)[source.Count];
        var backtracks = 0;

        for (var index = 0;
             index < source.Count;
             index++)
        {
            var dx =
                source[index].X -
                centerX;
            var dy =
                source[index].Y -
                centerY;

            projected[index] =
                (
                    U:
                        (dx *
                             axisX +
                         dy *
                             axisY) /
                        halfChord,
                    V:
                        dx *
                            normalX +
                        dy *
                            normalY
                );

            if (index == 0)
                continue;

            var delta =
                projected[index].U -
                projected[index - 1].U;

            if (delta <
                -SignificantBacktrack /
                Math.Max(
                    1d,
                    halfChord))
            {
                backtracks++;
            }
        }

        var backtrackFraction =
            backtracks /
            (double)Math.Max(
                1,
                source.Count - 1);

        // The principal path can be returned in either direction. Normalize it to increasing U.
        if (projected[^1].U <
            projected[0].U)
        {
            axisX =
                -axisX;
            axisY =
                -axisY;

            for (var index = 0;
                 index < source.Count;
                 index++)
            {
                var dx =
                    source[index].X -
                    centerX;
                var dy =
                    source[index].Y -
                    centerY;

                projected[index] =
                    (
                        U:
                            (dx *
                                 axisX +
                             dy *
                                 axisY) /
                            halfChord,
                        V:
                            dx *
                                normalX +
                            dy *
                                normalY
                    );
            }

            backtracks = 0;

            for (var index = 1;
                 index < projected.Length;
                 index++)
            {
                var delta =
                    projected[index].U -
                    projected[index - 1].U;

                if (delta <
                    -SignificantBacktrack /
                    Math.Max(
                        1d,
                        halfChord))
                {
                    backtracks++;
                }
            }

            backtrackFraction =
                backtracks /
                (double)Math.Max(
                    1,
                    source.Count - 1);
        }

        if (backtrackFraction >
            MaximumBacktrackFraction)
        {
            diagnostics =
                new BroadOvalArcFitDiagnostics(
                    "chord-backtracking",
                    0d,
                    0d,
                    0d,
                    backtrackFraction);
            return false;
        }

        // v = H * sqrt(1-u^2). Solve H in one least-squares step. Clamp only the basis;
        // endpoint cap pixels may project a fraction beyond the chord because of raster dilation.
        var numerator = 0d;
        var denominator = 0d;

        foreach (var point in projected)
        {
            var u =
                Math.Clamp(
                    point.U,
                    -1d,
                    1d);
            var basis =
                Math.Sqrt(
                    Math.Max(
                        0d,
                        1d -
                        u *
                        u));

            if (basis < 0.08)
                continue;

            numerator +=
                basis *
                point.V;
            denominator +=
                basis *
                basis;
        }

        if (denominator <= 1e-9)
        {
            diagnostics =
                new BroadOvalArcFitDiagnostics(
                    "degenerate-height-fit",
                    0d,
                    0d,
                    0d,
                    backtrackFraction);
            return false;
        }

        var height =
            numerator /
            denominator;

        // Refit with a robust Huber-style weight. Thick indexed arches often have a handful of
        // cap/shoulder pixels displaced by one or two cells; plain least squares lets those few
        // raster outliers flatten or over-inflate the entire oval. The robust pass keeps the macro
        // ellipse driven by the coherent majority while still using every source sample.
        var residuals =
            projected
                .Select(point =>
                {
                    var u =
                        Math.Clamp(
                            point.U,
                            -1d,
                            1d);
                    var basis =
                        Math.Sqrt(
                            Math.Max(
                                0d,
                                1d -
                                u *
                                    u));

                    return basis < 0.08
                        ? 0d
                        : Math.Abs(
                            point.V -
                            height *
                                basis);
                })
                .Where(value =>
                    value > 0d)
                .OrderBy(value =>
                    value)
                .ToArray();

        if (residuals.Length > 0)
        {
            var medianResidual =
                residuals[
                    residuals.Length /
                    2];
            var robustThreshold =
                Math.Max(
                    0.55,
                    medianResidual *
                        2.75);
            var weightedNumerator = 0d;
            var weightedDenominator = 0d;

            foreach (var point in projected)
            {
                var u =
                    Math.Clamp(
                        point.U,
                        -1d,
                        1d);
                var basis =
                    Math.Sqrt(
                        Math.Max(
                            0d,
                            1d -
                            u *
                                u));

                if (basis < 0.08)
                    continue;

                var residual =
                    Math.Abs(
                        point.V -
                        height *
                            basis);
                var weight =
                    residual <=
                    robustThreshold
                        ? 1d
                        : robustThreshold /
                          Math.Max(
                              residual,
                              1e-9);

                weightedNumerator +=
                    weight *
                    basis *
                    point.V;
                weightedDenominator +=
                    weight *
                    basis *
                    basis;
            }

            if (weightedDenominator >
                1e-9)
            {
                height =
                    weightedNumerator /
                    weightedDenominator;
            }
        }

        var heightRatio =
            height /
            halfChord;

        if (height <= 0d ||
            heightRatio <
                MinimumHeightToHalfChord ||
            heightRatio >
                MaximumHeightToHalfChord)
        {
            diagnostics =
                new BroadOvalArcFitDiagnostics(
                    "height-ratio",
                    0d,
                    0d,
                    heightRatio,
                    backtrackFraction);
            return false;
        }

        var continuousSamples =
            Math.Clamp(
                (int)Math.Ceiling(
                    chordLength *
                    ContinuousSamplesPerChordPixel),
                MinimumContinuousSamples,
                MaximumContinuousSamples);
        var points =
            new List<ElegantArcPoint>(
                continuousSamples);

        for (var index = 0;
             index < continuousSamples;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    continuousSamples - 1);
            var theta =
                Math.PI *
                (1d -
                 t);
            var localX =
                halfChord *
                Math.Cos(
                    theta);
            var localY =
                height *
                Math.Sin(
                    theta);

            points.Add(
                new ElegantArcPoint(
                    centerX +
                    axisX *
                        localX +
                    normalX *
                        localY,
                    centerY +
                    axisY *
                        localX +
                    normalY *
                        localY,
                    InterpolateHalfWidth(
                        source,
                        t)));
        }

        var deviation =
            SymmetricDeviation(
                points,
                source);
        var safe =
            deviation.Percentile95 <=
                MaximumP95Deviation &&
            deviation.Maximum <=
                MaximumDeviation;

        diagnostics =
            new BroadOvalArcFitDiagnostics(
                safe
                    ? "ok"
                    : deviation.Percentile95 >
                      MaximumP95Deviation
                        ? "typical-deviation"
                        : "maximum-deviation",
                deviation.Maximum,
                deviation.Percentile95,
                heightRatio,
                backtrackFraction);

        fit =
            new ElegantArcFit(
                points,
                safe,
                true,
                0,
                deviation.Maximum);

        return safe;
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

    private static (double Maximum, double Percentile95) SymmetricDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var distances =
            new List<double>(
                fit.Count +
                source.Count);

        foreach (var sample in source)
        {
            var nearestSquared =
                fit.Min(point =>
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

            distances.Add(
                Math.Sqrt(
                    nearestSquared));
        }

        foreach (var point in fit)
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

            distances.Add(
                Math.Sqrt(
                    nearestSquared));
        }

        distances.Sort();

        var percentileIndex =
            Math.Clamp(
                (int)Math.Ceiling(
                    distances.Count *
                    0.95) -
                1,
                0,
                distances.Count - 1);

        return
            (
                distances[^1],
                distances[percentileIndex]
            );
    }
}

internal readonly record struct BroadOvalArcFitDiagnostics(
    string Reason,
    double MaximumDeviation,
    double Percentile95Deviation,
    double HeightToHalfChordRatio,
    double BacktrackFraction);
