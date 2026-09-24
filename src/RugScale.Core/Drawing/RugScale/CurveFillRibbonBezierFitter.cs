namespace RugScale.Core.Drawing;

/// <summary>
/// Fits a high-confidence filled ribbon centreline with one continuous cubic Bezier.
///
/// The recovered medial skeleton is raster evidence, not geometry. A short sequence of spline
/// anchors can still inherit one-pixel staircase phase from that evidence. For a single-flow
/// oval/arch ribbon, one least-squares cubic is a better model of the designer's stroke: endpoints
/// are fixed, the two hidden handles are solved from the complete source path, and the result is
/// accepted only when it stays inside a strict source-space deviation corridor and has no
/// inflection.
/// </summary>
internal static class CurveFillRibbonBezierFitter
{
    private const int ContinuousSamples = 96;
    private const double MaximumTypicalCenterlineDeviation = 1.50;
    private const double MaximumOutlierCenterlineDeviation = 4.25;
    private const double MaximumHandleToChordRatio = 2.75;

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit) =>
        TryFit(
            model,
            out fit,
            out _);

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out RibbonCubicFitDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        diagnostics = default;
        fit =
            new ElegantArcFit(
                Array.Empty<ElegantArcPoint>(),
                false,
                false,
                int.MaxValue,
                double.PositiveInfinity);

        var source =
            model.Samples;

        if (source.Count < 8)
        {
            diagnostics =
                new RibbonCubicFitDiagnostics(
                    "too-few-samples",
                    0d,
                    0d,
                    0d,
                    0);
            return false;
        }

        var parameters =
            BuildChordLengthParameters(
                source);
        var p0 =
            (X: source[0].X, Y: source[0].Y);
        var p3 =
            (X: source[^1].X, Y: source[^1].Y);

        var chordX =
            p3.X -
            p0.X;
        var chordY =
            p3.Y -
            p0.Y;
        var chordLength =
            Math.Sqrt(
                chordX *
                chordX +
                chordY *
                chordY);

        if (chordLength < 6d)
        {
            diagnostics =
                new RibbonCubicFitDiagnostics(
                    "chord-too-short",
                    0d,
                    0d,
                    0d,
                    0);
            return false;
        }

        var a11 = 0d;
        var a12 = 0d;
        var a22 = 0d;
        var bx1 = 0d;
        var bx2 = 0d;
        var by1 = 0d;
        var by2 = 0d;

        for (var index = 1;
             index < source.Count - 1;
             index++)
        {
            var t =
                parameters[index];
            var oneMinus =
                1d -
                t;
            var b0 =
                oneMinus *
                oneMinus *
                oneMinus;
            var b1 =
                3d *
                oneMinus *
                oneMinus *
                t;
            var b2 =
                3d *
                oneMinus *
                t *
                t;
            var b3 =
                t *
                t *
                t;

            var rhsX =
                source[index].X -
                b0 *
                p0.X -
                b3 *
                p3.X;
            var rhsY =
                source[index].Y -
                b0 *
                p0.Y -
                b3 *
                p3.Y;

            a11 +=
                b1 *
                b1;
            a12 +=
                b1 *
                b2;
            a22 +=
                b2 *
                b2;
            bx1 +=
                b1 *
                rhsX;
            bx2 +=
                b2 *
                rhsX;
            by1 +=
                b1 *
                rhsY;
            by2 +=
                b2 *
                rhsY;
        }

        var determinant =
            a11 *
                a22 -
            a12 *
                a12;

        if (Math.Abs(
                determinant) <
            1e-9)
        {
            diagnostics =
                new RibbonCubicFitDiagnostics(
                    "singular-fit",
                    0d,
                    0d,
                    0d,
                    0);
            return false;
        }

        var p1 =
            (
                X:
                    (bx1 *
                         a22 -
                     bx2 *
                         a12) /
                    determinant,
                Y:
                    (by1 *
                         a22 -
                     by2 *
                         a12) /
                    determinant);
        var p2 =
            (
                X:
                    (a11 *
                         bx2 -
                     a12 *
                         bx1) /
                    determinant,
                Y:
                    (a11 *
                         by2 -
                     a12 *
                         by1) /
                    determinant);

        var firstHandle =
            Distance(
                p0,
                p1);
        var secondHandle =
            Distance(
                p2,
                p3);

        var maximumHandleRatio =
            Math.Max(
                firstHandle,
                secondHandle) /
            chordLength;

        if (maximumHandleRatio >
            MaximumHandleToChordRatio)
        {
            diagnostics =
                new RibbonCubicFitDiagnostics(
                    "handle-ratio",
                    0d,
                    0d,
                    maximumHandleRatio,
                    0);
            return false;
        }

        var points =
            new List<ElegantArcPoint>(
                ContinuousSamples);

        for (var index = 0;
             index < ContinuousSamples;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    ContinuousSamples - 1);
            var point =
                Evaluate(
                    p0,
                    p1,
                    p2,
                    p3,
                    t);

            points.Add(
                new ElegantArcPoint(
                    point.X,
                    point.Y,
                    InterpolateHalfWidth(
                        source,
                        parameters,
                        t)));
        }

        var curvatureFlips =
            CountCurvatureSignFlips(
                points);
        var deviation =
            SymmetricDeviation(
                points,
                source);
        var safe =
            curvatureFlips == 0 &&
            deviation.Percentile95 <=
                MaximumTypicalCenterlineDeviation &&
            deviation.Maximum <=
                MaximumOutlierCenterlineDeviation;

        diagnostics =
            new RibbonCubicFitDiagnostics(
                safe
                    ? "ok"
                    : curvatureFlips != 0
                        ? "curvature-flip"
                        : deviation.Percentile95 >
                          MaximumTypicalCenterlineDeviation
                            ? "typical-deviation"
                            : "maximum-deviation",
                deviation.Maximum,
                deviation.Percentile95,
                maximumHandleRatio,
                curvatureFlips);

        fit =
            new ElegantArcFit(
                points,
                safe,
                curvatureFlips == 0,
                curvatureFlips,
                deviation.Maximum);

        return safe;
    }

    private static double[] BuildChordLengthParameters(
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var parameters =
            new double[source.Count];
        var total = 0d;

        for (var index = 1;
             index < source.Count;
             index++)
        {
            var dx =
                source[index].X -
                source[index - 1].X;
            var dy =
                source[index].Y -
                source[index - 1].Y;

            total +=
                Math.Sqrt(
                    dx *
                        dx +
                    dy *
                        dy);
            parameters[index] =
                total;
        }

        if (total <= 1e-9)
        {
            for (var index = 0;
                 index < parameters.Length;
                 index++)
            {
                parameters[index] =
                    index /
                    (double)Math.Max(
                        1,
                        parameters.Length - 1);
            }

            return parameters;
        }

        for (var index = 1;
             index < parameters.Length;
             index++)
        {
            parameters[index] /=
                total;
        }

        return parameters;
    }

    private static (double X, double Y) Evaluate(
        (double X, double Y) p0,
        (double X, double Y) p1,
        (double X, double Y) p2,
        (double X, double Y) p3,
        double t)
    {
        var oneMinus =
            1d -
            t;
        var b0 =
            oneMinus *
            oneMinus *
            oneMinus;
        var b1 =
            3d *
            oneMinus *
            oneMinus *
            t;
        var b2 =
            3d *
            oneMinus *
            t *
            t;
        var b3 =
            t *
            t *
            t;

        return
            (
                b0 *
                    p0.X +
                b1 *
                    p1.X +
                b2 *
                    p2.X +
                b3 *
                    p3.X,
                b0 *
                    p0.Y +
                b1 *
                    p1.Y +
                b2 *
                    p2.Y +
                b3 *
                    p3.Y
            );
    }

    private static double InterpolateHalfWidth(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<double> parameters,
        double t)
    {
        if (source.Count == 0)
            return 0.5;

        var right = 1;

        while (right <
                   parameters.Count - 1 &&
               parameters[right] <
                   t)
        {
            right++;
        }

        var left =
            Math.Max(
                0,
                right - 1);
        var span =
            parameters[right] -
            parameters[left];
        var local =
            span <= 1e-9
                ? 0d
                : Math.Clamp(
                    (t -
                     parameters[left]) /
                    span,
                    0d,
                    1d);

        return Math.Max(
            0.5,
            source[left].HalfWidth +
            (source[right].HalfWidth -
             source[left].HalfWidth) *
            local);
    }

    private static int CountCurvatureSignFlips(
        IReadOnlyList<ElegantArcPoint> points)
    {
        var previousSign = 0;
        var flips = 0;

        for (var index = 1;
             index < points.Count - 1;
             index++)
        {
            var ax =
                points[index].X -
                points[index - 1].X;
            var ay =
                points[index].Y -
                points[index - 1].Y;
            var bx =
                points[index + 1].X -
                points[index].X;
            var by =
                points[index + 1].Y -
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
                0.025)
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

    private static (double Maximum, double Percentile95) SymmetricDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var distances =
            new List<double>(
                fit.Count +
                source.Count);

        foreach (var sample in source)
        {
            var nearest =
                fit.Min(point =>
                {
                    var dx =
                        point.X -
                        sample.X;
                    var dy =
                        point.Y -
                        sample.Y;

                    return dx *
                               dx +
                           dy *
                               dy;
                });

            distances.Add(
                Math.Sqrt(
                    nearest));
        }

        foreach (var point in fit)
        {
            var nearest =
                source.Min(sample =>
                {
                    var dx =
                        point.X -
                        sample.X;
                    var dy =
                        point.Y -
                        sample.Y;

                    return dx *
                               dx +
                           dy *
                               dy;
                });

            distances.Add(
                Math.Sqrt(
                    nearest));
        }

        if (distances.Count == 0)
            return (double.PositiveInfinity, double.PositiveInfinity);

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

    private static double Distance(
        (double X, double Y) a,
        (double X, double Y) b)
    {
        var dx =
            a.X -
            b.X;
        var dy =
            a.Y -
            b.Y;

        return Math.Sqrt(
            dx *
                dx +
            dy *
                dy);
    }
}


internal readonly record struct RibbonCubicFitDiagnostics(
    string Reason,
    double MaximumDeviation,
    double Percentile95Deviation,
    double MaximumHandleToChordRatio,
    int CurvatureSignFlips);
