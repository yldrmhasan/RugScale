namespace RugScale.Core.Drawing;

/// <summary>
/// Fuses two near-mirrored filled-ribbon source regions BEFORE curve fitting.
///
/// Production carpet designs often contain left/right copies of the same broad curved ribbon.
/// Raster phase, thinning order and tiny hand edits can move the recovered medial spine by a few
/// cells even when the underlying designer geometry is shared. Fitting each side independently
/// therefore amplifies noise.
///
/// This recovery is deliberately strict: same colour, opposite sides of the design centre,
/// near-identical bounds/area, >=94% source-pixel mirror agreement and bounded centreline fusion.
/// The two ordered medial paths are mirrored into one coordinate system, resampled at normalized
/// path position and averaged. Each side then sees the same denoised geometry (mirrored back by
/// the caller's own candidate coordinates) before Through-Points/Bezier fitting.
/// </summary>
internal static class CurveFillRibbonMirrorPairRecovery
{
    private const double MinimumMirrorAgreement = 0.94;
    private const double MaximumCenterAxisOffset = 2.0;
    private const double MinimumAreaRatio = 0.93;
    private const int MaximumBoundsDelta = 2;
    private const double MaximumMeanFusionShift = 2.25;
    private const double MaximumFusionShift = 4.50;

    public static bool TryRecover(
        LeafPetalArcModel model,
        IReadOnlyList<LeafPetalRegion> regions,
        int sourceWidth,
        out LeafPetalArcModel recovered,
        out RibbonMirrorPairRecoveryDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(regions);

        recovered = model;
        diagnostics = default;

        if (sourceWidth <= 0 ||
            model.Samples.Count < 8)
        {
            diagnostics =
                new RibbonMirrorPairRecoveryDiagnostics(
                    "insufficient-evidence",
                    0d,
                    0d,
                    0d,
                    0d,
                    0);
            return false;
        }

        var sourceRegion =
            model.Candidate.Region;
        var sourceCenter =
            (sourceRegion.MinX +
             sourceRegion.MaxX) *
            0.5;
        var designCenter =
            sourceWidth *
            0.5;

        // This helper is for two distinct left/right regions, not a single centred arch.
        if (Math.Abs(
                sourceCenter -
                designCenter) <
            Math.Max(
                2d,
                sourceRegion.Width *
                0.20))
        {
            diagnostics =
                new RibbonMirrorPairRecoveryDiagnostics(
                    "centred-region",
                    0d,
                    0d,
                    0d,
                    0d,
                    0);
            return false;
        }

        LeafPetalRegion? partnerRegion = null;
        var bestAgreement = 0d;
        var bestMirrorConstant = 0d;

        foreach (var candidateRegion in regions)
        {
            if (ReferenceEquals(
                    candidateRegion,
                    sourceRegion) ||
                candidateRegion.Color !=
                    sourceRegion.Color)
            {
                continue;
            }

            if (!TryMeasurePair(
                    sourceRegion,
                    candidateRegion,
                    sourceWidth,
                    out var mirrorConstant,
                    out var agreement))
            {
                continue;
            }

            if (agreement <=
                bestAgreement)
            {
                continue;
            }

            partnerRegion =
                candidateRegion;
            bestAgreement =
                agreement;
            bestMirrorConstant =
                mirrorConstant;
        }

        if (partnerRegion is null)
        {
            diagnostics =
                new RibbonMirrorPairRecoveryDiagnostics(
                    "no-partner",
                    0d,
                    0d,
                    0d,
                    0d,
                    0);
            return false;
        }

        if (!LeafPetalArcClassifier.TryClassify(
                partnerRegion,
                sourceWidth,
                out var partnerCandidate) ||
            !CurveFillRibbonCenterlineBuilder.TryBuild(
                partnerCandidate,
                sourceWidth,
                out var partnerModel,
                out _))
        {
            diagnostics =
                new RibbonMirrorPairRecoveryDiagnostics(
                    "partner-centerline",
                    bestAgreement,
                    bestMirrorConstant,
                    0d,
                    0d,
                    0);
            return false;
        }

        var sourceSamples =
            model.Samples;
        var partnerSamples =
            partnerModel.Samples;

        if (partnerSamples.Count < 8)
        {
            diagnostics =
                new RibbonMirrorPairRecoveryDiagnostics(
                    "partner-too-short",
                    bestAgreement,
                    bestMirrorConstant,
                    0d,
                    0d,
                    partnerSamples.Count);
            return false;
        }

        var forwardError =
            EndpointError(
                sourceSamples,
                partnerSamples,
                bestMirrorConstant,
                reversePartner: false);
        var reverseError =
            EndpointError(
                sourceSamples,
                partnerSamples,
                bestMirrorConstant,
                reversePartner: true);
        var reverse =
            reverseError <
            forwardError;

        var count =
            Math.Clamp(
                Math.Max(
                    sourceSamples.Count,
                    partnerSamples.Count),
                8,
                512);
        var fused =
            new LeafPetalAxisSample[count];
        var shiftSum = 0d;
        var maximumShift = 0d;

        for (var index = 0;
             index < count;
             index++)
        {
            var t =
                index /
                (double)Math.Max(
                    1,
                    count - 1);
            var sourceSample =
                SampleAt(
                    sourceSamples,
                    t);
            var partnerT =
                reverse
                    ? 1d -
                      t
                    : t;
            var partnerSample =
                SampleAt(
                    partnerSamples,
                    partnerT);
            var mirroredPartnerX =
                bestMirrorConstant -
                partnerSample.X;
            var x =
                (sourceSample.X +
                 mirroredPartnerX) *
                0.5;
            var y =
                (sourceSample.Y +
                 partnerSample.Y) *
                0.5;
            var halfWidth =
                Math.Max(
                    0.5,
                    (sourceSample.HalfWidth +
                     partnerSample.HalfWidth) *
                    0.5);

            var sourceShift =
                Distance(
                    sourceSample.X,
                    sourceSample.Y,
                    x,
                    y);
            var partnerShift =
                Distance(
                    mirroredPartnerX,
                    partnerSample.Y,
                    x,
                    y);
            var localShift =
                Math.Max(
                    sourceShift,
                    partnerShift);

            maximumShift =
                Math.Max(
                    maximumShift,
                    localShift);
            shiftSum +=
                (sourceShift +
                 partnerShift) *
                0.5;

            fused[index] =
                new LeafPetalAxisSample(
                    x,
                    y,
                    halfWidth,
                    t);
        }

        var meanShift =
            shiftSum /
            Math.Max(
                1,
                count);

        if (meanShift >
                MaximumMeanFusionShift ||
            maximumShift >
                MaximumFusionShift)
        {
            diagnostics =
                new RibbonMirrorPairRecoveryDiagnostics(
                    "fusion-shift",
                    bestAgreement,
                    bestMirrorConstant,
                    meanShift,
                    maximumShift,
                    count);
            return false;
        }

        var terminalWindow =
            Math.Clamp(
                count /
                8,
                2,
                5);
        var firstWidth =
            fused
                .Take(
                    terminalWindow)
                .Average(sample =>
                    sample.HalfWidth);
        var lastWidth =
            fused
                .TakeLast(
                    terminalWindow)
                .Average(sample =>
                    sample.HalfWidth);

        recovered =
            new LeafPetalArcModel(
                model.Candidate,
                fused,
                ReversedForApex: false,
                BaseWidth: firstWidth,
                ApexWidth: lastWidth,
                SkeletonCoverage:
                    Math.Min(
                        1d,
                        (model.SkeletonCoverage +
                         partnerModel.SkeletonCoverage) *
                        0.5));

        diagnostics =
            new RibbonMirrorPairRecoveryDiagnostics(
                "ok",
                bestAgreement,
                bestMirrorConstant,
                meanShift,
                maximumShift,
                count);

        return true;
    }

    private static bool TryMeasurePair(
        LeafPetalRegion a,
        LeafPetalRegion b,
        int sourceWidth,
        out double mirrorConstant,
        out double agreement)
    {
        mirrorConstant = 0d;
        agreement = 0d;

        if (a.Area <= 0 ||
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
             sourceWidth *
             0.5) ==
            (bCenter <
             sourceWidth *
             0.5))
        {
            return false;
        }

        if (Math.Abs(
                a.MinY -
                b.MinY) >
                MaximumBoundsDelta ||
            Math.Abs(
                a.MaxY -
                b.MaxY) >
                MaximumBoundsDelta ||
            Math.Abs(
                a.Width -
                b.Width) >
                MaximumBoundsDelta ||
            Math.Abs(
                a.Height -
                b.Height) >
                MaximumBoundsDelta)
        {
            return false;
        }

        var areaRatio =
            Math.Min(
                a.Area,
                b.Area) /
            (double)Math.Max(
                a.Area,
                b.Area);

        if (areaRatio <
            MinimumAreaRatio)
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
        var minMaxConstant =
            left.MinX +
            right.MaxX;
        var maxMinConstant =
            left.MaxX +
            right.MinX;

        if (Math.Abs(
                minMaxConstant -
                maxMinConstant) >
            1.5)
        {
            return false;
        }

        mirrorConstant =
            (minMaxConstant +
             maxMinConstant) *
            0.5;

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
                mirrorX >=
                    sourceWidth)
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

    private static double EndpointError(
        IReadOnlyList<LeafPetalAxisSample> source,
        IReadOnlyList<LeafPetalAxisSample> partner,
        double mirrorConstant,
        bool reversePartner)
    {
        var partnerFirst =
            reversePartner
                ? partner[^1]
                : partner[0];
        var partnerLast =
            reversePartner
                ? partner[0]
                : partner[^1];

        return
            Distance(
                source[0].X,
                source[0].Y,
                mirrorConstant -
                    partnerFirst.X,
                partnerFirst.Y) +
            Distance(
                source[^1].X,
                source[^1].Y,
                mirrorConstant -
                    partnerLast.X,
                partnerLast.Y);
    }

    private static LeafPetalAxisSample SampleAt(
        IReadOnlyList<LeafPetalAxisSample> samples,
        double t)
    {
        if (samples.Count == 1)
            return samples[0];

        var position =
            Math.Clamp(
                t,
                0d,
                1d) *
            (samples.Count -
             1);
        var left =
            Math.Clamp(
                (int)Math.Floor(
                    position),
                0,
                samples.Count - 1);
        var right =
            Math.Min(
                samples.Count - 1,
                left + 1);
        var local =
            position -
            left;
        var a =
            samples[left];
        var b =
            samples[right];

        return new LeafPetalAxisSample(
            a.X +
            (b.X -
             a.X) *
            local,
            a.Y +
            (b.Y -
             a.Y) *
            local,
            Math.Max(
                0.5,
                a.HalfWidth +
                (b.HalfWidth -
                 a.HalfWidth) *
                local),
            t);
    }

    private static double Distance(
        double ax,
        double ay,
        double bx,
        double by)
    {
        var dx =
            ax -
            bx;
        var dy =
            ay -
            by;

        return Math.Sqrt(
            dx *
                dx +
            dy *
                dy);
    }
}

internal readonly record struct RibbonMirrorPairRecoveryDiagnostics(
    string Reason,
    double MirrorAgreement,
    double MirrorConstant,
    double MeanFusionShift,
    double MaximumFusionShift,
    int FusedSamples);
