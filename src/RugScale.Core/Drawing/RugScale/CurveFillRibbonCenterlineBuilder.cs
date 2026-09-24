namespace RugScale.Core.Drawing;

/// <summary>
/// Builds an ordered centreline for a curved filled ribbon from its thinned source region.
///
/// Unlike <see cref="LeafPetalMedialAxisBuilder"/>, this does not project the whole region onto one
/// PCA axis. A broad oval arc is not single-valued in that projection: its shoulders can fold back
/// over the same major-axis coordinate and make an otherwise clean ribbon look discontinuous.
/// Instead, the actual digital medial skeleton is traced from end to end and only then smoothed by
/// <see cref="ElegantArcFitter"/>.
///
/// Small thinning forks are tolerated. The principal ribbon spine is the longest shortest path
/// between skeleton endpoints; short terminal artefacts therefore cannot become the geometry
/// authority. Local half-width is measured against the original region boundary, not inferred from
/// the enlarged bitmap.
/// </summary>
internal static class CurveFillRibbonCenterlineBuilder
{
    private const int MinimumSkeletonPixels = 8;
    private const int MinimumPathPixels = 7;
    private const double MinimumPrincipalPathCoverage = 0.58;

    private static readonly (int X, int Y)[] EightDirections =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0),           (1, 0),
        (-1, 1),  (0, 1), (1, 1),
    ];

    public static bool TryBuild(
        LeafPetalArcCandidate candidate,
        int sourceWidth,
        out LeafPetalArcModel model,
        out RibbonCenterlineBuildDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        model = null!;
        diagnostics = default;
        var region =
            candidate.Region;
        var width =
            region.Width;
        var height =
            region.Height;

        if (width < 3 ||
            height < 3)
        {
            return false;
        }

        var mask =
            new bool[
                checked(
                    width *
                    height)];

        foreach (var pixel in region.Pixels)
        {
            var x =
                pixel %
                sourceWidth -
                region.MinX;
            var y =
                pixel /
                sourceWidth -
                region.MinY;

            if ((uint)x >=
                    (uint)width ||
                (uint)y >=
                    (uint)height)
            {
                continue;
            }

            mask[
                y *
                width +
                x] = true;
        }

        var skeleton =
            CurveScaleEngine.ThinZhangSuen(
                mask,
                width,
                height);
        var skeletonPixels =
            Enumerable.Range(
                    0,
                    skeleton.Length)
                .Where(index =>
                    skeleton[index])
                .ToArray();

        if (skeletonPixels.Length <
            MinimumSkeletonPixels)
        {
            diagnostics = new RibbonCenterlineBuildDiagnostics(
                skeletonPixels.Length,
                0,
                0,
                0d,
                "skeleton-too-short");
            return false;
        }

        var adjacency =
            BuildAdjacency(
                skeleton,
                width,
                height);
        var endpoints =
            skeletonPixels
                .Where(pixel =>
                    adjacency[pixel].Count == 1)
                .ToArray();

        if (endpoints.Length < 2)
        {
            diagnostics = new RibbonCenterlineBuildDiagnostics(
                skeletonPixels.Length,
                endpoints.Length,
                0,
                0d,
                "not-enough-endpoints");
            return false;
        }

        if (!TryFindPrincipalPath(
                endpoints,
                adjacency,
                out var path) ||
            path.Count <
                MinimumPathPixels)
        {
            diagnostics = new RibbonCenterlineBuildDiagnostics(
                skeletonPixels.Length,
                endpoints.Length,
                path.Count,
                path.Count /
                    (double)Math.Max(
                        1,
                        skeletonPixels.Length),
                "principal-path-too-short");
            return false;
        }

        var pathCoverage =
            path.Count /
            (double)Math.Max(
                1,
                skeletonPixels.Length);

        if (pathCoverage <
            MinimumPrincipalPathCoverage)
        {
            diagnostics = new RibbonCenterlineBuildDiagnostics(
                skeletonPixels.Length,
                endpoints.Length,
                path.Count,
                pathCoverage,
                "principal-path-coverage");
            return false;
        }

        var boundary =
            region.BoundaryPixels
                .Select(pixel =>
                    (
                        X: pixel %
                           sourceWidth,
                        Y: pixel /
                           sourceWidth))
                .ToArray();

        if (boundary.Length == 0)
        {
            diagnostics = new RibbonCenterlineBuildDiagnostics(
                skeletonPixels.Length,
                endpoints.Length,
                path.Count,
                pathCoverage,
                "no-boundary");
            return false;
        }

        var samples =
            BuildSamples(
                path,
                boundary,
                width,
                region.MinX,
                region.MinY);

        if (samples.Count <
            MinimumPathPixels)
        {
            diagnostics = new RibbonCenterlineBuildDiagnostics(
                skeletonPixels.Length,
                endpoints.Length,
                path.Count,
                pathCoverage,
                "not-enough-samples");
            return false;
        }

        // A digital skeleton can carry a one-pixel width wobble. Smooth width only; geometry is
        // left to ElegantArcFitter's source-bounded macro-curve fit.
        samples =
            SmoothWidths(
                samples);

        var terminalWindow =
            Math.Clamp(
                samples.Count /
                8,
                2,
                5);
        var firstWidth =
            samples
                .Take(
                    terminalWindow)
                .Average(sample =>
                    sample.HalfWidth);
        var lastWidth =
            samples
                .TakeLast(
                    terminalWindow)
                .Average(sample =>
                    sample.HalfWidth);

        diagnostics = new RibbonCenterlineBuildDiagnostics(
            skeletonPixels.Length,
            endpoints.Length,
            path.Count,
            pathCoverage,
            "ok");

        model =
            new LeafPetalArcModel(
                candidate,
                samples,
                ReversedForApex: false,
                BaseWidth: firstWidth,
                ApexWidth: lastWidth,
                SkeletonCoverage:
                    pathCoverage);

        return true;
    }

    private static Dictionary<int, List<int>> BuildAdjacency(
        IReadOnlyList<bool> skeleton,
        int width,
        int height)
    {
        var result =
            new Dictionary<int, List<int>>();

        for (var index = 0;
             index < skeleton.Count;
             index++)
        {
            if (!skeleton[index])
                continue;

            var x =
                index %
                width;
            var y =
                index /
                width;
            var neighbors =
                new List<int>(8);

            foreach (var (dx, dy) in EightDirections)
            {
                var nx =
                    x +
                    dx;
                var ny =
                    y +
                    dy;

                if (nx < 0 ||
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                var next =
                    ny *
                    width +
                    nx;

                if (!skeleton[next])
                    continue;

                if (dx != 0 &&
                    dy != 0)
                {
                    // A diagonal is only a real skeleton edge when the staircase has no
                    // orthogonal bridge. This removes artificial degree-3 triangles.
                    var horizontal =
                        y *
                        width +
                        nx;
                    var vertical =
                        ny *
                        width +
                        x;

                    if (skeleton[horizontal] ||
                        skeleton[vertical])
                    {
                        continue;
                    }
                }

                neighbors.Add(
                    next);
            }

            result[index] =
                neighbors;
        }

        return result;
    }

    private static bool TryFindPrincipalPath(
        IReadOnlyList<int> endpoints,
        IReadOnlyDictionary<int, List<int>> adjacency,
        out List<int> path)
    {
        path =
            new List<int>();
        var bestDistance = -1;

        // Endpoint counts on accepted ribbon candidates are tiny. BFS from every endpoint keeps
        // this deterministic and robust to a few terminal thinning spurs.
        foreach (var start in endpoints)
        {
            var queue =
                new Queue<int>();
            var distance =
                new Dictionary<int, int>();
            var previous =
                new Dictionary<int, int>();

            queue.Enqueue(
                start);
            distance[start] = 0;

            while (queue.Count > 0)
            {
                var current =
                    queue.Dequeue();

                if (!adjacency.TryGetValue(
                        current,
                        out var neighbors))
                {
                    continue;
                }

                foreach (var next in neighbors)
                {
                    if (distance.ContainsKey(
                            next))
                    {
                        continue;
                    }

                    distance[next] =
                        distance[current] +
                        1;
                    previous[next] =
                        current;
                    queue.Enqueue(
                        next);
                }
            }

            foreach (var goal in endpoints)
            {
                if (goal == start ||
                    !distance.TryGetValue(
                        goal,
                        out var candidateDistance) ||
                    candidateDistance <=
                        bestDistance)
                {
                    continue;
                }

                var reversed =
                    new List<int>
                    {
                        goal,
                    };
                var cursor =
                    goal;

                while (cursor !=
                       start)
                {
                    if (!previous.TryGetValue(
                            cursor,
                            out cursor))
                    {
                        reversed.Clear();
                        break;
                    }

                    reversed.Add(
                        cursor);
                }

                if (reversed.Count == 0)
                    continue;

                reversed.Reverse();
                path =
                    reversed;
                bestDistance =
                    candidateDistance;
            }
        }

        return path.Count > 0;
    }

    private static List<LeafPetalAxisSample> BuildSamples(
        IReadOnlyList<int> path,
        IReadOnlyList<(int X, int Y)> boundary,
        int localWidth,
        int offsetX,
        int offsetY)
    {
        var result =
            new List<LeafPetalAxisSample>(
                path.Count);

        for (var index = 0;
             index < path.Count;
             index++)
        {
            var local =
                path[index];
            var x =
                local %
                localWidth +
                offsetX;
            var y =
                local /
                localWidth +
                offsetY;
            var nearestSquared =
                double.PositiveInfinity;

            foreach (var edge in boundary)
            {
                var dx =
                    edge.X -
                    x;
                var dy =
                    edge.Y -
                    y;
                var distance =
                    dx *
                    dx +
                    dy *
                    dy;

                if (distance <
                    nearestSquared)
                {
                    nearestSquared =
                        distance;
                }
            }

            // Boundary pixels represent occupied cell centres. +0.5 approximates the outer cell
            // edge so a 5px digital ribbon does not collapse to a 4px geometric band.
            var halfWidth =
                Math.Max(
                    0.5,
                    Math.Sqrt(
                        nearestSquared) +
                    0.5);

            result.Add(
                new LeafPetalAxisSample(
                    x,
                    y,
                    halfWidth,
                    index /
                    (double)Math.Max(
                        1,
                        path.Count - 1)));
        }

        return result;
    }

    private static List<LeafPetalAxisSample> SmoothWidths(
        IReadOnlyList<LeafPetalAxisSample> samples)
    {
        var result =
            new List<LeafPetalAxisSample>(
                samples.Count);

        for (var index = 0;
             index < samples.Count;
             index++)
        {
            var first =
                Math.Max(
                    0,
                    index - 2);
            var last =
                Math.Min(
                    samples.Count - 1,
                    index + 2);
            var sum = 0d;
            var weight = 0d;

            for (var sampleIndex = first;
                 sampleIndex <= last;
                 sampleIndex++)
            {
                var distance =
                    Math.Abs(
                        sampleIndex -
                        index);
                var localWeight =
                    distance switch
                    {
                        0 => 3d,
                        1 => 2d,
                        _ => 1d,
                    };

                sum +=
                    samples[sampleIndex].HalfWidth *
                    localWeight;
                weight +=
                    localWeight;
            }

            result.Add(
                samples[index] with
                {
                    HalfWidth =
                        Math.Max(
                            0.5,
                            sum /
                            Math.Max(
                                1d,
                                weight)),
                });
        }

        return result;
    }
}


internal readonly record struct RibbonCenterlineBuildDiagnostics(
    int SkeletonPixels,
    int Endpoints,
    int PrincipalPathPixels,
    double PrincipalPathCoverage,
    string Reason);
