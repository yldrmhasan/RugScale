namespace RugScale.Core.Drawing;

/// <summary>
/// Specialist inverse fitter for the two visible long sides of an outlined leaf/petal.
///
/// The side is known to be an open designer arc, so we deliberately fit RugScale's
/// SplineThroughPoints tool instead of searching unrelated curve families. Control points remain
/// ON the immutable source raster. Their positions along the source arc and the Curve roundness
/// are optimized together.
///
/// Selection favours:
/// - source-raster agreement,
/// - source endpoint tangent agreement,
/// - low macro curvature oscillation,
/// - few control points.
///
/// This produces a cleaner target-size arc than uniform control sampling while preserving the
/// original designer's path instead of inventing a generic smooth silhouette.
/// </summary>
internal static class LeafPetalBoundaryCurveFitter
{
    private static readonly int[] ControlCounts =
    [
        4,
        5,
        6,
        7,
    ];

    private static readonly double[] RoundnessValues =
    [
        0.10,
        0.15,
        0.20,
        0.25,
        0.35,
        0.50,
        0.70,
        0.85,
        1.00,
    ];

    private const double MinimumCompositeScore = 0.82;
    private const double MinimumNearF1 = 0.955;
    private const double MinimumEndpointTangentScore = 0.84;
    private const double MinimumMacroTangentScore = 0.88;

    private readonly record struct CandidateScore(
        double Composite,
        double NearF1,
        double ExactF1,
        double TangentScore,
        double MacroTangentScore,
        int CurvatureFlips,
        (int X, int Y)[] Controls,
        double Roundness);

    public static ToolFaithfulCurveStyleFit? Fit(
        IReadOnlyList<(int X, int Y)> sourcePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        if (sourcePath.Count < 12)
            return null;

        var sourceSet =
            sourcePath
                .ToHashSet();
        var modelPath =
            RemovePixelCordBridgeCells(
                sourcePath);

        if (modelPath.Count < 10)
            modelPath =
                sourcePath.ToArray();

        CandidateScore? best =
            null;

        foreach (var controlCount in ControlCounts)
        {
            if (controlCount >
                modelPath.Count)
            {
                continue;
            }

            var indices =
                InitialControlIndices(
                    modelPath.Count,
                    controlCount);

            var candidate =
                Optimize(
                    sourcePath,
                    sourceSet,
                    modelPath,
                    indices);

            if (candidate is null)
                continue;

            if (best is null ||
                candidate.Value.Composite >
                best.Value.Composite)
            {
                best =
                    candidate;
            }
        }

        if (best is null ||
            best.Value.Composite <
            MinimumCompositeScore ||
            best.Value.NearF1 <
            MinimumNearF1 ||
            best.Value.TangentScore <
            MinimumEndpointTangentScore ||
            best.Value.MacroTangentScore <
            MinimumMacroTangentScore)
        {
            return null;
        }

        return new ToolFaithfulCurveStyleFit(
            CurveType.SplineThroughPoints,
            best.Value.Roundness,
            best.Value.Controls,
            best.Value.Composite,
            best.Value.Composite,
            PolylineBaselineScore: 0d,
            PolylineBaselineModelScore: 0d,
            SimplifyTolerance: 0d);
    }

    private static CandidateScore? Optimize(
        IReadOnlyList<(int X, int Y)> sourcePath,
        IReadOnlySet<(int X, int Y)> sourceSet,
        IReadOnlyList<(int X, int Y)> modelPath,
        int[] indices)
    {
        var current =
            FindBestRoundness(
                sourcePath,
                sourceSet,
                modelPath,
                indices);

        if (current is null)
            return null;

        var steps =
            new[]
            {
                Math.Max(
                    1,
                    modelPath.Count /
                    10),
                Math.Max(
                    1,
                    modelPath.Count /
                    22),
                2,
                1,
            }
            .Distinct()
            .OrderByDescending(value =>
                value)
            .ToArray();

        foreach (var step in steps)
        {
            for (var pass = 0;
                 pass < 3;
                 pass++)
            {
                var changed =
                    false;

                for (var control = 1;
                     control <
                     indices.Length - 1;
                     control++)
                {
                    var original =
                        indices[control];
                    var minimum =
                        indices[control - 1] +
                        1;
                    var maximum =
                        indices[control + 1] -
                        1;

                    if (minimum >
                        maximum)
                    {
                        continue;
                    }

                    var localBest =
                        current.Value;
                    var localIndex =
                        original;

                    foreach (var trialIndex in new[]
                             {
                                 Math.Clamp(
                                     original - step,
                                     minimum,
                                     maximum),
                                 Math.Clamp(
                                     original + step,
                                     minimum,
                                     maximum),
                             }
                             .Distinct())
                    {
                        if (trialIndex ==
                            original)
                        {
                            continue;
                        }

                        indices[control] =
                            trialIndex;

                        var trial =
                            Evaluate(
                                sourcePath,
                                sourceSet,
                                modelPath,
                                indices,
                                current.Value.Roundness);

                        if (trial is not null &&
                            trial.Value.Composite >
                            localBest.Composite)
                        {
                            localBest =
                                trial.Value;
                            localIndex =
                                trialIndex;
                        }
                    }

                    indices[control] =
                        localIndex;

                    if (localIndex !=
                        original)
                    {
                        current =
                            localBest;
                        changed =
                            true;
                    }
                }

                var roundness =
                    FindBestRoundness(
                        sourcePath,
                        sourceSet,
                        modelPath,
                        indices);

                if (roundness is not null &&
                    roundness.Value.Composite >
                    current.Value.Composite)
                {
                    current =
                        roundness;
                    changed =
                        true;
                }

                if (!changed)
                    break;
            }
        }

        return current;
    }

    private static CandidateScore? FindBestRoundness(
        IReadOnlyList<(int X, int Y)> sourcePath,
        IReadOnlySet<(int X, int Y)> sourceSet,
        IReadOnlyList<(int X, int Y)> modelPath,
        IReadOnlyList<int> indices)
    {
        CandidateScore? best =
            null;

        foreach (var roundness in RoundnessValues)
        {
            var candidate =
                Evaluate(
                    sourcePath,
                    sourceSet,
                    modelPath,
                    indices,
                    roundness);

            if (candidate is null)
                continue;

            if (best is null ||
                candidate.Value.Composite >
                best.Value.Composite)
            {
                best =
                    candidate;
            }
        }

        return best;
    }

    private static CandidateScore? Evaluate(
        IReadOnlyList<(int X, int Y)> sourcePath,
        IReadOnlySet<(int X, int Y)> sourceSet,
        IReadOnlyList<(int X, int Y)> modelPath,
        IReadOnlyList<int> indices,
        double roundness)
    {
        var controls =
            indices
                .Select(index =>
                    modelPath[index])
                .ToArray();

        var curvatureFlips =
            CountMacroCurvatureFlips(
                controls);

        // Leaf/petal sides may contain one genuine S-like transition; more than two means the
        // control set is following pixel staircase noise rather than a designer arc.
        if (curvatureFlips > 2)
            return null;

        var renderedOrdered =
            ConsecutiveDistinct(
                Rasterizer.ConnectDiagonalSteps(
                    CurveRasterizer.Draw(
                        controls,
                        CurveType.SplineThroughPoints,
                        roundness)))
                .ToArray();

        if (renderedOrdered.Length <
            4)
        {
            return null;
        }

        var rendered =
            renderedOrdered
                .ToHashSet();
        var exact =
            F1Exact(
                sourceSet,
                rendered);
        var near =
            F1Near(
                sourceSet,
                rendered,
                radius: 1);
        var areaRatio =
            Math.Min(
                sourceSet.Count,
                rendered.Count) /
            (double)Math.Max(
                sourceSet.Count,
                rendered.Count);
        var tangentScore =
            EndpointTangentScore(
                sourcePath,
                renderedOrdered);
        var macroTangentScore =
            MacroTangentProfileScore(
                sourcePath,
                renderedOrdered);

        // A designer leaf-side arc should normally need only four or five through-points.
        // Six/seven points are still available for genuinely complex source geometry, but receive
        // an increasing penalty so the optimizer cannot win by following individual staircase
        // shoulders. This is deliberately non-linear: the seventh point must buy a meaningful
        // source-fit improvement.
        var complexityPenalty =
            controls.Length switch
            {
                <= 4 => 0d,
                5 => 0.004,
                6 => 0.013,
                _ => 0.026,
            };
        var curvaturePenalty =
            curvatureFlips *
            0.012;

        // Near-F1 is the dominant term because a one-cell digital phase difference is harmless
        // when the same visual arc is preserved. Exact-F1 still matters, endpoint tangent is
        // explicitly rewarded, and complexity/curvature stop overfitting staircase noise.
        var composite =
            near * 0.48 +
            exact * 0.16 +
            tangentScore * 0.12 +
            macroTangentScore * 0.16 +
            areaRatio * 0.08 -
            complexityPenalty -
            curvaturePenalty;

        return new CandidateScore(
            composite,
            near,
            exact,
            tangentScore,
            macroTangentScore,
            curvatureFlips,
            controls,
            roundness);
    }

    private static double MacroTangentProfileScore(
        IReadOnlyList<(int X, int Y)> source,
        IReadOnlyList<(int X, int Y)> candidate)
    {
        if (source.Count < 6 ||
            candidate.Count < 6)
        {
            return 0d;
        }

        const int Samples = 11;
        var total = 0d;
        var used = 0;

        for (var sample = 0;
             sample < Samples;
             sample++)
        {
            var t =
                sample /
                (double)(Samples - 1);
            var sourceTangent =
                EstimateMacroTangent(
                    source,
                    t);
            var candidateTangent =
                EstimateMacroTangent(
                    candidate,
                    t);

            if ((sourceTangent.X == 0d &&
                 sourceTangent.Y == 0d) ||
                (candidateTangent.X == 0d &&
                 candidateTangent.Y == 0d))
            {
                continue;
            }

            var cosine =
                Math.Clamp(
                    sourceTangent.X *
                    candidateTangent.X +
                    sourceTangent.Y *
                    candidateTangent.Y,
                    -1d,
                    1d);

            // Direction matters because both paths are ordered base -> apex. A profile that turns
            // backwards is not an equivalent undirected line.
            total +=
                (cosine + 1d) /
                2d;
            used++;
        }

        return used == 0
            ? 0d
            : total /
              used;
    }

    private static (double X, double Y) EstimateMacroTangent(
        IReadOnlyList<(int X, int Y)> path,
        double t)
    {
        if (path.Count < 2)
            return (0d, 0d);

        var center =
            (int)Math.Round(
                Math.Clamp(
                    t,
                    0d,
                    1d) *
                (path.Count - 1));
        var span =
            Math.Clamp(
                path.Count /
                10,
                3,
                12);
        var before =
            Math.Max(
                0,
                center -
                span);
        var after =
            Math.Min(
                path.Count - 1,
                center +
                span);

        if (after <= before)
            return (0d, 0d);

        return Normalize(
            path[after].X -
            path[before].X,
            path[after].Y -
            path[before].Y);
    }

    private static int[] InitialControlIndices(
        int pathCount,
        int controlCount)
    {
        var result =
            new int[controlCount];

        for (var i = 0;
             i < controlCount;
             i++)
        {
            result[i] =
                (int)Math.Round(
                    i *
                    (pathCount - 1d) /
                    (controlCount - 1d));
        }

        result[0] = 0;
        result[^1] =
            pathCount - 1;

        return result;
    }

    private static double EndpointTangentScore(
        IReadOnlyList<(int X, int Y)> source,
        IReadOnlyList<(int X, int Y)> candidate)
    {
        var sourceStart =
            EstimateStartTangent(
                source);
        var sourceEnd =
            EstimateEndTangent(
                source);
        var candidateStart =
            EstimateStartTangent(
                candidate);
        var candidateEnd =
            EstimateEndTangent(
                candidate);

        return (
                   DirectionAgreement(
                       sourceStart,
                       candidateStart) +
                   DirectionAgreement(
                       sourceEnd,
                       candidateEnd)) /
               2d;
    }

    private static (double X, double Y) EstimateStartTangent(
        IReadOnlyList<(int X, int Y)> path)
    {
        var span =
            Math.Clamp(
                path.Count /
                8,
                3,
                9);
        var end =
            Math.Min(
                path.Count - 1,
                span);

        return Normalize(
            path[end].X -
            path[0].X,
            path[end].Y -
            path[0].Y);
    }

    private static (double X, double Y) EstimateEndTangent(
        IReadOnlyList<(int X, int Y)> path)
    {
        var span =
            Math.Clamp(
                path.Count /
                8,
                3,
                9);
        var start =
            Math.Max(
                0,
                path.Count - 1 - span);

        return Normalize(
            path[^1].X -
            path[start].X,
            path[^1].Y -
            path[start].Y);
    }

    private static (double X, double Y) Normalize(
        double x,
        double y)
    {
        var length =
            Math.Sqrt(
                x * x +
                y * y);

        return length <= 1e-9
            ? (0d, 0d)
            : (
                x / length,
                y / length);
    }

    private static double DirectionAgreement(
        (double X, double Y) left,
        (double X, double Y) right)
    {
        if ((left.X == 0d &&
             left.Y == 0d) ||
            (right.X == 0d &&
             right.Y == 0d))
        {
            return 0d;
        }

        // Map cosine [-1,1] to [0,1]. Ordered paths must point base -> apex, so a reversed tangent
        // is strongly penalised instead of being treated as the same undirected line.
        var cosine =
            Math.Clamp(
                left.X *
                right.X +
                left.Y *
                right.Y,
                -1d,
                1d);

        return (
                   cosine +
                   1d) /
               2d;
    }

    private static IReadOnlyList<(int X, int Y)> RemovePixelCordBridgeCells(
        IReadOnlyList<(int X, int Y)> path)
    {
        if (path.Count < 3)
            return path.ToArray();

        var result =
            new List<(int X, int Y)>(
                path.Count);

        result.Add(
            path[0]);

        var index = 1;

        while (index <
               path.Count - 1)
        {
            var previous =
                result[^1];
            var current =
                path[index];
            var next =
                path[index + 1];

            var diagonalAcross =
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

            if (diagonalAcross &&
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
            path[^1]);

        return result;
    }

    private static IEnumerable<(int X, int Y)> ConsecutiveDistinct(
        IEnumerable<(int X, int Y)> points)
    {
        (int X, int Y)? previous =
            null;

        foreach (var point in points)
        {
            if (previous is not null &&
                previous.Value ==
                point)
            {
                continue;
            }

            yield return point;
            previous =
                point;
        }
    }

    private static int CountMacroCurvatureFlips(
        IReadOnlyList<(int X, int Y)> controls)
    {
        var flips = 0;
        var previousSign = 0;

        for (var i = 1;
             i < controls.Count - 1;
             i++)
        {
            var ax =
                controls[i].X -
                controls[i - 1].X;
            var ay =
                controls[i].Y -
                controls[i - 1].Y;
            var bx =
                controls[i + 1].X -
                controls[i].X;
            var by =
                controls[i + 1].Y -
                controls[i].Y;
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
                Math.Abs(cross) <
                scale *
                0.08)
            {
                continue;
            }

            var sign =
                Math.Sign(
                    cross);

            if (previousSign != 0 &&
                sign !=
                previousSign)
            {
                flips++;
            }

            previousSign =
                sign;
        }

        return flips;
    }

    private static double F1Exact(
        IReadOnlySet<(int X, int Y)> source,
        IReadOnlySet<(int X, int Y)> candidate)
    {
        var intersection =
            source.Count(
                candidate.Contains);
        var precision =
            intersection /
            (double)Math.Max(
                1,
                candidate.Count);
        var recall =
            intersection /
            (double)Math.Max(
                1,
                source.Count);

        return F1(
            precision,
            recall);
    }

    private static double F1Near(
        IReadOnlySet<(int X, int Y)> source,
        IReadOnlySet<(int X, int Y)> candidate,
        int radius)
    {
        var sourceSupported =
            source.Count(point =>
                HasNeighbor(
                    candidate,
                    point,
                    radius));
        var candidateSupported =
            candidate.Count(point =>
                HasNeighbor(
                    source,
                    point,
                    radius));
        var precision =
            candidateSupported /
            (double)Math.Max(
                1,
                candidate.Count);
        var recall =
            sourceSupported /
            (double)Math.Max(
                1,
                source.Count);

        return F1(
            precision,
            recall);
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
}
