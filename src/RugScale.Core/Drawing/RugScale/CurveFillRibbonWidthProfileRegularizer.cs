namespace RugScale.Core.Drawing;

/// <summary>
/// Removes one-pixel medial-skeleton width wobble from accepted filled ribbons while preserving
/// the designer's low-frequency taper/thickening trend.
///
/// The centre curve and palette ownership are unchanged. Only HalfWidth is low-pass filtered, and
/// only for already accepted ribbon-like regions with a reasonably stable width profile. Leaves,
/// petals and strongly tapered regions are intentionally left untouched.
/// </summary>
internal static class CurveFillRibbonWidthProfileRegularizer
{
    private const double MaximumSourceWidthCoefficientVariation = 0.38;
    private const double MinimumTerminalWidthRatio = 0.55;
    private const int MinimumSamples = 12;

    public static ElegantArcFit Regularize(
        LeafPetalArcModel model,
        ElegantArcFit fit,
        out RibbonWidthRegularizationDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);

        diagnostics = default;

        if (!fit.IsSafe ||
            fit.Points.Count < MinimumSamples ||
            model.Samples.Count < MinimumSamples)
        {
            return fit;
        }

        var sourceWidths =
            model.Samples
                .Select(sample =>
                    Math.Max(
                        0.45,
                        sample.HalfWidth))
                .ToArray();
        var mean =
            sourceWidths.Average();

        if (mean <= 1e-9)
            return fit;

        var variance =
            sourceWidths.Average(width =>
            {
                var delta =
                    width -
                    mean;

                return delta *
                       delta;
            });
        var coefficientVariation =
            Math.Sqrt(
                variance) /
            mean;

        var terminalWindow =
            Math.Clamp(
                sourceWidths.Length /
                10,
                2,
                6);
        var firstWidth =
            sourceWidths
                .Take(
                    terminalWindow)
                .Average();
        var lastWidth =
            sourceWidths
                .TakeLast(
                    terminalWindow)
                .Average();
        var terminalRatio =
            Math.Min(
                firstWidth,
                lastWidth) /
            Math.Max(
                firstWidth,
                lastWidth);

        if (coefficientVariation >
                MaximumSourceWidthCoefficientVariation ||
            terminalRatio <
                MinimumTerminalWidthRatio)
        {
            diagnostics =
                new RibbonWidthRegularizationDiagnostics(
                    false,
                    coefficientVariation,
                    terminalRatio,
                    0d,
                    0d,
                    0d);
            return fit;
        }

        var raw =
            fit.Points
                .Select(point =>
                    Math.Max(
                        0.45,
                        point.HalfWidth))
                .ToArray();
        var smoothed =
            raw.ToArray();

        // Use a path-relative window so long production arches receive stronger de-noising than
        // short synthetic examples. Two passes suppress alternating 1px skeleton phase without
        // flattening a genuine broad taper.
        var radius =
            Math.Clamp(
                fit.Points.Count /
                18,
                2,
                10);

        for (var pass = 0;
             pass < 2;
             pass++)
        {
            smoothed =
                SmoothTriangular(
                    smoothed,
                    radius);
        }

        // Preserve overall band weight. The regularizer changes local phase, not the intended
        // average thickness of the indexed colour region.
        var rawMean =
            raw.Average();
        var smoothMean =
            smoothed.Average();
        var scale =
            smoothMean <= 1e-9
                ? 1d
                : rawMean /
                  smoothMean;

        var sorted =
            sourceWidths
                .OrderBy(value =>
                    value)
                .ToArray();
        var lower =
            Percentile(
                sorted,
                0.05) *
            0.94;
        var upper =
            Percentile(
                sorted,
                0.95) *
            1.06;

        for (var index = 0;
             index < smoothed.Length;
             index++)
        {
            smoothed[index] =
                Math.Clamp(
                    smoothed[index] *
                    scale,
                    Math.Max(
                        0.45,
                        lower),
                    Math.Max(
                        lower,
                        upper));
        }

        var beforeVariation =
            MeanAbsoluteAdjacentDelta(
                raw);
        var afterVariation =
            MeanAbsoluteAdjacentDelta(
                smoothed);

        // Do not rewrite widths when filtering found no meaningful high-frequency content.
        if (afterVariation >=
            beforeVariation *
                0.985)
        {
            diagnostics =
                new RibbonWidthRegularizationDiagnostics(
                    false,
                    coefficientVariation,
                    terminalRatio,
                    beforeVariation,
                    afterVariation,
                    0d);
            return fit;
        }

        var maximumShift = 0d;
        var points =
            new ElegantArcPoint[
                fit.Points.Count];

        for (var index = 0;
             index < points.Length;
             index++)
        {
            maximumShift =
                Math.Max(
                    maximumShift,
                    Math.Abs(
                        smoothed[index] -
                        raw[index]));
            points[index] =
                fit.Points[index] with
                {
                    HalfWidth =
                        smoothed[index],
                };
        }

        diagnostics =
            new RibbonWidthRegularizationDiagnostics(
                true,
                coefficientVariation,
                terminalRatio,
                beforeVariation,
                afterVariation,
                maximumShift);

        return fit with
        {
            Points =
                points,
        };
    }

    private static double[] SmoothTriangular(
        IReadOnlyList<double> source,
        int radius)
    {
        var result =
            new double[source.Count];

        for (var index = 0;
             index < source.Count;
             index++)
        {
            var sum = 0d;
            var weightSum = 0d;

            for (var offset = -radius;
                 offset <= radius;
                 offset++)
            {
                var sampleIndex =
                    Math.Clamp(
                        index +
                        offset,
                        0,
                        source.Count - 1);
                var weight =
                    radius +
                    1 -
                    Math.Abs(
                        offset);

                sum +=
                    source[sampleIndex] *
                    weight;
                weightSum +=
                    weight;
            }

            result[index] =
                sum /
                Math.Max(
                    1d,
                    weightSum);
        }

        return result;
    }

    private static double Percentile(
        IReadOnlyList<double> sorted,
        double fraction)
    {
        if (sorted.Count == 0)
            return 0d;

        var position =
            Math.Clamp(
                fraction,
                0d,
                1d) *
            (sorted.Count -
             1);
        var left =
            (int)Math.Floor(
                position);
        var right =
            Math.Min(
                sorted.Count - 1,
                left + 1);
        var local =
            position -
            left;

        return sorted[left] +
               (sorted[right] -
                sorted[left]) *
               local;
    }

    private static double MeanAbsoluteAdjacentDelta(
        IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0d;

        var sum = 0d;

        for (var index = 1;
             index < values.Count;
             index++)
        {
            sum +=
                Math.Abs(
                    values[index] -
                    values[index - 1]);
        }

        return sum /
               (values.Count -
                1);
    }
}

internal readonly record struct RibbonWidthRegularizationDiagnostics(
    bool Applied,
    double SourceWidthCoefficientVariation,
    double TerminalWidthRatio,
    double BeforeAdjacentVariation,
    double AfterAdjacentVariation,
    double MaximumHalfWidthShift);
