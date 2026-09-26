namespace RugScale.Core.Drawing;

internal static class ElegantArcFitter
{
    // Ordinary leaf/oval fitting stays at twelve macro anchors: that suppresses pixel-staircase
    // wiggles on real designs such as B996. Mirror-fused compound ribbons are substantially
    // cleaner source evidence, so callers may explicitly request up to twenty anchors when a
    // long designer sweep continues into a tight hook/curl.
    private const int DefaultMaximumAnchors = 12;
    private const int MaximumSupportedAnchors = 20;
    private const int MinimumSamplesPerSegment = 4;
    private const int MaximumSamplesPerSegment = 24;
    private const double SamplesPerSourcePixel = 1.65;
    private const double MaximumCenterlineDeviation = 1.55;

    public static ElegantArcFit Fit(
        LeafPetalArcModel model,
        bool taperApex = true,
        int maximumAnchors = DefaultMaximumAnchors,
        int smoothingPasses = 1,
        bool useCentripetalInterpolation = false)
    {
        ArgumentNullException.ThrowIfNull(model);

        maximumAnchors =
            Math.Clamp(
                maximumAnchors,
                4,
                MaximumSupportedAnchors);
        smoothingPasses =
            Math.Clamp(
                smoothingPasses,
                1,
                4);

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

        IReadOnlyList<ElegantArcPoint> smoothed =
            SmoothSourceSamples(
                source);

        for (var pass = 1;
             pass < smoothingPasses;
             pass++)
        {
            smoothed =
                SmoothArcPoints(
                    smoothed);
        }

        var anchors =
            ReduceAnchors(
                smoothed,
                maximumAnchors);
        var points =
            useCentripetalInterpolation
                ? InterpolateCentripetalCatmullRom(
                    anchors,
                    MinimumSamplesPerSegment)
                : InterpolateCatmullRom(
                    anchors,
                    MinimumSamplesPerSegment);

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

    private static List<ElegantArcPoint> SmoothArcPoints(
        IReadOnlyList<ElegantArcPoint> source)
    {
        var result =
            new List<ElegantArcPoint>(
                source.Count);

        for (var i = 0;
             i < source.Count;
             i++)
        {
            if (i == 0 ||
                i == source.Count - 1)
            {
                result.Add(
                    source[i]);
                continue;
            }

            var previous =
                source[i - 1];
            var current =
                source[i];
            var next =
                source[i + 1];

            result.Add(
                new ElegantArcPoint(
                    previous.X * 0.25 +
                    current.X * 0.50 +
                    next.X * 0.25,
                    previous.Y * 0.25 +
                    current.Y * 0.50 +
                    next.Y * 0.25,
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

            var segmentX =
                p2.X -
                p1.X;
            var segmentY =
                p2.Y -
                p1.Y;
            var segmentLength =
                Math.Sqrt(
                    segmentX *
                        segmentX +
                    segmentY *
                        segmentY);
            var adaptiveSamples =
                Math.Clamp(
                    (int)Math.Ceiling(
                        segmentLength *
                        SamplesPerSourcePixel),
                    samplesPerSegment,
                    MaximumSamplesPerSegment);

            for (var step = 0;
                 step < adaptiveSamples;
                 step++)
            {
                var t =
                    step /
                    (double)adaptiveSamples;

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

    private static List<ElegantArcPoint> InterpolateCentripetalCatmullRom(
        IReadOnlyList<ElegantArcPoint> anchors,
        int samplesPerSegment)
    {
        var result =
            new List<ElegantArcPoint>();

        if (anchors.Count < 2)
            return result;

        for (var segment = 0;
             segment < anchors.Count - 1;
             segment++)
        {
            var p1 =
                anchors[segment];
            var p2 =
                anchors[segment + 1];
            var p0 =
                segment > 0
                    ? anchors[segment - 1]
                    : ReflectEndpoint(
                        p1,
                        p2);
            var p3 =
                segment + 2 <
                anchors.Count
                    ? anchors[segment + 2]
                    : ReflectEndpoint(
                        p2,
                        p1);

            var segmentX =
                p2.X -
                p1.X;
            var segmentY =
                p2.Y -
                p1.Y;
            var segmentLength =
                Math.Sqrt(
                    segmentX *
                        segmentX +
                    segmentY *
                        segmentY);
            var adaptiveSamples =
                Math.Clamp(
                    (int)Math.Ceiling(
                        segmentLength *
                        SamplesPerSourcePixel),
                    samplesPerSegment,
                    MaximumSamplesPerSegment);

            for (var step = 0;
                 step < adaptiveSamples;
                 step++)
            {
                var u =
                    step /
                    (double)adaptiveSamples;

                result.Add(
                    Centripetal(
                        p0,
                        p1,
                        p2,
                        p3,
                        u));
            }
        }

        result.Add(
            anchors[^1]);
        return result;
    }

    private static ElegantArcPoint ReflectEndpoint(
        ElegantArcPoint origin,
        ElegantArcPoint neighbor) =>
        new(
            origin.X *
                2d -
            neighbor.X,
            origin.Y *
                2d -
            neighbor.Y,
            Math.Max(
                0.45,
                origin.HalfWidth *
                    2d -
                neighbor.HalfWidth));

    private static ElegantArcPoint Centripetal(
        ElegantArcPoint p0,
        ElegantArcPoint p1,
        ElegantArcPoint p2,
        ElegantArcPoint p3,
        double u)
    {
        const double MinimumInterval = 1e-5;

        static double Interval(
            ElegantArcPoint a,
            ElegantArcPoint b)
        {
            var dx =
                b.X -
                a.X;
            var dy =
                b.Y -
                a.Y;
            var distance =
                Math.Sqrt(
                    dx *
                        dx +
                    dy *
                        dy);

            // alpha = 0.5: parameter interval is sqrt(chord length).
            return Math.Sqrt(
                Math.Max(
                    distance,
                    MinimumInterval));
        }

        var t0 = 0d;
        var t1 =
            t0 +
            Interval(
                p0,
                p1);
        var t2 =
            t1 +
            Interval(
                p1,
                p2);
        var t3 =
            t2 +
            Interval(
                p2,
                p3);
        var t =
            t1 +
            Math.Clamp(
                u,
                0d,
                1d) *
            (t2 -
             t1);

        static double LerpAt(
            double a,
            double b,
            double ta,
            double tb,
            double t)
        {
            var span =
                tb -
                ta;

            if (Math.Abs(
                    span) <=
                1e-12)
            {
                return
                    (a +
                     b) *
                    0.5;
            }

            return
                (tb -
                 t) /
                    span *
                    a +
                (t -
                 ta) /
                    span *
                    b;
        }

        static double Eval(
            double v0,
            double v1,
            double v2,
            double v3,
            double t0,
            double t1,
            double t2,
            double t3,
            double t)
        {
            var a1 =
                LerpAt(
                    v0,
                    v1,
                    t0,
                    t1,
                    t);
            var a2 =
                LerpAt(
                    v1,
                    v2,
                    t1,
                    t2,
                    t);
            var a3 =
                LerpAt(
                    v2,
                    v3,
                    t2,
                    t3,
                    t);
            var b1 =
                LerpAt(
                    a1,
                    a2,
                    t0,
                    t2,
                    t);
            var b2 =
                LerpAt(
                    a2,
                    a3,
                    t1,
                    t3,
                    t);

            return
                LerpAt(
                    b1,
                    b2,
                    t1,
                    t2,
                    t);
        }

        return new ElegantArcPoint(
            Eval(
                p0.X,
                p1.X,
                p2.X,
                p3.X,
                t0,
                t1,
                t2,
                t3,
                t),
            Eval(
                p0.Y,
                p1.Y,
                p2.Y,
                p3.Y,
                t0,
                t1,
                t2,
                t3,
                t),
            Math.Max(
                0.45,
                Eval(
                    p0.HalfWidth,
                    p1.HalfWidth,
                    p2.HalfWidth,
                    p3.HalfWidth,
                    t0,
                    t1,
                    t2,
                    t3,
                    t)));
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
