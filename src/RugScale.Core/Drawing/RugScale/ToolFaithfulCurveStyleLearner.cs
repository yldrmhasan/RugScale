namespace RugScale.Core.Drawing;

/// <summary>
/// Fits an ordered 1x1 source raster chain back to the RugScale Curve-tool family that most
/// plausibly produced it.
///
/// This is deliberately a deterministic source-trained model. The source raster is ground truth,
/// and RugScale's own CurveRasterizer is the hypothesis family. The learner compares:
/// - Spline Through Points with recovered source-through vertices + roundness search,
/// - compact effective cubic Bezier fits,
/// - clamped B-spline candidates,
/// against a polyline baseline.
///
/// Model selection is complexity-regularized: a compact 4/5-control curve may be preferred over a
/// 25-control polyline even when the polyline wins a few exact source pixels. That is intentional:
/// the compact model is much more likely to recover the designer's original drawing language and
/// gives the correct smooth behaviour when the carpet size changes.
///
/// If evidence is weak, Fit returns null and the caller preserves the original pixel graph
/// edge-for-edge. The learner must never smooth a deliberate corner merely because it can.
/// </summary>
internal static class ToolFaithfulCurveStyleLearner
{
    private const int MinimumChainPixels = 12;
    private const int MaximumFitPixels = 1500;
    private const int MaximumControls = 32;
    private const double MinimumAcceptedRawScore = 0.80;
    private const double MinimumModelGainOverPolyline = 0.003;
    private const double ComplexityPenaltyPerExtraControl = 0.003;
    private const double MaximumComplexityPenalty = 0.09;

    private static readonly double[] SimplifyTolerances =
    [
        0.35,
        0.50,
        0.70,
        0.95,
        1.25,
        1.60,
    ];

    private static readonly double[] ThroughPointRoundness =
    [
        0.10,
        0.18,
        0.25,
        0.35,
        // Oval / ellipse-like carpet curves are especially sensitive in the middle/high
        // roundness range. Keep a denser search there so a half-oval is not forced into
        // the nearest coarse slider bucket and then flattened during target-scale redraw.
        0.42,
        0.50,
        0.60,
        0.70,
        0.78,
        0.85,
        0.92,
        1.00,
    ];

    private static readonly double[] SplineRoundness =
    [
        0.30,
        0.45,
        0.60,
        0.75,
        0.90,
        1.00,
    ];

    public static ToolFaithfulCurveStyleFit? Fit(
        IReadOnlyList<(int X, int Y)> sourceChain,
        bool pixelCord)
    {
        ArgumentNullException.ThrowIfNull(sourceChain);

        if (sourceChain.Count < MinimumChainPixels ||
            sourceChain.Count > MaximumFitPixels)
        {
            return null;
        }

        var first =
            sourceChain[0];
        var last =
            sourceChain[^1];

        // RugScale's current Curve tool families in CurveRasterizer are open-path models. A closed
        // 1x1 outline loop has no uniquely recoverable start/tangent pair from raster alone.
        // Preserve it edge-for-edge instead of forcing an open spline/Bezier interpretation that
        // could move an enclosed fill boundary.
        if (sourceChain.Count >= 4 &&
            Math.Abs(first.X - last.X) <= 1 &&
            Math.Abs(first.Y - last.Y) <= 1)
        {
            return null;
        }

        // A nearly straight chain or one deliberate L/V corner belongs to literal graph/polyline
        // replay. Curvature fitting has no information to add there.
        if (!HasCurvatureEvidence(sourceChain))
            return null;

        var sourceSet =
            sourceChain
                .ToHashSet();

        ToolFaithfulCurveStyleFit? best = null;
        var bestPolylineRawScore = 0d;
        var bestPolylineModelScore =
            double.NegativeInfinity;

        // Polyline baseline + ordinary B-spline hypotheses from RDP controls.
        foreach (var tolerance in SimplifyTolerances)
        {
            var controls =
                Simplify(
                    sourceChain,
                    tolerance);

            controls =
                LimitControlPoints(
                    controls,
                    MaximumControls);

            if (controls.Count < 2)
                continue;

            var polylineRaw =
                Score(
                    sourceSet,
                    RenderPolyline(
                            controls,
                            pixelCord)
                        .ToHashSet());

            var polylineModel =
                ModelScore(
                    polylineRaw,
                    controls.Count);

            if (polylineModel >
                bestPolylineModelScore)
            {
                bestPolylineModelScore =
                    polylineModel;
                bestPolylineRawScore =
                    polylineRaw;
            }

            EvaluateFamily(
                CurveType.Spline,
                SplineRoundness,
                controls,
                sourceSet,
                pixelCord,
                tolerance,
                ref best);
        }

        // Through-points controls are special: unlike Bezier handles, they lie ON the source curve.
        // Recover them by ordered source-index optimization instead of RDP, because high-roundness
        // cardinal splines intentionally overshoot their control polygon and fool RDP into choosing
        // the overshoot extrema rather than the artist's real through-points.
        EvaluateOptimizedThroughPointFits(
            sourceChain,
            sourceSet,
            pixelCord,
            ref best);

        // Bezier handles usually do NOT lie on the rasterized path. Recover an effective cubic by
        // least squares, then optimize its two hidden handles directly against RugScale's rasterizer.
        EvaluateOptimizedBezierFit(
            sourceChain,
            sourceSet,
            pixelCord,
            ref best);

        if (best is null ||
            best.Value.Score <
            MinimumAcceptedRawScore ||
            best.Value.ModelScore <
            bestPolylineModelScore +
            MinimumModelGainOverPolyline)
        {
            return null;
        }

        return best.Value with
        {
            PolylineBaselineScore =
                bestPolylineRawScore,
            PolylineBaselineModelScore =
                bestPolylineModelScore,
        };
    }

    private static void EvaluateOptimizedThroughPointFits(
        IReadOnlyList<(int X, int Y)> sourceChain,
        IReadOnlySet<(int X, int Y)> sourceSet,
        bool pixelCord,
        ref ToolFaithfulCurveStyleFit? best)
    {
        // Keep the complete source chain for Through-Points fitting. Pixel Cord bridge pixels
        // are part of the designer-visible indexed raster; removing them produced smoother-looking
        // hypotheses but measurably reduced target exact-F1 on oval curves. The multi-seed search
        // below addresses local optima without changing the source grid evidence.
        IReadOnlyList<(int X, int Y)> modelChain =
            sourceChain.ToArray();

        if (modelChain.Count < MinimumChainPixels)
            return;

        // Source raster alone can make a 5-control smooth oval and a 6/7-control overfit look
        // almost equally good. Keep the best Through-Points candidate locally and use resize
        // consistency as a tie-break before competing with the other Curve families.
        (double RawScore,
         double ModelScore,
         double ScaleConsistency,
         double Roundness,
         (int X, int Y)[] Controls)? bestThroughPoint = null;

        var maximumControlCount =
            Math.Min(
                7,
                Math.Max(
                    3,
                    modelChain.Count /
                    10));

        for (var controlCount = 3;
             controlCount <= maximumControlCount;
             controlCount++)
        {
            var uniformFractions =
                Enumerable.Range(
                        0,
                        controlCount)
                    .Select(index =>
                        index /
                        (double)(controlCount - 1))
                    .ToArray();

            var seedFractions =
                new List<double[]>
                {
                    uniformFractions,
                };

            // Five through-points are the dominant compact representation for the broad oval
            // motifs used by carpet designers. One uniform initialization can settle in a local
            // raster optimum when the two shoulders have unequal arc length. Add two deterministic
            // asymmetric seeds; the normal source-fit + complexity gates still decide whether any
            // of them is trusted.
            if (controlCount == 5 &&
                modelChain.Count >= 20)
            {
                seedFractions.Add(
                [
                    0.00,
                    0.18,
                    0.43,
                    0.72,
                    1.00,
                ]);
                seedFractions.Add(
                [
                    0.00,
                    0.28,
                    0.57,
                    0.82,
                    1.00,
                ]);

                var axisSeed =
                    BuildDominantAxisOvalSeed(
                        modelChain);

                if (axisSeed is not null)
                {
                    seedFractions.Add(
                        axisSeed
                            .Select(index =>
                                index /
                                (double)(modelChain.Count - 1))
                            .ToArray());
                }
            }

            var seenSeeds =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var fractions in seedFractions)
            {
                var indices =
                    new int[controlCount];

                for (var i = 0;
                     i < controlCount;
                     i++)
                {
                    indices[i] =
                        (int)Math.Round(
                            fractions[i] *
                            (modelChain.Count - 1d));
                }

                indices[0] = 0;
                indices[^1] =
                    modelChain.Count - 1;

                // Rounding a short chain can collapse adjacent fractional seeds. Keep the controls
                // strictly ordered without moving either endpoint.
                for (var i = 1;
                     i < indices.Length - 1;
                     i++)
                {
                    var minimum =
                        indices[i - 1] +
                        1;
                    var maximum =
                        modelChain.Count -
                        (indices.Length - i);

                    indices[i] =
                        Math.Clamp(
                            indices[i],
                            minimum,
                            maximum);
                }

                var seedKey =
                    string.Join(
                        ',',
                        indices);

                if (!seenSeeds.Add(
                        seedKey))
                {
                    continue;
                }

                var (rawScore, roundness) =
                    FindBestThroughRoundness(
                        modelChain,
                        indices,
                        sourceSet,
                        pixelCord);

                var stepCandidates =
                    new[]
                    {
                        Math.Max(
                            1,
                            modelChain.Count /
                            12),
                        Math.Max(
                            1,
                            modelChain.Count /
                            28),
                        2,
                        1,
                    }
                    .Distinct()
                    .OrderByDescending(
                        value => value)
                    .ToArray();

                foreach (var step in stepCandidates)
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
                            var current =
                                indices[controlIndex];
                            var minimum =
                                indices[controlIndex - 1] +
                                1;
                            var maximum =
                                indices[controlIndex + 1] -
                                1;

                            if (minimum > maximum)
                                continue;

                            var candidates =
                                new[]
                                {
                                    Math.Clamp(
                                        current - step,
                                        minimum,
                                        maximum),
                                    Math.Clamp(
                                        current + step,
                                        minimum,
                                        maximum),
                                }
                                .Distinct();

                            var localBestScore =
                                rawScore;
                            var localBestIndex =
                                current;

                            foreach (var candidate in candidates)
                            {
                                if (candidate == current)
                                    continue;

                                indices[controlIndex] =
                                    candidate;

                                var candidateScore =
                                    ScoreThroughPoints(
                                        modelChain,
                                        indices,
                                        roundness,
                                        sourceSet,
                                        pixelCord);

                                if (candidateScore >
                                    localBestScore)
                                {
                                    localBestScore =
                                        candidateScore;
                                    localBestIndex =
                                        candidate;
                                }
                            }

                            indices[controlIndex] =
                                localBestIndex;

                            if (localBestIndex !=
                                current)
                            {
                                rawScore =
                                    localBestScore;
                                changed = true;
                            }
                        }

                        var optimizedRoundness =
                            FindBestThroughRoundness(
                                modelChain,
                                indices,
                                sourceSet,
                                pixelCord);

                        rawScore =
                            optimizedRoundness.Score;
                        roundness =
                            optimizedRoundness.Roundness;

                        if (!changed)
                            break;
                    }
                }

                var controls =
                    indices
                        .Select(index =>
                            modelChain[index])
                        .ToArray();

                var scaleConsistency =
                    ScaleConsistencyScore(
                        controls,
                        roundness,
                        sourceSet,
                        pixelCord);
                var candidateModelScore =
                    ModelScore(
                        rawScore,
                        controls.Length);

                const double SourceModelTieTolerance = 0.012;
                const double ScaleConsistencyTieTolerance = 0.003;

                var shouldReplace =
                    bestThroughPoint is null ||
                    candidateModelScore >
                    bestThroughPoint.Value.ModelScore +
                    SourceModelTieTolerance;

                if (!shouldReplace &&
                    bestThroughPoint is { } current &&
                    candidateModelScore >=
                    current.ModelScore -
                    SourceModelTieTolerance)
                {
                    // Within a source-equivalent band, prefer the candidate that survives a
                    // physical resize cycle. This is specifically what prevents oval shoulders
                    // from becoming scalloped/polygonal at the target size.
                    shouldReplace =
                        scaleConsistency >
                        current.ScaleConsistency +
                        ScaleConsistencyTieTolerance;

                    if (!shouldReplace &&
                        Math.Abs(
                            scaleConsistency -
                            current.ScaleConsistency) <=
                        ScaleConsistencyTieTolerance)
                    {
                        // If both source fit and scale stability are effectively tied, use the
                        // simpler designer model. Five controls are normally enough for one
                        // continuous oval arc and generalize better than raster-following 6/7
                        // control fits.
                        shouldReplace =
                            controls.Length <
                            current.Controls.Length;
                    }
                }

                if (shouldReplace)
                {
                    bestThroughPoint =
                        (
                            rawScore,
                            candidateModelScore,
                            scaleConsistency,
                            roundness,
                            controls);
                }
            }
        }

        if (bestThroughPoint is { } selected)
        {
            ConsiderFit(
                CurveType.SplineThroughPoints,
                selected.Roundness,
                selected.Controls,
                selected.RawScore,
                simplifyTolerance: 0d,
                ref best);
        }
    }

    private static int[]? BuildDominantAxisOvalSeed(
        IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count < 20)
            return null;

        var first =
            points[0];
        var last =
            points[^1];
        var deltaX =
            Math.Abs(
                last.X -
                first.X);
        var deltaY =
            Math.Abs(
                last.Y -
                first.Y);
        var useX =
            deltaX >= deltaY;

        var minimumAxis =
            points.Min(point =>
                useX
                    ? point.X
                    : point.Y);
        var maximumAxis =
            points.Max(point =>
                useX
                    ? point.X
                    : point.Y);
        var axisExtent =
            maximumAxis -
            minimumAxis;
        var startAxis =
            useX
                ? first.X
                : first.Y;
        var endAxis =
            useX
                ? last.X
                : last.Y;
        var endpointSpan =
            Math.Abs(
                endAxis -
                startAxis);

        // This seed is only meaningful for a broad open oval/arch whose endpoints span most of
        // its dominant axis. Waves/hooks can have a large bounding box but a short endpoint span;
        // leave those entirely to the generic seeds.
        if (axisExtent < 8 ||
            endpointSpan <
            axisExtent * 0.70)
        {
            return null;
        }

        double[] fractions =
        [
            0.00,
            0.11,
            0.41,
            0.78,
            1.00,
        ];

        var indices =
            new int[fractions.Length];

        indices[0] = 0;
        indices[^1] =
            points.Count - 1;

        for (var controlIndex = 1;
             controlIndex < indices.Length - 1;
             controlIndex++)
        {
            var targetAxis =
                startAxis +
                (endAxis - startAxis) *
                fractions[controlIndex];
            var minimumIndex =
                indices[controlIndex - 1] +
                1;
            var maximumIndex =
                points.Count -
                (indices.Length - controlIndex);

            var bestIndex =
                minimumIndex;
            var bestDistance =
                double.PositiveInfinity;

            for (var index = minimumIndex;
                 index <= maximumIndex;
                 index++)
            {
                var axis =
                    useX
                        ? points[index].X
                        : points[index].Y;
                var distance =
                    Math.Abs(
                        axis -
                        targetAxis);

                if (distance >=
                    bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestIndex = index;
            }

            indices[controlIndex] =
                bestIndex;
        }

        return indices;
    }

    private static (double Score, double Roundness) FindBestThroughRoundness(
        IReadOnlyList<(int X, int Y)> sourceChain,
        IReadOnlyList<int> indices,
        IReadOnlySet<(int X, int Y)> sourceSet,
        bool pixelCord)
    {
        const double SourceScoreTieTolerance = 0.004;

        var scored =
            ThroughPointRoundness
                .Select(roundness =>
                    (
                        Roundness: roundness,
                        Score: ScoreThroughPoints(
                            sourceChain,
                            indices,
                            roundness,
                            sourceSet,
                            pixelCord)))
                .ToArray();

        var bestSourceScore =
            scored.Max(candidate =>
                candidate.Score);

        var controls =
            indices
                .Select(index =>
                    sourceChain[index])
                .ToArray();

        var bestScore =
            double.NegativeInfinity;
        var bestScaleConsistency =
            double.NegativeInfinity;
        var bestRoundness =
            ThroughPointRoundness[0];

        // The 1.60x consistency probe is deliberately evaluated ONLY for source-equivalent
        // candidates. This keeps the oval tie-break useful without multiplying every coordinate
        // descent iteration by another complete target-scale render.
        foreach (var candidate in scored)
        {
            if (candidate.Score <
                bestSourceScore -
                SourceScoreTieTolerance)
            {
                continue;
            }

            var scaleConsistency =
                ScaleConsistencyScore(
                    controls,
                    candidate.Roundness,
                    sourceSet,
                    pixelCord);

            if (scaleConsistency <
                bestScaleConsistency - 0.002)
            {
                continue;
            }

            if (Math.Abs(
                    scaleConsistency -
                    bestScaleConsistency) <=
                0.002 &&
                candidate.Score <=
                bestScore)
            {
                continue;
            }

            bestScore =
                candidate.Score;
            bestScaleConsistency =
                scaleConsistency;
            bestRoundness =
                candidate.Roundness;
        }

        return (
            bestScore,
            bestRoundness);
    }

    private static double ScoreThroughPoints(
        IReadOnlyList<(int X, int Y)> sourceChain,
        IReadOnlyList<int> indices,
        double roundness,
        IReadOnlySet<(int X, int Y)> sourceSet,
        bool pixelCord)
    {
        var controls =
            indices
                .Select(index =>
                    sourceChain[index])
                .ToArray();

        return Score(
            sourceSet,
            RenderCurve(
                controls,
                CurveType.SplineThroughPoints,
                roundness,
                pixelCord));
    }

    private static double ScaleConsistencyScore(
        IReadOnlyList<(int X, int Y)> controls,
        double roundness,
        IReadOnlySet<(int X, int Y)> sourceSet,
        bool pixelCord)
    {
        const double ProbeScale = 1.60;

        var mappedControls =
            controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            ProbeScale -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            ProbeScale -
                            0.5)))
                .ToArray();

        IEnumerable<(int X, int Y)> enlarged =
            CurveRasterizer.Draw(
                mappedControls,
                CurveType.SplineThroughPoints,
                roundness);

        if (pixelCord)
        {
            enlarged =
                Rasterizer.ConnectDiagonalSteps(
                    enlarged);
        }

        var collapsed =
            enlarged
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) /
                            ProbeScale -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) /
                            ProbeScale -
                            0.5)))
                .ToHashSet();

        return Score(
            sourceSet,
            collapsed);
    }

    private static void EvaluateOptimizedBezierFit(
        IReadOnlyList<(int X, int Y)> sourceChain,
        IReadOnlySet<(int X, int Y)> sourceSet,
        bool pixelCord,
        ref ToolFaithfulCurveStyleFit? best)
    {
        var modelChain =
            pixelCord
                ? RemovePixelCordBridges(
                    sourceChain)
                : sourceChain.ToArray();

        if (modelChain.Count < 8)
            return;

        var controls =
            FitEffectiveCubicBezier(
                modelChain);

        if (controls is null)
            return;

        var mutable =
            controls.ToArray();

        double Evaluate() =>
            Score(
                sourceSet,
                RenderCurve(
                    mutable,
                    CurveType.Bezier,
                    1.0,
                    pixelCord));

        var rawScore =
            Evaluate();

        var minX =
            sourceChain.Min(point =>
                point.X);
        var maxX =
            sourceChain.Max(point =>
                point.X);
        var minY =
            sourceChain.Min(point =>
                point.Y);
        var maxY =
            sourceChain.Max(point =>
                point.Y);
        var extent =
            Math.Max(
                maxX - minX + 1,
                maxY - minY + 1);
        var margin =
            Math.Max(
                4,
                extent /
                2);

        var firstStep =
            Math.Clamp(
                extent /
                10,
                2,
                8);
        var steps =
            new[]
                {
                    firstStep,
                    Math.Max(
                        2,
                        firstStep /
                        2),
                    1,
                }
                .Distinct()
                .OrderByDescending(
                    value => value)
                .ToArray();

        foreach (var step in steps)
        {
            for (var pass = 0;
                 pass < 3;
                 pass++)
            {
                var changed = false;

                for (var controlIndex = 1;
                     controlIndex <= 2;
                     controlIndex++)
                {
                    var original =
                        mutable[controlIndex];
                    var localBest =
                        rawScore;
                    var localPoint =
                        original;

                    foreach (var dx in new[]
                             {
                                 -step,
                                 0,
                                 step,
                             })
                    {
                        foreach (var dy in new[]
                                 {
                                     -step,
                                     0,
                                     step,
                                 })
                        {
                            if (dx == 0 &&
                                dy == 0)
                            {
                                continue;
                            }

                            var candidate =
                                (
                                    X: Math.Clamp(
                                        original.X + dx,
                                        minX - margin,
                                        maxX + margin),
                                    Y: Math.Clamp(
                                        original.Y + dy,
                                        minY - margin,
                                        maxY + margin));

                            mutable[controlIndex] =
                                candidate;

                            var candidateScore =
                                Evaluate();

                            if (candidateScore >
                                localBest)
                            {
                                localBest =
                                    candidateScore;
                                localPoint =
                                    candidate;
                            }
                        }
                    }

                    mutable[controlIndex] =
                        localPoint;

                    if (localPoint !=
                        original)
                    {
                        rawScore =
                            localBest;
                        changed = true;
                    }
                }

                if (!changed)
                    break;
            }
        }

        ConsiderFit(
            CurveType.Bezier,
            roundness: 1.0,
            mutable,
            rawScore,
            simplifyTolerance: 0d,
            ref best);
    }

    private static (int X, int Y)[]? FitEffectiveCubicBezier(
        IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count < 4)
            return null;

        var parameters =
            new double[points.Count];
        var totalLength = 0d;

        for (var i = 1;
             i < points.Count;
             i++)
        {
            var dx =
                points[i].X -
                points[i - 1].X;
            var dy =
                points[i].Y -
                points[i - 1].Y;

            totalLength +=
                Math.Sqrt(
                    dx * dx +
                    dy * dy);
            parameters[i] =
                totalLength;
        }

        if (totalLength <= 0d)
            return null;

        for (var i = 1;
             i < parameters.Length;
             i++)
        {
            parameters[i] /=
                totalLength;
        }

        var p0 =
            points[0];
        var p3 =
            points[^1];

        var a11 = 0d;
        var a12 = 0d;
        var a22 = 0d;
        var c1x = 0d;
        var c2x = 0d;
        var c1y = 0d;
        var c2y = 0d;

        for (var i = 0;
             i < points.Count;
             i++)
        {
            var t =
                parameters[i];
            var oneMinusT =
                1d -
                t;
            var b0 =
                oneMinusT *
                oneMinusT *
                oneMinusT;
            var b1 =
                3d *
                oneMinusT *
                oneMinusT *
                t;
            var b2 =
                3d *
                oneMinusT *
                t *
                t;
            var b3 =
                t *
                t *
                t;

            var rx =
                points[i].X -
                b0 *
                p0.X -
                b3 *
                p3.X;
            var ry =
                points[i].Y -
                b0 *
                p0.Y -
                b3 *
                p3.Y;

            a11 +=
                b1 *
                b1;
            a12 +=
                b1 *
                b2;
            a22 +=
                b2 *
                b2;
            c1x +=
                b1 *
                rx;
            c2x +=
                b2 *
                rx;
            c1y +=
                b1 *
                ry;
            c2y +=
                b2 *
                ry;
        }

        var determinant =
            a11 *
            a22 -
            a12 *
            a12;

        if (Math.Abs(
                determinant) <
            1e-9)
        {
            return null;
        }

        var handle1X =
            (c1x *
             a22 -
             c2x *
             a12) /
            determinant;
        var handle2X =
            (a11 *
             c2x -
             a12 *
             c1x) /
            determinant;
        var handle1Y =
            (c1y *
             a22 -
             c2y *
             a12) /
            determinant;
        var handle2Y =
            (a11 *
             c2y -
             a12 *
             c1y) /
            determinant;

        return
        [
            p0,
            (
                (int)Math.Round(
                    handle1X),
                (int)Math.Round(
                    handle1Y)),
            (
                (int)Math.Round(
                    handle2X),
                (int)Math.Round(
                    handle2Y)),
            p3,
        ];
    }

    private static IReadOnlyList<(int X, int Y)> RemovePixelCordBridges(
        IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count < 3)
            return points.ToArray();

        var result =
            new List<(int X, int Y)>(
                points.Count);

        result.Add(
            points[0]);

        var index = 1;

        while (index <
               points.Count - 1)
        {
            var previous =
                result[^1];
            var current =
                points[index];
            var next =
                points[index + 1];

            var previousToNextDiagonal =
                Math.Abs(
                    next.X -
                    previous.X) == 1 &&
                Math.Abs(
                    next.Y -
                    previous.Y) == 1;
            var currentTouchesPrevious =
                Math.Abs(
                    current.X -
                    previous.X) +
                Math.Abs(
                    current.Y -
                    previous.Y) == 1;
            var nextTouchesCurrent =
                Math.Abs(
                    next.X -
                    current.X) +
                Math.Abs(
                    next.Y -
                    current.Y) == 1;

            if (previousToNextDiagonal &&
                currentTouchesPrevious &&
                nextTouchesCurrent)
            {
                index++;
                continue;
            }

            result.Add(
                current);
            index++;
        }

        result.Add(
            points[^1]);

        return result;
    }

    private static void EvaluateFamily(
        CurveType type,
        IReadOnlyList<double> roundnessValues,
        IReadOnlyList<(int X, int Y)> controls,
        IReadOnlySet<(int X, int Y)> sourceSet,
        bool pixelCord,
        double simplifyTolerance,
        ref ToolFaithfulCurveStyleFit? best)
    {
        foreach (var roundness in roundnessValues)
        {
            var rawScore =
                Score(
                    sourceSet,
                    RenderCurve(
                        controls,
                        type,
                        roundness,
                        pixelCord));

            ConsiderFit(
                type,
                roundness,
                controls,
                rawScore,
                simplifyTolerance,
                ref best);
        }
    }

    private static void ConsiderFit(
        CurveType type,
        double roundness,
        IReadOnlyList<(int X, int Y)> controls,
        double rawScore,
        double simplifyTolerance,
        ref ToolFaithfulCurveStyleFit? best)
    {
        var modelScore =
            ModelScore(
                rawScore,
                controls.Count);

        // Tiny deterministic tie preference only. It never compensates for a meaningful fit loss.
        modelScore +=
            type switch
            {
                CurveType.SplineThroughPoints => 0.00030,
                CurveType.Spline => 0.00015,
                _ => 0d,
            };

        if (best is not null &&
            modelScore <=
            best.Value.ModelScore)
        {
            return;
        }

        best =
            new ToolFaithfulCurveStyleFit(
                type,
                roundness,
                controls.ToArray(),
                rawScore,
                modelScore,
                PolylineBaselineScore: 0d,
                PolylineBaselineModelScore: 0d,
                SimplifyTolerance: simplifyTolerance);
    }

    private static double ModelScore(
        double rawScore,
        int controlCount)
    {
        var penalty =
            Math.Min(
                MaximumComplexityPenalty,
                Math.Max(
                    0,
                    controlCount - 2) *
                ComplexityPenaltyPerExtraControl);

        return rawScore -
               penalty;
    }

    private static HashSet<(int X, int Y)> RenderCurve(
        IReadOnlyList<(int X, int Y)> controls,
        CurveType type,
        double roundness,
        bool pixelCord)
    {
        IEnumerable<(int X, int Y)> rendered =
            CurveRasterizer.Draw(
                controls,
                type,
                roundness);

        if (pixelCord)
        {
            rendered =
                Rasterizer.ConnectDiagonalSteps(
                    rendered);
        }

        return rendered
            .ToHashSet();
    }

    private static double Score(
        IReadOnlySet<(int X, int Y)> source,
        IReadOnlySet<(int X, int Y)> candidate)
    {
        if (source.Count == 0 ||
            candidate.Count == 0)
        {
            return 0d;
        }

        var exactIntersection =
            source.Count(
                candidate.Contains);

        var exactPrecision =
            exactIntersection /
            (double)candidate.Count;
        var exactRecall =
            exactIntersection /
            (double)source.Count;
        var exactF1 =
            F1(
                exactPrecision,
                exactRecall);

        var sourceNear =
            source.Count(point =>
                HasNeighbor(
                    candidate,
                    point,
                    radius: 1));
        var candidateNear =
            candidate.Count(point =>
                HasNeighbor(
                    source,
                    point,
                    radius: 1));

        var nearPrecision =
            candidateNear /
            (double)candidate.Count;
        var nearRecall =
            sourceNear /
            (double)source.Count;
        var nearF1 =
            F1(
                nearPrecision,
                nearRecall);

        var areaRatio =
            Math.Min(
                source.Count,
                candidate.Count) /
            (double)Math.Max(
                source.Count,
                candidate.Count);

        return nearF1 * 0.58 +
               exactF1 * 0.34 +
               areaRatio * 0.08;
    }

    private static double F1(
        double precision,
        double recall)
    {
        var sum =
            precision +
            recall;

        return sum <= 0d
            ? 0d
            : 2d *
              precision *
              recall /
              sum;
    }

    private static bool HasNeighbor(
        IReadOnlySet<(int X, int Y)> points,
        (int X, int Y) point,
        int radius)
    {
        for (var dy = -radius;
             dy <= radius;
             dy++)
        {
            for (var dx = -radius;
                 dx <= radius;
                 dx++)
            {
                if (points.Contains(
                        (
                            point.X + dx,
                            point.Y + dy)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasCurvatureEvidence(
        IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count < 3)
            return false;

        var first =
            points[0];
        var last =
            points[^1];

        var maxDeviation = 0d;

        for (var i = 1;
             i < points.Count - 1;
             i++)
        {
            maxDeviation =
                Math.Max(
                    maxDeviation,
                    Math.Sqrt(
                        DistanceToSegmentSquared(
                            points[i],
                            first,
                            last)));
        }

        if (maxDeviation < 1.25)
            return false;

        var turns = 0;
        (int X, int Y)? previousDirection = null;

        for (var i = 1;
             i < points.Count;
             i++)
        {
            var dx =
                Math.Sign(
                    points[i].X -
                    points[i - 1].X);
            var dy =
                Math.Sign(
                    points[i].Y -
                    points[i - 1].Y);

            if (dx == 0 &&
                dy == 0)
            {
                continue;
            }

            var direction =
                (dx, dy);

            if (previousDirection is not null &&
                previousDirection.Value !=
                direction)
            {
                turns++;
            }

            previousDirection = direction;
        }

        return turns >= 3;
    }

    private static IEnumerable<(int X, int Y)> RenderPolyline(
        IReadOnlyList<(int X, int Y)> points,
        bool pixelCord)
    {
        IEnumerable<(int X, int Y)> Render()
        {
            for (var i = 1;
                 i < points.Count;
                 i++)
            {
                foreach (var point in Rasterizer.Line(
                             points[i - 1].X,
                             points[i - 1].Y,
                             points[i].X,
                             points[i].Y))
                {
                    yield return point;
                }
            }
        }

        var rendered =
            Render();

        return pixelCord
            ? Rasterizer.ConnectDiagonalSteps(
                rendered)
            : rendered;
    }

    private static IReadOnlyList<(int X, int Y)> Simplify(
        IReadOnlyList<(int X, int Y)> points,
        double tolerance)
    {
        if (points.Count <= 2)
            return points.ToArray();

        var keep =
            new bool[points.Count];

        keep[0] = true;
        keep[^1] = true;

        SimplifyRange(
            points,
            0,
            points.Count - 1,
            tolerance * tolerance,
            keep);

        var result =
            new List<(int X, int Y)>();

        for (var i = 0;
             i < points.Count;
             i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }

        return result;
    }

    private static void SimplifyRange(
        IReadOnlyList<(int X, int Y)> points,
        int first,
        int last,
        double toleranceSquared,
        bool[] keep)
    {
        if (last <=
            first + 1)
        {
            return;
        }

        var a =
            points[first];
        var b =
            points[last];

        var bestDistance = -1d;
        var bestIndex = -1;

        for (var i = first + 1;
             i < last;
             i++)
        {
            var distance =
                DistanceToSegmentSquared(
                    points[i],
                    a,
                    b);

            if (distance <=
                bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex < 0 ||
            bestDistance <=
            toleranceSquared)
        {
            return;
        }

        keep[bestIndex] = true;

        SimplifyRange(
            points,
            first,
            bestIndex,
            toleranceSquared,
            keep);
        SimplifyRange(
            points,
            bestIndex,
            last,
            toleranceSquared,
            keep);
    }

    private static double DistanceToSegmentSquared(
        (int X, int Y) point,
        (int X, int Y) a,
        (int X, int Y) b)
    {
        var vx =
            b.X -
            a.X;
        var vy =
            b.Y -
            a.Y;
        var wx =
            point.X -
            a.X;
        var wy =
            point.Y -
            a.Y;
        var lengthSquared =
            vx *
            vx +
            vy *
            vy;

        if (lengthSquared <= 0)
        {
            return wx *
                   wx +
                   wy *
                   wy;
        }

        var t =
            Math.Clamp(
                (wx * vx +
                 wy * vy) /
                (double)lengthSquared,
                0d,
                1d);

        var dx =
            point.X -
            (a.X +
             vx * t);
        var dy =
            point.Y -
            (a.Y +
             vy * t);

        return dx *
               dx +
               dy *
               dy;
    }

    private static IReadOnlyList<(int X, int Y)> LimitControlPoints(
        IReadOnlyList<(int X, int Y)> controls,
        int maximum)
    {
        if (controls.Count <= maximum)
            return controls;

        var result =
            new List<(int X, int Y)>(
                maximum);

        for (var i = 0;
             i < maximum;
             i++)
        {
            var index =
                (int)Math.Round(
                    i *
                    (controls.Count - 1d) /
                    (maximum - 1d));

            var point =
                controls[index];

            if (result.Count == 0 ||
                result[^1] != point)
            {
                result.Add(point);
            }
        }

        return result;
    }
}

internal readonly record struct ToolFaithfulCurveStyleFit(
    CurveType Type,
    double Roundness,
    IReadOnlyList<(int X, int Y)> Controls,
    double Score,
    double ModelScore,
    double PolylineBaselineScore,
    double PolylineBaselineModelScore,
    double SimplifyTolerance);
