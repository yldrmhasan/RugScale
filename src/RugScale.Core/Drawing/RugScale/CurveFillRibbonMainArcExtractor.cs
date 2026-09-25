namespace RugScale.Core.Drawing;

/// <summary>
/// Extracts the dominant one-curvature arch from a recovered broad ribbon path.
///
/// Self-symmetric arches use a local curvature span and symmetric trimming. A ribbon that was
/// denoised from a LEFT/RIGHT mirror pair is different: one member can carry a hook on only its
/// outer endpoint, and its thinned spine can contain many small staircase/branch turns. For that
/// case the extractor searches progressively larger macro curvature spans and keeps the FIRST
/// span that exposes a valid one-sided designer sweep. This retains as much source detail as
/// possible without teaching the model to magnify raster wobble.
/// </summary>
internal static class CurveFillRibbonMainArcExtractor
{
    private const double MinimumNormalizedCurvature = 0.03;
    private const double MinimumKeptFraction = 0.42;
    private const double MaximumKeptFraction = 0.82;
    private const double MinimumOneSidedKeptFraction = 0.48;
    private const double MaximumOneSidedKeptFraction = 0.95;
    private const double MinimumOneSidedTrimFraction = 0.05;
    private const int MinimumSamples = 24;
    private const int MaximumZeroGap = 5;

    private static readonly int[] MirrorMacroDivisors =
    [
        16,
        14,
        12,
        10,
        9,
        8,
        7,
        6,
        5,
    ];

    public static bool TryExtract(
        LeafPetalArcModel model,
        out LeafPetalArcModel extracted,
        out RibbonMainArcExtractionDiagnostics diagnostics) =>
        TryExtractCore(
            model,
            symmetricTrim: true,
            radiusOverride: null,
            out extracted,
            out diagnostics);

    /// <summary>
    /// Extracts the dominant sweep from one member of a mirrored ribbon PAIR. The macro span is
    /// learned from the source path itself: small spans are tried first and larger spans are only
    /// used when raster/branch micro-turns prevent a valid one-curvature sweep.
    /// </summary>
    public static bool TryExtractMirrorFusedSweep(
        LeafPetalArcModel model,
        out LeafPetalArcModel extracted,
        out RibbonMainArcExtractionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        extracted = model;
        diagnostics = default;

        if (model.Samples.Count <
            MinimumSamples)
        {
            diagnostics =
                new RibbonMainArcExtractionDiagnostics(
                    "too-few-samples",
                    0,
                    0,
                    0d,
                    0,
                    0);
            return false;
        }

        RibbonMainArcExtractionDiagnostics? bestFailure = null;
        var bestFailureDistance =
            double.PositiveInfinity;

        foreach (var radius in
                 BuildMirrorMacroRadii(
                     model.Samples.Count))
        {
            if (TryExtractCore(
                    model,
                    symmetricTrim: false,
                    radiusOverride: radius,
                    out extracted,
                    out diagnostics))
            {
                return true;
            }

            var distance =
                DistanceToOneSidedAcceptance(
                    diagnostics);

            if (bestFailure is null ||
                distance <
                bestFailureDistance ||
                Math.Abs(
                    distance -
                    bestFailureDistance) <=
                1e-9 &&
                diagnostics.Radius <
                bestFailure.Value.Radius)
            {
                bestFailure =
                    diagnostics;
                bestFailureDistance =
                    distance;
            }
        }

        extracted =
            model;
        diagnostics =
            bestFailure ??
            new RibbonMainArcExtractionDiagnostics(
                "no-coherent-run",
                0,
                0,
                0d,
                0,
                0);
        return false;
    }

    private static IReadOnlyList<int> BuildMirrorMacroRadii(
        int sampleCount)
    {
        var maximumRadius =
            Math.Max(
                6,
                Math.Min(
                    32,
                    Math.Max(
                        6,
                        sampleCount /
                        3)));

        return MirrorMacroDivisors
            .Select(divisor =>
                Math.Clamp(
                    sampleCount /
                    divisor,
                    6,
                    maximumRadius))
            .Distinct()
            .OrderBy(radius =>
                radius)
            .ToArray();
    }

    private static double DistanceToOneSidedAcceptance(
        RibbonMainArcExtractionDiagnostics diagnostics)
    {
        if (diagnostics.Reason ==
            "no-coherent-run")
        {
            return 10d;
        }

        if (diagnostics.KeptFraction <
            MinimumOneSidedKeptFraction)
        {
            return
                MinimumOneSidedKeptFraction -
                diagnostics.KeptFraction;
        }

        if (diagnostics.KeptFraction >
            MaximumOneSidedKeptFraction)
        {
            return
                diagnostics.KeptFraction -
                MaximumOneSidedKeptFraction;
        }

        return 0d;
    }

    private static bool TryExtractCore(
        LeafPetalArcModel model,
        bool symmetricTrim,
        int? radiusOverride,
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
                    0,
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
            radiusOverride ??
            Math.Clamp(
                samples.Count /
                55,
                3,
                8);

        radius =
            Math.Clamp(
                radius,
                2,
                Math.Max(
                    2,
                    (samples.Count -
                     3) /
                    2));

        var signs =
            BuildCurvatureSigns(
                smoothed,
                radius);

        if (!TryFindLongestCurvatureRun(
                signs,
                radius,
                out var bestStart,
                out var bestEnd,
                out var bestSign))
        {
            diagnostics =
                new RibbonMainArcExtractionDiagnostics(
                    "no-coherent-run",
                    0,
                    0,
                    0d,
                    0,
                    radius);
            return false;
        }

        int startIndex;
        int endIndex;
        double minimumKeptFraction;
        double maximumKeptFraction;

        if (symmetricTrim)
        {
            // Self-symmetry recovery guarantees sample i pairs with sample (N-1-i). Use the larger
            // trim demanded by either side so the specialist arc stays exactly paired.
            var trim =
                Math.Max(
                    bestStart,
                    samples.Count -
                        1 -
                        bestEnd);
            startIndex =
                trim;
            endIndex =
                samples.Count -
                1 -
                trim;
            minimumKeptFraction =
                MinimumKeptFraction;
            maximumKeptFraction =
                MaximumKeptFraction;
        }
        else
        {
            // A mirror-paired region is symmetric with its sibling, not along its own path.
            // Preserve the clean terminal side; only stop when the macro curvature truly changes
            // sign into the hook.
            startIndex =
                bestStart;
            endIndex =
                bestEnd;

            while (startIndex > 0)
            {
                var sign =
                    signs[startIndex - 1];

                if (sign != 0 &&
                    sign != bestSign)
                {
                    break;
                }

                startIndex--;
            }

            while (endIndex <
                   samples.Count - 1)
            {
                var sign =
                    signs[endIndex + 1];

                if (sign != 0 &&
                    sign != bestSign)
                {
                    break;
                }

                endIndex++;
            }

            minimumKeptFraction =
                MinimumOneSidedKeptFraction;
            maximumKeptFraction =
                MaximumOneSidedKeptFraction;
        }

        var kept =
            endIndex -
            startIndex +
            1;
        var keptFraction =
            kept /
            (double)samples.Count;
        var trimmedFraction =
            (samples.Count -
             kept) /
            (double)samples.Count;

        if (kept <
                MinimumSamples ||
            keptFraction <
                minimumKeptFraction ||
            keptFraction >
                maximumKeptFraction ||
            (!symmetricTrim &&
             trimmedFraction <
                 MinimumOneSidedTrimFraction))
        {
            diagnostics =
                new RibbonMainArcExtractionDiagnostics(
                    symmetricTrim
                        ? "kept-fraction"
                        : "one-sided-kept-fraction",
                    startIndex,
                    endIndex,
                    keptFraction,
                    bestSign,
                    radius);
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
                symmetricTrim
                    ? "ok"
                    : "ok-one-sided",
                startIndex,
                endIndex,
                keptFraction,
                bestSign,
                radius);

        return true;
    }

    private static int[] BuildCurvatureSigns(
        IReadOnlyList<LeafPetalAxisSample> smoothed,
        int radius)
    {
        var signs =
            new int[smoothed.Count];

        for (var index = radius;
             index < smoothed.Count - radius;
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

        return signs;
    }

    private static bool TryFindLongestCurvatureRun(
        IReadOnlyList<int> signs,
        int radius,
        out int bestStart,
        out int bestEnd,
        out int bestSign)
    {
        bestStart = -1;
        bestEnd = -1;
        bestSign = 0;
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
                 index < signs.Count - radius;
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

                CommitRun();

                start = -1;
                lastMatching = -1;
                zeroGap = 0;
            }

            CommitRun();

            void CommitRun()
            {
                if (start < 0 ||
                    lastMatching <
                        start)
                {
                    return;
                }

                var length =
                    lastMatching -
                    start +
                    1;

                if (length <=
                    bestLength)
                {
                    return;
                }

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

        return bestStart >= 0 &&
               bestEnd >
               bestStart;
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
                    X =
                        source[index - 1].X *
                            0.25 +
                        source[index].X *
                            0.50 +
                        source[index + 1].X *
                            0.25,
                    Y =
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
    int CurvatureSign,
    int Radius = 0);
