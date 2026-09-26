namespace RugScale.Core.Drawing;

/// <summary>
/// Restricts a specialist ribbon redraw to the continuous sweep that was actually fitted.
///
/// When a hooked/branched filled region is reduced to its dominant main arc, Candidate.Region
/// still describes the complete categorical source region. Without an explicit edit scope the
/// rasterizer can shave a decorative hook merely because it lies near the main arc's target
/// bounding box. This helper keeps edits inside a source-space tube around the fitted sweep and
/// preserves a short terminal transition zone so the untouched hook joins the redrawn arc without
/// being eaten by the smoother.
/// </summary>
internal static class CurveFillRibbonRasterScope
{
    private const double TerminalGuardFraction = 0.035;

    public static bool Contains(
        ElegantArcFit fit,
        double sourceX,
        double sourceY,
        double extraMargin)
    {
        ArgumentNullException.ThrowIfNull(fit);

        var points =
            fit.Points;

        if (points.Count < 2)
            return false;

        var bestDistanceSquared =
            double.PositiveInfinity;
        var bestHalfWidth = 0d;
        var bestPosition = 0d;

        for (var index = 1;
             index < points.Count;
             index++)
        {
            var a =
                points[index - 1];
            var b =
                points[index];
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
            double t;

            if (lengthSquared <= 1e-12)
            {
                t = 0d;
            }
            else
            {
                t =
                    Math.Clamp(
                        ((sourceX -
                          a.X) *
                             dx +
                         (sourceY -
                          a.Y) *
                             dy) /
                        lengthSquared,
                        0d,
                        1d);
            }

            var qx =
                a.X +
                dx *
                    t;
            var qy =
                a.Y +
                dy *
                    t;
            var ex =
                sourceX -
                qx;
            var ey =
                sourceY -
                qy;
            var distanceSquared =
                ex *
                    ex +
                ey *
                    ey;

            if (distanceSquared >=
                bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared =
                distanceSquared;
            bestHalfWidth =
                Math.Max(
                    0.5,
                    a.HalfWidth +
                    (b.HalfWidth -
                     a.HalfWidth) *
                    t);
            bestPosition =
                (index -
                 1 +
                 t) /
                Math.Max(
                    1d,
                    points.Count -
                    1d);
        }

        if (bestPosition <=
                TerminalGuardFraction ||
            bestPosition >=
                1d -
                TerminalGuardFraction)
        {
            return false;
        }

        var radius =
            bestHalfWidth +
            Math.Max(
                0d,
                extraMargin);

        return bestDistanceSquared <=
               radius *
               radius;
    }
}
