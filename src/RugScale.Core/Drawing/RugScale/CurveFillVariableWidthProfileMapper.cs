namespace RugScale.Core.Drawing;

/// <summary>
/// Transfers a genuine low-frequency source half-width profile onto an already accepted geometric
/// centreline.
///
/// Ordinary carpet ribbons are intentionally regularized toward one robust width because skeleton
/// phase creates one-pixel breathing. Some ornaments, however, are designer sweeps whose width
/// changes materially along the curve. Using CurveFillRibbonThroughPointsFitter directly on those
/// regions would flatten the motif because that fitter deliberately emits one robust half-width.
///
/// This mapper is separate so normal ribbons keep their constant-width behaviour. It removes only
/// local width spikes, preserves the source mean/range, then resamples the ordered source profile
/// onto the continuous fitted centreline.
/// </summary>
internal static class CurveFillVariableWidthProfileMapper
{
    private const int MedianRadius = 2;
    private const int SmoothingPasses = 2;
    private const double MinimumHalfWidth = 0.45;

    public static ElegantArcFit Apply(
        LeafPetalArcModel model,
        ElegantArcFit fit,
        out VariableWidthProfileDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);

        diagnostics = default;

        if (model.Samples.Count < 6 ||
            fit.Points.Count < 6)
        {
            return fit;
        }

        var original =
            model.Samples
                .Select(sample =>
                    Math.Max(
                        MinimumHalfWidth,
                        sample.HalfWidth))
                .ToArray();
        var sourceMean =
            original.Average();
        var sourceCv =
            CoefficientVariation(
                original);

        var profile =
            MedianFilter(
                original,
                MedianRadius);

        for (var pass = 0;
             pass < SmoothingPasses;
             pass++)
        {
            profile =
                Smooth(
                    profile);
        }

        var lower =
            Percentile(
                original,
                0.05);
        var upper =
            Percentile(
                original,
                0.95);

        for (var index = 0;
             index < profile.Length;
             index++)
        {
            profile[index] =
                Math.Clamp(
                    profile[index],
                    lower,
                    upper);
        }

        // Low-pass filtering can shift a strongly tapered profile's total visual weight. Restore
        // the source mean before target resampling, then clamp once more to robust source bounds.
        var filteredMean =
            profile.Average();

        if (filteredMean > 1e-9)
        {
            var scale =
                sourceMean /
                filteredMean;

            for (var index = 0;
                 index < profile.Length;
                 index++)
            {
                profile[index] =
                    Math.Clamp(
                        profile[index] *
                        scale,
                        lower,
                        upper);
            }
        }

        var mapped =
            new ElegantArcPoint[
                fit.Points.Count];

        for (var index = 0;
             index < mapped.Length;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    mapped.Length - 1);
            var sourcePosition =
                t *
                (profile.Length - 1);
            var left =
                (int)Math.Floor(
                    sourcePosition);
            var right =
                Math.Min(
                    profile.Length - 1,
                    left + 1);
            var local =
                sourcePosition -
                left;
            var halfWidth =
                profile[left] +
                (profile[right] -
                 profile[left]) *
                local;
            var point =
                fit.Points[index];

            mapped[index] =
                point with
                {
                    HalfWidth =
                        Math.Max(
                            MinimumHalfWidth,
                            halfWidth),
                };
        }

        var mappedWidths =
            mapped
                .Select(point =>
                    point.HalfWidth)
                .ToArray();
        var maximumAdjacentVariation = 0d;

        for (var index = 1;
             index < mappedWidths.Length;
             index++)
        {
            maximumAdjacentVariation =
                Math.Max(
                    maximumAdjacentVariation,
                    Math.Abs(
                        mappedWidths[index] -
                        mappedWidths[index - 1]));
        }

        diagnostics =
            new VariableWidthProfileDiagnostics(
                Applied: true,
                SourceCoefficientVariation:
                    sourceCv,
                MappedCoefficientVariation:
                    CoefficientVariation(
                        mappedWidths),
                SourceMinimum:
                    original.Min(),
                SourceMaximum:
                    original.Max(),
                MappedMinimum:
                    mappedWidths.Min(),
                MappedMaximum:
                    mappedWidths.Max(),
                MaximumAdjacentVariation:
                    maximumAdjacentVariation);

        return fit with
        {
            Points =
                mapped,
        };
    }

    private static double[] MedianFilter(
        IReadOnlyList<double> values,
        int radius)
    {
        var result =
            new double[
                values.Count];

        for (var index = 0;
             index < values.Count;
             index++)
        {
            var start =
                Math.Max(
                    0,
                    index - radius);
            var end =
                Math.Min(
                    values.Count - 1,
                    index + radius);
            var window =
                new double[
                    end -
                    start +
                    1];

            for (var source = start;
                 source <= end;
                 source++)
            {
                window[
                    source -
                    start] =
                    values[source];
            }

            Array.Sort(
                window);
            var middle =
                window.Length /
                2;

            result[index] =
                window.Length % 2 == 0
                    ? (window[middle - 1] +
                       window[middle]) *
                      0.5
                    : window[middle];
        }

        return result;
    }

    private static double[] Smooth(
        IReadOnlyList<double> values)
    {
        var result =
            values.ToArray();

        for (var index = 1;
             index < values.Count - 1;
             index++)
        {
            result[index] =
                values[index - 1] *
                    0.25 +
                values[index] *
                    0.50 +
                values[index + 1] *
                    0.25;
        }

        return result;
    }

    private static double CoefficientVariation(
        IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0d;

        var mean =
            values.Average();

        if (mean <= 1e-9)
            return 0d;

        var variance =
            values.Average(value =>
            {
                var delta =
                    value -
                    mean;

                return delta *
                       delta;
            });

        return Math.Sqrt(
                   variance) /
               mean;
    }

    private static double Percentile(
        IReadOnlyList<double> values,
        double percentile)
    {
        if (values.Count == 0)
            return MinimumHalfWidth;

        var ordered =
            values
                .Order()
                .ToArray();
        var position =
            Math.Clamp(
                percentile,
                0d,
                1d) *
            (ordered.Length - 1);
        var left =
            (int)Math.Floor(
                position);
        var right =
            Math.Min(
                ordered.Length - 1,
                left + 1);
        var local =
            position -
            left;

        return ordered[left] +
               (ordered[right] -
                ordered[left]) *
               local;
    }
}

internal readonly record struct VariableWidthProfileDiagnostics(
    bool Applied,
    double SourceCoefficientVariation,
    double MappedCoefficientVariation,
    double SourceMinimum,
    double SourceMaximum,
    double MappedMinimum,
    double MappedMaximum,
    double MaximumAdjacentVariation);
