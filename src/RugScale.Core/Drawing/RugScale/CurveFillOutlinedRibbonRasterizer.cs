using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Moves a high-confidence filled ribbon boundary against its own dedicated outline colour.
///
/// Real N69 ornaments often encode a coloured band (for example the C069 green half-oval) fully
/// surrounded by the white 1x1 Pixel-Cord role. The generic specialist correctly treats that role
/// as a hard barrier, but that also means the coloured band can never shed its source staircase.
/// This rasterizer is intentionally narrower: it only swaps the region colour with ONE dominant
/// protected outline colour in the local source-boundary corridor. It never paints over a third
/// colour and never moves the outer side of the outline.
/// </summary>
internal static class CurveFillOutlinedRibbonRasterizer
{
    private const double BoundaryBandRadius = 2.15;
    private const double CompoundBoundaryBandRadius = 3.25;
    private const double OutlineExpansionSource = 1.0;
    private const int NearbyOutlineRadius = 3;
    private const double MinimumOutlineNeighbourShare = 0.78;
    private const int MinimumOutlineContacts = 12;

    public static bool TryApply(
        DesignDocument source,
        DesignDocument destination,
        LeafPetalArcModel model,
        ElegantArcFit fit,
        IReadOnlySet<byte> protectedStrokeColors,
        out int changed,
        bool restrictToFittedSweep = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        changed = 0;

        if (!fit.IsSafe ||
            fit.Points.Count < 4 ||
            !TryFindDominantOutlineColor(
                source,
                model.Candidate.Region,
                protectedStrokeColors,
                out var outlineColor))
        {
            return false;
        }

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var polygon =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                fit.Points,
                scaleX,
                scaleY);

        if (polygon.Count < 6)
            return false;

        var targetMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                polygon,
                destination.Width,
                destination.Height);

        if (targetMask.Count == 0)
            return false;

        // Rebuild the dedicated 1x1 outline as the same continuous ribbon geometry expanded by
        // one source pixel. This is the missing outer silhouette: smoothing only the coloured
        // band/outline boundary still leaves the eye following the old block-scaled white edge.
        var compoundPoints =
            fit.Points
                .Select(point =>
                    point with
                    {
                        HalfWidth =
                            point.HalfWidth +
                            OutlineExpansionSource,
                    })
                .ToArray();
        var compoundPolygon =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                compoundPoints,
                scaleX,
                scaleY);
        var compoundMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                compoundPolygon,
                destination.Width,
                destination.Height);

        if (compoundMask.Count == 0)
            return false;

        var region =
            model.Candidate.Region;
        var boundary =
            region.BoundaryPixels.ToHashSet();
        var minX =
            Math.Max(
                0,
                (int)Math.Floor(
                    compoundPolygon.Min(point =>
                        point.X)) -
                3);
        var maxX =
            Math.Min(
                destination.Width - 1,
                (int)Math.Ceiling(
                    compoundPolygon.Max(point =>
                        point.X)) +
                3);
        var minY =
            Math.Max(
                0,
                (int)Math.Floor(
                    compoundPolygon.Min(point =>
                        point.Y)) -
                3);
        var maxY =
            Math.Min(
                destination.Height - 1,
                (int)Math.Ceiling(
                    compoundPolygon.Max(point =>
                        point.Y)) +
                3);

        var regionColor =
            region.Color;

        // Snapshot which target cells already carry the dedicated outline before editing. This
        // prevents scan-order effects when checking that an expansion leaves outline support on
        // the outside of the new smooth boundary.
        var originalOutline =
            new HashSet<int>();

        for (var y = minY;
             y <= maxY;
             y++)
        {
            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                if (destination.GetPixel(
                        x,
                        y) ==
                    outlineColor)
                {
                    originalOutline.Add(
                        y *
                        destination.Width +
                        x);
                }
            }
        }

        for (var y = minY;
             y <= maxY;
             y++)
        {
            var sourceY =
                (y + 0.5) /
                scaleY -
                0.5;

            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                var sourceX =
                    (x + 0.5) /
                    scaleX -
                    0.5;

                if (!IsNearSourceBoundary(
                        boundary,
                        source.Width,
                        source.Height,
                        sourceX,
                        sourceY,
                        BoundaryBandRadius))
                {
                    continue;
                }

                if (restrictToFittedSweep &&
                    !CurveFillRibbonRasterScope.Contains(
                        fit,
                        sourceX,
                        sourceY,
                        extraMargin: BoundaryBandRadius))
                {
                    continue;
                }

                var key =
                    y *
                    destination.Width +
                    x;
                var inside =
                    targetMask.Contains(
                        key);
                var current =
                    destination.GetPixel(
                        x,
                        y);

                if (inside)
                {
                    if (current == regionColor)
                        continue;

                    // Expansion is legal ONLY into this region's own protected outline, and only
                    // when at least one outline cell remains immediately outside the new mask.
                    if (current != outlineColor ||
                        !HasPlannedOutsideOutlineSupport(
                            x,
                            y,
                            destination.Width,
                            destination.Height,
                            targetMask,
                            compoundMask,
                            originalOutline))
                    {
                        continue;
                    }

                    destination.SetPixel(
                        x,
                        y,
                        regionColor);
                    changed++;
                    continue;
                }

                // A geometric shave converts old coloured staircase protrusions to the SAME
                // outline colour. No third palette role is ever introduced or crossed.
                if (current != regionColor)
                    continue;

                // Do not trade a smoother outline for a broken ribbon. Source staircases may
                // contain one-pixel neck cells; those are topology anchors even when the fitted
                // target mask would otherwise shave them away.
                if (WouldDisconnectRegion(
                        destination,
                        x,
                        y,
                        regionColor))
                {
                    continue;
                }

                destination.SetPixel(
                    x,
                    y,
                    outlineColor);
                changed++;
            }
        }

        // Phase 2: move the OUTER side of the dedicated outline to the same smooth geometry.
        // Edits stay in the original local outline corridor and never cross another protected
        // stroke role. When the old outline recedes, source-local exterior ownership restores the
        // uncovered cell.
        for (var y = minY;
             y <= maxY;
             y++)
        {
            var sourceY =
                (y + 0.5) /
                scaleY -
                0.5;

            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                var sourceX =
                    (x + 0.5) /
                    scaleX -
                    0.5;

                if (!IsNearSourceBoundary(
                        boundary,
                        source.Width,
                        source.Height,
                        sourceX,
                        sourceY,
                        CompoundBoundaryBandRadius))
                {
                    continue;
                }

                if (restrictToFittedSweep &&
                    !CurveFillRibbonRasterScope.Contains(
                        fit,
                        sourceX,
                        sourceY,
                        extraMargin: CompoundBoundaryBandRadius))
                {
                    continue;
                }

                var key =
                    y *
                    destination.Width +
                    x;
                var insideFill =
                    targetMask.Contains(
                        key);
                var insideCompound =
                    compoundMask.Contains(
                        key);
                var wantOutline =
                    insideCompound &&
                    !insideFill;
                var current =
                    destination.GetPixel(
                        x,
                        y);

                var sx =
                    Math.Clamp(
                        (int)Math.Round(
                            sourceX),
                        0,
                        source.Width - 1);
                var sy =
                    Math.Clamp(
                        (int)Math.Round(
                            sourceY),
                        0,
                        source.Height - 1);
                var sourceOwner =
                    source.GetPixel(
                        sx,
                        sy);

                if (wantOutline)
                {
                    if (current ==
                        outlineColor)
                    {
                        continue;
                    }

                    if (protectedStrokeColors.Contains(
                            current))
                    {
                        continue;
                    }

                    if (protectedStrokeColors.Contains(
                            sourceOwner) &&
                        sourceOwner !=
                            outlineColor)
                    {
                        continue;
                    }

                    if (!HasNearbyOriginalOutline(
                            x,
                            y,
                            destination.Width,
                            destination.Height,
                            originalOutline,
                            NearbyOutlineRadius))
                    {
                        continue;
                    }

                    if (current ==
                            regionColor &&
                        WouldDisconnectRegion(
                            destination,
                            x,
                            y,
                            regionColor))
                    {
                        continue;
                    }

                    destination.SetPixel(
                        x,
                        y,
                        outlineColor);
                    changed++;
                    continue;
                }

                if (insideCompound ||
                    current !=
                        outlineColor ||
                    !originalOutline.Contains(
                        key))
                {
                    continue;
                }

                // The target fill must retain an outline neighbour. This makes the outer-boundary
                // move safe even when the white role participates in a larger separator network.
                if (HasTargetFillNeighbour(
                        x,
                        y,
                        destination.Width,
                        destination.Height,
                        targetMask))
                {
                    continue;
                }

                if (!TryFindNearestExteriorColor(
                        source,
                        sourceX,
                        sourceY,
                        regionColor,
                        outlineColor,
                        protectedStrokeColors,
                        out var replacement))
                {
                    continue;
                }

                destination.SetPixel(
                    x,
                    y,
                    replacement);
                changed++;
            }
        }

        return true;
    }

    private static bool WouldDisconnectRegion(
        DesignDocument document,
        int x,
        int y,
        byte color)
    {
        Span<bool> ring =
        [
            y > 0 &&
            document.GetPixel(x, y - 1) == color,
            x + 1 < document.Width &&
            y > 0 &&
            document.GetPixel(x + 1, y - 1) == color,
            x + 1 < document.Width &&
            document.GetPixel(x + 1, y) == color,
            x + 1 < document.Width &&
            y + 1 < document.Height &&
            document.GetPixel(x + 1, y + 1) == color,
            y + 1 < document.Height &&
            document.GetPixel(x, y + 1) == color,
            x > 0 &&
            y + 1 < document.Height &&
            document.GetPixel(x - 1, y + 1) == color,
            x > 0 &&
            document.GetPixel(x - 1, y) == color,
            x > 0 &&
            y > 0 &&
            document.GetPixel(x - 1, y - 1) == color,
        ];

        var neighbours = 0;
        var groups = 0;

        for (var index = 0;
             index < ring.Length;
             index++)
        {
            if (!ring[index])
                continue;

            neighbours++;

            if (!ring[
                    (index +
                     ring.Length -
                     1) %
                    ring.Length])
            {
                groups++;
            }
        }

        return neighbours >= 2 &&
               groups >= 2;
    }

    private static bool HasNearbyOriginalOutline(
        int x,
        int y,
        int width,
        int height,
        IReadOnlySet<int> originalOutline,
        int radius)
    {
        var radiusSquared =
            radius *
            radius;

        for (var dy = -radius;
             dy <= radius;
             dy++)
        {
            for (var dx = -radius;
                 dx <= radius;
                 dx++)
            {
                if (dx *
                        dx +
                    dy *
                        dy >
                    radiusSquared)
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
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                if (originalOutline.Contains(
                        ny *
                            width +
                        nx))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasTargetFillNeighbour(
        int x,
        int y,
        int width,
        int height,
        IReadOnlySet<int> targetMask)
    {
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
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                if (targetMask.Contains(
                        ny *
                            width +
                        nx))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool TryFindNearestExteriorColor(
        DesignDocument source,
        double sourceX,
        double sourceY,
        byte regionColor,
        byte outlineColor,
        IReadOnlySet<byte> protectedStrokeColors,
        out byte color)
    {
        var centerX =
            Math.Clamp(
                (int)Math.Round(
                    sourceX),
                0,
                source.Width - 1);
        var centerY =
            Math.Clamp(
                (int)Math.Round(
                    sourceY),
                0,
                source.Height - 1);
        var bestDistance =
            double.PositiveInfinity;
        var found = false;
        color = 0;

        const int SearchRadius = 4;

        for (var y =
                 Math.Max(
                     0,
                     centerY - SearchRadius);
             y <=
             Math.Min(
                 source.Height - 1,
                 centerY + SearchRadius);
             y++)
        {
            for (var x =
                     Math.Max(
                         0,
                         centerX - SearchRadius);
                 x <=
                 Math.Min(
                     source.Width - 1,
                     centerX + SearchRadius);
                 x++)
            {
                var candidate =
                    source.GetPixel(
                        x,
                        y);

                if (candidate ==
                        regionColor ||
                    candidate ==
                        outlineColor ||
                    protectedStrokeColors.Contains(
                        candidate))
                {
                    continue;
                }

                var dx =
                    x -
                    sourceX;
                var dy =
                    y -
                    sourceY;
                var distance =
                    dx *
                        dx +
                    dy *
                        dy;

                if (distance >=
                    bestDistance)
                {
                    continue;
                }

                bestDistance =
                    distance;
                color =
                    candidate;
                found = true;
            }
        }

        return found;
    }

    internal static bool TryFindDominantOutlineColor(
        DesignDocument source,
        LeafPetalRegion region,
        IReadOnlySet<byte> protectedStrokeColors,
        out byte outlineColor)
    {
        Span<int> contacts =
            stackalloc int[256];
        var total = 0;
        var regionPixels =
            region.Pixels.ToHashSet();

        foreach (var pixel in region.BoundaryPixels)
        {
            var x =
                pixel %
                source.Width;
            var y =
                pixel /
                source.Width;

            foreach (var (dx, dy) in
                     new[]
                     {
                         (-1, 0),
                         (1, 0),
                         (0, -1),
                         (0, 1),
                     })
            {
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

                var next =
                    ny *
                    source.Width +
                    nx;

                if (regionPixels.Contains(
                        next))
                {
                    continue;
                }

                var color =
                    source.GetPixel(
                        nx,
                        ny);

                total++;

                if (protectedStrokeColors.Contains(
                        color))
                {
                    contacts[color]++;
                }
            }
        }

        var bestColor = -1;
        var bestContacts = 0;

        for (var index = 0;
             index < contacts.Length;
             index++)
        {
            if (contacts[index] <=
                bestContacts)
            {
                continue;
            }

            bestContacts =
                contacts[index];
            bestColor =
                index;
        }

        if (bestColor < 0 ||
            bestContacts <
                MinimumOutlineContacts ||
            total <= 0 ||
            bestContacts /
                (double)total <
                MinimumOutlineNeighbourShare)
        {
            outlineColor = 0;
            return false;
        }

        outlineColor =
            (byte)bestColor;
        return true;
    }

    private static bool HasPlannedOutsideOutlineSupport(
        int x,
        int y,
        int width,
        int height,
        IReadOnlySet<int> targetMask,
        IReadOnlySet<int> compoundMask,
        IReadOnlySet<int> originalOutline)
    {
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
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                var key =
                    ny *
                    width +
                    nx;

                if (targetMask.Contains(
                        key) ||
                    !compoundMask.Contains(
                        key))
                {
                    continue;
                }

                // The new continuous geometry explicitly reserves this neighbour for the
                // dedicated outline. It may be one raster phase outside the old white line, so
                // accept either direct old-outline ownership or close old-outline evidence. Phase
                // 2 will materialize the planned outline before any old exterior is released.
                if (originalOutline.Contains(
                        key) ||
                    HasNearbyOriginalOutline(
                        nx,
                        ny,
                        width,
                        height,
                        originalOutline,
                        radius: 2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNearSourceBoundary(
        IReadOnlySet<int> boundary,
        int width,
        int height,
        double sourceX,
        double sourceY,
        double radius)
    {
        var centerX =
            Math.Clamp(
                (int)Math.Round(
                    sourceX),
                0,
                width - 1);
        var centerY =
            Math.Clamp(
                (int)Math.Round(
                    sourceY),
                0,
                height - 1);
        var search =
            (int)Math.Ceiling(
                radius) +
            1;
        var radiusSquared =
            radius *
            radius;

        for (var y =
                 Math.Max(
                     0,
                     centerY - search);
             y <=
             Math.Min(
                 height - 1,
                 centerY + search);
             y++)
        {
            for (var x =
                     Math.Max(
                         0,
                         centerX - search);
                 x <=
                 Math.Min(
                     width - 1,
                     centerX + search);
                 x++)
            {
                if (!boundary.Contains(
                        y *
                        width +
                        x))
                {
                    continue;
                }

                var dx =
                    x -
                    sourceX;
                var dy =
                    y -
                    sourceY;

                if (dx *
                        dx +
                    dy *
                        dy <=
                    radiusSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
