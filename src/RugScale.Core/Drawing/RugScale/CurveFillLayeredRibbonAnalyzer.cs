using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Detects stable multi-band colour structure beside a filled ribbon.
///
/// Some carpet curves are not a fill plus one symmetric outline. A source section may be layered,
/// for example: fill -> white cord -> navy cord -> white cord -> background. If the fill is
/// geometrically redrawn while those neighbouring protected bands remain on the nearest-neighbour
/// baseline, the final curve cannot stay parallel even when the centreline is correct.
///
/// This analyzer has no raster authority. It samples immutable source evidence along normals to the
/// recovered centreline and reports whether one side carries a repeatable colour-run sequence.
/// </summary>
internal static class CurveFillLayeredRibbonAnalyzer
{
    private const int MaximumRuns = 4;
    // Long ornamental bands can sit beside a broad protected outer colour. Twelve source pixels
    // was enough to measure separator/band thickness but sometimes stopped before immutable
    // exterior ownership was reached. This remains diagnostic source sampling only.
    private const double MaximumNormalDistance = 22d;
    private const double MinimumDominantCoverage = 0.60;

    public static LayeredRibbonProfileDiagnostics Analyze(
        DesignDocument source,
        LeafPetalArcModel model,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        if (model.Samples.Count < 12)
        {
            return new LayeredRibbonProfileDiagnostics(
                false,
                "none",
                string.Empty,
                0d,
                0d,
                0,
                0,
                Array.Empty<double>());
        }

        var positive =
            AnalyzeSide(
                source,
                model,
                protectedStrokeColors,
                side: 1d);
        var negative =
            AnalyzeSide(
                source,
                model,
                protectedStrokeColors,
                side: -1d);

        var chosen =
            positive.Coverage >=
            negative.Coverage
                ? positive
                : negative;

        if (chosen.Coverage <
                MinimumDominantCoverage ||
            chosen.Sequence.Length == 0)
        {
            return new LayeredRibbonProfileDiagnostics(
                false,
                "none",
                chosen.Sequence,
                chosen.Coverage,
                chosen.BracketedCoverage,
                chosen.SampleCount,
                chosen.MatchingSamples,
                chosen.MeanRunWidths);
        }

        return new LayeredRibbonProfileDiagnostics(
            true,
            ReferenceEquals(
                chosen,
                positive)
                ? "positive"
                : "negative",
            chosen.Sequence,
            chosen.Coverage,
            chosen.BracketedCoverage,
            chosen.SampleCount,
            chosen.MatchingSamples,
            chosen.MeanRunWidths);
    }

    private static SideProfile AnalyzeSide(
        DesignDocument source,
        LeafPetalArcModel model,
        IReadOnlySet<byte> protectedStrokeColors,
        double side)
    {
        var sequences =
            new Dictionary<string, SequenceAccumulator>(
                StringComparer.Ordinal);
        var validSamples = 0;

        for (var index = 3;
             index < model.Samples.Count - 3;
             index += 2)
        {
            var previous =
                model.Samples[index - 3];
            var current =
                model.Samples[index];
            var next =
                model.Samples[index + 3];
            var tx =
                next.X -
                previous.X;
            var ty =
                next.Y -
                previous.Y;
            var length =
                Math.Sqrt(
                    tx *
                        tx +
                    ty *
                        ty);

            if (length <= 1e-9)
                continue;

            var nx =
                -ty /
                length *
                side;
            var ny =
                tx /
                length *
                side;
            var runs =
                SampleRuns(
                    source,
                    current,
                    nx,
                    ny,
                    model.Candidate.Region.Color,
                    protectedStrokeColors);

            if (runs.Count < 2)
                continue;

            validSamples++;
            var sequence =
                string.Join(
                    ">",
                    runs
                        .Take(
                            MaximumRuns)
                        .Select(run =>
                            run.Color
                                .ToString()));

            if (!sequences.TryGetValue(
                    sequence,
                    out var accumulator))
            {
                accumulator =
                    new SequenceAccumulator(
                        0,
                        new double[
                            Math.Min(
                                MaximumRuns,
                                runs.Count)]);
            }

            var widths =
                accumulator.WidthSums
                    .ToArray();

            for (var runIndex = 0;
                 runIndex < widths.Length;
                 runIndex++)
            {
                widths[runIndex] +=
                    runs[runIndex].Width;
            }

            sequences[sequence] =
                new SequenceAccumulator(
                    accumulator.Count + 1,
                    widths);
        }

        if (validSamples == 0 ||
            sequences.Count == 0)
        {
            return new SideProfile(
                string.Empty,
                0d,
                0d,
                validSamples,
                0,
                Array.Empty<double>());
        }

        var dominant =
            sequences
                .OrderByDescending(pair =>
                    pair.Value.Count)
                .ThenBy(pair =>
                    pair.Key,
                    StringComparer.Ordinal)
                .First();
        var coverage =
            dominant.Value.Count /
            (double)validSamples;
        var means =
            dominant.Value.WidthSums
                .Select(sum =>
                    sum /
                    Math.Max(
                        1,
                        dominant.Value.Count))
                .ToArray();
        var dominantParts =
            dominant.Key
                .Split(
                    '>',
                    StringSplitOptions.RemoveEmptyEntries);
        var bracketedCoverage = 0d;

        if (dominantParts.Length >= 2)
        {
            var bracketPrefix =
                dominantParts[0] +
                ">" +
                dominantParts[1] +
                ">" +
                dominantParts[0];
            var bracketedCount =
                sequences
                    .Where(pair =>
                        pair.Key.StartsWith(
                            bracketPrefix,
                            StringComparison.Ordinal))
                    .Sum(pair =>
                        pair.Value.Count);
            bracketedCoverage =
                bracketedCount /
                (double)validSamples;
        }

        return new SideProfile(
            dominant.Key,
            coverage,
            bracketedCoverage,
            validSamples,
            dominant.Value.Count,
            means);
    }

    private static IReadOnlyList<ColorRun> SampleRuns(
        DesignDocument source,
        LeafPetalAxisSample sample,
        double nx,
        double ny,
        byte regionColor,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        const double Step = 0.25;

        var samples =
            new List<(double Distance, byte Color)>();
        var startDistance =
            Math.Max(
                0d,
                sample.HalfWidth -
                0.75);

        for (var distance = startDistance;
             distance <=
             sample.HalfWidth +
             MaximumNormalDistance;
             distance += Step)
        {
            var x =
                Math.Clamp(
                    (int)Math.Round(
                        sample.X +
                        nx *
                        distance),
                    0,
                    source.Width - 1);
            var y =
                Math.Clamp(
                    (int)Math.Round(
                        sample.Y +
                        ny *
                        distance),
                    0,
                    source.Height - 1);

            samples.Add(
                (
                    distance,
                    source.GetPixel(
                        x,
                        y)
                ));
        }

        if (samples.Count == 0)
            return Array.Empty<ColorRun>();

        // Drop the leading candidate-fill samples. HalfWidth can be phase-shifted by thinning, so
        // a scan may begin slightly inside the candidate region.
        var index = 0;

        while (index < samples.Count &&
               samples[index].Color ==
                   regionColor)
        {
            index++;
        }

        if (index >= samples.Count ||
            !protectedStrokeColors.Contains(
                samples[index].Color))
        {
            return Array.Empty<ColorRun>();
        }

        var runs =
            new List<ColorRun>();

        while (index < samples.Count &&
               runs.Count <
               MaximumRuns)
        {
            var color =
                samples[index].Color;
            var start =
                samples[index].Distance;
            var end =
                start;
            index++;

            while (index < samples.Count &&
                   samples[index].Color ==
                       color)
            {
                end =
                    samples[index].Distance;
                index++;
            }

            // Distance along the fitted normal is the authority. Unlike counting crossed grid
            // cells, this does not make a 45-degree band appear sqrt(2) thicker.
            var width =
                Math.Max(
                    Step,
                    end -
                    start +
                    Step);

            runs.Add(
                new ColorRun(
                    color,
                    width));

            if (!protectedStrokeColors.Contains(
                    color) &&
                runs.Count > 1)
            {
                break;
            }
        }

        return runs;
    }

    private readonly record struct ColorRun(
        byte Color,
        double Width);

    private readonly record struct SequenceAccumulator(
        int Count,
        IReadOnlyList<double> WidthSums);

    private sealed record SideProfile(
        string Sequence,
        double Coverage,
        double BracketedCoverage,
        int SampleCount,
        int MatchingSamples,
        IReadOnlyList<double> MeanRunWidths);
}

internal readonly record struct LayeredRibbonProfileDiagnostics(
    bool Detected,
    string Side,
    string Sequence,
    double Coverage,
    double BracketedCoverage,
    int SampleCount,
    int MatchingSamples,
    IReadOnlyList<double> MeanRunWidths);
