using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Recovers the two long outer sides of an outlined leaf/petal as independent Curve-tool paths.
/// This solves the main visual weakness of generic contour interpolation: the leaf is treated as
/// two intentional designer arcs (base -> apex) rather than a softened bitmap silhouette.
/// </summary>
internal static class LeafPetalBoundaryCurveBuilder
{
    private const double MinimumProtectedOutlineCoverage = 0.72;
    private const int MinimumSideSamples = 8;

    private sealed class Bin
    {
        public bool HasMinimum;
        public bool HasMaximum;
        public double MinimumNormal = double.PositiveInfinity;
        public double MaximumNormal = double.NegativeInfinity;
        public (int X, int Y) MinimumPoint;
        public (int X, int Y) MaximumPoint;
    }

    internal static bool TryMeasureOutlineEvidence(
        DesignDocument source,
        LeafPetalRegion region,
        IReadOnlySet<byte> protectedStrokeColors,
        out byte outlineColor,
        out double coverage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        var regionSet =
            region.Pixels
                .ToHashSet();

        return TryFindOutlineColor(
            source,
            region.BoundaryPixels,
            regionSet,
            protectedStrokeColors,
            out outlineColor,
            out coverage);
    }

    public static bool TryBuild(
        DesignDocument source,
        LeafPetalArcModel model,
        IReadOnlySet<byte> protectedStrokeColors,
        out LeafPetalBoundaryCurveModel boundaryModel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        boundaryModel = null!;

        var region = model.Candidate.Region;
        var regionSet = region.Pixels.ToHashSet();

        if (!TryMeasureOutlineEvidence(
                source,
                region,
                protectedStrokeColors,
                out var outlineColor,
                out var outlineCoverage) ||
            outlineCoverage < MinimumProtectedOutlineCoverage)
        {
            return false;
        }

        var actualOutlinePixels =
            CollectActualOutlinePixels(
                source,
                region.BoundaryPixels,
                regionSet,
                outlineColor);

        if (actualOutlinePixels.Count <
            MinimumSideSamples * 2)
        {
            return false;
        }

        var axisSign =
            model.ReversedForApex
                ? -1d
                : 1d;
        var axisX =
            model.Candidate.AxisX *
            axisSign;
        var axisY =
            model.Candidate.AxisY *
            axisSign;
        var normalX =
            model.Candidate.NormalX;
        var normalY =
            model.Candidate.NormalY;

        var projected =
            new List<((int X, int Y) Point, double Major, double Normal)>(
                region.BoundaryPixels.Count);
        var minimumMajor =
            double.PositiveInfinity;
        var maximumMajor =
            double.NegativeInfinity;

        foreach (var pixel in region.BoundaryPixels)
        {
            var x =
                pixel %
                source.Width;
            var y =
                pixel /
                source.Width;
            var point =
                (X: x, Y: y);
            var dx =
                x -
                model.Candidate.CenterX;
            var dy =
                y -
                model.Candidate.CenterY;
            var major =
                dx *
                axisX +
                dy *
                axisY;
            var normal =
                dx *
                normalX +
                dy *
                normalY;

            projected.Add(
                (
                    point,
                    major,
                    normal));
            minimumMajor =
                Math.Min(
                    minimumMajor,
                    major);
            maximumMajor =
                Math.Max(
                    maximumMajor,
                    major);
        }

        var span =
            maximumMajor -
            minimumMajor;

        if (span < 8d)
            return false;

        var binCount =
            Math.Clamp(
                (int)Math.Round(
                    span /
                    1.35) +
                1,
                10,
                120);
        var bins =
            Enumerable
                .Range(
                    0,
                    binCount)
                .Select(_ =>
                    new Bin())
                .ToArray();

        foreach (var item in projected)
        {
            var normalized =
                (item.Major -
                 minimumMajor) /
                Math.Max(
                    1e-9,
                    span);
            var index =
                Math.Clamp(
                    (int)Math.Round(
                        normalized *
                        (binCount - 1)),
                    0,
                    binCount - 1);
            var bin =
                bins[index];

            if (!bin.HasMinimum ||
                item.Normal <
                bin.MinimumNormal)
            {
                bin.HasMinimum = true;
                bin.MinimumNormal =
                    item.Normal;
                bin.MinimumPoint =
                    item.Point;
            }

            if (!bin.HasMaximum ||
                item.Normal >
                bin.MaximumNormal)
            {
                bin.HasMaximum = true;
                bin.MaximumNormal =
                    item.Normal;
                bin.MaximumPoint =
                    item.Point;
            }
        }

        var minimumSide =
            new List<(int X, int Y)>();
        var maximumSide =
            new List<(int X, int Y)>();

        foreach (var bin in bins)
        {
            if (bin.HasMinimum)
                AddDistinct(
                    minimumSide,
                    bin.MinimumPoint);

            if (bin.HasMaximum)
                AddDistinct(
                    maximumSide,
                    bin.MaximumPoint);
        }

        if (minimumSide.Count <
                MinimumSideSamples ||
            maximumSide.Count <
                MinimumSideSamples)
        {
            return false;
        }

        var outlineSet =
            actualOutlinePixels
                .ToHashSet();

        minimumSide =
            SnapSideToActualOutline(
                minimumSide,
                outlineSet,
                model.Candidate.CenterX,
                model.Candidate.CenterY,
                normalX,
                normalY,
                expectedNormalSign: -1,
                maximumDistance: 2.6);
        maximumSide =
            SnapSideToActualOutline(
                maximumSide,
                outlineSet,
                model.Candidate.CenterX,
                model.Candidate.CenterY,
                normalX,
                normalY,
                expectedNormalSign: +1,
                maximumDistance: 2.6);

        if (minimumSide.Count <
                MinimumSideSamples ||
            maximumSide.Count <
                MinimumSideSamples)
        {
            return false;
        }

        var leftPath =
            TraceActualOutlineSide(
                minimumSide,
                outlineSet,
                model.Candidate.CenterX,
                model.Candidate.CenterY,
                axisX,
                axisY,
                normalX,
                normalY,
                expectedNormalSign: -1);
        var rightPath =
            TraceActualOutlineSide(
                maximumSide,
                outlineSet,
                model.Candidate.CenterX,
                model.Candidate.CenterY,
                axisX,
                axisY,
                normalX,
                normalY,
                expectedNormalSign: +1);

        if (leftPath.Count < 12 ||
            rightPath.Count < 12)
        {
            return false;
        }

        var leftFit =
            LeafPetalBoundaryCurveFitter.Fit(
                leftPath);
        var rightFit =
            LeafPetalBoundaryCurveFitter.Fit(
                rightPath);

        if (leftFit is null ||
            rightFit is null)
        {
            return false;
        }

        var actualOutlineProjection =
            actualOutlinePixels
                .Select(point =>
                {
                    var dx =
                        point.X -
                        model.Candidate.CenterX;
                    var dy =
                        point.Y -
                        model.Candidate.CenterY;

                    return (
                        Point: point,
                        Major:
                            dx *
                            axisX +
                            dy *
                            axisY,
                        Normal:
                            dx *
                            normalX +
                            dy *
                            normalY);
                })
                .ToArray();
        var outlineMaximumMajor =
            actualOutlineProjection.Length == 0
                ? maximumMajor
                : actualOutlineProjection.Max(item =>
                    item.Major);

        var drawApexCap =
            TryFindSharedApex(
                actualOutlineProjection,
                outlineMaximumMajor,
                span,
                model,
                leftPath[^1],
                rightPath[^1],
                out var sharedApex);

        if (drawApexCap)
        {
            leftFit =
                SnapLastControl(
                    leftFit.Value,
                    sharedApex);
            rightFit =
                SnapLastControl(
                    rightFit.Value,
                    sharedApex);
        }

        var sourceOuter =
            new HashSet<(int X, int Y)>(
                leftPath);
        sourceOuter.UnionWith(
            rightPath);

        boundaryModel =
            new LeafPetalBoundaryCurveModel(
                model,
                outlineColor,
                outlineCoverage,
                leftPath,
                rightPath,
                sourceOuter,
                leftFit.Value,
                rightFit.Value,
                DrawBaseCap: false,
                DrawApexCap: drawApexCap);

        return true;
    }

    private static IReadOnlyList<(int X, int Y)> CollectActualOutlinePixels(
        DesignDocument source,
        IReadOnlyList<int> boundaryPixels,
        IReadOnlySet<int> region,
        byte outlineColor)
    {
        var result =
            new HashSet<(int X, int Y)>();

        foreach (var pixel in boundaryPixels)
        {
            var x =
                pixel %
                source.Width;
            var y =
                pixel /
                source.Width;

            // Radius 1 captures a normal 1x1 separator. Radius 2 is needed at diagonal
            // Pixel-Cord corners where the visible white bridge sits one extra cell away from the
            // fill boundary. We still require the exact learned outline colour.
            for (var dy = -2;
                 dy <= 2;
                 dy++)
            {
                for (var dx = -2;
                     dx <= 2;
                     dx++)
                {
                    if (dx == 0 &&
                        dy == 0)
                    {
                        continue;
                    }

                    var nx =
                        x +
                        dx;
                    var ny =
                        y +
                        dy;

                    if (nx < 0 ||
                        nx >= source.Width ||
                        ny < 0 ||
                        ny >= source.Height)
                    {
                        continue;
                    }

                    var key =
                        ny *
                        source.Width +
                        nx;

                    if (region.Contains(key) ||
                        source.GetPixel(
                            nx,
                            ny) != outlineColor)
                    {
                        continue;
                    }

                    // Keep the outline attached to THIS filled region. A radius-2 search can see
                    // an unrelated nearby white ornament; require at least one 8-neighbour touch
                    // back to the region.
                    var touchesRegion =
                        false;

                    for (var oy = -1;
                         oy <= 1 &&
                         !touchesRegion;
                         oy++)
                    {
                        for (var ox = -1;
                             ox <= 1;
                             ox++)
                        {
                            var rx =
                                nx +
                                ox;
                            var ry =
                                ny +
                                oy;

                            if (rx < 0 ||
                                rx >= source.Width ||
                                ry < 0 ||
                                ry >= source.Height)
                            {
                                continue;
                            }

                            if (region.Contains(
                                    ry *
                                    source.Width +
                                    rx))
                            {
                                touchesRegion =
                                    true;
                                break;
                            }
                        }
                    }

                    if (touchesRegion)
                    {
                        result.Add(
                            (nx, ny));
                    }
                }
            }
        }

        return result.ToArray();
    }

    private static List<(int X, int Y)> SnapSideToActualOutline(
        IReadOnlyList<(int X, int Y)> side,
        IReadOnlySet<(int X, int Y)> actualOutline,
        double centerX,
        double centerY,
        double normalX,
        double normalY,
        int expectedNormalSign,
        double maximumDistance)
    {
        var result =
            new List<(int X, int Y)>(
                side.Count);
        var search =
            (int)Math.Ceiling(
                maximumDistance);
        var maximumSquared =
            maximumDistance *
            maximumDistance;

        foreach (var seed in side)
        {
            var seedNormal =
                (seed.X -
                 centerX) *
                normalX +
                (seed.Y -
                 centerY) *
                normalY;
            var best =
                seed;
            var bestSquared =
                double.PositiveInfinity;

            for (var y =
                     seed.Y - search;
                 y <=
                 seed.Y + search;
                 y++)
            {
                for (var x =
                         seed.X - search;
                     x <=
                     seed.X + search;
                     x++)
                {
                    if (!actualOutline.Contains(
                            (x, y)))
                    {
                        continue;
                    }

                    var candidateNormal =
                        (x -
                         centerX) *
                        normalX +
                        (y -
                         centerY) *
                        normalY;

                    // The white point must stay on the OUTER side of the fill-boundary seed.
                    // This is the decisive protection against an internal white slit stealing a
                    // left/right outer curve when the leaf becomes narrow near its apex.
                    if (expectedNormalSign < 0 &&
                        candidateNormal >
                        seedNormal +
                        0.75)
                    {
                        continue;
                    }

                    if (expectedNormalSign > 0 &&
                        candidateNormal <
                        seedNormal -
                        0.75)
                    {
                        continue;
                    }

                    var dx =
                        x -
                        seed.X;
                    var dy =
                        y -
                        seed.Y;
                    var distance =
                        dx *
                        dx +
                        dy *
                        dy;

                    if (distance >
                            maximumSquared ||
                        distance >=
                            bestSquared)
                    {
                        continue;
                    }

                    best =
                        (x, y);
                    bestSquared =
                        distance;
                }
            }

            // No real outline close to this outer fill-edge sample means this bin is likely an
            // artificial lobe cut. Skip it instead of snapping to a distant internal ornament.
            if (double.IsPositiveInfinity(
                    bestSquared))
            {
                continue;
            }

            AddDistinct(
                result,
                best);
        }

        return result;
    }

    private static List<(int X, int Y)> TraceActualOutlineSide(
        IReadOnlyList<(int X, int Y)> samples,
        IReadOnlySet<(int X, int Y)> actualOutline,
        double centerX,
        double centerY,
        double axisX,
        double axisY,
        double normalX,
        double normalY,
        int expectedNormalSign)
    {
        if (samples.Count == 0)
            return new List<(int X, int Y)>();

        var result =
            new List<(int X, int Y)>();

        AddDistinct(
            result,
            samples[0]);

        for (var i = 1;
             i < samples.Count;
             i++)
        {
            var start =
                result[^1];
            var end =
                samples[i];

            if (start == end)
                continue;

            var segment =
                FindDirectedOutlinePath(
                    start,
                    end,
                    actualOutline,
                    centerX,
                    centerY,
                    axisX,
                    axisY,
                    normalX,
                    normalY,
                    expectedNormalSign);

            if (segment.Count == 0)
            {
                // Never invent a shortcut through the fill. If this real source outline cannot be
                // followed on the correct side and in the base->apex direction, the paired-curve
                // model is rejected and Curve & Fill remains the safe fallback.
                return new List<(int X, int Y)>();
            }

            for (var pointIndex = 1;
                 pointIndex < segment.Count;
                 pointIndex++)
            {
                AddDistinct(
                    result,
                    segment[pointIndex]);
            }
        }

        return result;
    }

    private static List<(int X, int Y)> FindDirectedOutlinePath(
        (int X, int Y) start,
        (int X, int Y) end,
        IReadOnlySet<(int X, int Y)> actualOutline,
        double centerX,
        double centerY,
        double axisX,
        double axisY,
        double normalX,
        double normalY,
        int expectedNormalSign)
    {
        if (!actualOutline.Contains(start) ||
            !actualOutline.Contains(end))
        {
            return new List<(int X, int Y)>();
        }

        var directDistance =
            Math.Max(
                Math.Abs(
                    end.X -
                    start.X),
                Math.Abs(
                    end.Y -
                    start.Y));
        var margin =
            Math.Clamp(
                directDistance +
                4,
                5,
                12);
        var minX =
            Math.Min(
                start.X,
                end.X) -
            margin;
        var maxX =
            Math.Max(
                start.X,
                end.X) +
            margin;
        var minY =
            Math.Min(
                start.Y,
                end.Y) -
            margin;
        var maxY =
            Math.Max(
                start.Y,
                end.Y) +
            margin;

        var startMajor =
            ProjectMajor(
                start,
                centerX,
                centerY,
                axisX,
                axisY);
        var endMajor =
            ProjectMajor(
                end,
                centerX,
                centerY,
                axisX,
                axisY);
        var forwardSign =
            endMajor >=
            startMajor
                ? 1d
                : -1d;
        var startNormal =
            ProjectNormal(
                start,
                centerX,
                centerY,
                normalX,
                normalY);
        var endNormal =
            ProjectNormal(
                end,
                centerX,
                centerY,
                normalX,
                normalY);
        var allowedInnerNormal =
            expectedNormalSign < 0
                ? Math.Max(
                    startNormal,
                    endNormal) +
                  1.15
                : Math.Min(
                    startNormal,
                    endNormal) -
                  1.15;

        var frontier =
            new PriorityQueue<
                (int X, int Y),
                double>();
        var previous =
            new Dictionary<
                (int X, int Y),
                (int X, int Y)>();
        var bestCost =
            new Dictionary<
                (int X, int Y),
                double>
            {
                [start] = 0d,
            };

        frontier.Enqueue(
            start,
            0d);

        (int X, int Y)[] directions =
        [
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1),
            (-1, -1),
            (1, -1),
            (-1, 1),
            (1, 1),
        ];

        var maximumVisited =
            Math.Max(
                96,
                (maxX -
                 minX +
                 1) *
                (maxY -
                 minY +
                 1));
        var visited =
            0;

        while (frontier.Count > 0 &&
               visited++ <=
               maximumVisited)
        {
            var current =
                frontier.Dequeue();

            if (current == end)
                break;

            if (!bestCost.TryGetValue(
                    current,
                    out var currentCost))
            {
                continue;
            }

            var currentMajor =
                ProjectMajor(
                    current,
                    centerX,
                    centerY,
                    axisX,
                    axisY);

            foreach (var (dx, dy) in directions)
            {
                var next =
                    (
                        X: current.X +
                           dx,
                        Y: current.Y +
                           dy);

                if (next.X < minX ||
                    next.X > maxX ||
                    next.Y < minY ||
                    next.Y > maxY ||
                    !actualOutline.Contains(next))
                {
                    continue;
                }

                var nextNormal =
                    ProjectNormal(
                        next,
                        centerX,
                        centerY,
                        normalX,
                        normalY);

                // A side path may approach the centre by roughly one Pixel-Cord cell, but it may
                // not jump onto an internal slit / opposite outer side.
                if (expectedNormalSign < 0 &&
                    nextNormal >
                    allowedInnerNormal)
                {
                    continue;
                }

                if (expectedNormalSign > 0 &&
                    nextNormal <
                    allowedInnerNormal)
                {
                    continue;
                }

                var nextMajor =
                    ProjectMajor(
                        next,
                        centerX,
                        centerY,
                        axisX,
                        axisY);
                var majorDelta =
                    (nextMajor -
                     currentMajor) *
                    forwardSign;
                var regression =
                    Math.Max(
                        0d,
                        -majorDelta);
                var stepCost =
                    dx == 0 ||
                    dy == 0
                        ? 1d
                        : 1.41421356237;

                // Prefer real-outline paths that consistently progress base -> apex. A one-cell
                // staircase regression is possible, but repeated/backward travel becomes much
                // more expensive than the intended designer arc.
                var directionPenalty =
                    regression *
                    5.0;

                var lineDistance =
                    DistanceToSegment(
                        next,
                        start,
                        end);
                var linePenalty =
                    lineDistance *
                    0.035;

                // Stay on the same outer side rather than drifting toward the centre. This term is
                // intentionally mild: actual source geometry remains the primary authority.
                var sidePenalty =
                    expectedNormalSign < 0
                        ? Math.Max(
                              0d,
                              nextNormal -
                              Math.Min(
                                  startNormal,
                                  endNormal)) *
                          0.08
                        : Math.Max(
                              0d,
                              Math.Max(
                                  startNormal,
                                  endNormal) -
                              nextNormal) *
                          0.08;

                var newCost =
                    currentCost +
                    stepCost +
                    directionPenalty +
                    linePenalty +
                    sidePenalty;

                if (bestCost.TryGetValue(
                        next,
                        out var existing) &&
                    existing <=
                    newCost)
                {
                    continue;
                }

                bestCost[next] =
                    newCost;
                previous[next] =
                    current;

                // A small heuristic toward the requested seed keeps the search local without
                // changing source-pixel admissibility.
                var heuristic =
                    Math.Sqrt(
                        Math.Pow(
                            end.X -
                            next.X,
                            2) +
                        Math.Pow(
                            end.Y -
                            next.Y,
                            2)) *
                    0.15;

                frontier.Enqueue(
                    next,
                    newCost +
                    heuristic);
            }
        }

        if (!bestCost.ContainsKey(end))
            return new List<(int X, int Y)>();

        var reverse =
            new List<(int X, int Y)>
            {
                end,
            };
        var cursor =
            end;
        var guard =
            maximumVisited +
            2;

        while (cursor != start &&
               guard-- > 0)
        {
            if (!previous.TryGetValue(
                    cursor,
                    out var parent))
            {
                return new List<(int X, int Y)>();
            }

            cursor =
                parent;
            reverse.Add(
                cursor);
        }

        reverse.Reverse();
        return reverse;
    }

    private static double ProjectMajor(
        (int X, int Y) point,
        double centerX,
        double centerY,
        double axisX,
        double axisY) =>
        (point.X -
         centerX) *
        axisX +
        (point.Y -
         centerY) *
        axisY;

    private static double ProjectNormal(
        (int X, int Y) point,
        double centerX,
        double centerY,
        double normalX,
        double normalY) =>
        (point.X -
         centerX) *
        normalX +
        (point.Y -
         centerY) *
        normalY;

    private static double DistanceToSegment(
        (int X, int Y) point,
        (int X, int Y) start,
        (int X, int Y) end)
    {
        var vx =
            end.X -
            start.X;
        var vy =
            end.Y -
            start.Y;
        var wx =
            point.X -
            start.X;
        var wy =
            point.Y -
            start.Y;
        var lengthSquared =
            vx *
            vx +
            vy *
            vy;

        if (lengthSquared <= 0)
        {
            return Math.Sqrt(
                wx *
                wx +
                wy *
                wy);
        }

        var t =
            Math.Clamp(
                (wx *
                 vx +
                 wy *
                 vy) /
                (double)lengthSquared,
                0d,
                1d);
        var dx =
            point.X -
            (start.X +
             vx *
             t);
        var dy =
            point.Y -
            (start.Y +
             vy *
             t);

        return Math.Sqrt(
            dx *
            dx +
            dy *
            dy);
    }

    private static bool TryFindSharedApex(
        IReadOnlyList<((int X, int Y) Point, double Major, double Normal)> projected,
        double maximumMajor,
        double span,
        LeafPetalArcModel model,
        (int X, int Y) leftEnd,
        (int X, int Y) rightEnd,
        out (int X, int Y) apex)
    {
        apex = default;

        // Only a genuinely tapered leaf/petal receives a common sharp apex. The medial-axis
        // width estimate is intentionally a little tolerant because a 1px Pixel-Cord outline and
        // scan-bin quantisation can make the last source cross-section look 1-2 cells wider than
        // the visual tip.
        if (model.ApexWidth >
                Math.Max(
                    4.5,
                    model.BaseWidth *
                    0.70))
        {
            return false;
        }

        var endDistance =
            Math.Sqrt(
                Math.Pow(
                    leftEnd.X -
                    rightEnd.X,
                    2) +
                Math.Pow(
                    leftEnd.Y -
                    rightEnd.Y,
                    2));
        var maximumEndDistance =
            Math.Max(
                10.0,
                Math.Min(
                    14.0,
                    span *
                    0.16));

        if (endDistance >
            maximumEndDistance)
        {
            return false;
        }

        var threshold =
            maximumMajor -
            Math.Max(
                3.5,
                span *
                0.12);

        var candidates =
            projected
                .Where(item =>
                    item.Major >=
                    threshold)
                .OrderBy(item =>
                    Math.Abs(
                        item.Normal) +
                    (maximumMajor -
                     item.Major) *
                    0.20)
                .ToArray();

        foreach (var candidate in candidates)
        {
            var point =
                candidate.Point;
            var leftDistance =
                Math.Max(
                    Math.Abs(
                        point.X -
                        leftEnd.X),
                    Math.Abs(
                        point.Y -
                        leftEnd.Y));
            var rightDistance =
                Math.Max(
                    Math.Abs(
                        point.X -
                        rightEnd.X),
                    Math.Abs(
                        point.Y -
                        rightEnd.Y));

            // The shared anchor must itself be a real source-outline pixel very close to both
            // extracted sides. This prevents an artificial centre point from moving the tip.
            var maximumAnchorDistance =
                Math.Max(
                    4,
                    (int)Math.Ceiling(
                        span *
                        0.07));

            if (leftDistance <=
                    maximumAnchorDistance &&
                rightDistance <=
                    maximumAnchorDistance)
            {
                apex =
                    point;
                return true;
            }
        }

        return false;
    }

    private static ToolFaithfulCurveStyleFit SnapLastControl(
        ToolFaithfulCurveStyleFit fit,
        (int X, int Y) apex)
    {
        var controls =
            fit.Controls
                .ToArray();

        if (controls.Length == 0)
            return fit;

        controls[^1] =
            apex;

        return fit with
        {
            Controls =
                controls,
        };
    }

    private static bool TryFindOutlineColor(
        DesignDocument source,
        IReadOnlyList<int> boundaryPixels,
        IReadOnlySet<int> region,
        IReadOnlySet<byte> protectedStrokeColors,
        out byte outlineColor,
        out double coverage)
    {
        var counts =
            new int[256];
        var exposedSamples = 0;

        foreach (var pixel in boundaryPixels)
        {
            var x =
                pixel %
                source.Width;
            var y =
                pixel /
                source.Width;
            var hadOutside =
                false;
            var seenProtected =
                new HashSet<byte>();

            for (var dy = -1;
                 dy <= 1;
                 dy++)
            {
                for (var dx = -1;
                     dx <= 1;
                     dx++)
                {
                    if (dx == 0 &&
                        dy == 0)
                    {
                        continue;
                    }

                    var nx =
                        x +
                        dx;
                    var ny =
                        y +
                        dy;

                    if (nx < 0 ||
                        nx >= source.Width ||
                        ny < 0 ||
                        ny >= source.Height)
                    {
                        continue;
                    }

                    var key =
                        ny *
                        source.Width +
                        nx;

                    if (region.Contains(key))
                        continue;

                    hadOutside = true;

                    var color =
                        source.GetPixel(
                            nx,
                            ny);

                    if (protectedStrokeColors.Contains(
                            color))
                    {
                        seenProtected.Add(
                            color);
                    }
                }
            }

            if (!hadOutside)
                continue;

            exposedSamples++;

            foreach (var color in seenProtected)
                counts[color]++;
        }

        outlineColor = 0;
        coverage = 0d;

        if (exposedSamples == 0)
            return false;

        var bestCount = 0;

        foreach (var color in protectedStrokeColors)
        {
            if (counts[color] <=
                bestCount)
            {
                continue;
            }

            bestCount =
                counts[color];
            outlineColor =
                color;
        }

        coverage =
            bestCount /
            (double)exposedSamples;

        return bestCount > 0;
    }

    private static void AddDistinct(
        ICollection<(int X, int Y)> points,
        (int X, int Y) point)
    {
        if (points is List<(int X, int Y)> list &&
            list.Count > 0 &&
            list[^1] == point)
        {
            return;
        }

        points.Add(point);
    }
}
