namespace RugScale.Core.Drawing;

/// <summary>
/// Identifies elongated filled components that are plausible leaf/petal bodies.
///
/// This is deliberately conservative. It does not try to call every floral component a leaf:
/// nearly round medallions, very thin outline networks, dense rectangles and heavily branched
/// colour masses remain on normal Curve & Fill.
/// </summary>
internal static class LeafPetalArcClassifier
{
    // Inner veins/slits remove mass from the centre and can reduce PCA elongation even when the
    // visible outer silhouette is an unmistakably long leaf. Keep the classifier permissive and
    // let medial-axis + paired-boundary safety decide whether specialist redraw is allowed.
    private const double MinimumElongation = 1.55;
    private const double MinimumMajorExtent = 9.0;
    private const double MinimumMinorExtent = 2.5;
    private const double MaximumBoundaryRatio = 0.62;
    private const double MinimumBoundingFillRatio = 0.10;
    private const double MaximumBoundingFillRatio = 0.82;

    public static bool TryClassify(
        LeafPetalRegion region,
        int sourceWidth,
        out LeafPetalArcCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(region);

        candidate = null!;

        if (region.Pixels.Count == 0)
            return false;

        var centerX =
            region.Pixels.Average(pixel =>
                pixel %
                sourceWidth);
        var centerY =
            region.Pixels.Average(pixel =>
                pixel /
                sourceWidth);

        var covarianceXX = 0d;
        var covarianceYY = 0d;
        var covarianceXY = 0d;

        foreach (var pixel in region.Pixels)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var dx =
                x -
                centerX;
            var dy =
                y -
                centerY;

            covarianceXX +=
                dx *
                dx;
            covarianceYY +=
                dy *
                dy;
            covarianceXY +=
                dx *
                dy;
        }

        var count =
            Math.Max(
                1,
                region.Pixels.Count);
        covarianceXX /=
            count;
        covarianceYY /=
            count;
        covarianceXY /=
            count;

        var theta =
            0.5 *
            Math.Atan2(
                2d *
                covarianceXY,
                covarianceXX -
                covarianceYY);
        var axisX =
            Math.Cos(theta);
        var axisY =
            Math.Sin(theta);
        var normalX =
            -axisY;
        var normalY =
            axisX;

        var minimumMajor =
            double.PositiveInfinity;
        var maximumMajor =
            double.NegativeInfinity;
        var minimumMinor =
            double.PositiveInfinity;
        var maximumMinor =
            double.NegativeInfinity;

        foreach (var pixel in region.Pixels)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var dx =
                x -
                centerX;
            var dy =
                y -
                centerY;

            var major =
                dx *
                axisX +
                dy *
                axisY;
            var minor =
                dx *
                normalX +
                dy *
                normalY;

            minimumMajor =
                Math.Min(
                    minimumMajor,
                    major);
            maximumMajor =
                Math.Max(
                    maximumMajor,
                    major);
            minimumMinor =
                Math.Min(
                    minimumMinor,
                    minor);
            maximumMinor =
                Math.Max(
                    maximumMinor,
                    minor);
        }

        var majorExtent =
            maximumMajor -
            minimumMajor +
            1d;
        var minorExtent =
            maximumMinor -
            minimumMinor +
            1d;
        var elongation =
            majorExtent /
            Math.Max(
                1d,
                minorExtent);
        var boundaryRatio =
            region.BoundaryPixels.Count /
            (double)Math.Max(
                1,
                region.Pixels.Count);
        var boundingArea =
            Math.Max(
                1,
                region.Width *
                region.Height);
        var boundingFillRatio =
            region.Area /
            (double)boundingArea;

        if (majorExtent <
                MinimumMajorExtent ||
            minorExtent <
                MinimumMinorExtent ||
            elongation <
                MinimumElongation ||
            boundaryRatio >
                MaximumBoundaryRatio ||
            boundingFillRatio <
                MinimumBoundingFillRatio ||
            boundingFillRatio >
                MaximumBoundingFillRatio)
        {
            return false;
        }

        candidate =
            new LeafPetalArcCandidate(
                region,
                centerX,
                centerY,
                axisX,
                axisY,
                normalX,
                normalY,
                majorExtent,
                minorExtent,
                elongation,
                boundaryRatio);

        return true;
    }
}
