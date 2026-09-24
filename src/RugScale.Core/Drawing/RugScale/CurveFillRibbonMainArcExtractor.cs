namespace RugScale.Core.Drawing;

/// <summary>
/// Extracts the dominant one-curvature arch from a mirror-recovered broad ribbon path.
///
/// Some carpet ornaments intentionally attach small same-colour hooks/shoulders to both ends of a
/// large oval arch. The complete component is one connected region, so its principal skeleton path
/// correctly includes those terminal hooks. Trying to explain the entire path with one smooth
/// Curve, however, creates two false "family" curvature reversals and rejects the visually obvious
/// central arch.
///
/// This helper is intentionally used only AFTER source-mirror recovery on broad multi-endpoint
/// ribbons. It finds the longest macro-scale run with one curvature sign, then trims both ends by
/// the same amount so the recovered main arc remains exactly mirror-paired. The detached terminal
/// hooks are not deleted; they simply remain owned by the baseline categorical resize while the
/// central oval segment receives specialist Curve redraw.
/// </summary>
internal static class CurveFillRibbonMainArcExtractor
{
    private const double MinimumNormalizedCurvature = 0.03;
    private const double MinimumKeptFraction = 0.42;
    private const double MaximumKeptFraction = 0.82;
    private const int MinimumSamples = 24;
    private const int MaximumZeroGap = 5;

    public static bool TryExtract(
        LeafPetalArcModel model,
        out LeafPetalArcModel extracted,
        out RibbonMainArcExtractionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        extracted = model;
        diagnostics = default;

        var samples =
            model.Samples;

        if (samples.Count <
            MinimumSamples)
        {
            diagnostics =
                new RibbonMainArcExtractionDiagnostics(
                    "too-few-samples",
                    0,
                    0,
                    0d,
                    0);
            return false;
        }

        var smoothed =
            SmoothGeometry(
                samples);
        smoothed =
            SmoothGeometry(
                smoothed);

        var radius =
            Math.Clamp(
                samples.Count /
                55,
                3,
                8);
        var signs =
            new int[samples.Count];

        for (var index = radius;
             index < samples.Count - radius;
             index++)
        {
            var previous =
                smoothed[index - radius];
            var current =
                smoothed[index];
            var next =
                smoothed[index + radius];
            var ax =
                current.X -
                previous.X;
            var ay =
                current.Y -
                previous.Y;
            var bx =
                next.X -
                current.X;
            var by =
                next.Y -
                current.Y;
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

            if (scale <=
                1e-9)
            {
                continue;
            }

            var normalized =
                (ax *
                     by -
                 ay *
                     bx) /
                scale;

            if (Math.Abs(
                    normalized) <
                MinimumNormalizedCurvature)
            {
                continue;
            }

            signs[index] =
                Math.Sign(
                    normalized);
        }

        var bestStart = -1;
        var bestEnd = -1;
        var bestSign = 0;
        var bestLength = 0;

        foreach (var sign in
                 new[]
                 {
                     -1,
                     1,
                 })
        {
            var start = -1;
            var lastMatching = -1;
            var zeroGap = 0;

            for (var index = radius;
                 index < samples.Count - radius;
                 index++)
            {
                if (signs[index] ==
                    sign)
                {
                    if (start < 0)
                    {
                        start =
                            index;
                    }

                    lastMatching =
                        index;
                    zeroGap = 0;
                    continue;
                }

                if (start >= 0 &&
                    signs[index] == 0 &&
                    zeroGap <
                    MaximumZeroGap)
                {
                    zeroGap++;
                    continue;
                }

                if (start >= 0 &&
                    lastMatching >=
                        start)
                {
                    var length =
                        lastMatching -
                        start +
                        1;

                    if (length >
                        bestLength)
                    {
                        bestLength =
                            length;
                        bestStart =
                            start;
                        bestEnd =
                            lastMatching;
                        bestSign =
                            sign;
                    }
                }

                start = -1;
                lastMatching = -1;
                zeroGap = 0;
            }

            if (start >= 0 &&
                lastMatching >=
                    start)
            {
                var length =
                    lastMatching -
                    start +
                    1;

                if (length >
                    bestLength)
                {
                    bestLength =
                        length;
                    bestStart =
                        start;
                    bestEnd =
                        lastMatching;
                    bestSign =
                        sign;
                }
            }
        }

        if (bestStart < 0 ||
            bestEnd <=
                bestStart)
        {
            diagnostics =
                new RibbonMainArcExtractionDiagnostics(
                    "no-coherent-run",
                    0,
                    0,
                    0d,
                    0);
            return false;
        }

        // Symmetry recovery guarantees sample i pairs with sample (N-1-i). Use the larger trim
        // demanded by either side so the specialist arc cannot become asymmetrical due to a
        // one-sided curvature quantization at the shoulder.
        var trim =
            Math.Max(
                bestStart,
                samples.Count -
                    1 -
                    bestEnd);
        var startIndex =
            trim;
        var endIndex =
            samples.Count -
            1 -
            trim;
        var kept =
            endIndex -
            startIndex +
            1;
        var keptFraction =
            kept /
            (double)samples.Count;

        if (kept <
                MinimumSamples ||
            keptFraction <
                MinimumKeptFraction ||
            keptFraction >
                MaximumKeptFraction)
        {
            diagnostics =
                new RibbonMainArcExtractionDiagnostics(
                    "kept-fraction",
                    startIndex,
                    endIndex,
                    keptFraction,
                    bestSign);
            return false;
        }

        var arc =
            new LeafPetalAxisSample[
                kept];

        for (var sourceIndex = startIndex;
             sourceIndex <= endIndex;
             sourceIndex++)
        {
            var targetIndex =
                sourceIndex -
                startIndex;
            var sourceSample =
                samples[sourceIndex];

            arc[targetIndex] =
                sourceSample with
                {
                    AxisPosition =
                        targetIndex /
                        (double)Math.Max(
                            1,
                            kept -
                            1),
                };
        }

        var terminalWidth =
            Math.Max(
                0.5,
                (arc[0].HalfWidth +
                 arc[^1].HalfWidth) *
                0.5);

        extracted =
            new LeafPetalArcModel(
                model.Candidate,
                arc,
                ReversedForApex: false,
                BaseWidth: terminalWidth,
                ApexWidth: terminalWidth,
                SkeletonCoverage:
                    model.SkeletonCoverage);

        diagnostics =
            new RibbonMainArcExtractionDiagnostics(
                "ok",
                startIndex,
                endIndex,
                keptFraction,
                bestSign);

        return true;
    }

    private static LeafPetalAxisSample[] SmoothGeometry(
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var result =
            source.ToArray();

        for (var index = 1;
             index < source.Count - 1;
             index++)
        {
            result[index] =
                source[index] with
                {
                    X:
                        source[index - 1].X *
                            0.25 +
                        source[index].X *
                            0.50 +
                        source[index + 1].X *
                            0.25,
                    Y:
                        source[index - 1].Y *
                            0.25 +
                        source[index].Y *
                            0.50 +
                        source[index + 1].Y *
                            0.25,
                };
        }

        return result;
    }
}

internal readonly record struct RibbonMainArcExtractionDiagnostics(
    string Reason,
    int StartIndex,
    int EndIndex,
    double KeptFraction,
    int CurvatureSign);
