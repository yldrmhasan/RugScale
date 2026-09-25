namespace RugScale.Core.Drawing;

/// <summary>
/// Builds one smooth compound designer curve for a strongly mirror-fused filled ribbon.
///
/// Some carpet ribbons are not one elementary oval: a long arc may continue into a curl/spiral.
/// Forcing the complete medial path into one five-point Curve family either underfits the curl or
/// rejects an otherwise aesthetically clean redraw. When TWO mirrored source regions have already
/// been fused, however, raster phase noise is strongly suppressed and a low-frequency macro spline
/// is reliable evidence of the intended designer stroke.
///
/// This fitter deliberately has no authority on ordinary regions. The caller may invoke it only
/// after strict mirror-pair source fusion. It uses 12 macro anchors and validates the continuous
/// fit against the complete fused source polyline with robust symmetric distances.
/// </summary>
internal static class CurveFillRibbonCompoundFitter
{
    private const int MaximumAnchors = 12;
    private const int SmoothingPasses = 2;
    private const int MaximumCurvatureSignFlips = 2;
    private const double MaximumP95Deviation = 3.80;
    private const double MaximumDeviation = 6.10;

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out RibbonCompoundFitDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        fit =
            ElegantArcFitter.Fit(
                model,
                taperApex: false,
                maximumAnchors: MaximumAnchors,
                smoothingPasses: SmoothingPasses);

        diagnostics = default;

        if (fit.Points.Count < 8 ||
            model.Samples.Count < 8)
        {
            diagnostics =
                new RibbonCompoundFitDiagnostics(
                    "insufficient-evidence",
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue);
            return false;
        }

        var deviation =
            SymmetricPolylineDeviation(
                fit.Points,
                model.Samples);
        var safe =
            fit.CurvatureSignFlips <=
                MaximumCurvatureSignFlips &&
            deviation.Percentile95 <=
                MaximumP95Deviation &&
            deviation.Maximum <=
                MaximumDeviation;

        diagnostics =
            new RibbonCompoundFitDiagnostics(
                safe
                    ? "ok"
                    : fit.CurvatureSignFlips >
                      MaximumCurvatureSignFlips
                        ? "curvature-flips"
                        : deviation.Percentile95 >
                          MaximumP95Deviation
                            ? "typical-deviation"
                            : "maximum-deviation",
                deviation.Maximum,
                deviation.Percentile95,
                fit.CurvatureSignFlips);

        fit =
            fit with
            {
                IsSafe = safe,
                IsMonotonic =
                    fit.CurvatureSignFlips <=
                    MaximumCurvatureSignFlips,
                MaximumCenterlineDeviation =
                    deviation.Maximum,
            };

        return safe;
    }

    private static (double Maximum, double Percentile95) SymmetricPolylineDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var distances =
            new List<double>(
                fit.Count +
                source.Count);

        foreach (var point in fit)
        {
            distances.Add(
                DistanceToSourcePolyline(
                    point.X,
                    point.Y,
                    source));
        }

        foreach (var sample in source)
        {
            distances.Add(
                DistanceToFitPolyline(
                    sample.X,
                    sample.Y,
                    fit));
        }

        if (distances.Count == 0)
        {
            return
                (
                    double.PositiveInfinity,
                    double.PositiveInfinity
                );
        }

        distances.Sort();

        var percentileIndex =
            Math.Clamp(
                (int)Math.Ceiling(
                    distances.Count *
                    0.95) -
                1,
                0,
                distances.Count - 1);

        return
            (
                distances[^1],
                distances[percentileIndex]
            );
    }

    private static double DistanceToSourcePolyline(
        double x,
        double y,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var minimumSquared =
            double.PositiveInfinity;

        for (var index = 1;
             index < source.Count;
             index++)
        {
            minimumSquared =
                Math.Min(
                    minimumSquared,
                    PointSegmentDistanceSquared(
                        x,
                        y,
                        source[index - 1].X,
                        source[index - 1].Y,
                        source[index].X,
                        source[index].Y));
        }

        return Math.Sqrt(
            minimumSquared);
    }

    private static double DistanceToFitPolyline(
        double x,
        double y,
        IReadOnlyList<ElegantArcPoint> fit)
    {
        var minimumSquared =
            double.PositiveInfinity;

        for (var index = 1;
             index < fit.Count;
             index++)
        {
            minimumSquared =
                Math.Min(
                    minimumSquared,
                    PointSegmentDistanceSquared(
                        x,
                        y,
                        fit[index - 1].X,
                        fit[index - 1].Y,
                        fit[index].X,
                        fit[index].Y));
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

internal readonly record struct RibbonCompoundFitDiagnostics(
    string Reason,
    double MaximumDeviation,
    double Percentile95Deviation,
    int CurvatureSignFlips);
