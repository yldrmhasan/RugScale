namespace RugScale.Core.Drawing;

/// <summary>
/// Diagnostic curvature fairing for an already-safe compound ribbon.
///
/// This does NOT refit the source into a different model family. It starts from the accepted
/// compound centreline and applies a small Taubin-style low-pass fairing while every point remains
/// constrained by immutable source evidence. Endpoints are fixed, total displacement is bounded,
/// and the final complete curve is revalidated with symmetric source deviation.
///
/// The intent is to remove residual one-pixel staircase frequency without flattening the genuine
/// long S sweep or inventing a new path.
/// </summary>
internal static class CurveFillRibbonConstrainedFairing
{
    private const int MinimumPoints = 48;
    private const int Passes = 5;
    private const double Lambda = 0.34;
    private const double Mu = -0.35;
    private const double MaximumShiftFromAcceptedFit = 0.90;
    private const double MaximumCandidateSourceDistance = 3.10;
    private const double MaximumP95Deviation = 3.80;
    private const double MaximumDeviation = 6.10;
    private const int MaximumCurvatureSignFlips = 2;

    public static bool TryFair(
        LeafPetalArcModel model,
        ElegantArcFit acceptedFit,
        out ElegantArcFit fairedFit,
        out RibbonConstrainedFairingDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(acceptedFit);

        fairedFit = acceptedFit;
        diagnostics = default;

        if (!acceptedFit.IsSafe ||
            acceptedFit.Points.Count <
                MinimumPoints ||
            model.Samples.Count < 8)
        {
            diagnostics =
                new RibbonConstrainedFairingDiagnostics(
                    "insufficient-evidence",
                    CurveFillRibbonSmoothness.Measure(
                        acceptedFit.Points),
                    double.PositiveInfinity,
                    acceptedFit.MaximumCenterlineDeviation,
                    double.PositiveInfinity,
                    0d,
                    acceptedFit.CurvatureSignFlips,
                    int.MaxValue);
            return false;
        }

        var original =
            acceptedFit.Points
                .ToArray();
        var points =
            original
                .ToArray();

        for (var pass = 0;
             pass < Passes;
             pass++)
        {
            points =
                ApplyStep(
                    points,
                    original,
                    model.Samples,
                    Lambda);
            points =
                ApplyStep(
                    points,
                    original,
                    model.Samples,
                    Mu);
        }

        var beforeRoughness =
            CurveFillRibbonSmoothness.Measure(
                original);
        var afterRoughness =
            CurveFillRibbonSmoothness.Measure(
                points);
        var deviation =
            SymmetricPolylineDeviation(
                points,
                model.Samples);
        var beforeFlips =
            CountMacroCurvatureSignFlips(
                original);
        var flips =
            CountMacroCurvatureSignFlips(
                points);
        var maximumShift =
            0d;

        for (var index = 0;
             index < points.Length;
             index++)
        {
            maximumShift =
                Math.Max(
                    maximumShift,
                    Distance(
                        points[index].X,
                        points[index].Y,
                        original[index].X,
                        original[index].Y));
        }

        var materiallySmoother =
            beforeRoughness <= 1e-9
                ? afterRoughness <=
                  beforeRoughness
                : afterRoughness <=
                  beforeRoughness *
                  0.90;
        var safe =
            deviation.Percentile95 <=
                MaximumP95Deviation &&
            deviation.Maximum <=
                MaximumDeviation &&
            flips <=
                MaximumCurvatureSignFlips &&
            (beforeFlips == 0 ||
             flips >= 1) &&
            maximumShift <=
                MaximumShiftFromAcceptedFit +
                1e-9;

        diagnostics =
            new RibbonConstrainedFairingDiagnostics(
                !safe
                    ? deviation.Percentile95 >
                      MaximumP95Deviation
                        ? "typical-deviation"
                        : deviation.Maximum >
                          MaximumDeviation
                            ? "maximum-deviation"
                            : flips >
                              MaximumCurvatureSignFlips
                                ? "curvature-flips"
                                : "maximum-shift"
                    : materiallySmoother
                        ? "ok"
                        : "not-materially-smoother",
                beforeRoughness,
                afterRoughness,
                deviation.Maximum,
                deviation.Percentile95,
                maximumShift,
                beforeFlips,
                flips);

        fairedFit =
            new ElegantArcFit(
                points,
                safe,
                flips <=
                    MaximumCurvatureSignFlips,
                flips,
                deviation.Maximum);

        return safe &&
               materiallySmoother;
    }

    private static ElegantArcPoint[] ApplyStep(
        IReadOnlyList<ElegantArcPoint> current,
        IReadOnlyList<ElegantArcPoint> original,
        IReadOnlyList<LeafPetalAxisSample> source,
        double coefficient)
    {
        var result =
            current.ToArray();

        // Keep a few cap samples fixed so fairing cannot rotate or shorten the ribbon endpoints.
        const int FixedEndSamples = 3;

        for (var index = FixedEndSamples;
             index < current.Count - FixedEndSamples;
             index++)
        {
            var point =
                current[index];
            var neighbourX =
                (current[index - 1].X +
                 current[index + 1].X) *
                0.5;
            var neighbourY =
                (current[index - 1].Y +
                 current[index + 1].Y) *
                0.5;
            var candidateX =
                point.X +
                (neighbourX -
                 point.X) *
                coefficient;
            var candidateY =
                point.Y +
                (neighbourY -
                 point.Y) *
                coefficient;

            var shiftX =
                candidateX -
                original[index].X;
            var shiftY =
                candidateY -
                original[index].Y;
            var shift =
                Math.Sqrt(
                    shiftX *
                        shiftX +
                    shiftY *
                        shiftY);

            if (shift >
                MaximumShiftFromAcceptedFit)
            {
                var scale =
                    MaximumShiftFromAcceptedFit /
                    shift;
                candidateX =
                    original[index].X +
                    shiftX *
                        scale;
                candidateY =
                    original[index].Y +
                    shiftY *
                        scale;
            }

            var currentSourceDistance =
                DistanceToSourcePolyline(
                    point.X,
                    point.Y,
                    source);
            var allowedSourceDistance =
                Math.Min(
                    MaximumCandidateSourceDistance,
                    Math.Max(
                        1.25,
                        currentSourceDistance +
                        0.20));

            // Back off the fairing step instead of projecting onto a nearest source pixel; nearest
            // projection would reintroduce the exact staircase phase we are trying to suppress.
            for (var attempt = 0;
                 attempt < 4;
                 attempt++)
            {
                var candidateSourceDistance =
                    DistanceToSourcePolyline(
                        candidateX,
                        candidateY,
                        source);

                if (candidateSourceDistance <=
                    allowedSourceDistance)
                {
                    break;
                }

                candidateX =
                    point.X +
                    (candidateX -
                     point.X) *
                    0.5;
                candidateY =
                    point.Y +
                    (candidateY -
                     point.Y) *
                    0.5;
            }

            if (DistanceToSourcePolyline(
                    candidateX,
                    candidateY,
                    source) >
                allowedSourceDistance)
            {
                continue;
            }

            result[index] =
                point with
                {
                    X = candidateX,
                    Y = candidateY,
                };
        }

        return result;
    }

    private static int CountMacroCurvatureSignFlips(
        IReadOnlyList<ElegantArcPoint> points)
    {
        if (points.Count < 12)
            return 0;

        // Curvature safety here is intentionally macro-scale. Dense target/source-independent
        // sampling can produce tiny alternating cross-product signs after a sub-pixel fairing step
        // even when the visible designer curve has one clean S inflection. Use a wider chord and a
        // stronger normalized bend threshold so only real lobe reversals are counted.
        var stride =
            Math.Clamp(
                points.Count /
                40,
                4,
                18);
        var sampleStep =
            Math.Max(
                1,
                stride /
                2);
        var previousSign = 0;
        var flips = 0;

        for (var index = stride;
             index < points.Count - stride;
             index += sampleStep)
        {
            var ax =
                points[index].X -
                points[index - stride].X;
            var ay =
                points[index].Y -
                points[index - stride].Y;
            var bx =
                points[index + stride].X -
                points[index].X;
            var by =
                points[index + stride].Y -
                points[index].Y;
            var cross =
                ax *
                    by -
                ay *
                    bx;
            var scale =
                Math.Sqrt(
                    (ax *
                         ax +
                     ay *
                         ay) *
                    (bx *
                         bx +
                     by *
                         by));

            if (scale <= 1e-9 ||
                Math.Abs(
                    cross) <
                scale *
                0.060)
            {
                continue;
            }

            var sign =
                Math.Sign(
                    cross);

            if (previousSign != 0 &&
                sign != previousSign)
            {
                flips++;
            }

            previousSign =
                sign;
        }

        return flips;
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
        var p95Index =
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
                distances[p95Index]
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
            return
                DistanceSquared(
                    px,
                    py,
                    ax,
                    ay);
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

        return
            DistanceSquared(
                px,
                py,
                qx,
                qy);
    }

    private static double DistanceSquared(
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

        return
            dx *
                dx +
            dy *
                dy;
    }

    private static double Distance(
        double ax,
        double ay,
        double bx,
        double by) =>
        Math.Sqrt(
            DistanceSquared(
                ax,
                ay,
                bx,
                by));
}

internal readonly record struct RibbonConstrainedFairingDiagnostics(
    string Reason,
    double BeforeRoughness,
    double AfterRoughness,
    double MaximumDeviation,
    double Percentile95Deviation,
    double MaximumShift,
    int BeforeCurvatureSignFlips,
    int AfterCurvatureSignFlips);
