namespace RugScale.Core.Drawing;

/// <summary>
/// Geometry primitives for jointly redrawing layered ribbon bands around one accepted centreline.
///
/// This class intentionally starts as mask-only infrastructure. Production ownership changes are
/// not allowed until real-raster audits prove that the same source cross-section sequence is stable
/// enough to move the neighbouring bands together.
/// </summary>
internal static class CurveFillLayeredRibbonRasterizer
{
    internal static HashSet<int> BuildOneSidedBandMask(
        IReadOnlyList<ElegantArcPoint> points,
        double side,
        double scaleX,
        double scaleY,
        int targetWidth,
        int targetHeight,
        double innerAdditionalTargetPixels,
        double sourceBandWidth,
        double fixedBandWidthTargetPixels = 0d)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result =
            new HashSet<int>();

        if (points.Count < 3 ||
            targetWidth <= 0 ||
            targetHeight <= 0 ||
            Math.Abs(
                side) <
            0.5)
        {
            return result;
        }

        side =
            Math.Sign(
                side);
        var inner =
            new List<(double X, double Y)>(
                points.Count);
        var outer =
            new List<(double X, double Y)>(
                points.Count);

        for (var index = 0;
             index < points.Count;
             index++)
        {
            var previous =
                points[
                    Math.Max(
                        0,
                        index - 1)];
            var next =
                points[
                    Math.Min(
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

            if (tangentLength <= 1e-9)
            {
                if (index > 0)
                {
                    inner.Add(
                        inner[^1]);
                    outer.Add(
                        outer[^1]);
                }

                continue;
            }

            var sourceNormalX =
                -tangentY /
                tangentLength *
                side;
            var sourceNormalY =
                tangentX /
                tangentLength *
                side;
            var targetNormalX =
                sourceNormalX *
                scaleX;
            var targetNormalY =
                sourceNormalY *
                scaleY;
            var normalScale =
                Math.Sqrt(
                    targetNormalX *
                        targetNormalX +
                    targetNormalY *
                        targetNormalY);

            if (normalScale <= 1e-9)
                continue;

            var unitTargetNormalX =
                targetNormalX /
                normalScale;
            var unitTargetNormalY =
                targetNormalY /
                normalScale;
            var centerX =
                (points[index].X +
                 0.5) *
                    scaleX -
                0.5;
            var centerY =
                (points[index].Y +
                 0.5) *
                    scaleY -
                0.5;

            // The fitted half-width is a source-space motif dimension and therefore scales with
            // the local mapped normal. Additional target-pixel offsets (such as a 1x1 Pixel-Cord
            // separator) are deliberately applied AFTER scaling.
            var fillBoundaryX =
                centerX +
                sourceNormalX *
                    points[index].HalfWidth *
                    scaleX;
            var fillBoundaryY =
                centerY +
                sourceNormalY *
                    points[index].HalfWidth *
                    scaleY;
            var innerX =
                fillBoundaryX +
                unitTargetNormalX *
                    innerAdditionalTargetPixels;
            var innerY =
                fillBoundaryY +
                unitTargetNormalY *
                    innerAdditionalTargetPixels;
            var bandWidthTarget =
                fixedBandWidthTargetPixels >
                0d
                    ? fixedBandWidthTargetPixels
                    : Math.Max(
                        0d,
                        sourceBandWidth) *
                      normalScale;

            inner.Add(
                (
                    innerX,
                    innerY
                ));
            outer.Add(
                (
                    innerX +
                    unitTargetNormalX *
                        bandWidthTarget,
                    innerY +
                    unitTargetNormalY *
                        bandWidthTarget
                ));
        }

        if (inner.Count < 3 ||
            outer.Count !=
            inner.Count)
        {
            return result;
        }

        var polygon =
            new List<(double X, double Y)>(
                inner.Count *
                2);
        polygon.AddRange(
            inner);
        polygon.AddRange(
            outer
                .AsEnumerable()
                .Reverse());

        return LeafPetalArcRasterizer.RasterizePolygon(
            polygon,
            targetWidth,
            targetHeight);
    }
}
