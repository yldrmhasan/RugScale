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
    private const int MaximumCurvatureSignFlips = 2;
    private const double MaximumP95Deviation = 3.80;
    private const double MaximumDeviation = 6.10;
    private const double SmoothnessWeight = 4.25;
    private const double AnchorComplexityWeight = 0.025;
    private const double SmoothingPassComplexityWeight = 0.010;

    private static readonly (int Anchors, int SmoothingPasses)[] CandidateSettings =
    [
        // Low-frequency redraw candidates. These intentionally underfit one-pixel skeleton phase;
        // they are still required to pass the exact same symmetric source-distance safety gates.
        (5, 2),
        (5, 3),
        (6, 2),
        (6, 3),
        (6, 4),
        (7, 2),
        (7, 3),
        (8, 1),
        (8, 2),
        (8, 3),
        (8, 4),
        (10, 1),
        (10, 2),
        (10, 3),
        (10, 4),
        (12, 1),
        (12, 2),
        (12, 3),
        (14, 1),
        (14, 2),
        (16, 1),
        (16, 2),
        (18, 2),
        (20, 2),
        (20, 3),
    ];

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out RibbonCompoundFitDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        diagnostics = default;

        if (model.Samples.Count < 8)
        {
            fit =
                new ElegantArcFit(
                    Array.Empty<ElegantArcPoint>(),
                    false,
                    false,
                    int.MaxValue,
                    double.PositiveInfinity);
            diagnostics =
                new RibbonCompoundFitDiagnostics(
                    "insufficient-evidence",
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue);
            return false;
        }

        CompoundCandidate? best = null;
        CompoundCandidate? smoothestSafe = null;
        CompoundCandidate? smoothestCurvatureValid = null;

        foreach (var setting in CandidateSettings)
        {
            var candidateFit =
                ElegantArcFitter.Fit(
                    model,
                    taperApex: false,
                    maximumAnchors: setting.Anchors,
                    smoothingPasses: setting.SmoothingPasses,
                    useCentripetalInterpolation: true);

            if (candidateFit.Points.Count < 8)
                continue;

            var deviation =
                SymmetricPolylineDeviation(
                    candidateFit.Points,
                    model.Samples);
            var safe =
                candidateFit.CurvatureSignFlips <=
                    MaximumCurvatureSignFlips &&
                deviation.Percentile95 <=
                    MaximumP95Deviation &&
                deviation.Maximum <=
                    MaximumDeviation;

            // Source geometry remains a hard safety gate, but once two candidates are safely
            // inside that corridor the carpet designer's low-frequency curve must beat residual
            // skeleton staircase. Previously deviation dominated so strongly that a 16-20 anchor
            // spline could win by hugging every one-pixel phase error. Curvature roughness now has
            // real authority, while a modest complexity term prefers the simpler redraw when two
            // curves look equally smooth.
            var roughness =
                CurveFillRibbonSmoothness.Measure(
                    candidateFit.Points);
            var score =
                deviation.Percentile95 +
                deviation.Maximum *
                    0.10 +
                roughness *
                    SmoothnessWeight +
                candidateFit.CurvatureSignFlips *
                    1.50 +
                setting.Anchors *
                    AnchorComplexityWeight +
                setting.SmoothingPasses *
                    SmoothingPassComplexityWeight;

            var candidate =
                new CompoundCandidate(
                    candidateFit,
                    deviation.Maximum,
                    deviation.Percentile95,
                    roughness,
                    setting.Anchors,
                    setting.SmoothingPasses,
                    safe,
                    score);

            if (candidateFit.CurvatureSignFlips <=
                    MaximumCurvatureSignFlips &&
                (smoothestCurvatureValid is null ||
                 candidate.Roughness <
                    smoothestCurvatureValid.Value.Roughness -
                    1e-9 ||
                 Math.Abs(
                     candidate.Roughness -
                     smoothestCurvatureValid.Value.Roughness) <=
                    1e-9 &&
                 candidate.Percentile95Deviation <
                    smoothestCurvatureValid.Value.Percentile95Deviation))
            {
                smoothestCurvatureValid =
                    candidate;
            }

            if (candidate.Safe &&
                (smoothestSafe is null ||
                 candidate.Roughness <
                    smoothestSafe.Value.Roughness -
                    1e-9 ||
                 Math.Abs(
                     candidate.Roughness -
                     smoothestSafe.Value.Roughness) <=
                    1e-9 &&
                 candidate.Score <
                    smoothestSafe.Value.Score))
            {
                smoothestSafe =
                    candidate;
            }

            if (best is null ||
                candidate.Safe &&
                !best.Value.Safe ||
                candidate.Safe ==
                    best.Value.Safe &&
                candidate.Score <
                    best.Value.Score)
            {
                best =
                    candidate;
            }
        }

        if (best is null)
        {
            fit =
                new ElegantArcFit(
                    Array.Empty<ElegantArcPoint>(),
                    false,
                    false,
                    int.MaxValue,
                    double.PositiveInfinity);
            diagnostics =
                new RibbonCompoundFitDiagnostics(
                    "insufficient-evidence",
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue);
            return false;
        }

        var selected =
            best.Value;

        if (smoothestSafe is { } smoother &&
            CurveFillRibbonCompoundFitSelector.PreferSmootherSafeAlternative(
                selected.Roughness,
                selected.Percentile95Deviation,
                selected.MaximumDeviation,
                selected.Anchors,
                smoother.Roughness,
                smoother.Percentile95Deviation,
                smoother.MaximumDeviation,
                smoother.Anchors))
        {
            selected =
                smoother;
        }

        fit =
            selected.Fit with
            {
                IsSafe =
                    selected.Safe,
                IsMonotonic =
                    selected.Fit.CurvatureSignFlips <=
                    MaximumCurvatureSignFlips,
                MaximumCenterlineDeviation =
                    selected.MaximumDeviation,
            };

        diagnostics =
            new RibbonCompoundFitDiagnostics(
                selected.Safe
                    ? "ok"
                    : selected.Fit.CurvatureSignFlips >
                      MaximumCurvatureSignFlips
                        ? "curvature-flips"
                        : selected.Percentile95Deviation >
                          MaximumP95Deviation
                            ? "typical-deviation"
                            : "maximum-deviation",
                selected.MaximumDeviation,
                selected.Percentile95Deviation,
                selected.Fit.CurvatureSignFlips,
                selected.Roughness,
                selected.Anchors,
                selected.SmoothingPasses,
                smoothestSafe?.Roughness ?? 0d,
                smoothestSafe?.Anchors ?? 0,
                smoothestSafe?.SmoothingPasses ?? 0,
                smoothestSafe?.Percentile95Deviation ?? 0d,
                smoothestSafe?.MaximumDeviation ?? 0d,
                smoothestCurvatureValid?.Roughness ?? 0d,
                smoothestCurvatureValid?.Anchors ?? 0,
                smoothestCurvatureValid?.SmoothingPasses ?? 0,
                smoothestCurvatureValid?.Percentile95Deviation ?? 0d,
                smoothestCurvatureValid?.MaximumDeviation ?? 0d);

        return selected.Safe;
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
    private readonly record struct CompoundCandidate(
        ElegantArcFit Fit,
        double MaximumDeviation,
        double Percentile95Deviation,
        double Roughness,
        int Anchors,
        int SmoothingPasses,
        bool Safe,
        double Score);
}

internal readonly record struct RibbonCompoundFitDiagnostics(
    string Reason,
    double MaximumDeviation,
    double Percentile95Deviation,
    int CurvatureSignFlips,
    double Roughness = 0d,
    int Anchors = 0,
    int SmoothingPasses = 0,
    double SmoothestSafeRoughness = 0d,
    int SmoothestSafeAnchors = 0,
    int SmoothestSafeSmoothingPasses = 0,
    double SmoothestSafeP95Deviation = 0d,
    double SmoothestSafeMaximumDeviation = 0d,
    double SmoothestCurvatureValidRoughness = 0d,
    int SmoothestCurvatureValidAnchors = 0,
    int SmoothestCurvatureValidSmoothingPasses = 0,
    double SmoothestCurvatureValidP95Deviation = 0d,
    double SmoothestCurvatureValidMaximumDeviation = 0d);
