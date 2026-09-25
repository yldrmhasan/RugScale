namespace RugScale.Core.Drawing;

/// <summary>
/// Normalizes separately connected mirror-paired filled ribbons to one shared designer geometry.
///
/// A carpet motif frequently stores the left/right sides as two independent indexed regions. Their
/// source pixels can be exact mirror partners while thinning/path tie-breaking makes the inverse
/// fit choose different Curve families or roundness on each side. That is visually wrong: a
/// mirrored pair should look like one designer stroke reflected across the centre.
///
/// This pass is deliberately strict. It only pairs regions with the same colour, matching bounds
/// and area, a mirror axis at the design centre (allowing the common one-pixel phase shift), and
/// >=96.5% source-pixel mirror agreement. The lower-deviation fit becomes authority; its continuous
/// points are mirrored to the partner only when that mirrored geometry remains source-bounded.
/// </summary>
internal static class CurveFillRibbonMirrorPairNormalizer
{
    private const double MinimumMirrorAgreement = 0.965;
    private const double MaximumCenterAxisOffset = 2.0;
    private const double MaximumAreaRatioError = 0.06;
    private const double MaximumMirroredFitDeviation = 1.85;
    private const double MaximumDeviationRegression = 0.40;

    public static RibbonMirrorPairNormalizationDiagnostics Normalize(
        IList<(LeafPetalArcModel Model, ElegantArcFit Fit)> accepted,
        int sourceWidth)
    {
        ArgumentNullException.ThrowIfNull(accepted);

        if (accepted.Count < 2 ||
            sourceWidth <= 0)
        {
            return default;
        }

        var used =
            new bool[accepted.Count];
        var pairs = 0;
        var replacements = 0;
        var bestAgreement = 0d;
        var maximumMirroredDeviation = 0d;

        for (var firstIndex = 0;
             firstIndex < accepted.Count;
             firstIndex++)
        {
            if (used[firstIndex])
                continue;

            var bestPartner = -1;
            var bestPartnerAgreement = 0d;
            var bestMirrorConstant = 0d;

            for (var secondIndex = firstIndex + 1;
                 secondIndex < accepted.Count;
                 secondIndex++)
            {
                if (used[secondIndex])
                    continue;

                if (!TryMeasureMirrorPair(
                        accepted[firstIndex].Model.Candidate.Region,
                        accepted[secondIndex].Model.Candidate.Region,
                        sourceWidth,
                        out var mirrorConstant,
                        out var agreement))
                {
                    continue;
                }

                if (agreement <=
                    bestPartnerAgreement)
                {
                    continue;
                }

                bestPartner =
                    secondIndex;
                bestPartnerAgreement =
                    agreement;
                bestMirrorConstant =
                    mirrorConstant;
            }

            if (bestPartner < 0)
                continue;

            used[firstIndex] = true;
            used[bestPartner] = true;
            pairs++;
            bestAgreement =
                Math.Max(
                    bestAgreement,
                    bestPartnerAgreement);

            var first =
                accepted[firstIndex];
            var second =
                accepted[bestPartner];

            var authorityIsFirst =
                first.Fit.MaximumCenterlineDeviation <=
                second.Fit.MaximumCenterlineDeviation;
            var authority =
                authorityIsFirst
                    ? first
                    : second;
            var follower =
                authorityIsFirst
                    ? second
                    : first;
            var followerIndex =
                authorityIsFirst
                    ? bestPartner
                    : firstIndex;

            var mirrored =
                MirrorFit(
                    authority.Fit,
                    follower.Model,
                    bestMirrorConstant);
            var mirroredDeviation =
                SymmetricMaximumDeviation(
                    mirrored.Points,
                    follower.Model.Samples);

            maximumMirroredDeviation =
                Math.Max(
                    maximumMirroredDeviation,
                    mirroredDeviation);

            var allowedDeviation =
                Math.Min(
                    MaximumMirroredFitDeviation,
                    follower.Fit.MaximumCenterlineDeviation +
                    MaximumDeviationRegression);

            if (mirroredDeviation >
                allowedDeviation)
            {
                continue;
            }

            accepted[followerIndex] =
                (
                    follower.Model,
                    mirrored with
                    {
                        IsSafe = true,
                        IsMonotonic =
                            authority.Fit.IsMonotonic,
                        CurvatureSignFlips =
                            authority.Fit.CurvatureSignFlips,
                        MaximumCenterlineDeviation =
                            mirroredDeviation,
                    }
                );
            replacements++;
        }

        return new RibbonMirrorPairNormalizationDiagnostics(
            pairs,
            replacements,
            bestAgreement,
            maximumMirroredDeviation);
    }

    private static bool TryMeasureMirrorPair(
        LeafPetalRegion a,
        LeafPetalRegion b,
        int sourceWidth,
        out double mirrorConstant,
        out double agreement)
    {
        mirrorConstant = 0d;
        agreement = 0d;

        if (a.Color !=
                b.Color ||
            a.Area <= 0 ||
            b.Area <= 0)
        {
            return false;
        }

        var aCenter =
            (a.MinX +
             a.MaxX) *
            0.5;
        var bCenter =
            (b.MinX +
             b.MaxX) *
            0.5;

        if ((aCenter <
             sourceWidth * 0.5) ==
            (bCenter <
             sourceWidth * 0.5))
        {
            return false;
        }

        var left =
            aCenter <
            bCenter
                ? a
                : b;
        var right =
            ReferenceEquals(
                left,
                a)
                ? b
                : a;

        if (Math.Abs(
                left.MinY -
                right.MinY) >
                2 ||
            Math.Abs(
                left.MaxY -
                right.MaxY) >
                2 ||
            Math.Abs(
                left.Width -
                right.Width) >
                2 ||
            Math.Abs(
                left.Height -
                right.Height) >
                2)
        {
            return false;
        }

        var areaRatio =
            Math.Min(
                left.Area,
                right.Area) /
            (double)Math.Max(
                left.Area,
                right.Area);

        if (1d -
                areaRatio >
            MaximumAreaRatioError)
        {
            return false;
        }

        var minMaxConstant =
            left.MinX +
            right.MaxX;
        var maxMinConstant =
            left.MaxX +
            right.MinX;

        if (Math.Abs(
                minMaxConstant -
                maxMinConstant) >
            1.25)
        {
            return false;
        }

        mirrorConstant =
            (minMaxConstant +
             maxMinConstant) *
            0.5;

        // Pixel-centre symmetry may land on width-1 (ordinary mirror) or width (one-pixel phase
        // shift, common in production carpet repeats). Do not infer arbitrary local axes.
        var centerDistance =
            Math.Min(
                Math.Abs(
                    mirrorConstant -
                    (sourceWidth -
                     1d)),
                Math.Abs(
                    mirrorConstant -
                    sourceWidth));

        if (centerDistance >
            MaximumCenterAxisOffset)
        {
            return false;
        }

        var rightPixels =
            right.Pixels.ToHashSet();
        var matched = 0;

        foreach (var pixel in left.Pixels)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var mirrorX =
                (int)Math.Round(
                    mirrorConstant -
                    x);

            if (mirrorX < 0 ||
                mirrorX >= sourceWidth)
            {
                continue;
            }

            if (rightPixels.Contains(
                    y *
                        sourceWidth +
                    mirrorX))
            {
                matched++;
            }
        }

        agreement =
            matched /
            (double)Math.Max(
                1,
                left.Pixels.Count);

        return agreement >=
               MinimumMirrorAgreement;
    }

    private static ElegantArcFit MirrorFit(
        ElegantArcFit source,
        LeafPetalArcModel targetModel,
        double mirrorConstant)
    {
        var points =
            source.Points
                .Select(point =>
                    new ElegantArcPoint(
                        mirrorConstant -
                        point.X,
                        point.Y,
                        point.HalfWidth))
                .ToArray();

        if (points.Length >= 2 &&
            targetModel.Samples.Count >= 2)
        {
            var forward =
                Distance(
                    points[0],
                    targetModel.Samples[0]) +
                Distance(
                    points[^1],
                    targetModel.Samples[^1]);
            var reversed =
                Distance(
                    points[0],
                    targetModel.Samples[^1]) +
                Distance(
                    points[^1],
                    targetModel.Samples[0]);

            if (reversed <
                forward)
            {
                Array.Reverse(
                    points);
            }
        }

        return new ElegantArcFit(
            points,
            source.IsSafe,
            source.IsMonotonic,
            source.CurvatureSignFlips,
            source.MaximumCenterlineDeviation);
    }

    private static double SymmetricMaximumDeviation(
        IReadOnlyList<ElegantArcPoint> fit,
        IReadOnlyList<LeafPetalAxisSample> source)
    {
        if (fit.Count == 0 ||
            source.Count == 0)
        {
            return double.PositiveInfinity;
        }

        var maximum = 0d;

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

            maximum =
                Math.Max(
                    maximum,
                    Math.Sqrt(
                        nearest));
        }

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

            maximum =
                Math.Max(
                    maximum,
                    Math.Sqrt(
                        nearest));
        }

        return maximum;
    }

    private static double Distance(
        ElegantArcPoint point,
        LeafPetalAxisSample sample)
    {
        var dx =
            point.X -
            sample.X;
        var dy =
            point.Y -
            sample.Y;

        return Math.Sqrt(
            dx *
                dx +
            dy *
                dy);
    }
}

internal readonly record struct RibbonMirrorPairNormalizationDiagnostics(
    int Pairs,
    int Replacements,
    double BestMirrorAgreement,
    double MaximumMirroredDeviation);
