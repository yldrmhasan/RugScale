using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Authoritative redraw path for a fitted, outlined designer ribbon.
///
/// The older ribbon rasterizers intentionally behave like boundary smoothers: they only edit a
/// narrow neighbourhood around the block-scaled source boundary and preserve topology anchors.
/// That is safe, but it cannot produce a genuinely redrawn curve when the source staircase itself
/// is the problem.
///
/// This path uses the accepted geometric fit as the target authority. Inside a tightly source-
/// bounded sweep tube it rebuilds BOTH the coloured ribbon and its dedicated protected outline from
/// the fitted centreline + width profile. Other protected stroke roles remain hard barriers.
///
/// It is deliberately not a generic region resampler. The caller must grant high-confidence redraw
/// authority, and this class additionally requires one dominant local outline colour.
/// </summary>
internal static class CurveFillTrueRibbonRasterizer
{
    private const double OutlineExpansionTarget = 1.0;
    private const double EditScopeMarginSource = 3.25;
    private const double SourceBoundaryAuthorityRadius = 4.25;

    public static bool TryApply(
        DesignDocument source,
        DesignDocument destination,
        LeafPetalArcModel model,
        ElegantArcFit fit,
        IReadOnlySet<byte> protectedStrokeColors,
        out int changed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        changed = 0;

        if (!fit.IsSafe ||
            fit.Points.Count < 4 ||
            !CurveFillOutlinedRibbonRasterizer.TryFindDominantOutlineColor(
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

        var fillPolygon =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                fit.Points,
                scaleX,
                scaleY);

        if (fillPolygon.Count < 6)
            return false;

        var fillMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                fillPolygon,
                destination.Width,
                destination.Height);

        if (fillMask.Count == 0)
            return false;

        var outerPolygon =
            LeafPetalArcRasterizer.BuildTargetExpandedPolygon(
                fit.Points,
                scaleX,
                scaleY,
                OutlineExpansionTarget);
        var outerMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                outerPolygon,
                destination.Width,
                destination.Height);

        if (outerMask.Count == 0)
            return false;

        var region =
            model.Candidate.Region;
        var regionColor =
            region.Color;
        var sourceBoundary =
            region.BoundaryPixels.ToHashSet();

        // Include both old mapped ownership and the new fitted geometry in the edit box. The actual
        // per-pixel authority remains much tighter via CurveFillRibbonRasterScope + boundary
        // evidence below.
        var mappedMinX =
            MapSourceToTarget(
                region.MinX - 3,
                scaleX);
        var mappedMaxX =
            MapSourceToTarget(
                region.MaxX + 3,
                scaleX);
        var mappedMinY =
            MapSourceToTarget(
                region.MinY - 3,
                scaleY);
        var mappedMaxY =
            MapSourceToTarget(
                region.MaxY + 3,
                scaleY);

        var minX =
            Math.Max(
                0,
                Math.Min(
                    mappedMinX,
                    (int)Math.Floor(
                        outerPolygon.Min(point =>
                            point.X)) -
                    3));
        var maxX =
            Math.Min(
                destination.Width - 1,
                Math.Max(
                    mappedMaxX,
                    (int)Math.Ceiling(
                        outerPolygon.Max(point =>
                            point.X)) +
                    3));
        var minY =
            Math.Max(
                0,
                Math.Min(
                    mappedMinY,
                    (int)Math.Floor(
                        outerPolygon.Min(point =>
                            point.Y)) -
                    3));
        var maxY =
            Math.Min(
                destination.Height - 1,
                Math.Max(
                    mappedMaxY,
                    (int)Math.Ceiling(
                        outerPolygon.Max(point =>
                            point.Y)) +
                    3));

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

                if (!CurveFillRibbonRasterScope.Contains(
                        fit,
                        sourceX,
                        sourceY,
                        extraMargin:
                            EditScopeMarginSource))
                {
                    continue;
                }

                if (!IsNearSourceBoundary(
                        sourceBoundary,
                        source.Width,
                        source.Height,
                        sourceX,
                        sourceY,
                        SourceBoundaryAuthorityRadius))
                {
                    continue;
                }

                var key =
                    y *
                    destination.Width +
                    x;
                var wantFill =
                    fillMask.Contains(
                        key);
                var wantOutline =
                    !wantFill &&
                    outerMask.Contains(
                        key);
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

                if (wantFill)
                {
                    if (current ==
                        regionColor)
                    {
                        continue;
                    }

                    // A true redraw may consume the region's own outline and nearby unprotected
                    // exterior phase, but never another protected drawing role.
                    if (protectedStrokeColors.Contains(
                            current) &&
                        current !=
                            outlineColor)
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

                    destination.SetPixel(
                        x,
                        y,
                        regionColor);
                    changed++;
                    continue;
                }

                if (wantOutline)
                {
                    if (current ==
                        outlineColor)
                    {
                        continue;
                    }

                    if (protectedStrokeColors.Contains(
                            current) &&
                        current !=
                            outlineColor)
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

                    destination.SetPixel(
                        x,
                        y,
                        outlineColor);
                    changed++;
                    continue;
                }

                // Outside the authoritative fitted compound silhouette, remove stale block-scaled
                // ribbon/outline ownership. Nothing else is touched.
                if (current !=
                        regionColor &&
                    current !=
                        outlineColor)
                {
                    continue;
                }

                if (!CurveFillOutlinedRibbonRasterizer.TryFindNearestExteriorColor(
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

                if (replacement ==
                        regionColor ||
                    replacement ==
                        outlineColor)
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

        return changed > 0;
    }

    private static int MapSourceToTarget(
        double sourceCoordinate,
        double scale)
    {
        return (int)Math.Round(
            (sourceCoordinate + 0.5) *
            scale -
            0.5);
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
