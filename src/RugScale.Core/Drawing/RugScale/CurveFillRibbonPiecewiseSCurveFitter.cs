namespace RugScale.Core.Drawing;

/// <summary>
/// Diagnostic fitter for long S-shaped designer ribbons.
///
/// A single high-anchor compound spline can stay source-bounded while still inheriting raster
/// staircase phase. A five-control Through-Points curve can be much smoother, but may underfit a
/// genuine long S sweep. This fitter keeps the real source inflection, fits the two curvature
/// lobes independently, and rebuilds a short C1 join around the inflection.
///
/// It deliberately has no production authority yet. Callers must compare its source deviation and
/// roughness against the current safe compound fit before any later selector is considered.
/// </summary>
internal static class CurveFillRibbonPiecewiseSCurveFitter
{
    private const int MinimumSamples = 72;
    private const double MaximumP95Deviation = 3.80;
    private const double MaximumDeviation = 6.10;
    private const double MaximumJoinAngleDegrees = 8.0;
    private const int MaximumCurvatureSignFlips = 2;
    private const int SegmentAnchors = 10;
    private const int SegmentSmoothingPasses = 2;
    private const int JoinBlendPoints = 10;

    public static bool TryFit(
        LeafPetalArcModel model,
        out ElegantArcFit fit,
        out RibbonPiecewiseSCurveFitDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        diagnostics = default;
        fit =
            UnsafeFit();

        if (model.Samples.Count <
            MinimumSamples)
        {
            diagnostics =
                new RibbonPiecewiseSCurveFitDiagnostics(
                    "too-few-samples",
                    0,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue,
                    double.PositiveInfinity);
            return false;
        }

        if (!TryFindInflection(
                model.Samples,
                out var splitIndex))
        {
            diagnostics =
                new RibbonPiecewiseSCurveFitDiagnostics(
                    "no-stable-inflection",
                    0,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue,
                    double.PositiveInfinity);
            return false;
        }

        var leftSamples =
            NormalizeSamples(
                model.Samples
                    .Take(
                        splitIndex + 1)
                    .ToArray());
        var rightSamples =
            NormalizeSamples(
                model.Samples
                    .Skip(
                        splitIndex)
                    .ToArray());

        if (leftSamples.Count < 12 ||
            rightSamples.Count < 12)
        {
            diagnostics =
                new RibbonPiecewiseSCurveFitDiagnostics(
                    "unbalanced-split",
                    splitIndex,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue,
                    double.PositiveInfinity);
            return false;
        }

        var leftModel =
            model with
            {
                Samples = leftSamples,
                BaseWidth = leftSamples[0].HalfWidth,
                ApexWidth = leftSamples[^1].HalfWidth,
            };
        var rightModel =
            model with
            {
                Samples = rightSamples,
                BaseWidth = rightSamples[0].HalfWidth,
                ApexWidth = rightSamples[^1].HalfWidth,
            };

        var leftFit =
            ElegantArcFitter.Fit(
                leftModel,
                taperApex: false,
                maximumAnchors:
                    SegmentAnchors,
                smoothingPasses:
                    SegmentSmoothingPasses,
                useCentripetalInterpolation: true);
        var rightFit =
            ElegantArcFitter.Fit(
                rightModel,
                taperApex: false,
                maximumAnchors:
                    SegmentAnchors,
                smoothingPasses:
                    SegmentSmoothingPasses,
                useCentripetalInterpolation: true);

        if (leftFit.Points.Count <
                JoinBlendPoints + 4 ||
            rightFit.Points.Count <
                JoinBlendPoints + 4)
        {
            diagnostics =
                new RibbonPiecewiseSCurveFitDiagnostics(
                    "segment-fit-too-short",
                    splitIndex,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue,
                    double.PositiveInfinity);
            return false;
        }

        var joined =
            JoinC1(
                leftFit.Points,
                rightFit.Points,
                model.Samples[splitIndex],
                out var joinPointIndex);

        if (joined.Count < 16)
        {
            diagnostics =
                new RibbonPiecewiseSCurveFitDiagnostics(
                    "join-failed",
                    splitIndex,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    int.MaxValue,
                    double.PositiveInfinity);
            return false;
        }

        var deviation =
            SymmetricPolylineDeviation(
                joined,
                model.Samples);
        var flips =
            CountCurvatureSignFlips(
                joined);
        var roughness =
            CurveFillRibbonSmoothness.Measure(
                joined);
        var joinAngle =
            MeasureJoinAngleDegrees(
                joined,
                joinPointIndex);

        var safe =
            deviation.Percentile95 <=
                MaximumP95Deviation &&
            deviation.Maximum <=
                MaximumDeviation &&
            flips >= 1 &&
            flips <=
                MaximumCurvatureSignFlips &&
            joinAngle <=
                MaximumJoinAngleDegrees;

        diagnostics =
            new RibbonPiecewiseSCurveFitDiagnostics(
                safe
                    ? "ok"
                    : deviation.Percentile95 >
                      MaximumP95Deviation
                        ? "typical-deviation"
                        : deviation.Maximum >
                          MaximumDeviation
                            ? "maximum-deviation"
                            : flips < 1
                                ? "lost-inflection"
                                : flips >
                                  MaximumCurvatureSignFlips
                                    ? "too-many-curvature-flips"
                                    : "join-angle",
                splitIndex,
                deviation.Maximum,
                deviation.Percentile95,
                roughness,
                flips,
                joinAngle);

        fit =
            new ElegantArcFit(
                joined,
                safe,
                flips <=
                    MaximumCurvatureSignFlips,
                flips,
                deviation.Maximum);

        return safe;
    }

    private static ElegantArcFit UnsafeFit() =>
        new(
            Array.Empty<ElegantArcPoint>(),
            false,
            false,
            int.MaxValue,
            double.PositiveInfinity);

    private static IReadOnlyList<LeafPetalAxisSample> NormalizeSamples(
        IReadOnlyList<LeafPetalAxisSample> samples)
    {
        if (samples.Count == 0)
            return Array.Empty<LeafPetalAxisSample>();

        return samples
            .Select((sample, index) =>
                sample with
                {
                    AxisPosition =
                        index /
                        (double)Math.Max(
                            1,
                            samples.Count - 1),
                })
            .ToArray();
    }

    private static bool TryFindInflection(
        IReadOnlyList<LeafPetalAxisSample> samples,
        out int splitIndex)
    {
        splitIndex = -1;

        var smoothed =
            Smooth(
                samples);
        var minimumIndex =
            Math.Max(
                8,
                samples.Count /
                5);
        var maximumIndex =
            Math.Min(
                samples.Count - 9,
                samples.Count *
                4 /
                5);
        var previousSign = 0;
        var previousIndex = -1;
        var bestStrength =
            double.NegativeInfinity;

        for (var index = 3;
             index < smoothed.Length - 3;
             index++)
        {
            var ax =
                smoothed[index].X -
                smoothed[index - 3].X;
            var ay =
                smoothed[index].Y -
                smoothed[index - 3].Y;
            var bx =
                smoothed[index + 3].X -
                smoothed[index].X;
            var by =
                smoothed[index + 3].Y -
                smoothed[index].Y;
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
                0.020)
            {
                continue;
            }

            var sign =
                Math.Sign(
                    cross);

            if (previousSign != 0 &&
                sign != previousSign)
            {
                var candidate =
                    (previousIndex +
                     index) /
                    2;

                if (candidate >=
                        minimumIndex &&
                    candidate <=
                        maximumIndex)
                {
                    var centerBias =
                        1d -
                        Math.Abs(
                            candidate -
                            samples.Count *
                            0.5) /
                        Math.Max(
                            1d,
                            samples.Count *
                            0.5);
                    var strength =
                        Math.Abs(
                            cross) /
                        scale +
                        centerBias *
                        0.10;

                    if (strength >
                        bestStrength)
                    {
                        bestStrength =
                            strength;
                        splitIndex =
                            candidate;
                    }
                }
            }

            previousSign =
                sign;
            previousIndex =
                index;
        }

        return splitIndex >= 0;
    }

    private static (double X, double Y)[] Smooth(
        IReadOnlyList<LeafPetalAxisSample> samples)
    {
        var result =
            new (double X, double Y)[
                samples.Count];

        for (var index = 0;
             index < samples.Count;
             index++)
        {
            var start =
                Math.Max(
                    0,
                    index - 2);
            var end =
                Math.Min(
                    samples.Count - 1,
                    index + 2);
            var sumX = 0d;
            var sumY = 0d;
            var weight = 0d;

            for (var sourceIndex = start;
                 sourceIndex <= end;
                 sourceIndex++)
            {
                var distance =
                    Math.Abs(
                        sourceIndex -
                        index);
                var localWeight =
                    distance switch
                    {
                        0 => 4d,
                        1 => 2d,
                        _ => 1d,
                    };
                sumX +=
                    samples[sourceIndex].X *
                    localWeight;
                sumY +=
                    samples[sourceIndex].Y *
                    localWeight;
                weight +=
                    localWeight;
            }

            result[index] =
                (
                    sumX /
                    weight,
                    sumY /
                    weight
                );
        }

        return result;
    }

    private static IReadOnlyList<ElegantArcPoint> JoinC1(
        IReadOnlyList<ElegantArcPoint> left,
        IReadOnlyList<ElegantArcPoint> right,
        LeafPetalAxisSample sourceJoin,
        out int joinPointIndex)
    {
        var leftPoints =
            left.ToArray();
        var rightPoints =
            right.ToArray();
        var leftStart =
            Math.Max(
                1,
                leftPoints.Length -
                JoinBlendPoints -
                1);
        var rightEnd =
            Math.Min(
                rightPoints.Length - 2,
                JoinBlendPoints);

        var join =
            new ElegantArcPoint(
                sourceJoin.X,
                sourceJoin.Y,
                sourceJoin.HalfWidth);

        var leftDirection =
            Normalize(
                join.X -
                leftPoints[leftStart].X,
                join.Y -
                leftPoints[leftStart].Y);
        var rightDirection =
            Normalize(
                rightPoints[rightEnd].X -
                join.X,
                rightPoints[rightEnd].Y -
                join.Y);
        var tangent =
            Normalize(
                leftDirection.X +
                rightDirection.X,
                leftDirection.Y +
                rightDirection.Y);

        if (tangent.Length <= 1e-9)
        {
            tangent =
                leftDirection.Length >
                rightDirection.Length
                    ? leftDirection
                    : rightDirection;
        }

        var leftDistance =
            Distance(
                leftPoints[leftStart],
                join);
        var rightDistance =
            Distance(
                join,
                rightPoints[rightEnd]);

        for (var index = leftStart;
             index < leftPoints.Length;
             index++)
        {
            var t =
                (index -
                 leftStart) /
                (double)Math.Max(
                    1,
                    leftPoints.Length -
                    1 -
                    leftStart);
            var start =
                leftPoints[leftStart];
            var startTangent =
                EstimateForwardTangent(
                    leftPoints,
                    leftStart,
                    leftDistance);
            leftPoints[index] =
                Hermite(
                    start,
                    join,
                    startTangent,
                    (
                        tangent.X *
                            leftDistance,
                        tangent.Y *
                            leftDistance
                    ),
                    t);
        }

        for (var index = 0;
             index <= rightEnd;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    rightEnd);
            var end =
                rightPoints[rightEnd];
            var endTangent =
                EstimateBackwardTangent(
                    rightPoints,
                    rightEnd,
                    rightDistance);
            rightPoints[index] =
                Hermite(
                    join,
                    end,
                    (
                        tangent.X *
                            rightDistance,
                        tangent.Y *
                            rightDistance
                    ),
                    endTangent,
                    t);
        }

        joinPointIndex =
            leftPoints.Length -
            1;
        var result =
            new List<ElegantArcPoint>(
                leftPoints.Length +
                rightPoints.Length -
                1);
        result.AddRange(
            leftPoints);
        result.AddRange(
            rightPoints.Skip(1));

        return result;
    }

    private static ElegantArcPoint Hermite(
        ElegantArcPoint start,
        ElegantArcPoint end,
        (double X, double Y) startTangent,
        (double X, double Y) endTangent,
        double t)
    {
        var t2 =
            t *
            t;
        var t3 =
            t2 *
            t;
        var h00 =
            2d *
                t3 -
            3d *
                t2 +
            1d;
        var h10 =
            t3 -
            2d *
                t2 +
            t;
        var h01 =
            -2d *
                t3 +
            3d *
                t2;
        var h11 =
            t3 -
            t2;

        return new ElegantArcPoint(
            h00 *
                start.X +
            h10 *
                startTangent.X +
            h01 *
                end.X +
            h11 *
                endTangent.X,
            h00 *
                start.Y +
            h10 *
                startTangent.Y +
            h01 *
                end.Y +
            h11 *
                endTangent.Y,
            start.HalfWidth +
            (end.HalfWidth -
             start.HalfWidth) *
            t);
    }

    private static (double X, double Y) EstimateForwardTangent(
        IReadOnlyList<ElegantArcPoint> points,
        int index,
        double magnitude)
    {
        var next =
            points[
                Math.Min(
                    points.Count - 1,
                    index + 2)];
        var direction =
            Normalize(
                next.X -
                points[index].X,
                next.Y -
                points[index].Y);

        return
            (
                direction.X *
                    magnitude,
                direction.Y *
                    magnitude
            );
    }

    private static (double X, double Y) EstimateBackwardTangent(
        IReadOnlyList<ElegantArcPoint> points,
        int index,
        double magnitude)
    {
        var previous =
            points[
                Math.Max(
                    0,
                    index - 2)];
        var direction =
            Normalize(
                points[index].X -
                previous.X,
                points[index].Y -
                previous.Y);

        return
            (
                direction.X *
                    magnitude,
                direction.Y *
                    magnitude
            );
    }

    private static (double X, double Y, double Length) Normalize(
        double x,
        double y)
    {
        var length =
            Math.Sqrt(
                x *
                    x +
                y *
                    y);

        if (length <= 1e-9)
            return (0d, 0d, 0d);

        return
            (
                x /
                    length,
                y /
                    length,
                length
            );
    }

    private static double Distance(
        ElegantArcPoint a,
        ElegantArcPoint b)
    {
        var dx =
            b.X -
            a.X;
        var dy =
            b.Y -
            a.Y;

        return Math.Sqrt(
            dx *
                dx +
            dy *
                dy);
    }

    private static double MeasureJoinAngleDegrees(
        IReadOnlyList<ElegantArcPoint> points,
        int center)
    {
        if (points.Count < 12 ||
            center < 4 ||
            center + 4 >=
                points.Count)
        {
            return 180d;
        }
        var before =
            Normalize(
                points[center].X -
                points[Math.Max(
                    0,
                    center - 4)].X,
                points[center].Y -
                points[Math.Max(
                    0,
                    center - 4)].Y);
        var after =
            Normalize(
                points[Math.Min(
                    points.Count - 1,
                    center + 4)].X -
                points[center].X,
                points[Math.Min(
                    points.Count - 1,
                    center + 4)].Y -
                points[center].Y);

        if (before.Length <= 1e-9 ||
            after.Length <= 1e-9)
            return 180d;

        var dot =
            Math.Clamp(
                before.X *
                    after.X +
                before.Y *
                    after.Y,
                -1d,
                1d);

        return Math.Acos(
                   dot) *
               180d /
               Math.PI;
    }

    private static int CountCurvatureSignFlips(
        IReadOnlyList<ElegantArcPoint> points)
    {
        var previousSign = 0;
        var flips = 0;

        for (var index = 2;
             index < points.Count - 2;
             index++)
        {
            var ax =
                points[index].X -
                points[index - 2].X;
            var ay =
                points[index].Y -
                points[index - 2].Y;
            var bx =
                points[index + 2].X -
                points[index].X;
            var by =
                points[index + 2].Y -
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
                continue;

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

        distances.Sort();

        if (distances.Count == 0)
        {
            return
                (
                    double.PositiveInfinity,
                    double.PositiveInfinity
                );
        }

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

internal readonly record struct RibbonPiecewiseSCurveFitDiagnostics(
    string Reason,
    int SplitIndex,
    double MaximumDeviation,
    double Percentile95Deviation,
    double Roughness,
    int CurvatureSignFlips,
    double JoinAngleDegrees);
