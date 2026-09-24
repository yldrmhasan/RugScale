using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Replays source 1x1 curve/cord artwork through RugScale's OWN curve rasterizer after Curve & Fill
/// has reconstructed the filled colour regions.
///
/// This pass is intentionally conservative. It only accepts source components that look like a
/// genuine one-cell tool stroke: no 2x2 same-colour block, long enough to be a path, sparse inside
/// its bounding box, and connected as a chain/network. Filled leaves and colour islands therefore
/// stay owned by CurveFillScaleEngine; they are never skeletonized.
///
/// Accepted paths are vectorised from the immutable source raster, simplified into control points,
/// mapped to the target grid, then rasterized with the same CurveRasterizer + Pixel Cord bridge
/// rule used by DesignCanvas. In other words, a source 1x1 Pixel-Cord curve is redrawn as a 1x1
/// Pixel-Cord curve instead of being enlarged as a bitmap block.
/// </summary>
internal static class ToolFaithfulPixelCordOverlay
{
    private const int MinimumPathPixels = 8;
    private const double MaximumComponentFill = 0.58;
    private const double SimplifyTolerance = 0.72;
    private const double CurveDeviationThreshold = 1.15;
    private const int MaximumControlPoints = 96;

    // Four real N69 training fixtures all use one dedicated 1x1/Pixel-Cord outline palette role:
    // only ~4.7-8.5% of that colour participates in any solid 2x2 block, while genuine filled
    // palette roles are ~97.7-99.9%. 12% leaves headroom for intersections and rounded junctions
    // without confusing a filled leaf/body with a drawing-tool stroke role.
    private const double MaximumStrokeRoleSolidBlockRatio = 0.12;

    private static readonly (int X, int Y)[] FourDirections =
    [
        (-1, 0),
        (1, 0),
        (0, -1),
        (0, 1),
    ];

    private static readonly (int X, int Y)[] EightDirections =
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

    public static ToolFaithfulOverlayReport Apply(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        // Learn palette roles from THIS source raster before classifying components.
        // A dedicated 1x1 outline colour (the white Pixel-Cord network in the supplied N69
        // designs) is allowed to contain occasional 2x2 junction blocks and closed loops. Other
        // colours remain conservative so filled ornaments can never be mistaken for line work.
        var strokeRoles =
            DetectStrokePaletteRoles(
                source);

        var components =
            BuildComponents(
                source,
                strokeRoles);

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;

        var penSizeX =
            Math.Max(
                1,
                (int)Math.Round(
                    sourceWarpDensity > 0 &&
                    targetWarpDensity > 0
                        ? targetWarpDensity /
                          (double)sourceWarpDensity
                        : 1d));
        var penSizeY =
            Math.Max(
                1,
                (int)Math.Round(
                    sourceWeftDensity > 0 &&
                    targetWeftDensity > 0
                        ? targetWeftDensity /
                          (double)sourceWeftDensity
                        : 1d));

        var acceptedComponents = 0;
        var pixelCordComponents = 0;
        var redrawnChains = 0;
        var redrawnPixels = 0;
        var learnedCurves = 0;
        var learnedThroughPoints = 0;
        var learnedSpline = 0;
        var learnedBezier = 0;
        var learnedEllipses = 0;
        var graphFallbacks = 0;
        var curveSafetyFallbacks = 0;
        var corridorClippedPixels = 0;
        var learnedRoundnessSum = 0d;
        var styleFitCacheHits = 0;
        var completePathRecoveries = 0;

        // Repeated carpet ornaments frequently contain the exact same 1x1 curve translated many
        // times. Learn its Curve family once and reuse the translation-invariant fit. This both
        // reduces preview cost and guarantees that repeated copies are redrawn with the same tool
        // family/roundness instead of accumulating tiny per-instance fitting differences.
        var styleFitCache =
            new Dictionary<string, CachedCurveStyleFit?>(
                StringComparer.Ordinal);

        foreach (var component in components)
        {
            if (!AcceptComponent(
                    component,
                    source.Width))
            {
                continue;
            }

            acceptedComponents++;

            var pixelCord =
                IsFourConnected(
                    component,
                    source.Width);
            if (pixelCord)
                pixelCordComponents++;

            var componentPixels =
                component.Pixels
                    .ToHashSet();

            // Destructive cleanup is allowed only for a proven 4-connected Pixel-Cord path.
            // Plain diagonal curves are replayed additively; filled-region boundaries must never
            // be hollowed merely because a short edge fragment happens to look one-cell wide.
            var physicallyEnlarging =
                scaleX > 1.0001 ||
                scaleY > 1.0001;

            if (physicallyEnlarging &&
                pixelCord &&
                (component.TrustedStrokeRole ||
                 component.Pixels.Count <= 4096))
            {
                // Shrink has no block-expanded pen residue to erase. Destructive cleanup while
                // shrinking was needlessly reassigning both sides of the outline and inflated
                // round-trip palette drift. Cleanup belongs only to physical enlargement.
                ClearProjectedStrokeResidue(
                    source,
                    destination,
                    component,
                    scaleX,
                    scaleY);
            }

            // Closed oval/ellipse outlines are a separate native RugScale tool family. They must
            // NOT be forced through the open Curve inverse model: doing that invents a start/end
            // tangent and can flatten the seam. If the complete source component is explained by
            // RugScale's own Ellipse outline raster with strong evidence, redraw it using that exact
            // tool at the target bounding box.
            if (component.TrustedStrokeRole &&
                TryFitNativeEllipse(
                    component,
                    source.Width,
                    pixelCord,
                    out _))
            {
                var targetLeft =
                    MapCenter(
                        component.MinX,
                        scaleX,
                        destination.Width);
                var targetTop =
                    MapCenter(
                        component.MinY,
                        scaleY,
                        destination.Height);
                var targetRight =
                    MapCenter(
                        component.MaxX,
                        scaleX,
                        destination.Width);
                var targetBottom =
                    MapCenter(
                        component.MaxY,
                        scaleY,
                        destination.Height);

                IEnumerable<(int X, int Y)> ellipse =
                    pixelCord
                        ? Rasterizer.EllipseOutlineConnected(
                            targetLeft,
                            targetTop,
                            targetRight,
                            targetBottom)
                        : Rasterizer.EllipseOutline(
                            targetLeft,
                            targetTop,
                            targetRight,
                            targetBottom);

                ellipse =
                    Rasterizer.Dilate(
                        ellipse,
                        penSizeX,
                        penSizeY);

                ellipse =
                    ConstrainToSourceStrokeCorridor(
                        ellipse,
                        componentPixels,
                        source.Width,
                        source.Height,
                        scaleX,
                        scaleY,
                        maximumSourceDistance: 1.35,
                        out var ellipseClipped);
                corridorClippedPixels +=
                    ellipseClipped;

                var paintedEllipse =
                    Paint(
                        destination,
                        ellipse,
                        component.Color);

                learnedEllipses++;

                if (paintedEllipse > 0)
                {
                    redrawnChains++;
                    redrawnPixels +=
                        paintedEllipse;
                }

                continue;
            }

            var chains =
                TraceChains(
                    component,
                    source.Width,
                    pixelCord);

            // Pixel Cord inserts orthogonal bridge cells between diagonal Curve pixels. On a
            // smooth oval those bridge cells can touch a nearby part of the same stroke and make
            // the raw graph look artificially branched, causing TraceChains to split one designer
            // curve into many fragments. For a small two-endpoint component, recover the COMPLETE
            // endpoint-to-endpoint Pixel-Cord drawing order. True branched networks continue through
            // the conservative multi-chain path below.
            var recoveredSmoothOval =
                false;

            if (component.TrustedStrokeRole &&
                pixelCord &&
                TryTraceCompletePixelCordPath(
                    component,
                    source.Width,
                    out var completePath) &&
                LooksLikeSmoothOvalRecovery(
                    completePath))
            {
                // Recover the original bridge-connected drawing order rather than taking a graph
                // shortest path. The complete path must visit every component pixel exactly once,
                // so false local contacts cannot cut across an oval shoulder.
                chains =
                new[]
                {
                    completePath,
                };
                completePathRecoveries++;
                recoveredSmoothOval =
                    true;
            }

            if (component.TrustedStrokeRole)
            {
                foreach (var chain in chains)
                {
                    if (chain.Count < 2)
                        continue;

                    var hasSourceCusp =
                        !recoveredSmoothOval &&
                        HasSourceCusp(
                            chain);

                    var cacheHit = false;
                    var fit =
                        hasSourceCusp
                            ? null
                            : ResolveStyleFit(
                                chain,
                                pixelCord,
                                styleFitCache,
                                out cacheHit);

                    if (cacheHit)
                        styleFitCacheHits++;

                    IEnumerable<(int X, int Y)> rendered;

                    if (fit is { } learned)
                    {
                        learnedCurves++;
                        learnedRoundnessSum +=
                            learned.Roundness;

                        switch (learned.Type)
                        {
                            case CurveType.SplineThroughPoints:
                                learnedThroughPoints++;
                                break;
                            case CurveType.Spline:
                                learnedSpline++;
                                break;
                            case CurveType.Bezier:
                                learnedBezier++;
                                break;
                        }

                        var mappedControls =
                            MapControlSequence(
                                learned.Controls,
                                scaleX,
                                scaleY,
                                destination.Width,
                                destination.Height);

                        if (mappedControls.Count >= 2)
                        {
                            // Infer which REAL RugScale Curve family and roundness most plausibly
                            // generated the source chain. The fitted curve is still not trusted
                            // blindly: at target size it must stay inside a one-pixel neighbourhood
                            // of the literal source-graph replay. This blocks attractive-looking
                            // but unsupported bulges at leaf tips and tight inner arcs.
                            IEnumerable<(int X, int Y)> learnedRendered =
                                CurveRasterizer.Draw(
                                    mappedControls,
                                    learned.Type,
                                    learned.Roundness);

                            if (pixelCord)
                            {
                                learnedRendered =
                                    Rasterizer.ConnectDiagonalSteps(
                                        learnedRendered);
                            }

                            var learnedSet =
                                learnedRendered
                                    .ToHashSet();
                            var fallbackSet =
                                RenderMappedChainGraph(
                                        chain,
                                        destination.Width,
                                        destination.Height,
                                        scaleX,
                                        scaleY,
                                        pixelCord)
                                    .ToHashSet();

                            var learnedCurveSafe =
                                recoveredSmoothOval
                                    ? IsLearnedCurveSafeAgainstSourceGraph(
                                        learnedSet,
                                        fallbackSet,
                                        radius: 2,
                                        minimumLearnedSupport: 0.96,
                                        minimumGraphSupport: 0.86)
                                    : IsLearnedCurveSafeAgainstSourceGraph(
                                        learnedSet,
                                        fallbackSet);

                            if (learnedCurveSafe)
                            {
                                rendered =
                                    learnedSet;
                            }
                            else
                            {
                                curveSafetyFallbacks++;
                                graphFallbacks++;
                                rendered =
                                    fallbackSet;
                            }
                        }
                        else
                        {
                            curveSafetyFallbacks++;
                            graphFallbacks++;
                            rendered =
                                RenderMappedChainGraph(
                                    chain,
                                    destination.Width,
                                    destination.Height,
                                    scaleX,
                                    scaleY,
                                    pixelCord);
                        }
                    }
                    else
                    {
                        graphFallbacks++;
                        if (hasSourceCusp)
                            curveSafetyFallbacks++;

                        rendered =
                            RenderMappedChainGraph(
                                chain,
                                destination.Width,
                                destination.Height,
                                scaleX,
                                scaleY,
                                pixelCord);
                    }

                    rendered =
                        Rasterizer.Dilate(
                            rendered,
                            penSizeX,
                            penSizeY);

                    rendered =
                        ConstrainToSourceStrokeCorridor(
                            rendered,
                            componentPixels,
                            source.Width,
                            source.Height,
                            scaleX,
                            scaleY,
                            maximumSourceDistance: 1.35,
                            out var clipped);
                    corridorClippedPixels +=
                        clipped;

                    var painted =
                        Paint(
                            destination,
                            rendered,
                            component.Color);

                    if (painted <= 0)
                        continue;

                    redrawnChains++;
                    redrawnPixels +=
                        painted;
                }

                continue;
            }

            foreach (var chain in chains)
            {
                if (chain.Count < 2)
                    continue;

                var controls =
                    Simplify(
                        chain,
                        SimplifyTolerance);

                controls =
                    LimitControlPoints(
                        controls,
                        MaximumControlPoints);

                var mapped =
                    controls
                        .Select(point =>
                            (
                                X: MapCenter(
                                    point.X,
                                    scaleX,
                                    destination.Width),
                                Y: MapCenter(
                                    point.Y,
                                    scaleY,
                                    destination.Height)))
                        .Distinct()
                        .ToArray();

                if (mapped.Length < 2)
                    continue;

                IEnumerable<(int X, int Y)> points =
                    RenderWithToolRules(
                        controls,
                        mapped);

                if (pixelCord)
                {
                    // This is exactly the same bridge rule as Curve > Pixel Cord in DesignCanvas.
                    points =
                        Rasterizer.ConnectDiagonalSteps(
                            points);
                }

                // Pen width belongs to QUALITY, not physical resize. Same quality => 1x1 remains
                // 1x1. A denser target loom may legitimately request a wider pixel pen.
                points =
                    Rasterizer.Dilate(
                        points,
                        penSizeX,
                        penSizeY);

                points =
                    ConstrainToSourceStrokeCorridor(
                        points,
                        componentPixels,
                        source.Width,
                        source.Height,
                        scaleX,
                        scaleY,
                        maximumSourceDistance: 1.35,
                        out var genericClipped);
                corridorClippedPixels +=
                    genericClipped;

                var painted =
                    Paint(
                        destination,
                        points,
                        component.Color);

                if (painted <= 0)
                    continue;

                redrawnChains++;
                redrawnPixels += painted;
            }
        }

        return new ToolFaithfulOverlayReport(
            acceptedComponents,
            pixelCordComponents,
            redrawnChains,
            redrawnPixels,
            learnedCurves,
            learnedThroughPoints,
            learnedSpline,
            learnedBezier,
            learnedEllipses,
            graphFallbacks,
            styleFitCacheHits,
            curveSafetyFallbacks,
            corridorClippedPixels,
            learnedCurves == 0
                ? 0d
                : learnedRoundnessSum /
                  learnedCurves,
            CompletePathRecoveries:
                completePathRecoveries);
    }

    private static void ClearProjectedStrokeResidue(
        DesignDocument source,
        DesignDocument destination,
        StrokeComponent component,
        double scaleX,
        double scaleY)
    {
        var set =
            component.Pixels
                .ToHashSet();

        var targetLeft =
            Math.Max(
                0,
                (int)Math.Floor(
                    component.MinX *
                    scaleX) -
                2);
        var targetTop =
            Math.Max(
                0,
                (int)Math.Floor(
                    component.MinY *
                    scaleY) -
                2);
        var targetRight =
            Math.Min(
                destination.Width - 1,
                (int)Math.Ceiling(
                    (component.MaxX + 1) *
                    scaleX) +
                1);
        var targetBottom =
            Math.Min(
                destination.Height - 1,
                (int)Math.Ceiling(
                    (component.MaxY + 1) *
                    scaleY) +
                1);

        for (var ty = targetTop;
             ty <= targetBottom;
             ty++)
        {
            var sourceY =
                (ty + 0.5) /
                scaleY -
                0.5;
            var sy =
                Math.Clamp(
                    (int)Math.Round(sourceY),
                    0,
                    source.Height - 1);

            for (var tx = targetLeft;
                 tx <= targetRight;
                 tx++)
            {
                if (destination.GetPixel(
                        tx,
                        ty) != component.Color)
                {
                    continue;
                }

                var sourceX =
                    (tx + 0.5) /
                    scaleX -
                    0.5;
                var sx =
                    Math.Clamp(
                        (int)Math.Round(sourceX),
                        0,
                        source.Width - 1);

                if (!NearComponent(
                        set,
                        source.Width,
                        source.Height,
                        sx,
                        sy))
                {
                    continue;
                }

                // Do not replace a widened stroke pixel with the majority colour around the
                // source cell: that destroys the narrow fill on the minority side of a 1x1
                // outline. Pick the geometrically nearest non-stroke source cell to THIS target
                // subpixel instead. Left/right (or inside/outside) therefore restore to their
                // original indexed region before the 1x1 tool stroke is replayed.
                var replacement =
                    FindNearestOtherColor(
                        source,
                        sourceX,
                        sourceY,
                        component.Color);

                if (replacement == component.Color)
                    continue;

                destination.SetPixel(
                    tx,
                    ty,
                    replacement);
            }
        }
    }

    private static bool NearComponent(
        IReadOnlySet<int> component,
        int width,
        int height,
        int x,
        int y)
    {
        for (var dy = -1;
             dy <= 1;
             dy++)
        {
            var py = y + dy;
            if (py < 0 ||
                py >= height)
            {
                continue;
            }

            for (var dx = -1;
                 dx <= 1;
                 dx++)
            {
                var px = x + dx;
                if (px < 0 ||
                    px >= width)
                {
                    continue;
                }

                if (component.Contains(
                        py *
                        width +
                        px))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static byte FindNearestOtherColor(
        DesignDocument source,
        double sourceX,
        double sourceY,
        byte strokeColor)
    {
        var centerX =
            Math.Clamp(
                (int)Math.Round(sourceX),
                0,
                source.Width - 1);
        var centerY =
            Math.Clamp(
                (int)Math.Round(sourceY),
                0,
                source.Height - 1);

        var bestColor =
            strokeColor;
        var bestDistance =
            double.PositiveInfinity;
        var bestRingDistance =
            int.MaxValue;

        // Four source pixels is enough for every 1x1/Pixel-Cord outline in the training fixtures
        // and prevents a thin line from borrowing a distant unrelated ornament colour.
        for (var radius = 1;
             radius <= 4;
             radius++)
        {
            for (var y =
                     Math.Max(
                         0,
                         centerY - radius);
                 y <=
                 Math.Min(
                     source.Height - 1,
                     centerY + radius);
                 y++)
            {
                for (var x =
                         Math.Max(
                             0,
                             centerX - radius);
                     x <=
                     Math.Min(
                         source.Width - 1,
                         centerX + radius);
                     x++)
                {
                    var ringDistance =
                        Math.Max(
                            Math.Abs(
                                x - centerX),
                            Math.Abs(
                                y - centerY));

                    if (ringDistance != radius)
                        continue;

                    var color =
                        source.GetPixel(
                            x,
                            y);

                    if (color == strokeColor)
                        continue;

                    var dx =
                        x - sourceX;
                    var dy =
                        y - sourceY;
                    var distance =
                        dx * dx +
                        dy * dy;

                    if (distance >
                        bestDistance +
                        1e-9)
                    {
                        continue;
                    }

                    // Equal geometric distance: prefer the closest source ring, then the lower
                    // palette index for deterministic builds. No frequency/majority bias is used.
                    if (Math.Abs(
                            distance -
                            bestDistance) <=
                        1e-9 &&
                        (ringDistance >
                         bestRingDistance ||
                         (ringDistance ==
                          bestRingDistance &&
                          color >= bestColor)))
                    {
                        continue;
                    }

                    bestDistance = distance;
                    bestRingDistance =
                        ringDistance;
                    bestColor = color;
                }
            }

            if (bestColor != strokeColor &&
                bestRingDistance < radius)
            {
                break;
            }
        }

        return bestColor;
    }

    internal static HashSet<byte> DetectStrokePaletteRoles(
        DesignDocument source)
    {
        var counts =
            new int[256];
        var solidBlockPixels =
            new int[256];
        var minX =
            Enumerable.Repeat(
                int.MaxValue,
                256)
                .ToArray();
        var minY =
            Enumerable.Repeat(
                int.MaxValue,
                256)
                .ToArray();
        var maxX =
            Enumerable.Repeat(
                int.MinValue,
                256)
                .ToArray();
        var maxY =
            Enumerable.Repeat(
                int.MinValue,
                256)
                .ToArray();

        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                var color =
                    source.GetPixel(
                        x,
                        y);

                counts[color]++;
                minX[color] =
                    Math.Min(
                        minX[color],
                        x);
                minY[color] =
                    Math.Min(
                        minY[color],
                        y);
                maxX[color] =
                    Math.Max(
                        maxX[color],
                        x);
                maxY[color] =
                    Math.Max(
                        maxY[color],
                        y);

                if (ParticipatesInSolidTwoByTwo(
                        source,
                        x,
                        y,
                        color))
                {
                    solidBlockPixels[color]++;
                }
            }
        }

        var result =
            new HashSet<byte>();

        for (var index = 0;
             index < counts.Length;
             index++)
        {
            var count =
                counts[index];

            if (count <
                MinimumPathPixels * 2)
            {
                continue;
            }

            var width =
                maxX[index] -
                minX[index] +
                1;
            var height =
                maxY[index] -
                minY[index] +
                1;

            var technicalVerticalEdge =
                width <= 2 &&
                height >=
                source.Height *
                0.85 &&
                (minX[index] == 0 ||
                 maxX[index] ==
                 source.Width - 1);
            var technicalHorizontalEdge =
                height <= 2 &&
                width >=
                source.Width *
                0.85 &&
                (minY[index] == 0 ||
                 maxY[index] ==
                 source.Height - 1);

            if (technicalVerticalEdge ||
                technicalHorizontalEdge)
            {
                continue;
            }

            var solidRatio =
                solidBlockPixels[index] /
                (double)count;

            if (solidRatio <=
                MaximumStrokeRoleSolidBlockRatio)
            {
                result.Add(
                    (byte)index);
            }
        }

        return result;
    }

    private static IReadOnlyList<StrokeComponent> BuildComponents(
        DesignDocument source,
        IReadOnlySet<byte> trustedStrokeRoles)
    {
        var width = source.Width;
        var height = source.Height;
        var count =
            checked(
                width *
                height);
        var visited =
            new bool[count];
        var queue =
            new int[count];
        var result =
            new List<StrokeComponent>();

        for (var start = 0;
             start < count;
             start++)
        {
            if (visited[start])
                continue;

            var startX =
                start %
                width;
            var startY =
                start /
                width;
            var color =
                source.GetPixel(
                    startX,
                    startY);

            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;

            var pixels =
                new List<int>();
            var minX = startX;
            var minY = startY;
            var maxX = startX;
            var maxY = startY;
            var hasSolidTwoByTwo = false;

            while (head < tail)
            {
                var pixel =
                    queue[head++];
                pixels.Add(pixel);

                var x =
                    pixel %
                    width;
                var y =
                    pixel /
                    width;

                minX =
                    Math.Min(
                        minX,
                        x);
                minY =
                    Math.Min(
                        minY,
                        y);
                maxX =
                    Math.Max(
                        maxX,
                        x);
                maxY =
                    Math.Max(
                        maxY,
                        y);

                if (!hasSolidTwoByTwo &&
                    ParticipatesInSolidTwoByTwo(
                        source,
                        x,
                        y,
                        color))
                {
                    hasSolidTwoByTwo = true;
                }

                foreach (var (dx, dy) in EightDirections)
                {
                    var nx = x + dx;
                    var ny = y + dy;

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

                    if (visited[next] ||
                        source.GetPixel(
                            nx,
                            ny) != color)
                    {
                        continue;
                    }

                    visited[next] = true;
                    queue[tail++] = next;
                }
            }

            var trustedStrokeRole =
                trustedStrokeRoles.Contains(
                    color);

            // Outside a learned outline/cord palette role, even one solid 2x2 proves this
            // connected region is not a pure 1x1 tool stroke. Trusted stroke roles are different:
            // Pixel Cord elbows and multi-branch junctions can legitimately create a few 2x2
            // blocks while the palette role as a whole is still overwhelmingly one-cell wide.
            if (!trustedStrokeRole &&
                hasSolidTwoByTwo)
            {
                continue;
            }

            result.Add(
                new StrokeComponent(
                    color,
                    pixels,
                    minX,
                    minY,
                    maxX,
                    maxY,
                    trustedStrokeRole));
        }

        return result;
    }

    private static bool ParticipatesInSolidTwoByTwo(
        DesignDocument source,
        int x,
        int y,
        byte color)
    {
        for (var oy = -1;
             oy <= 0;
             oy++)
        {
            for (var ox = -1;
                 ox <= 0;
                 ox++)
            {
                var left = x + ox;
                var top = y + oy;

                if (left < 0 ||
                    top < 0 ||
                    left + 1 >= source.Width ||
                    top + 1 >= source.Height)
                {
                    continue;
                }

                if (source.GetPixel(
                        left,
                        top) == color &&
                    source.GetPixel(
                        left + 1,
                        top) == color &&
                    source.GetPixel(
                        left,
                        top + 1) == color &&
                    source.GetPixel(
                        left + 1,
                        top + 1) == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AcceptComponent(
        StrokeComponent component,
        int sourceWidth)
    {
        if (component.Pixels.Count <
            MinimumPathPixels)
        {
            return false;
        }

        // A palette role learned as dedicated stroke/outline is allowed to be a huge connected
        // network: this is exactly how the supplied N69 white Pixel-Cord outline behaves. A
        // non-role component stays tightly bounded so a filled categorical region can never enter
        // the tool replay path.
        if (!component.TrustedStrokeRole &&
            component.Pixels.Count > 4096)
        {
            return false;
        }

        var width =
            component.MaxX -
            component.MinX +
            1;
        var height =
            component.MaxY -
            component.MinY +
            1;

        if (Math.Max(
                width,
                height) < 6)
        {
            return false;
        }

        var fill =
            component.Pixels.Count /
            (double)Math.Max(
                1,
                width *
                height);

        if (fill >
            MaximumComponentFill &&
            Math.Min(
                width,
                height) > 2)
        {
            return false;
        }

        var pixelCord =
            IsFourConnected(
                component,
                sourceWidth);
        var endpoints =
            CountEndpoints(
                component,
                sourceWidth,
                pixelCord);

        // A learned stroke palette role may legitimately contain closed Curve-tool loops and a
        // large branched network. Other colours still require an open path so the thin boundary of
        // a filled leaf cannot be promoted into a destructive overlay.
        return component.TrustedStrokeRole ||
               endpoints >= 2;
    }

    private static int CountEndpoints(
        StrokeComponent component,
        int sourceWidth,
        bool pixelCord)
    {
        var set =
            component.Pixels
                .ToHashSet();
        var directions =
            pixelCord
                ? FourDirections
                : EightDirections;
        var endpoints = 0;

        foreach (var pixel in component.Pixels)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var degree = 0;

            foreach (var (dx, dy) in directions)
            {
                var next =
                    (y + dy) *
                    sourceWidth +
                    (x + dx);

                if (set.Contains(next))
                    degree++;
            }

            if (degree == 1)
                endpoints++;
        }

        return endpoints;
    }

    private static bool IsFourConnected(
        StrokeComponent component,
        int sourceWidth)
    {
        var set =
            component.Pixels
                .ToHashSet();

        var visited =
            new HashSet<int>();
        var queue =
            new Queue<int>();

        var start =
            component.Pixels[0];

        visited.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var pixel =
                queue.Dequeue();
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;

            foreach (var (dx, dy) in FourDirections)
            {
                var next =
                    (y + dy) *
                    sourceWidth +
                    (x + dx);

                if (!set.Contains(next) ||
                    !visited.Add(next))
                {
                    continue;
                }

                queue.Enqueue(next);
            }
        }

        return visited.Count ==
               component.Pixels.Count;
    }

    private static bool TryFitNativeEllipse(
        StrokeComponent component,
        int sourceWidth,
        bool pixelCord,
        out ToolFaithfulEllipseFit fit)
    {
        fit =
            default;

        var width =
            component.MaxX -
            component.MinX +
            1;
        var height =
            component.MaxY -
            component.MinY +
            1;

        if (width < 5 ||
            height < 5 ||
            component.Pixels.Count < 12)
        {
            return false;
        }

        // Cheap O(1) rejection before EllipseOutline scans the entire bounding box. A simple
        // one-cell ellipse perimeter cannot contain anything close to a dense branched ornament
        // network. The generous 4*(w+h) ceiling still admits Pixel-Cord bridges, local hand edits
        // and very eccentric ellipses while skipping the huge connected outline graphs found in
        // real N69 designs.
        if (component.Pixels.Count >
            4L *
            (width + height))
        {
            return false;
        }

        var sourceSet =
            component.Pixels
                .Select(pixel =>
                    (
                        X: pixel %
                           sourceWidth,
                        Y: pixel /
                           sourceWidth))
                .ToHashSet();

        var candidate =
            (pixelCord
                ? Rasterizer.EllipseOutlineConnected(
                    component.MinX,
                    component.MinY,
                    component.MaxX,
                    component.MaxY)
                : Rasterizer.EllipseOutline(
                    component.MinX,
                    component.MinY,
                    component.MaxX,
                    component.MaxY))
            .ToHashSet();

        if (candidate.Count == 0)
            return false;

        var exactIntersection =
            sourceSet.Count(
                candidate.Contains);
        var exactPrecision =
            exactIntersection /
            (double)candidate.Count;
        var exactRecall =
            exactIntersection /
            (double)sourceSet.Count;
        var exactF1 =
            F1(
                exactPrecision,
                exactRecall);

        var sourceNear =
            sourceSet.Count(point =>
                HasPointNear(
                    candidate,
                    point,
                    radius: 1));
        var candidateNear =
            candidate.Count(point =>
                HasPointNear(
                    sourceSet,
                    point,
                    radius: 1));

        var nearF1 =
            F1(
                candidateNear /
                (double)candidate.Count,
                sourceNear /
                (double)sourceSet.Count);

        var areaRatio =
            Math.Min(
                sourceSet.Count,
                candidate.Count) /
            (double)Math.Max(
                sourceSet.Count,
                candidate.Count);

        var score =
            exactF1 *
            0.65 +
            nearF1 *
            0.25 +
            areaRatio *
            0.10;

        // High precision is intentional. A leaf perimeter can also be a closed one-cell loop but
        // must stay on exact graph replay unless it is genuinely explained by the native Ellipse
        // tool. This threshold accepts tool-created ellipses and lightly edited variants, not
        // generic floral loops.
        if (exactF1 < 0.80 ||
            nearF1 < 0.97 ||
            areaRatio < 0.80 ||
            score < 0.90)
        {
            return false;
        }

        fit =
            new ToolFaithfulEllipseFit(
                exactF1,
                nearF1,
                areaRatio,
                score);

        return true;
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

    private static bool HasPointNear(
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

    private static ToolFaithfulCurveStyleFit? ResolveStyleFit(
        IReadOnlyList<(int X, int Y)> chain,
        bool pixelCord,
        IDictionary<string, CachedCurveStyleFit?> cache,
        out bool cacheHit)
    {
        var canonical =
            CanonicalizeChain(
                chain,
                pixelCord);

        cacheHit =
            cache.TryGetValue(
                canonical.Key,
                out var cached);

        if (!cacheHit)
        {
            var learned =
                ToolFaithfulCurveStyleLearner.Fit(
                    canonical.Points,
                    pixelCord);

            if (learned is { } fit)
            {
                var origin =
                    canonical.Points[0];

                cached =
                    new CachedCurveStyleFit(
                        fit.Type,
                        fit.Roundness,
                        fit.Controls
                            .Select(point =>
                                (
                                    X: point.X -
                                       origin.X,
                                    Y: point.Y -
                                       origin.Y))
                            .ToArray(),
                        fit.Score,
                        fit.ModelScore,
                        fit.PolylineBaselineScore,
                        fit.PolylineBaselineModelScore,
                        fit.SimplifyTolerance);
            }
            else
            {
                cached = null;
            }

            cache[canonical.Key] =
                cached;
        }

        if (cached is null)
            return null;

        var currentOrigin =
            canonical.Points[0];

        var controls =
            cached.RelativeControls
                .Select(point =>
                    (
                        X: currentOrigin.X +
                           point.X,
                        Y: currentOrigin.Y +
                           point.Y))
                .ToArray();

        return new ToolFaithfulCurveStyleFit(
            cached.Type,
            cached.Roundness,
            controls,
            cached.Score,
            cached.ModelScore,
            cached.PolylineBaselineScore,
            cached.PolylineBaselineModelScore,
            cached.SimplifyTolerance);
    }

    private static CanonicalChain CanonicalizeChain(
        IReadOnlyList<(int X, int Y)> chain,
        bool pixelCord)
    {
        // Cache only translation-equivalent chains in the SAME traversal direction. Reversing a
        // chain is geometrically equivalent, but the inverse optimizer can legitimately settle on
        // a slightly different control model because endpoint tangents swap roles. A cache must
        // never change drawing semantics; reverse-oriented copies therefore fit independently.
        var forward =
            EncodeChainSteps(
                chain);

        return new CanonicalChain(
            (pixelCord ? "P:" : "C:") +
            forward,
            chain.ToArray());
    }

    private static string EncodeChainSteps(
        IReadOnlyList<(int X, int Y)> chain)
    {
        var builder =
            new System.Text.StringBuilder(
                Math.Max(
                    0,
                    chain.Count - 1));

        for (var i = 1;
             i < chain.Count;
             i++)
        {
            var dx =
                Math.Clamp(
                    chain[i].X -
                    chain[i - 1].X,
                    -1,
                    1);
            var dy =
                Math.Clamp(
                    chain[i].Y -
                    chain[i - 1].Y,
                    -1,
                    1);

            var code =
                (dy + 1) *
                3 +
                (dx + 1);

            builder.Append(
                (char)('A' + code));
        }

        return builder.ToString();
    }

    private static bool HasSourceCusp(
        IReadOnlyList<(int X, int Y)> chain)
    {
        var simplified =
            Simplify(
                chain,
                tolerance: 0.80);

        if (simplified.Count < 3)
            return false;

        for (var i = 1;
             i < simplified.Count - 1;
             i++)
        {
            var previous =
                simplified[i - 1];
            var current =
                simplified[i];
            var next =
                simplified[i + 1];

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
            var lengthA =
                Math.Sqrt(
                    ax * ax +
                    ay * ay);
            var lengthB =
                Math.Sqrt(
                    bx * bx +
                    by * by);

            if (lengthA <= 0d ||
                lengthB <= 0d)
            {
                continue;
            }

            var cosine =
                (ax * bx +
                 ay * by) /
                (lengthA *
                 lengthB);

            // More than ~70 degrees of direction change after RDP simplification is a genuine
            // cusp/tip, not Pixel-Cord stair-stepping. Preserve it literally.
            if (cosine < 0.34)
                return true;
        }

        return false;
    }

    private static bool IsLearnedCurveSafeAgainstSourceGraph(
        IReadOnlySet<(int X, int Y)> learned,
        IReadOnlySet<(int X, int Y)> sourceGraph,
        int radius = 1,
        double minimumLearnedSupport = 0.98,
        double minimumGraphSupport = 0.90)
    {
        if (learned.Count == 0 ||
            sourceGraph.Count == 0)
        {
            return false;
        }

        var learnedSupported =
            learned.Count(point =>
                HasPointNear(
                    sourceGraph,
                    point,
                    radius)) /
            (double)learned.Count;

        var graphSupported =
            sourceGraph.Count(point =>
                HasPointNear(
                    learned,
                    point,
                    radius)) /
            (double)sourceGraph.Count;

        // Almost every learned target pixel must be source-graph supported. Recall is slightly
        // looser because a smooth curve may legitimately skip a staircase shoulder while still
        // following exactly the same visual arc.
        return learnedSupported >=
               minimumLearnedSupport &&
               graphSupported >=
               minimumGraphSupport;
    }

    private static IReadOnlyList<(int X, int Y)> ConstrainToSourceStrokeCorridor(
        IEnumerable<(int X, int Y)> points,
        IReadOnlySet<int> sourcePixels,
        int sourceWidth,
        int sourceHeight,
        double scaleX,
        double scaleY,
        double maximumSourceDistance,
        out int clipped)
    {
        var result =
            new List<(int X, int Y)>();
        var seen =
            new HashSet<int>();
        clipped = 0;
        var maximumSquared =
            maximumSourceDistance *
            maximumSourceDistance;
        var searchRadius =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    maximumSourceDistance) +
                1);

        foreach (var point in points)
        {
            var targetKey =
                point.Y *
                1_000_003 +
                point.X;

            if (!seen.Add(targetKey))
                continue;

            var sourceX =
                (point.X + 0.5) /
                scaleX -
                0.5;
            var sourceY =
                (point.Y + 0.5) /
                scaleY -
                0.5;
            var centerX =
                (int)Math.Round(
                    sourceX);
            var centerY =
                (int)Math.Round(
                    sourceY);
            var supported = false;

            for (var y =
                     Math.Max(
                         0,
                         centerY - searchRadius);
                 y <=
                 Math.Min(
                     sourceHeight - 1,
                     centerY + searchRadius) &&
                 !supported;
                 y++)
            {
                for (var x =
                         Math.Max(
                             0,
                             centerX - searchRadius);
                     x <=
                     Math.Min(
                         sourceWidth - 1,
                         centerX + searchRadius);
                     x++)
                {
                    if (!sourcePixels.Contains(
                            y *
                            sourceWidth +
                            x))
                    {
                        continue;
                    }

                    var dx =
                        x - sourceX;
                    var dy =
                        y - sourceY;

                    if (dx * dx +
                        dy * dy <=
                        maximumSquared)
                    {
                        supported = true;
                        break;
                    }
                }
            }

            if (supported)
            {
                result.Add(point);
            }
            else
            {
                clipped++;
            }
        }

        return result;
    }

    private static IReadOnlyList<(int X, int Y)> MapControlSequence(
        IReadOnlyList<(int X, int Y)> controls,
        double scaleX,
        double scaleY,
        int targetWidth,
        int targetHeight)
    {
        var mapped =
            new List<(int X, int Y)>(
                controls.Count);

        foreach (var control in controls)
        {
            var point =
                (
                    X: MapCenter(
                        control.X,
                        scaleX,
                        targetWidth),
                    Y: MapCenter(
                        control.Y,
                        scaleY,
                        targetHeight));

            if (mapped.Count == 0 ||
                mapped[^1] != point)
            {
                mapped.Add(point);
            }
        }

        return mapped;
    }

    private static IEnumerable<(int X, int Y)> RenderMappedChainGraph(
        IReadOnlyList<(int X, int Y)> chain,
        int targetWidth,
        int targetHeight,
        double scaleX,
        double scaleY,
        bool pixelCord)
    {
        if (chain.Count == 0)
            yield break;

        var previous =
            (
                X: MapCenter(
                    chain[0].X,
                    scaleX,
                    targetWidth),
                Y: MapCenter(
                    chain[0].Y,
                    scaleY,
                    targetHeight));

        yield return previous;

        for (var i = 1;
             i < chain.Count;
             i++)
        {
            var current =
                (
                    X: MapCenter(
                        chain[i].X,
                        scaleX,
                        targetWidth),
                    Y: MapCenter(
                        chain[i].Y,
                        scaleY,
                        targetHeight));

            IEnumerable<(int X, int Y)> segment =
                Rasterizer.Line(
                    previous.X,
                    previous.Y,
                    current.X,
                    current.Y);

            if (pixelCord)
            {
                segment =
                    Rasterizer.ConnectDiagonalSteps(
                        segment);
            }

            foreach (var point in segment)
                yield return point;

            previous = current;
        }
    }

    private static bool TryTraceCompletePixelCordPath(
        StrokeComponent component,
        int sourceWidth,
        out List<(int X, int Y)> path)
    {
        path =
            new List<(int X, int Y)>();

        const int MaximumSearchPixels = 512;
        const int MaximumSearchStates = 250_000;

        if (component.Pixels.Count <
                MinimumPathPixels ||
            component.Pixels.Count >
                MaximumSearchPixels)
        {
            return false;
        }

        var set =
            component.Pixels
                .ToHashSet();
        var adjacency =
            new Dictionary<int, List<int>>(
                set.Count);

        foreach (var pixel in set)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var neighbors =
                new List<int>(4);

            foreach (var (dx, dy) in FourDirections)
            {
                var next =
                    (y + dy) *
                    sourceWidth +
                    (x + dx);

                if (set.Contains(next))
                    neighbors.Add(next);
            }

            adjacency[pixel] =
                neighbors;
        }

        var endpoints =
            adjacency
                .Where(pair =>
                    pair.Value.Count == 1)
                .Select(pair =>
                    pair.Key)
                .Order()
                .ToArray();

        if (endpoints.Length != 2)
            return false;

        var start =
            endpoints[0];
        var goal =
            endpoints[1];
        var visited =
            new HashSet<int>
            {
                start,
            };
        var ordered =
            new List<int>(
                set.Count)
            {
                start,
            };
        var states = 0;

        int RemainingDegree(
            int pixel) =>
            adjacency[pixel].Count(next =>
                !visited.Contains(next));

        bool Search(
            int current)
        {
            states++;

            if (states >
                MaximumSearchStates)
            {
                return false;
            }

            if (ordered.Count ==
                set.Count)
            {
                return current ==
                       goal;
            }

            if (current ==
                goal)
            {
                return false;
            }

            var candidates =
                adjacency[current]
                    .Where(next =>
                        !visited.Contains(next))
                    .OrderBy(next =>
                        next == goal
                            ? int.MaxValue
                            : RemainingDegree(next))
                    .ThenBy(next =>
                        next)
                    .ToArray();

            foreach (var next in candidates)
            {
                // The goal is the final endpoint. Entering it early would strand remaining pixels.
                if (next == goal &&
                    ordered.Count + 1 <
                    set.Count)
                {
                    continue;
                }

                visited.Add(next);
                ordered.Add(next);

                var stranded = false;

                // Cheap Hamiltonian pruning: every still-unvisited non-goal pixel must retain at
                // least one route into the remaining graph.
                foreach (var pixel in set)
                {
                    if (visited.Contains(pixel) ||
                        pixel == goal)
                    {
                        continue;
                    }

                    if (adjacency[pixel].Any(candidate =>
                            !visited.Contains(candidate) ||
                            candidate == next))
                    {
                        continue;
                    }

                    stranded = true;
                    break;
                }

                if (!stranded &&
                    Search(next))
                {
                    return true;
                }

                ordered.RemoveAt(
                    ordered.Count - 1);
                visited.Remove(next);
            }

            return false;
        }

        if (!Search(start))
            return false;

        path =
            ordered
                .Select(pixel =>
                    (
                        X: pixel %
                           sourceWidth,
                        Y: pixel /
                           sourceWidth))
                .ToList();

        return path.Count ==
               component.Pixels.Count;
    }

    private static bool LooksLikeSmoothOvalRecovery(
        IReadOnlyList<(int X, int Y)> path)
    {
        if (path.Count < 20)
            return false;

        var simplified =
            Simplify(
                path,
                tolerance: 0.80);

        // A literal L/V corner collapses to roughly three RDP vertices. A broad oval/arch keeps
        // several gradual direction changes, even when high roundness creates a short shoulder
        // reversal in the raster. Require that richer curve evidence before bypassing cusp logic.
        if (simplified.Count < 5)
            return false;

        var minX =
            path.Min(point =>
                point.X);
        var maxX =
            path.Max(point =>
                point.X);
        var minY =
            path.Min(point =>
                point.Y);
        var maxY =
            path.Max(point =>
                point.Y);
        var width =
            maxX -
            minX;
        var height =
            maxY -
            minY;
        var first =
            path[0];
        var last =
            path[^1];
        var useX =
            width >=
            height;
        var extent =
            useX
                ? width
                : height;
        var endpointSpan =
            useX
                ? Math.Abs(
                    last.X -
                    first.X)
                : Math.Abs(
                    last.Y -
                    first.Y);

        return extent >= 8 &&
               endpointSpan >=
               extent * 0.75;
    }

    private static IReadOnlyList<List<(int X, int Y)>> TraceChains(
        StrokeComponent component,
        int sourceWidth,
        bool pixelCord)
    {
        var set =
            component.Pixels
                .ToHashSet();
        var directions =
            pixelCord
                ? FourDirections
                : EightDirections;

        var adjacency =
            new Dictionary<int, List<int>>(
                set.Count);

        foreach (var pixel in set)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var neighbors =
                new List<int>();

            foreach (var (dx, dy) in directions)
            {
                var nx = x + dx;
                var ny = y + dy;
                var next =
                    ny *
                    sourceWidth +
                    nx;

                if (set.Contains(next))
                    neighbors.Add(next);
            }

            // In plain 8-connected mode, do not take a diagonal shortcut when the source already
            // has an orthogonal bridge. That keeps the graph faithful to Pixel-Cord-like elbows.
            if (!pixelCord)
            {
                neighbors.RemoveAll(next =>
                {
                    var nx =
                        next %
                        sourceWidth;
                    var ny =
                        next /
                        sourceWidth;
                    var dx =
                        nx - x;
                    var dy =
                        ny - y;

                    if (Math.Abs(dx) != 1 ||
                        Math.Abs(dy) != 1)
                    {
                        return false;
                    }

                    var bridgeA =
                        y *
                        sourceWidth +
                        (x + dx);
                    var bridgeB =
                        (y + dy) *
                        sourceWidth +
                        x;

                    return set.Contains(bridgeA) ||
                           set.Contains(bridgeB);
                });
            }

            adjacency[pixel] = neighbors;
        }

        var chains =
            new List<List<(int X, int Y)>>();
        var visitedEdges =
            new HashSet<long>();

        static long EdgeKey(
            int a,
            int b)
        {
            var lo =
                Math.Min(
                    a,
                    b);
            var hi =
                Math.Max(
                    a,
                    b);

            return ((long)lo << 32) |
                   (uint)hi;
        }

        var nodes =
            adjacency
                .Where(pair =>
                    pair.Value.Count != 2)
                .Select(pair =>
                    pair.Key)
                .Order()
                .ToArray();

        void TraceFrom(
            int start,
            int next)
        {
            var edge =
                EdgeKey(
                    start,
                    next);

            if (!visitedEdges.Add(edge))
                return;

            var chain =
                new List<int>
                {
                    start,
                    next,
                };

            var previous = start;
            var current = next;

            while (true)
            {
                var options =
                    adjacency[current]
                        .Where(candidate =>
                            candidate != previous &&
                            !visitedEdges.Contains(
                                EdgeKey(
                                    current,
                                    candidate)))
                        .ToArray();

                if (adjacency[current].Count != 2 ||
                    options.Length == 0)
                {
                    break;
                }

                var chosen =
                    options[0];

                visitedEdges.Add(
                    EdgeKey(
                        current,
                        chosen));

                chain.Add(chosen);
                previous = current;
                current = chosen;
            }

            if (chain.Count >= 2)
            {
                chains.Add(
                    chain
                        .Select(pixel =>
                            (
                                X: pixel %
                                   sourceWidth,
                                Y: pixel /
                                   sourceWidth))
                        .ToList());
            }
        }

        foreach (var node in nodes)
        {
            foreach (var neighbor in adjacency[node])
                TraceFrom(node, neighbor);
        }

        // Pure loop: every pixel has degree 2, so there was no natural start node.
        foreach (var pixel in set.Order())
        {
            foreach (var neighbor in adjacency[pixel])
            {
                if (visitedEdges.Contains(
                        EdgeKey(
                            pixel,
                            neighbor)))
                {
                    continue;
                }

                TraceFrom(
                    pixel,
                    neighbor);
            }
        }

        return chains;
    }

    private static IReadOnlyList<(int X, int Y)> Simplify(
        IReadOnlyList<(int X, int Y)> points,
        double tolerance)
    {
        if (points.Count <= 2)
            return points.ToArray();

        var keep =
            new bool[
                points.Count];
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
        if (last <= first + 1)
            return;

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

            if (distance <= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex < 0 ||
            bestDistance <= toleranceSquared)
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
            b.X - a.X;
        var vy =
            b.Y - a.Y;
        var wx =
            point.X - a.X;
        var wy =
            point.Y - a.Y;
        var lengthSquared =
            vx * vx +
            vy * vy;

        if (lengthSquared <= 0)
        {
            return wx * wx +
                   wy * wy;
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

        return dx * dx +
               dy * dy;
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

    private static IEnumerable<(int X, int Y)> RenderWithToolRules(
        IReadOnlyList<(int X, int Y)> sourceControls,
        IReadOnlyList<(int X, int Y)> mappedControls)
    {
        if (mappedControls.Count < 2)
            yield break;

        var breaks =
            new SortedSet<int>
            {
                0,
                mappedControls.Count - 1,
            };

        for (var i = 1;
             i < sourceControls.Count - 1;
             i++)
        {
            if (IsSharpCorner(
                    sourceControls[i - 1],
                    sourceControls[i],
                    sourceControls[i + 1]))
            {
                breaks.Add(i);
            }
        }

        var breakArray =
            breaks.ToArray();

        for (var segmentIndex = 1;
             segmentIndex < breakArray.Length;
             segmentIndex++)
        {
            var first =
                breakArray[segmentIndex - 1];
            var last =
                breakArray[segmentIndex];

            if (last <= first)
                continue;

            var sourceSegment =
                sourceControls
                    .Skip(first)
                    .Take(last - first + 1)
                    .ToArray();
            var mappedSegment =
                mappedControls
                    .Skip(first)
                    .Take(last - first + 1)
                    .ToArray();

            IEnumerable<(int X, int Y)> segment;

            if (mappedSegment.Length >= 3 &&
                LooksCurved(
                    sourceSegment))
            {
                // Standard RugScale Curve / through-points rasterization.
                segment =
                    CurveRasterizer.Draw(
                        mappedSegment,
                        CurveType.SplineThroughPoints,
                        0.25);
            }
            else
            {
                // Sharp source corners (rectangle/border/leaf cusp) are kept as actual tool
                // polyline corners instead of being rounded by a spline.
                segment =
                    Polyline(
                        mappedSegment);
            }

            foreach (var point in segment)
                yield return point;
        }
    }

    private static bool IsSharpCorner(
        (int X, int Y) previous,
        (int X, int Y) current,
        (int X, int Y) next)
    {
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

        var lengthA =
            Math.Sqrt(
                ax * ax +
                ay * ay);
        var lengthB =
            Math.Sqrt(
                bx * bx +
                by * by);

        if (lengthA <= 0 ||
            lengthB <= 0)
        {
            return false;
        }

        var cosine =
            (ax * bx +
             ay * by) /
            (lengthA *
             lengthB);

        // cos(60deg) = .5. A turn sharper than roughly 60 degrees is a deliberate corner/cusp,
        // not something the Curve tool should smooth through.
        return cosine < 0.5;
    }

    private static bool LooksCurved(
        IReadOnlyList<(int X, int Y)> controls)
    {
        if (controls.Count < 3)
            return false;

        var a =
            controls[0];
        var b =
            controls[^1];

        var maximumDeviation = 0d;

        for (var i = 1;
             i < controls.Count - 1;
             i++)
        {
            maximumDeviation =
                Math.Max(
                    maximumDeviation,
                    Math.Sqrt(
                        DistanceToSegmentSquared(
                            controls[i],
                            a,
                            b)));
        }

        return maximumDeviation >=
               CurveDeviationThreshold;
    }

    private static IEnumerable<(int X, int Y)> Polyline(
        IReadOnlyList<(int X, int Y)> points)
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

    private static int Paint(
        DesignDocument destination,
        IEnumerable<(int X, int Y)> points,
        byte color)
    {
        var changed = 0;
        var seen =
            new HashSet<int>();

        foreach (var (x, y) in points)
        {
            if (x < 0 ||
                x >= destination.Width ||
                y < 0 ||
                y >= destination.Height)
            {
                continue;
            }

            var key =
                y *
                destination.Width +
                x;

            if (!seen.Add(key))
                continue;

            if (destination.GetPixel(
                    x,
                    y) == color)
            {
                continue;
            }

            destination.SetPixel(
                x,
                y,
                color);
            changed++;
        }

        return changed;
    }

    private static int MapCenter(
        int sourceCoordinate,
        double scale,
        int targetLength) =>
        Math.Clamp(
            (int)Math.Round(
                (sourceCoordinate + 0.5) *
                scale -
                0.5),
            0,
            targetLength - 1);

    private readonly record struct ToolFaithfulEllipseFit(
        double ExactF1,
        double NearF1,
        double AreaRatio,
        double Score);

    private sealed record CachedCurveStyleFit(
        CurveType Type,
        double Roundness,
        IReadOnlyList<(int X, int Y)> RelativeControls,
        double Score,
        double ModelScore,
        double PolylineBaselineScore,
        double PolylineBaselineModelScore,
        double SimplifyTolerance);

    private sealed record CanonicalChain(
        string Key,
        IReadOnlyList<(int X, int Y)> Points);

    private sealed record StrokeComponent(
        byte Color,
        IReadOnlyList<int> Pixels,
        int MinX,
        int MinY,
        int MaxX,
        int MaxY,
        bool TrustedStrokeRole);
}

internal readonly record struct ToolFaithfulOverlayReport(
    int AcceptedComponents,
    int PixelCordComponents,
    int RedrawnChains,
    int RedrawnPixels,
    int LearnedCurves,
    int LearnedThroughPoints,
    int LearnedSpline,
    int LearnedBezier,
    int LearnedEllipses,
    int GraphFallbacks,
    int StyleFitCacheHits,
    int CurveSafetyFallbacks,
    int CorridorClippedPixels,
    double MeanLearnedRoundness,
    int RegionOwnershipCorrections = 0,
    int BarrierCrossingCorrections = 0,
    int CompletePathRecoveries = 0);
