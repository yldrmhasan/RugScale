namespace RugScale.Core.Drawing;

/// <summary>
/// Forces one self-mirrored filled ribbon to use one exact shared designer geometry.
///
/// A centred carpet arch can be a single connected indexed region whose source pixels are an exact
/// LR mirror. Even after symmetry-aware centreline recovery, inverse fitting may nudge left/right
/// control geometry independently by a fraction of a source pixel. At enlargement that becomes a
/// visibly different staircase phase on each shoulder. This pass averages mirrored continuous fit
/// samples before rasterization and accepts the result only when it stays source-bounded.
/// </summary>
internal static class CurveFillRibbonSelfSymmetryNormalizer
{
    private const double MinimumSourceMirrorAgreement = 0.985;
    private const double MaximumNormalizedDeviation = 1.95;
    private const double MaximumDeviationRegression = 0.45;

    public static bool TryNormalize(
        LeafPetalArcModel model,
        ElegantArcFit fit,
        int sourceWidth,
        out ElegantArcFit normalized,
        out RibbonSelfSymmetryDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);

        normalized = fit;
        diagnostics = default;

        if (!fit.IsSafe ||
            fit.Points.Count < 5 ||
            sourceWidth <= 0 ||
            !TryFindMirrorConstant(
                model.Candidate.Region,
                sourceWidth,
                out var mirrorConstant,
                out var agreement))
        {
            return false;
        }

        var points =
            fit.Points
                .ToArray();
        var count =
            points.Length;

        for (var leftIndex = 0;
             leftIndex < count / 2;
             leftIndex++)
        {
            var rightIndex =
                count -
                1 -
                leftIndex;
            var left =
                points[leftIndex];
            var right =
                points[rightIndex];

            var mirroredRightX =
                mirrorConstant -
                right.X;
            var leftX =
                (left.X +
                 mirroredRightX) *
                0.5;
            var sharedY =
                (left.Y +
                 right.Y) *
                0.5;
            var sharedWidth =
                (left.HalfWidth +
                 right.HalfWidth) *
                0.5;

            points[leftIndex] =
                new ElegantArcPoint(
                    leftX,
                    sharedY,
                    sharedWidth);
            points[rightIndex] =
                new ElegantArcPoint(
                    mirrorConstant -
                    leftX,
                    sharedY,
                    sharedWidth);
        }

        if ((count &
             1) !=
            0)
        {
            var middle =
                count /
                2;
            var point =
                points[middle];

            points[middle] =
                point with
                {
                    X =
                        mirrorConstant *
                        0.5,
                };
        }

        var deviation =
            SymmetricMaximumDeviation(
                points,
                model.Samples);
        var allowed =
            Math.Min(
                MaximumNormalizedDeviation,
                fit.MaximumCenterlineDeviation +
                MaximumDeviationRegression);

        if (deviation >
            allowed)
        {
            diagnostics =
                new RibbonSelfSymmetryDiagnostics(
                    false,
                    agreement,
                    mirrorConstant,
                    deviation,
                    allowed);
            return false;
        }

        normalized =
            fit with
            {
                Points =
                    points,
                MaximumCenterlineDeviation =
                    deviation,
            };
        diagnostics =
            new RibbonSelfSymmetryDiagnostics(
                true,
                agreement,
                mirrorConstant,
                deviation,
                allowed);

        return true;
    }

    private static bool TryFindMirrorConstant(
        LeafPetalRegion region,
        int sourceWidth,
        out double mirrorConstant,
        out double agreement)
    {
        mirrorConstant = 0d;
        agreement = 0d;

        if (region.Pixels.Count < 8)
            return false;

        var pixels =
            region.Pixels.ToHashSet();
        var candidates =
            new[]
            {
                sourceWidth -
                1d,
                sourceWidth * 1d,
            };

        foreach (var candidate in candidates)
        {
            var matched = 0;

            foreach (var pixel in region.Pixels)
            {
                var x =
                    pixel %
                    sourceWidth;
                var y =
                    pixel /
                    sourceWidth;
                var mirroredX =
                    (int)Math.Round(
                        candidate -
                        x);

                if (mirroredX < 0 ||
                    mirroredX >=
                    sourceWidth)
                {
                    continue;
                }

                if (pixels.Contains(
                        y *
                            sourceWidth +
                        mirroredX))
                {
                    matched++;
                }
            }

            var current =
                matched /
                (double)region.Pixels.Count;

            if (current >
                agreement)
            {
                agreement =
                    current;
                mirrorConstant =
                    candidate;
            }
        }

        if (agreement <
            MinimumSourceMirrorAgreement)
        {
            return false;
        }

        var center =
            (region.MinX +
             region.MaxX) *
            0.5;

        return Math.Abs(
                   center -
                   mirrorConstant *
                       0.5) <=
               2.0;
    }

    private static double SymmetricMaximumDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        if (fit.Count < 2 ||
            source.Count < 2)
        {
            return double.PositiveInfinity;
        }

        var maximum = 0d;

        foreach (var point in fit)
        {
            maximum =
                Math.Max(
                    maximum,
                    DistanceToSourcePolyline(
                        point.X,
                        point.Y,
                        source));
        }

        foreach (var sample in source)
        {
            maximum =
                Math.Max(
                    maximum,
                    DistanceToFitPolyline(
                        sample.X,
                        sample.Y,
                        fit));
        }

        return maximum;
    }

    private static double DistanceToSourcePolyline(
        double x,
        double y,
        IReadOnlyList<LeafPetalAxisSample> polyline)
    {
        var minimumSquared =
            double.PositiveInfinity;

        for (var index = 1;
             index < polyline.Count;
             index++)
        {
            minimumSquared =
                Math.Min(
                    minimumSquared,
                    PointSegmentDistanceSquared(
                        x,
                        y,
                        polyline[index - 1].X,
                        polyline[index - 1].Y,
                        polyline[index].X,
                        polyline[index].Y));
        }

        return Math.Sqrt(
            minimumSquared);
    }

    private static double DistanceToFitPolyline(
        double x,
        double y,
        IReadOnlyList<ElegantArcPoint> polyline)
    {
        var minimumSquared =
            double.PositiveInfinity;

        for (var index = 1;
             index < polyline.Count;
             index++)
        {
            minimumSquared =
                Math.Min(
                    minimumSquared,
                    PointSegmentDistanceSquared(
                        x,
                        y,
                        polyline[index - 1].X,
                        polyline[index - 1].Y,
                        polyline[index].X,
                        polyline[index].Y));
        }

        return Math.Sqrt(
            minimumSquared);
    }

    private static double PointSegmentDistanceSquared(
        double px,
        double py,
        double ax,
        double ay,
        double bx,
        double by)
    {
        var dx =
            bx -
            ax;
        var dy =
            by -
            ay;
        var lengthSquared =
            dx *
                dx +
            dy *
                dy;

        if (lengthSquared <= 1e-12)
        {
            var ex =
                px -
                ax;
            var ey =
                py -
                ay;

            return ex *
                       ex +
                   ey *
                       ey;
        }

        var t =
            Math.Clamp(
                ((px -
                  ax) *
                     dx +
                 (py -
                  ay) *
                     dy) /
                lengthSquared,
                0d,
                1d);
        var qx =
            ax +
            dx *
                t;
        var qy =
            ay +
            dy *
                t;
        var rx =
            px -
            qx;
        var ry =
            py -
            qy;

        return rx *
                   rx +
               ry *
                   ry;
    }

}

internal readonly record struct RibbonSelfSymmetryDiagnostics(
    bool Applied,
    double SourceMirrorAgreement,
    double MirrorConstant,
    double MaximumDeviation,
    double AllowedDeviation);
