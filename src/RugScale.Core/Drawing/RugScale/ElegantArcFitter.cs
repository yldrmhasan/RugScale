namespace RugScale.Core.Drawing;

internal static class ElegantArcFitter
{
    // Twelve macro anchors suppress pixel-staircase wiggles while still following long floral
    // bends. More anchors reintroduced tiny alternating curvature on the real B996 green leaf.
    private const int MaximumAnchors = 12;
    private const int SamplesPerSegment = 4;
    private const double MaximumCenterlineDeviation = 1.55;

    public static ElegantArcFit Fit(
        LeafPetalArcModel model,
        bool taperApex = true)
    {
        ArgumentNullException.ThrowIfNull(model);

        var source = model.Samples;
        if (source.Count < 4)
        {
            return new ElegantArcFit(
                Array.Empty<ElegantArcPoint>(),
                false,
                false,
                int.MaxValue,
                double.PositiveInfinity);
        }

        var smoothed = SmoothSourceSamples(source);
        var anchors = ReduceAnchors(smoothed, MaximumAnchors);
        var points = InterpolateCatmullRom(anchors, SamplesPerSegment);

        if (points.Count < 4)
        {
            return new ElegantArcFit(
                points,
                false,
                false,
                int.MaxValue,
                double.PositiveInfinity);
        }

        if (taperApex)
            ApplyApexTaper(points);

        var signFlips = CountCurvatureSignFlips(points);
        // Indexed 1x1 staircases can produce one or two microscopic sign alternations even on a
        // visually single-flow leaf arc. Two macro-scale flips are tolerated only while the
        // centreline remains inside the strict source-deviation corridor below.
        var monotonic = signFlips <= 2;
        var maxDeviation = MaximumDeviation(points, source);
        var safe =
            monotonic &&
            maxDeviation <= MaximumCenterlineDeviation;

        return new ElegantArcFit(
            points,
            safe,
            monotonic,
            signFlips,
            maxDeviation);
    }

    private static List<ElegantArcPoint> SmoothSourceSamples(
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var result = new List<ElegantArcPoint>(source.Count);

        for (var i = 0; i < source.Count; i++)
        {
            if (i == 0 ||
                i == source.Count - 1)
            {
                result.Add(
                    new ElegantArcPoint(
                        source[i].X,
                        source[i].Y,
                        source[i].HalfWidth));
                continue;
            }

            var previous = source[Math.Max(0, i - 1)];
            var current = source[i];
            var next = source[Math.Min(source.Count - 1, i + 1)];

            result.Add(
                new ElegantArcPoint(
                    previous.X * 0.20 +
                    current.X * 0.60 +
                    next.X * 0.20,
                    previous.Y * 0.20 +
                    current.Y * 0.60 +
                    next.Y * 0.20,
                    Math.Max(
                        0.45,
                        previous.HalfWidth * 0.20 +
                        current.HalfWidth * 0.60 +
                        next.HalfWidth * 0.20)));
        }

        return result;
    }

    private static IReadOnlyList<ElegantArcPoint> ReduceAnchors(
        IReadOnlyList<ElegantArcPoint> points,
        int maximum)
    {
        if (points.Count <= maximum)
            return points.ToArray();

        var result = new List<ElegantArcPoint>(maximum);

        for (var i = 0; i < maximum; i++)
        {
            var index = (int)Math.Round(
                i *
                (points.Count - 1d) /
                (maximum - 1d));

            result.Add(points[index]);
        }

        return result;
    }

    private static List<ElegantArcPoint> InterpolateCatmullRom(
        IReadOnlyList<ElegantArcPoint> anchors,
        int samplesPerSegment)
    {
        var result = new List<ElegantArcPoint>();

        for (var segment = 0;
             segment < anchors.Count - 1;
             segment++)
        {
            var p0 = anchors[Math.Max(0, segment - 1)];
            var p1 = anchors[segment];
            var p2 = anchors[segment + 1];
            var p3 = anchors[Math.Min(anchors.Count - 1, segment + 2)];

            for (var step = 0;
                 step < samplesPerSegment;
                 step++)
            {
                var t =
                    step /
                    (double)samplesPerSegment;

                result.Add(
                    new ElegantArcPoint(
                        Catmull(
                            p0.X,
                            p1.X,
                            p2.X,
                            p3.X,
                            t),
                        Catmull(
                            p0.Y,
                            p1.Y,
                            p2.Y,
                            p3.Y,
                            t),
                        Math.Max(
                            0.45,
                            Catmull(
                                p0.HalfWidth,
                                p1.HalfWidth,
                                p2.HalfWidth,
                                p3.HalfWidth,
                                t))));
            }
        }

        result.Add(anchors[^1]);
        return result;
    }

    private static double Catmull(
        double p0,
        double p1,
        double p2,
        double p3,
        double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;

        return 0.5 *
               (2d * p1 +
                (-p0 + p2) * t +
                (2d * p0 -
                 5d * p1 +
                 4d * p2 -
                 p3) * t2 +
                (-p0 +
                 3d * p1 -
                 3d * p2 +
                 p3) * t3);
    }

    private static void ApplyApexTaper(
        IList<ElegantArcPoint> points)
    {
        var start =
            Math.Clamp(
                (int)Math.Round(
                    points.Count *
                    0.70),
                1,
                points.Count - 1);

        for (var i = start + 1;
             i < points.Count;
             i++)
        {
            var previous =
                points[i - 1].HalfWidth;
            var current =
                points[i];

            points[i] =
                current with
                {
                    HalfWidth =
                        Math.Min(
                            current.HalfWidth,
                            previous * 1.02),
                };
        }
    }

    private static int CountCurvatureSignFlips(
        IReadOnlyList<ElegantArcPoint> points)
    {
        var flips = 0;
        var previousSign = 0;

        for (var i = 1;
             i < points.Count - 1;
             i++)
        {
            var ax =
                points[i].X -
                points[i - 1].X;
            var ay =
                points[i].Y -
                points[i - 1].Y;
            var bx =
                points[i + 1].X -
                points[i].X;
            var by =
                points[i + 1].Y -
                points[i].Y;

            var cross =
                ax * by -
                ay * bx;
            var scale =
                Math.Sqrt(
                    (ax * ax + ay * ay) *
                    (bx * bx + by * by));

            if (scale <= 1e-9 ||
                Math.Abs(cross) <
                scale * 0.08)
            {
                continue;
            }

            var sign =
                Math.Sign(cross);

            if (previousSign != 0 &&
                sign != previousSign)
            {
                flips++;
            }

            previousSign = sign;
        }

        return flips;
    }

    private static double MaximumDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        var maximum = 0d;

        foreach (var point in fit)
        {
            var nearestSquared =
                double.PositiveInfinity;

            foreach (var sample in source)
            {
                var dx =
                    point.X -
                    sample.X;
                var dy =
                    point.Y -
                    sample.Y;
                var distance =
                    dx * dx +
                    dy * dy;

                nearestSquared =
                    Math.Min(
                        nearestSquared,
                        distance);
            }

            maximum =
                Math.Max(
                    maximum,
                    Math.Sqrt(
                        nearestSquared));
        }

        return maximum;
    }
}
