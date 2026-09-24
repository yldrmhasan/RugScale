using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Rebuilds the outer separator + fill edge of a fully outlined leaf/petal from two learned
/// Curve-tool sides. Only a narrow corridor around the original OUTER path is touched, so inner
/// slits, nested colours and unrelated white line work remain exactly on the Curve & Fill baseline.
/// </summary>
internal static class LeafPetalBoundaryCurveRasterizer
{
    private const double SourceOuterCorridor = 2.35;
    // Curve & Fill's pre-existing outline can land anywhere inside the same outer-path
    // ownership corridor after anisotropic resize. Once the paired replacement is validated,
    // every stale OUTER outline pixel in that corridor must be eligible for cleanup; otherwise
    // detached white fragments can survive beside the rebuilt curve. Internal slits remain safe
    // because this test is measured against SourceOuterPath, not against every outline-colour pixel.
    private const double SourceOutlineEraseCorridor = SourceOuterCorridor;
    private const double MaximumFillShift = 1.75;
    // Source support is measured after integer target rasterization. An anisotropically scaled
    // 1x1 Curve/Pixel-Cord path can shift about two source cells at isolated high-curvature
    // shoulders without changing the intended designer arc. The paired-width and indexed-fill
    // gates below still reject real geometric drift.
    private const double MaximumCurveSourceDeviation = 2.00;
    private const double MinimumCurveSourceSupport = 0.960;

    public static int Apply(
        DesignDocument source,
        DesignDocument destination,
        LeafPetalBoundaryCurveModel model,
        IReadOnlySet<byte> protectedStrokeColors,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity,
        ISet<int> committedOutlinePixels)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);
        ArgumentNullException.ThrowIfNull(committedOutlinePixels);

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

        // The two source sides are judged as one leaf/petal form. Keep their learned
        // source controls fixed, but choose target-size roundness jointly so anisotropic scaling
        // cannot make one side bulge while the other stays flat.
        model =
            LeafPetalBoundaryPairOptimizer.Optimize(
                model,
                scaleX,
                scaleY);

        var left =
            RenderTarget(
                model.LeftFit,
                scaleX,
                scaleY);
        var right =
            RenderTarget(
                model.RightFit,
                scaleX,
                scaleY);

        if (left.Count < 4 ||
            right.Count < 4)
        {
            return 0;
        }

        if (!HasSourcePathSupport(
                left,
                model.LeftSourcePath,
                scaleX,
                scaleY,
                MaximumCurveSourceDeviation,
                MinimumCurveSourceSupport) ||
            !HasSourcePathSupport(
                right,
                model.RightSourcePath,
                scaleX,
                scaleY,
                MaximumCurveSourceDeviation,
                MinimumCurveSourceSupport) ||
            !HasPairedWidthProfileSupport(
                left,
                right,
                model.LeftSourcePath,
                model.RightSourcePath,
                scaleX,
                scaleY))
        {
            // Reject the WHOLE paired fit. Applying only the locally-supported pieces would mix
            // old and new outlines and create the exact small shoulders/kinks the specialist mode
            // is intended to remove.
            return 0;
        }

        var polygon =
            new List<(double X, double Y)>(
                left.Count +
                right.Count);

        polygon.AddRange(
            left.Select(point =>
                ((double)point.X, (double)point.Y)));

        for (var i =
                 right.Count - 1;
             i >= 0;
             i--)
        {
            polygon.Add(
                (
                    right[i].X,
                    right[i].Y));
        }

        var fillMask =
            RasterizePolygon(
                polygon,
                destination.Width,
                destination.Height);
        var newOutline =
            new HashSet<int>();

        AddDilatedPath(
            newOutline,
            left,
            penSizeX,
            penSizeY,
            destination.Width,
            destination.Height);
        AddDilatedPath(
            newOutline,
            right,
            penSizeX,
            penSizeY,
            destination.Width,
            destination.Height);
        if (model.DrawBaseCap)
        {
            AddDilatedLine(
                newOutline,
                left[0],
                right[0],
                penSizeX,
                penSizeY,
                destination.Width,
                destination.Height);
        }

        var sourceApexDistance =
            Distance(
                model.LeftSourcePath[^1],
                model.RightSourcePath[^1]);
        var closeSourceApex =
            sourceApexDistance <= 4.0;

        if (model.DrawApexCap ||
            closeSourceApex)
        {
            // If the recovered source sides already converge to the same 1x1/Pixel-Cord tip,
            // always reconnect their target raster endpoints. Independent curve rasterization can
            // otherwise leave a one-cell phase gap even though the source designer drew a single
            // shared apex.
            AddDilatedLine(
                newOutline,
                left[^1],
                right[^1],
                penSizeX,
                penSizeY,
                destination.Width,
                destination.Height);
        }

        var minX =
            Math.Max(
                0,
                Math.Min(
                    left.Min(point =>
                        point.X),
                    right.Min(point =>
                        point.X)) -
                5);
        var maxX =
            Math.Min(
                destination.Width - 1,
                Math.Max(
                    left.Max(point =>
                        point.X),
                    right.Max(point =>
                        point.X)) +
                5);
        var minY =
            Math.Max(
                0,
                Math.Min(
                    left.Min(point =>
                        point.Y),
                    right.Min(point =>
                        point.Y)) -
                5);
        var maxY =
            Math.Min(
                destination.Height - 1,
                Math.Max(
                    left.Max(point =>
                        point.Y),
                    right.Max(point =>
                        point.Y)) +
                5);

        var region =
            model.ArcModel.Candidate.Region;
        var regionSet =
            region.Pixels.ToHashSet();
        var changed = 0;

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
                var targetKey =
                    y *
                    destination.Width +
                    x;
                var current =
                    destination.GetPixel(
                        x,
                        y);

                if (newOutline.Contains(
                        targetKey))
                {
                    // The paired left/right model already passed whole-curve source support AND
                    // width-profile safety above. Do not clip individual validated target pixels
                    // against the source corridor a second time: integer target raster phase can
                    // move isolated bridge cells slightly outside that corridor and would split an
                    // otherwise correct Pixel-Cord curve into visible fragments.
                    if (current !=
                            model.OutlineColor &&
                        protectedStrokeColors.Contains(
                            current))
                    {
                        // A different proven separator/tool colour still owns this pixel.
                        continue;
                    }

                    if (current !=
                        model.OutlineColor)
                    {
                        destination.SetPixel(
                            x,
                            y,
                            model.OutlineColor);
                        changed++;
                    }

                    // Once an accepted paired fit owns an outline pixel, a later overlapping
                    // leaf/petal candidate may not erase it while cleaning its own old outline.
                    committedOutlinePixels.Add(
                        targetKey);
                    continue;
                }

                if (!IsNearSourceOuterPath(
                        model.SourceOuterPath,
                        source.Width,
                        source.Height,
                        sourceX,
                        sourceY,
                        SourceOuterCorridor))
                {
                    continue;
                }

                var inside =
                    fillMask.Contains(
                        targetKey);
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

                if (current ==
                        model.OutlineColor &&
                    committedOutlinePixels.Contains(
                        targetKey))
                {
                    continue;
                }

                if (inside)
                {
                    if (current ==
                        region.Color)
                    {
                        continue;
                    }

                    if (current ==
                        model.OutlineColor)
                    {
                        // Same palette index can also be an INTERNAL slit/decorative line. Erase
                        // white only when this pixel maps tightly to the recovered OUTER designer
                        // path; the wider outer corridor is intentionally insufficient here.
                        if (IsNearSourceOuterPath(
                                model.SourceOuterPath,
                                source.Width,
                                source.Height,
                                sourceX,
                                sourceY,
                                SourceOutlineEraseCorridor))
                        {
                            destination.SetPixel(
                                x,
                                y,
                                region.Color);
                            changed++;
                        }

                        continue;
                    }

                    if (protectedStrokeColors.Contains(
                            current))
                    {
                        continue;
                    }

                    if (sourceOwner ==
                            region.Color ||
                        sourceOwner ==
                            model.OutlineColor ||
                        CanExpandFillLocally(
                            source,
                            regionSet,
                            sourceX,
                            sourceY,
                            region.Color,
                            model.OutlineColor,
                            protectedStrokeColors))
                    {
                        destination.SetPixel(
                            x,
                            y,
                            region.Color);
                        changed++;
                    }

                    continue;
                }

                if (current !=
                        region.Color &&
                    current !=
                        model.OutlineColor)
                {
                    continue;
                }

                if (current ==
                        model.OutlineColor &&
                    !IsNearSourceOuterPath(
                        model.SourceOuterPath,
                        source.Width,
                        source.Height,
                        sourceX,
                        sourceY,
                        SourceOutlineEraseCorridor))
                {
                    continue;
                }

                if (!TryFindOutsideColor(
                        source,
                        sourceX,
                        sourceY,
                        region.Color,
                        model.OutlineColor,
                        protectedStrokeColors,
                        out var replacement))
                {
                    continue;
                }

                if (replacement ==
                        current ||
                    protectedStrokeColors.Contains(
                        replacement))
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

        return changed;
    }

    private static bool HasPairedWidthProfileSupport(
        IReadOnlyList<(int X, int Y)> targetLeft,
        IReadOnlyList<(int X, int Y)> targetRight,
        IReadOnlyList<(int X, int Y)> sourceLeft,
        IReadOnlyList<(int X, int Y)> sourceRight,
        double scaleX,
        double scaleY)
    {
        const int SampleCount = 32;
        var supported = 0;
        var previousWidth =
            double.NaN;
        var abruptJumps = 0;

        for (var sample = 0;
             sample < SampleCount;
             sample++)
        {
            var t =
                sample /
                (double)(SampleCount - 1);
            var targetL =
                SamplePath(
                    targetLeft,
                    t);
            var targetR =
                SamplePath(
                    targetRight,
                    t);
            var sourceL =
                SamplePath(
                    sourceLeft,
                    t);
            var sourceR =
                SamplePath(
                    sourceRight,
                    t);
            var expectedL =
                MapSourcePoint(
                    sourceL,
                    scaleX,
                    scaleY);
            var expectedR =
                MapSourcePoint(
                    sourceR,
                    scaleX,
                    scaleY);
            var actualWidth =
                Distance(
                    targetL,
                    targetR);
            var expectedWidth =
                Distance(
                    expectedL,
                    expectedR);
            var tolerance =
                Math.Max(
                    2.75,
                    expectedWidth *
                    0.28);

            if (Math.Abs(
                    actualWidth -
                    expectedWidth) <=
                tolerance)
            {
                supported++;
            }

            if (!double.IsNaN(
                    previousWidth))
            {
                var allowedJump =
                    Math.Max(
                        3.0,
                        Math.Max(
                            previousWidth,
                            actualWidth) *
                        0.32);

                if (Math.Abs(
                        actualWidth -
                        previousWidth) >
                    allowedJump)
                {
                    abruptJumps++;
                }
            }

            previousWidth =
                actualWidth;
        }

        // Raster phase and a real sharp apex can consume a few samples even when the paired
        // designer curves are visually faithful. Require 87.5% width support and allow two local
        // jumps; repeated shocks still reject mismatched/bulged sides.
        return supported >=
                   SampleCount -
                   4 &&
               abruptJumps <= 2;
    }

    private static (double X, double Y) SamplePath(
        IReadOnlyList<(int X, int Y)> path,
        double t)
    {
        if (path.Count == 0)
            return (0d, 0d);

        if (path.Count == 1)
            return path[0];

        var position =
            Math.Clamp(
                t,
                0d,
                1d) *
            (path.Count - 1);
        var lower =
            (int)Math.Floor(
                position);
        var upper =
            Math.Min(
                path.Count - 1,
                lower + 1);
        var fraction =
            position -
            lower;

        return (
            path[lower].X +
            (path[upper].X -
             path[lower].X) *
            fraction,
            path[lower].Y +
            (path[upper].Y -
             path[lower].Y) *
            fraction);
    }

    private static (double X, double Y) MapSourcePoint(
        (double X, double Y) point,
        double scaleX,
        double scaleY) =>
        (
            (point.X + 0.5) *
            scaleX -
            0.5,
            (point.Y + 0.5) *
            scaleY -
            0.5);

    private static double Distance(
        (double X, double Y) left,
        (double X, double Y) right)
    {
        var dx =
            left.X -
            right.X;
        var dy =
            left.Y -
            right.Y;

        return Math.Sqrt(
            dx *
            dx +
            dy *
            dy);
    }

    private static bool HasSourcePathSupport(
        IReadOnlyList<(int X, int Y)> targetPath,
        IReadOnlyList<(int X, int Y)> sourcePath,
        double scaleX,
        double scaleY,
        double maximumSourceDistance,
        double minimumRatio)
    {
        if (targetPath.Count == 0 ||
            sourcePath.Count == 0)
        {
            return false;
        }

        var sourceSet =
            sourcePath
                .ToHashSet();
        var radius =
            (int)Math.Ceiling(
                maximumSourceDistance) +
            1;
        var maximumSquared =
            maximumSourceDistance *
            maximumSourceDistance;
        var supported = 0;

        foreach (var point in targetPath)
        {
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
            var found =
                false;

            for (var y =
                     centerY - radius;
                 y <=
                 centerY + radius &&
                 !found;
                 y++)
            {
                for (var x =
                         centerX - radius;
                     x <=
                     centerX + radius;
                     x++)
                {
                    if (!sourceSet.Contains(
                            (x, y)))
                    {
                        continue;
                    }

                    var dx =
                        x -
                        sourceX;
                    var dy =
                        y -
                        sourceY;

                    if (dx * dx +
                        dy * dy <=
                        maximumSquared)
                    {
                        found =
                            true;
                        break;
                    }
                }
            }

            if (found)
                supported++;
        }

        return supported /
               (double)targetPath.Count >=
               minimumRatio;
    }

    private static List<(int X, int Y)> RenderTarget(
        ToolFaithfulCurveStyleFit fit,
        double scaleX,
        double scaleY)
    {
        var controls =
            fit.Controls
                .Select(point =>
                    (
                        X: (int)Math.Round(
                            (point.X + 0.5) *
                            scaleX -
                            0.5),
                        Y: (int)Math.Round(
                            (point.Y + 0.5) *
                            scaleY -
                            0.5)))
                .ToArray();
        var result =
            new List<(int X, int Y)>();

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         fit.Type,
                         fit.Roundness)))
        {
            if (result.Count == 0 ||
                result[^1] !=
                point)
            {
                result.Add(
                    point);
            }
        }

        return result;
    }

    private static bool CanExpandFillLocally(
        DesignDocument source,
        IReadOnlySet<int> region,
        double sourceX,
        double sourceY,
        byte regionColor,
        byte outlineColor,
        IReadOnlySet<byte> protectedStrokeColors)
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
        var search =
            (int)Math.Ceiling(
                MaximumFillShift) +
            1;
        var nearestDistance =
            double.PositiveInfinity;
        var nearestX = -1;
        var nearestY = -1;

        for (var y =
                 Math.Max(
                     0,
                     centerY - search);
             y <=
             Math.Min(
                 source.Height - 1,
                 centerY + search);
             y++)
        {
            for (var x =
                     Math.Max(
                         0,
                         centerX - search);
                 x <=
                 Math.Min(
                     source.Width - 1,
                     centerX + search);
                 x++)
            {
                if (!region.Contains(
                        y *
                        source.Width +
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
                var distance =
                    Math.Sqrt(
                        dx *
                        dx +
                        dy *
                        dy);

                if (distance >=
                    nearestDistance)
                {
                    continue;
                }

                nearestDistance =
                    distance;
                nearestX = x;
                nearestY = y;
            }
        }

        if (nearestX < 0 ||
            nearestDistance >
            MaximumFillShift)
        {
            return false;
        }

        return PathContainsOnly(
            source,
            centerX,
            centerY,
            nearestX,
            nearestY,
            regionColor,
            outlineColor,
            protectedStrokeColors);
    }

    private static bool PathContainsOnly(
        DesignDocument source,
        int startX,
        int startY,
        int endX,
        int endY,
        byte regionColor,
        byte outlineColor,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        var x = startX;
        var y = startY;
        var dx =
            Math.Abs(
                endX -
                startX);
        var dy =
            Math.Abs(
                endY -
                startY);
        var stepX =
            startX < endX
                ? 1
                : -1;
        var stepY =
            startY < endY
                ? 1
                : -1;
        var error =
            dx -
            dy;

        while (true)
        {
            var color =
                source.GetPixel(
                    x,
                    y);

            if (color != regionColor &&
                color != outlineColor)
            {
                if (protectedStrokeColors.Contains(
                        color))
                {
                    return false;
                }

                // A third fill/background colour may occur on the OUTSIDE of the 1px separator,
                // but it may not be crossed en route to the original region.
                if (x != startX ||
                    y != startY)
                {
                    return false;
                }
            }

            if (x == endX &&
                y == endY)
            {
                return true;
            }

            var doubled =
                2 *
                error;

            if (doubled >
                -dy)
            {
                error -=
                    dy;
                x +=
                    stepX;
            }

            if (doubled <
                dx)
            {
                error +=
                    dx;
                y +=
                    stepY;
            }
        }
    }

    private static bool TryFindOutsideColor(
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
        color =
            regionColor;

        for (var radius = 1;
             radius <= 6;
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
                }
            }

            if (color !=
                regionColor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNearSourceOuterPath(
        IReadOnlySet<(int X, int Y)> path,
        int width,
        int height,
        double sourceX,
        double sourceY,
        double radius)
    {
        var centerX =
            (int)Math.Round(
                sourceX);
        var centerY =
            (int)Math.Round(
                sourceY);
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
                if (!path.Contains(
                        (x, y)))
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

    private static HashSet<int> RasterizePolygon(
        IReadOnlyList<(double X, double Y)> polygon,
        int width,
        int height)
    {
        var result =
            new HashSet<int>();
        var intersections =
            new List<double>(
                polygon.Count);
        var minY =
            Math.Max(
                0,
                (int)Math.Floor(
                    polygon.Min(point =>
                        point.Y)) -
                1);
        var maxY =
            Math.Min(
                height - 1,
                (int)Math.Ceiling(
                    polygon.Max(point =>
                        point.Y)) +
                1);

        for (var y = minY;
             y <= maxY;
             y++)
        {
            intersections.Clear();
            var scanY =
                y +
                0.5;

            for (var i = 0;
                 i < polygon.Count;
                 i++)
            {
                var a =
                    polygon[i];
                var b =
                    polygon[
                        (i + 1) %
                        polygon.Count];

                if ((a.Y <= scanY &&
                     b.Y > scanY) ||
                    (b.Y <= scanY &&
                     a.Y > scanY))
                {
                    var t =
                        (scanY -
                         a.Y) /
                        (b.Y -
                         a.Y);

                    intersections.Add(
                        a.X +
                        (b.X -
                         a.X) *
                        t);
                }
            }

            intersections.Sort();

            for (var i = 0;
                 i + 1 <
                 intersections.Count;
                 i += 2)
            {
                var start =
                    Math.Max(
                        0,
                        (int)Math.Ceiling(
                            intersections[i] -
                            0.5));
                var end =
                    Math.Min(
                        width - 1,
                        (int)Math.Floor(
                            intersections[i + 1] -
                            0.5));

                for (var x = start;
                     x <= end;
                     x++)
                {
                    result.Add(
                        y *
                        width +
                        x);
                }
            }
        }

        return result;
    }

    private static void AddDilatedPath(
        ISet<int> target,
        IReadOnlyList<(int X, int Y)> path,
        int penSizeX,
        int penSizeY,
        int width,
        int height)
    {
        foreach (var point in Rasterizer.Dilate(
                     path,
                     penSizeX,
                     penSizeY))
        {
            if (point.X < 0 ||
                point.X >= width ||
                point.Y < 0 ||
                point.Y >= height)
            {
                continue;
            }

            target.Add(
                point.Y *
                width +
                point.X);
        }
    }

    private static void AddDilatedLine(
        ISet<int> target,
        (int X, int Y) start,
        (int X, int Y) end,
        int penSizeX,
        int penSizeY,
        int width,
        int height)
    {
        // Caps are part of the same 1x1 / Pixel-Cord drawing language as the side
        // curves. A plain Bresenham diagonal can be only 8-connected and split the final outline
        // under RugCAD's 4-connected pixel semantics; bridge its diagonal steps before applying
        // the quality-owned pen size.
        foreach (var point in Rasterizer.Dilate(
                     Rasterizer.ConnectDiagonalSteps(
                         Rasterizer.Line(
                             start.X,
                             start.Y,
                             end.X,
                             end.Y)),
                     penSizeX,
                     penSizeY))
        {
            if (point.X < 0 ||
                point.X >= width ||
                point.Y < 0 ||
                point.Y >= height)
            {
                continue;
            }

            target.Add(
                point.Y *
                width +
                point.X);
        }
    }

    private static void AddPath(
        ISet<int> target,
        IReadOnlyList<(int X, int Y)> path,
        int width,
        int height)
    {
        foreach (var point in path)
        {
            if (point.X < 0 ||
                point.X >= width ||
                point.Y < 0 ||
                point.Y >= height)
            {
                continue;
            }

            target.Add(
                point.Y *
                width +
                point.X);
        }
    }

    private static void AddLine(
        ISet<int> target,
        (int X, int Y) a,
        (int X, int Y) b,
        int width,
        int height)
    {
        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     Rasterizer.Line(
                         a.X,
                         a.Y,
                         b.X,
                         b.Y)))
        {
            if (point.X < 0 ||
                point.X >= width ||
                point.Y < 0 ||
                point.Y >= height)
            {
                continue;
            }

            target.Add(
                point.Y *
                width +
                point.X);
        }
    }
}
