using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

internal static class LeafPetalArcRasterizer
{
    private const double BoundaryBandRadius = 2.0;
    private const double MaximumBoundaryShift = 1.65;

    public static int Apply(
        DesignDocument source,
        DesignDocument destination,
        LeafPetalArcModel model,
        ElegantArcFit fit,
        IReadOnlySet<byte> protectedStrokeColors,
        bool restrictToFittedSweep = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);

        if (!fit.IsSafe ||
            fit.Points.Count < 4)
        {
            return 0;
        }

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var polygon =
            BuildTargetPolygon(
                fit.Points,
                scaleX,
                scaleY);

        if (polygon.Count < 6)
            return 0;

        var mask =
            RasterizePolygon(
                polygon,
                destination.Width,
                destination.Height);

        if (mask.Count == 0)
            return 0;

        var region =
            model.Candidate.Region;
        var regionSet =
            region.Pixels.ToHashSet();
        var boundarySet =
            region.BoundaryPixels.ToHashSet();

        var minX =
            Math.Max(
                0,
                (int)Math.Floor(
                    polygon.Min(point => point.X)) -
                2);
        var maxX =
            Math.Min(
                destination.Width - 1,
                (int)Math.Ceiling(
                    polygon.Max(point => point.X)) +
                2);
        var minY =
            Math.Max(
                0,
                (int)Math.Floor(
                    polygon.Min(point => point.Y)) -
                2);
        var maxY =
            Math.Min(
                destination.Height - 1,
                (int)Math.Ceiling(
                    polygon.Max(point => point.Y)) +
                2);

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

                if (!IsNearBoundary(
                        boundarySet,
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

                var targetKey =
                    y *
                    destination.Width +
                    x;
                var inside =
                    mask.Contains(
                        targetKey);
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
                var sourceKey =
                    sy *
                    source.Width +
                    sx;
                var sourceOwner =
                    source.GetPixel(
                        sx,
                        sy);

                if (inside)
                {
                    if (current == region.Color)
                        continue;

                    // White / Pixel-Cord outline roles are hard barriers. The specialist arc may
                    // move a fill boundary against a neighbouring fill/background, but it never
                    // paints over a source or already-rasterized separator.
                    if (protectedStrokeColors.Contains(current) ||
                        protectedStrokeColors.Contains(sourceOwner))
                    {
                        continue;
                    }

                    var directlyOwned =
                        regionSet.Contains(
                            sourceKey);

                    if (!directlyOwned &&
                        !CanShiftIntoRegion(
                            source,
                            regionSet,
                            source.Width,
                            source.Height,
                            sourceX,
                            sourceY,
                            sourceOwner,
                            region.Color,
                            protectedStrokeColors))
                    {
                        continue;
                    }

                    destination.SetPixel(
                        x,
                        y,
                        region.Color);
                    changed++;
                    continue;
                }

                if (current != region.Color)
                    continue;

                // Geometric smoothing may shave a staircase protrusion, but it must never turn
                // one continuous designer ribbon/leaf into disconnected islands. Treat pixels
                // whose removal would split the local 8-neighbour ring as topology anchors.
                if (WouldDisconnectRegion(
                        destination,
                        x,
                        y,
                        region.Color))
                {
                    continue;
                }

                // Never shave a specialist region into a protected separator role merely to make
                // the outline look smoother; RugScale's tool replay owns those pixels.
                if (protectedStrokeColors.Contains(sourceOwner))
                    continue;

                byte replacement;

                if (sourceOwner != region.Color)
                {
                    replacement =
                        sourceOwner;
                }
                else if (!TryFindNearestOtherColor(
                             source,
                             sourceX,
                             sourceY,
                             region.Color,
                             protectedStrokeColors,
                             out replacement,
                             out var replacementX,
                             out var replacementY) ||
                         !IsTwoColourTransition(
                             source,
                             sx,
                             sy,
                             replacementX,
                             replacementY,
                             region.Color,
                             replacement,
                             protectedStrokeColors))
                {
                    continue;
                }

                if (replacement == region.Color ||
                    protectedStrokeColors.Contains(replacement))
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

    internal static List<(double X, double Y)> BuildTargetPolygon(
        IReadOnlyList<ElegantArcPoint> points,
        double scaleX,
        double scaleY)
    {
        var left =
            new List<(double X, double Y)>(
                points.Count);
        var right =
            new List<(double X, double Y)>(
                points.Count);

        for (var i = 0;
             i < points.Count;
             i++)
        {
            var previous =
                points[Math.Max(
                    0,
                    i - 1)];
            var next =
                points[Math.Min(
                    points.Count - 1,
                    i + 1)];
            var tangentX =
                next.X -
                previous.X;
            var tangentY =
                next.Y -
                previous.Y;
            var length =
                Math.Sqrt(
                    tangentX *
                    tangentX +
                    tangentY *
                    tangentY);

            if (length <= 1e-9)
                continue;

            var normalX =
                -tangentY /
                length;
            var normalY =
                tangentX /
                length;
            var point =
                points[i];

            left.Add(
                Map(
                    point.X +
                    normalX *
                    point.HalfWidth,
                    point.Y +
                    normalY *
                    point.HalfWidth,
                    scaleX,
                    scaleY));
            right.Add(
                Map(
                    point.X -
                    normalX *
                    point.HalfWidth,
                    point.Y -
                    normalY *
                    point.HalfWidth,
                    scaleX,
                    scaleY));
        }

        right.Reverse();
        left.AddRange(right);
        return left;
    }

    /// <summary>
    /// Builds the scaled ribbon polygon and then expands each side by a fixed number of TARGET
    /// pixels. This is different from increasing source HalfWidth before scaling: a dedicated 1x1
    /// Pixel-Cord outline must stay one target-grid pixel thick when physical design size changes
    /// at the same warp/weft quality.
    /// </summary>
    internal static List<(double X, double Y)> BuildTargetExpandedPolygon(
        IReadOnlyList<ElegantArcPoint> points,
        double scaleX,
        double scaleY,
        double additionalTargetPixels)
    {
        var left =
            new List<(double X, double Y)>(
                points.Count);
        var right =
            new List<(double X, double Y)>(
                points.Count);

        additionalTargetPixels =
            Math.Max(
                0d,
                additionalTargetPixels);

        for (var i = 0;
             i < points.Count;
             i++)
        {
            var previous =
                points[Math.Max(
                    0,
                    i - 1)];
            var next =
                points[Math.Min(
                    points.Count - 1,
                    i + 1)];
            var sourceTangentX =
                next.X -
                previous.X;
            var sourceTangentY =
                next.Y -
                previous.Y;
            var sourceLength =
                Math.Sqrt(
                    sourceTangentX *
                        sourceTangentX +
                    sourceTangentY *
                        sourceTangentY);

            if (sourceLength <=
                1e-9)
            {
                continue;
            }

            var sourceNormalX =
                -sourceTangentY /
                sourceLength;
            var sourceNormalY =
                sourceTangentX /
                sourceLength;
            var point =
                points[i];

            var leftBase =
                Map(
                    point.X +
                    sourceNormalX *
                        point.HalfWidth,
                    point.Y +
                    sourceNormalY *
                        point.HalfWidth,
                    scaleX,
                    scaleY);
            var rightBase =
                Map(
                    point.X -
                    sourceNormalX *
                        point.HalfWidth,
                    point.Y -
                    sourceNormalY *
                        point.HalfWidth,
                    scaleX,
                    scaleY);

            var previousTarget =
                Map(
                    previous.X,
                    previous.Y,
                    scaleX,
                    scaleY);
            var nextTarget =
                Map(
                    next.X,
                    next.Y,
                    scaleX,
                    scaleY);
            var targetTangentX =
                nextTarget.X -
                previousTarget.X;
            var targetTangentY =
                nextTarget.Y -
                previousTarget.Y;
            var targetLength =
                Math.Sqrt(
                    targetTangentX *
                        targetTangentX +
                    targetTangentY *
                        targetTangentY);

            if (targetLength <=
                1e-9)
            {
                continue;
            }

            var targetNormalX =
                -targetTangentY /
                targetLength;
            var targetNormalY =
                targetTangentX /
                targetLength;

            left.Add(
                (
                    leftBase.X +
                    targetNormalX *
                        additionalTargetPixels,
                    leftBase.Y +
                    targetNormalY *
                        additionalTargetPixels
                ));
            right.Add(
                (
                    rightBase.X -
                    targetNormalX *
                        additionalTargetPixels,
                    rightBase.Y -
                    targetNormalY *
                        additionalTargetPixels
                ));
        }

        right.Reverse();
        left.AddRange(
            right);
        return left;
    }

    private static (double X, double Y) Map(
        double x,
        double y,
        double scaleX,
        double scaleY) =>
        (
            (x + 0.5) *
            scaleX -
            0.5,
            (y + 0.5) *
            scaleY -
            0.5);

    internal static HashSet<int> RasterizePolygon(
        IReadOnlyList<(double X, double Y)> polygon,
        int width,
        int height)
    {
        var result =
            new HashSet<int>();

        if (polygon.Count < 3)
            return result;

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

        var intersections =
            new List<double>(
                polygon.Count);

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
                 i + 1 < intersections.Count;
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

    private static bool IsNearBoundary(
        IReadOnlySet<int> boundary,
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

                if (dx * dx +
                    dy * dy <=
                    radiusSquared)
                {
                    return true;
                }
            }
        }

        return false;
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

    private static bool CanShiftIntoRegion(
        DesignDocument source,
        IReadOnlySet<int> region,
        int width,
        int height,
        double sourceX,
        double sourceY,
        byte sourceOwner,
        byte regionColor,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        if (protectedStrokeColors.Contains(sourceOwner))
            return false;

        if (!TryFindNearestRegionPixel(
                region,
                width,
                height,
                sourceX,
                sourceY,
                out var regionX,
                out var regionY,
                out var distance) ||
            distance > MaximumBoundaryShift)
        {
            return false;
        }

        return IsTwoColourTransition(
            source,
            Math.Clamp((int)Math.Round(sourceX), 0, width - 1),
            Math.Clamp((int)Math.Round(sourceY), 0, height - 1),
            regionX,
            regionY,
            sourceOwner,
            regionColor,
            protectedStrokeColors);
    }

    private static bool TryFindNearestRegionPixel(
        IReadOnlySet<int> region,
        int width,
        int height,
        double sourceX,
        double sourceY,
        out int bestX,
        out int bestY,
        out double bestDistance)
    {
        var centerX =
            Math.Clamp(
                (int)Math.Round(sourceX),
                0,
                width - 1);
        var centerY =
            Math.Clamp(
                (int)Math.Round(sourceY),
                0,
                height - 1);
        var search =
            (int)Math.Ceiling(MaximumBoundaryShift) +
            1;

        bestX = -1;
        bestY = -1;
        bestDistance =
            double.PositiveInfinity;

        for (var y =
                 Math.Max(0, centerY - search);
             y <=
             Math.Min(height - 1, centerY + search);
             y++)
        {
            for (var x =
                     Math.Max(0, centerX - search);
                 x <=
                 Math.Min(width - 1, centerX + search);
                 x++)
            {
                if (!region.Contains(
                        y * width + x))
                {
                    continue;
                }

                var dx =
                    x - sourceX;
                var dy =
                    y - sourceY;
                var distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestX = x;
                bestY = y;
            }
        }

        return bestX >= 0;
    }

    private static bool IsTwoColourTransition(
        DesignDocument source,
        int startX,
        int startY,
        int endX,
        int endY,
        byte firstColor,
        byte secondColor,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        var x = startX;
        var y = startY;
        var dx =
            Math.Abs(endX - startX);
        var dy =
            Math.Abs(endY - startY);
        var stepX =
            startX < endX
                ? 1
                : -1;
        var stepY =
            startY < endY
                ? 1
                : -1;
        var error =
            dx - dy;

        while (true)
        {
            var color =
                source.GetPixel(
                    x,
                    y);

            if (protectedStrokeColors.Contains(color) ||
                (color != firstColor &&
                 color != secondColor))
            {
                return false;
            }

            if (x == endX &&
                y == endY)
            {
                return true;
            }

            var doubled =
                2 * error;

            if (doubled > -dy)
            {
                error -= dy;
                x += stepX;
            }

            if (doubled < dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    private static bool TryFindNearestOtherColor(
        DesignDocument source,
        double sourceX,
        double sourceY,
        byte excluded,
        IReadOnlySet<byte> protectedStrokeColors,
        out byte color,
        out int bestX,
        out int bestY)
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
        color = excluded;
        bestX = -1;
        bestY = -1;

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
                    var candidate =
                        source.GetPixel(
                            x,
                            y);

                    if (candidate == excluded ||
                        protectedStrokeColors.Contains(candidate))
                    {
                        continue;
                    }

                    var dx =
                        x - sourceX;
                    var dy =
                        y - sourceY;
                    var distance =
                        dx * dx +
                        dy * dy;

                    if (distance >=
                        bestDistance)
                    {
                        continue;
                    }

                    bestDistance =
                        distance;
                    color =
                        candidate;
                    bestX = x;
                    bestY = y;
                }
            }

            if (color != excluded)
                return true;
        }

        return false;
    }

}
