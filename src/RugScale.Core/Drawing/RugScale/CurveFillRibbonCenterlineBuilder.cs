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
    private const int SkeletonPadding = 2;

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

        // Zhang-Suen deliberately skips the outer raster ring. A connected colour region is
        // cropped exactly to its bounding box, so an open ribbon endpoint normally touches that
        // ring. Thinning the unpadded crop therefore leaves a 2-3px cap at the endpoint, which
        // looks like a junction and can make the principal skeleton path start halfway through the
        // real designer arc. Pad with empty pixels so BOTH endpoints are processed as ordinary
        // interior foreground.
        var skeletonWidth =
            checked(
                width +
                SkeletonPadding * 2);
        var skeletonHeight =
            checked(
                height +
                SkeletonPadding * 2);
        var mask =
            new bool[
                checked(
                    skeletonWidth *
                    skeletonHeight)];

        foreach (var pixel in region.Pixels)
        {
            var x =
                pixel %
                sourceWidth -
                region.MinX +
                SkeletonPadding;
            var y =
                pixel /
                sourceWidth -
                region.MinY +
                SkeletonPadding;

            if ((uint)x >=
                    (uint)skeletonWidth ||
                (uint)y >=
                    (uint)skeletonHeight)
            {
                continue;
            }

            mask[
                y *
                skeletonWidth +
                x] = true;
        }

        var skeleton =
            CurveScaleEngine.ThinZhangSuen(
                mask,
                skeletonWidth,
                skeletonHeight);
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
                skeletonWidth,
                skeletonHeight);
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
                skeletonWidth,
                region.MinX -
                    SkeletonPadding,
                region.MinY -
                    SkeletonPadding);

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

        // Remove only sub-pixel staircase phase from the medial path before any macro fitter sees
        // it. This is deliberately source-bounded: tight turns and endpoints are preserved, and no
        // sample is allowed to move more than a fraction of one source cell. The goal is not to
        // smooth the designer curve away; it is to stop 8-connected skeleton phase from becoming
        // visible as a saw-tooth after enlargement.
        samples =
            StabilizeCenterlinePhase(
                samples);

        // A digital skeleton can also carry a one-pixel width wobble. Geometry and width are
        // regularized independently so a cleaner centreline cannot accidentally thicken the band.
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
        const double CrossSectionTangentialWindow = 2.25;
        const int TangentSampleRadius = 3;

        var result =
            new List<LeafPetalAxisSample>(
                path.Count);

        (double X, double Y) GlobalPoint(
            int pathIndex)
        {
            var local =
                path[pathIndex];

            return
                (
                    X:
                        local %
                            localWidth +
                        offsetX,
                    Y:
                        local /
                            localWidth +
                        offsetY
                );
        }

        for (var index = 0;
             index < path.Count;
             index++)
        {
            var point =
                GlobalPoint(
                    index);
            var x =
                point.X;
            var y =
                point.Y;
            var firstTangentIndex =
                Math.Max(
                    0,
                    index -
                    TangentSampleRadius);
            var lastTangentIndex =
                Math.Min(
                    path.Count - 1,
                    index +
                    TangentSampleRadius);
            var firstTangentPoint =
                GlobalPoint(
                    firstTangentIndex);
            var lastTangentPoint =
                GlobalPoint(
                    lastTangentIndex);
            var tangentX =
                lastTangentPoint.X -
                firstTangentPoint.X;
            var tangentY =
                lastTangentPoint.Y -
                firstTangentPoint.Y;
            var tangentLength =
                Math.Sqrt(
                    tangentX *
                        tangentX +
                    tangentY *
                        tangentY);

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

                nearestSquared =
                    Math.Min(
                        nearestSquared,
                        distance);
            }

            var halfWidth =
                Math.Max(
                    0.5,
                    Math.Sqrt(
                        nearestSquared) +
                    0.5);

            // Re-centre the digital skeleton from actual opposite ribbon boundaries. Thinning is
            // topologically excellent but can sit one or more cells off the visual centre on a
            // square-dilated diagonal. Using a LOCAL tangent avoids the PCA-fold problem on broad
            // oval shoulders.
            if (tangentLength >
                    1e-9 &&
                index >= 2 &&
                index <=
                    path.Count - 3)
            {
                tangentX /=
                    tangentLength;
                tangentY /=
                    tangentLength;

                var normalX =
                    -tangentY;
                var normalY =
                    tangentX;
                var positive =
                    double.PositiveInfinity;
                var negative =
                    double.NegativeInfinity;

                foreach (var edge in boundary)
                {
                    var dx =
                        edge.X -
                        x;
                    var dy =
                        edge.Y -
                        y;
                    var along =
                        dx *
                            tangentX +
                        dy *
                            tangentY;

                    if (Math.Abs(
                            along) >
                        CrossSectionTangentialWindow)
                    {
                        continue;
                    }

                    var across =
                        dx *
                            normalX +
                        dy *
                            normalY;

                    if (across > 0d)
                    {
                        positive =
                            Math.Min(
                                positive,
                                across);
                    }
                    else if (across < 0d)
                    {
                        negative =
                            Math.Max(
                                negative,
                                across);
                    }
                }

                if (double.IsFinite(
                        positive) &&
                    double.IsFinite(
                        negative) &&
                    positive -
                        negative >=
                    1.0)
                {
                    var offset =
                        (positive +
                         negative) *
                        0.5;

                    // Never let one noisy cross-section jump the centreline more than two source
                    // cells. Larger corrections indicate cap/branch contamination and fall back to
                    // the topologically safe skeleton point.
                    if (Math.Abs(
                            offset) <=
                        2.0)
                    {
                        x +=
                            normalX *
                            offset;
                        y +=
                            normalY *
                            offset;
                        halfWidth =
                            Math.Max(
                                0.5,
                                (positive -
                                 negative) *
                                    0.5 +
                                0.5);
                    }
                }
            }

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

    private static List<LeafPetalAxisSample> StabilizeCenterlinePhase(
        IReadOnlyList<LeafPetalAxisSample> samples)
    {
        if (samples.Count < 7)
            return samples.ToList();

        const double MaximumSampleShift = 0.58;
        const double MinimumTurnCosine = 0.20;

        var source =
            samples.ToArray();
        var result =
            source.ToArray();

        // One binomial pass is enough to cancel alternating raster phase without rounding away
        // intentional hooks. A five-sample stencil is more stable than a 3-point average on
        // 45-degree staircases and remains local enough for carpet-scale curls.
        for (var index = 2;
             index <= source.Length - 3;
             index++)
        {
            var current =
                source[index];
            var incomingX =
                current.X -
                source[index - 2].X;
            var incomingY =
                current.Y -
                source[index - 2].Y;
            var outgoingX =
                source[index + 2].X -
                current.X;
            var outgoingY =
                source[index + 2].Y -
                current.Y;
            var incomingLength =
                Math.Sqrt(
                    incomingX *
                        incomingX +
                    incomingY *
                        incomingY);
            var outgoingLength =
                Math.Sqrt(
                    outgoingX *
                        outgoingX +
                    outgoingY *
                        outgoingY);

            if (incomingLength <= 1e-9 ||
                outgoingLength <= 1e-9)
            {
                continue;
            }

            var turnCosine =
                (incomingX *
                     outgoingX +
                 incomingY *
                     outgoingY) /
                (incomingLength *
                 outgoingLength);

            // A real tight hook/cusp is designer geometry, not skeleton noise.
            if (turnCosine <
                MinimumTurnCosine)
            {
                continue;
            }

            var targetX =
                (source[index - 2].X +
                 4d *
                    source[index - 1].X +
                 6d *
                    current.X +
                 4d *
                    source[index + 1].X +
                 source[index + 2].X) /
                16d;
            var targetY =
                (source[index - 2].Y +
                 4d *
                    source[index - 1].Y +
                 6d *
                    current.Y +
                 4d *
                    source[index + 1].Y +
                 source[index + 2].Y) /
                16d;
            var shiftX =
                targetX -
                current.X;
            var shiftY =
                targetY -
                current.Y;
            var shift =
                Math.Sqrt(
                    shiftX *
                        shiftX +
                    shiftY *
                        shiftY);

            if (shift >
                MaximumSampleShift)
            {
                var scale =
                    MaximumSampleShift /
                    shift;
                shiftX *=
                    scale;
                shiftY *=
                    scale;
            }

            result[index] =
                current with
                {
                    X =
                        current.X +
                        shiftX,
                    Y =
                        current.Y +
                        shiftY,
                };
        }

        return result.ToList();
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
