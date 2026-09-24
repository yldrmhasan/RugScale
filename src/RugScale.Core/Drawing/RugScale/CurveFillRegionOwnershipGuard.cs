using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Final categorical ownership guard for Curve & Fill before tool-faithful outlines are replayed.
///
/// Signed-distance interpolation is excellent at producing smooth curved boundaries, but at a
/// narrow separator it can occasionally let a fill colour "jump" across a one-cell outline. That
/// is visually unacceptable in indexed carpet artwork: a colour belongs to a source region and
/// may move its boundary locally, but it may not cross an intervening third-colour barrier.
///
/// This guard works entirely in SOURCE coordinates. A target colour change is accepted only when
/// that colour has local source support and the path from the inverse-mapped source cell to that
/// support does not cross a third palette region.
/// </summary>
internal static class CurveFillRegionOwnershipGuard
{
    private const int SearchRadius = 4;
    private const double MaximumLocalSupportDistance = 1.60;

    public static CurveFillOwnershipReport Apply(
        DesignDocument source,
        DesignDocument destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;

        var unsupported = 0;
        var barrierCrossings = 0;

        for (var ty = 0;
             ty < destination.Height;
             ty++)
        {
            var sourceY =
                (ty + 0.5) /
                scaleY -
                0.5;
            var sy =
                Math.Clamp(
                    (int)Math.Round(
                        sourceY),
                    0,
                    source.Height - 1);

            for (var tx = 0;
                 tx < destination.Width;
                 tx++)
            {
                var sourceX =
                    (tx + 0.5) /
                    scaleX -
                    0.5;
                var sx =
                    Math.Clamp(
                        (int)Math.Round(
                            sourceX),
                    0,
                    source.Width - 1);

                var owner =
                    source.GetPixel(
                        sx,
                        sy);
                var current =
                    destination.GetPixel(
                        tx,
                        ty);

                if (current == owner)
                    continue;

                if (!TryFindNearestSourcePixel(
                        source,
                        sourceX,
                        sourceY,
                        current,
                        SearchRadius,
                        out var supportX,
                        out var supportY,
                        out var supportDistance) ||
                    supportDistance >
                    MaximumLocalSupportDistance)
                {
                    destination.SetPixel(
                        tx,
                        ty,
                        owner);
                    unsupported++;
                    continue;
                }

                if (!CrossesThirdColourBarrier(
                        source,
                        sx,
                        sy,
                        supportX,
                        supportY,
                        owner,
                        current))
                {
                    continue;
                }

                destination.SetPixel(
                    tx,
                    ty,
                    owner);
                barrierCrossings++;
            }
        }

        return new CurveFillOwnershipReport(
            unsupported,
            barrierCrossings);
    }

    private static bool TryFindNearestSourcePixel(
        DesignDocument source,
        double sourceX,
        double sourceY,
        byte colour,
        int radius,
        out int bestX,
        out int bestY,
        out double bestDistance)
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

        bestX = -1;
        bestY = -1;
        bestDistance =
            double.PositiveInfinity;

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
                if (source.GetPixel(
                        x,
                        y) != colour)
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

                if (distance >=
                    bestDistance)
                {
                    continue;
                }

                bestDistance =
                    distance;
                bestX = x;
                bestY = y;
            }
        }

        return bestX >= 0;
    }

    private static bool CrossesThirdColourBarrier(
        DesignDocument source,
        int startX,
        int startY,
        int endX,
        int endY,
        byte owner,
        byte candidate)
    {
        var x = startX;
        var y = startY;
        var dx =
            Math.Abs(
                endX - startX);
        var dy =
            Math.Abs(
                endY - startY);
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
            var colour =
                source.GetPixel(
                    x,
                    y);

            if (colour != owner &&
                colour != candidate)
            {
                return true;
            }

            if (x == endX &&
                y == endY)
            {
                return false;
            }

            var doubled =
                2 *
                error;

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
}

internal readonly record struct CurveFillOwnershipReport(
    int UnsupportedColourCorrections,
    int BarrierCrossingCorrections)
{
    public int TotalCorrections =>
        UnsupportedColourCorrections +
        BarrierCrossingCorrections;
}
