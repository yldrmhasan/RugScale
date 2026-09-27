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
        out int changed,
        bool useSweptTube = false)
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

        HashSet<int> fillMask;
        HashSet<int> outerMask;

        if (useSweptTube)
        {
            // Tight ornamental hooks can make paired normal-offset polygons self-intersect. A
            // variable-radius swept tube is topologically safer: it is the union of source-faithful
            // centreline capsules and therefore cannot create polygon winding holes at a hairpin.
            fillMask =
                BuildTargetSweptTubeMask(
                    fit.Points,
                    scaleX,
                    scaleY,
                    destination.Width,
                    destination.Height,
                    additionalTargetPixels: 0d);
            outerMask =
                BuildTargetSweptTubeMask(
                    fit.Points,
                    scaleX,
                    scaleY,
                    destination.Width,
                    destination.Height,
                    additionalTargetPixels:
                        OutlineExpansionTarget);
        }
        else
        {
            var fillPolygon =
                LeafPetalArcRasterizer.BuildTargetPolygon(
                    fit.Points,
                    scaleX,
                    scaleY);

            if (fillPolygon.Count < 6)
                return false;

            fillMask =
                LeafPetalArcRasterizer.RasterizePolygon(
                    fillPolygon,
                    destination.Width,
                    destination.Height);

            var outerPolygon =
                LeafPetalArcRasterizer.BuildTargetExpandedPolygon(
                    fit.Points,
                    scaleX,
                    scaleY,
                    OutlineExpansionTarget);
            outerMask =
                LeafPetalArcRasterizer.RasterizePolygon(
                    outerPolygon,
                    destination.Width,
                    destination.Height);
        }

        if (fillMask.Count == 0 ||
            outerMask.Count == 0)
        {
            return false;
        }

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

        var outerBounds =
            MaskBounds(
                outerMask,
                destination.Width);

        var minX =
            Math.Max(
                0,
                Math.Min(
                    mappedMinX,
                    outerBounds.MinX -
                    3));
        var maxX =
            Math.Min(
                destination.Width - 1,
                Math.Max(
                    mappedMaxX,
                    outerBounds.MaxX +
                    3));
        var minY =
            Math.Max(
                0,
                Math.Min(
                    mappedMinY,
                    outerBounds.MinY -
                    3));
        var maxY =
            Math.Min(
                destination.Height - 1,
                Math.Max(
                    mappedMaxY,
                    outerBounds.MaxY +
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

    internal static HashSet<int> BuildTargetSweptTubeMask(
        IReadOnlyList<ElegantArcPoint> points,
        double scaleX,
        double scaleY,
        int targetWidth,
        int targetHeight,
        double additionalTargetPixels)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result =
            new HashSet<int>();

        if (points.Count < 2 ||
            targetWidth <= 0 ||
            targetHeight <= 0)
        {
            return result;
        }

        var mapped =
            new TargetTubePoint[
                points.Count];

        for (var index = 0;
             index < points.Count;
             index++)
        {
            var previous =
                points[Math.Max(
                    0,
                    index - 1)];
            var next =
                points[Math.Min(
                    points.Count - 1,
                    index + 1)];
            var tangentX =
                next.X -
                previous.X;
            var tangentY =
                next.Y -
                previous.Y;
            var tangentLength =
                Math.Sqrt(
                    tangentX *
                        tangentX +
                    tangentY *
                        tangentY);
            var normalScale =
                (scaleX +
                 scaleY) *
                0.5;

            if (tangentLength > 1e-9)
            {
                var normalX =
                    -tangentY /
                    tangentLength;
                var normalY =
                    tangentX /
                    tangentLength;

                normalScale =
                    Math.Sqrt(
                        normalX *
                            normalX *
                            scaleX *
                            scaleX +
                        normalY *
                            normalY *
                            scaleY *
                            scaleY);
            }

            mapped[index] =
                new TargetTubePoint(
                    (points[index].X + 0.5) *
                        scaleX -
                    0.5,
                    (points[index].Y + 0.5) *
                        scaleY -
                    0.5,
                    Math.Max(
                        0.45,
                        points[index].HalfWidth *
                            normalScale +
                        additionalTargetPixels));
        }

        var maximumRadius =
            mapped.Max(point =>
                point.Radius);
        var minX =
            Math.Max(
                0,
                (int)Math.Floor(
                    mapped.Min(point =>
                        point.X) -
                    maximumRadius -
                    1d));
        var maxX =
            Math.Min(
                targetWidth - 1,
                (int)Math.Ceiling(
                    mapped.Max(point =>
                        point.X) +
                    maximumRadius +
                    1d));
        var minY =
            Math.Max(
                0,
                (int)Math.Floor(
                    mapped.Min(point =>
                        point.Y) -
                    maximumRadius -
                    1d));
        var maxY =
            Math.Min(
                targetHeight - 1,
                (int)Math.Ceiling(
                    mapped.Max(point =>
                        point.Y) +
                    maximumRadius +
                    1d));

        for (var y = minY;
             y <= maxY;
             y++)
        {
            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                var inside = false;

                for (var segment = 1;
                     segment < mapped.Length;
                     segment++)
                {
                    var a =
                        mapped[segment - 1];
                    var b =
                        mapped[segment];
                    var dx =
                        b.X -
                        a.X;
                    var dy =
                        b.Y -
                        a.Y;
                    var lengthSquared =
                        dx *
                            dx +
                        dy *
                            dy;
                    var t =
                        lengthSquared <= 1e-12
                            ? 0d
                            : Math.Clamp(
                                ((x -
                                  a.X) *
                                     dx +
                                 (y -
                                  a.Y) *
                                     dy) /
                                lengthSquared,
                                0d,
                                1d);
                    var qx =
                        a.X +
                        dx *
                            t;
                    var qy =
                        a.Y +
                        dy *
                            t;
                    var radius =
                        a.Radius +
                        (b.Radius -
                         a.Radius) *
                        t;
                    var ex =
                        x -
                        qx;
                    var ey =
                        y -
                        qy;

                    if (ex *
                            ex +
                        ey *
                            ey <=
                        radius *
                            radius)
                    {
                        inside = true;
                        break;
                    }
                }

                if (inside)
                {
                    result.Add(
                        y *
                            targetWidth +
                        x);
                }
            }
        }

        return result;
    }

    private static (
        int MinX,
        int MinY,
        int MaxX,
        int MaxY) MaskBounds(
        IReadOnlySet<int> mask,
        int width)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var key in mask)
        {
            var x =
                key %
                width;
            var y =
                key /
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
        }

        return (
            minX,
            minY,
            maxX,
            maxY
        );
    }

    private readonly record struct TargetTubePoint(
        double X,
        double Y,
        double Radius);

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
