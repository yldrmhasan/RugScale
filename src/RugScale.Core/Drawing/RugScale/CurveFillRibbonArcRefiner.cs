using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Geometric post-refiner for long filled carpet arcs/ribbons.
///
/// CurveFill's signed-distance reconstruction is deliberately conservative and preserves every
/// indexed region, but a long 3-6px designer ribbon can still inherit the source raster's tiny
/// staircase phase when enlarged. This pass is narrower and more aesthetic: it only accepts
/// elongated filled regions whose two ends have comparable thickness, whose centreline has one
/// coherent bend, and whose width profile is stable. Those regions are rebuilt as a smooth
/// centreline plus two parallel boundaries.
///
/// Tapered leaves/petals are intentionally excluded here; they remain owned by LeafPetalArcs.
/// Straight borders are excluded by the minimum-bend gate. Protected Curve/Pixel-Cord palette
/// roles are never painted over.
/// </summary>
internal static class CurveFillRibbonArcRefiner
{
    private const int MaximumRefinedRegions = 160;
    private const double MinimumRibbonElongation = 2.00;
    private const double MinimumBroadArchElongation = 1.25;
    private const double MaximumBroadArchBoundingFill = 0.28;
    private const double MaximumBroadArchBoundaryRatio = 0.58;
    private const double MaximumBoundaryRatio = 0.66;
    private const double MaximumWidthCoefficientVariation = 0.55;
    private const double MinimumTerminalWidthRatio = 0.45;
    private const double MinimumAbsoluteBend = 1.35;
    private const double MinimumRelativeBend = 0.035;

    public static int Apply(
        DesignDocument source,
        DesignDocument destination) =>
        ApplyWithDiagnostics(
            source,
            destination)
        .BoundaryPixelsChanged;

    internal static RibbonArcRefinementDiagnostics ApplyWithDiagnostics(
        DesignDocument source,
        DesignDocument destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Width == destination.Width &&
            source.Height == destination.Height)
        {
            return default;
        }

        var protectedStrokeColors =
            ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                source);
        var regions =
            LeafPetalRegionExtractor.Extract(
                source);

        var classified = 0;
        var axisBuilt = 0;
        var maxSkeletonPixels = 0;
        var maxEndpoints = 0;
        var maxPrincipalPathPixels = 0;
        var maxPrincipalPathCoverage = 0d;
        var lastCenterlineReason = "not-attempted";
        var ribbonGeometryAccepted = 0;
        var fitSafe = 0;
        var curveToolFits = 0;
        var cubicBezierFits = 0;
        var maxFitDeviation = 0d;
        var maxFitCurvatureFlips = 0;
        var accepted =
            new List<(LeafPetalArcModel Model, ElegantArcFit Fit)>();

        foreach (var region in regions)
        {
            if (protectedStrokeColors.Contains(
                    region.Color))
            {
                continue;
            }

            if (!LeafPetalArcClassifier.TryClassify(
                    region,
                    source.Width,
                    out var candidate))
            {
                continue;
            }

            classified++;

            var boundingFillRatio =
                region.Area /
                (double)Math.Max(
                    1,
                    region.Width *
                    region.Height);
            var broadSparseArch =
                candidate.Elongation >=
                    MinimumBroadArchElongation &&
                boundingFillRatio <=
                    MaximumBroadArchBoundingFill &&
                candidate.BoundaryRatio <=
                    MaximumBroadArchBoundaryRatio;

            // A wide U/half-oval has poor PCA elongation because its two shoulders spread across
            // both axes, even though visually it is one long ribbon. Admit only sparse broad
            // arches here; the medial-skeleton, stable-width, bend and no-inflection gates below
            // remain the actual redraw authority.
            if ((candidate.Elongation <
                     MinimumRibbonElongation &&
                 !broadSparseArch) ||
                candidate.BoundaryRatio >
                    MaximumBoundaryRatio)
            {
                continue;
            }

            var centerlineBuilt =
                CurveFillRibbonCenterlineBuilder.TryBuild(
                    candidate,
                    source.Width,
                    out var model,
                    out var centerlineDiagnostics);

            maxSkeletonPixels =
                Math.Max(
                    maxSkeletonPixels,
                    centerlineDiagnostics.SkeletonPixels);
            maxEndpoints =
                Math.Max(
                    maxEndpoints,
                    centerlineDiagnostics.Endpoints);
            maxPrincipalPathPixels =
                Math.Max(
                    maxPrincipalPathPixels,
                    centerlineDiagnostics.PrincipalPathPixels);
            maxPrincipalPathCoverage =
                Math.Max(
                    maxPrincipalPathCoverage,
                    centerlineDiagnostics.PrincipalPathCoverage);
            lastCenterlineReason =
                centerlineDiagnostics.Reason;

            if (!centerlineBuilt)
                continue;

            axisBuilt++;

            if (!LooksLikeDesignerRibbon(
                    model))
            {
                continue;
            }

            ribbonGeometryAccepted++;

            // Ribbon geometry is deliberately NOT apex-tapered. The source width profile already
            // contains the designer's constant/slowly-varying band thickness.
            ElegantArcFit fit;

            if (CurveFillRibbonToolFitter.TryFit(
                    model,
                    out var toolFit,
                    out _))
            {
                fit =
                    toolFit;
                curveToolFits++;
            }
            else if (CurveFillRibbonBezierFitter.TryFit(
                         model,
                         out var bezierFit))
            {
                // A single cubic removes the last source-staircase phase from simple oval/arch
                // ribbons while staying source-bounded. Complex or inflected shapes still fall
                // through to the conservative multi-anchor fitter below.
                fit =
                    bezierFit;
                cubicBezierFits++;
            }
            else
            {
                fit =
                    ElegantArcFitter.Fit(
                        model,
                        taperApex: false,
                        maximumAnchors: 8,
                        smoothingPasses: 2);
            }

            maxFitDeviation =
                Math.Max(
                    maxFitDeviation,
                    fit.MaximumCenterlineDeviation);
            maxFitCurvatureFlips =
                Math.Max(
                    maxFitCurvatureFlips,
                    fit.CurvatureSignFlips);

            if (!fit.IsSafe ||
                fit.CurvatureSignFlips > 1)
            {
                continue;
            }

            fitSafe++;

            accepted.Add(
                (model, fit));
        }

        var changed = 0;
        var outlinedRefined = 0;

        foreach (var item in accepted
                     .OrderByDescending(pair =>
                         pair.Model.Candidate.MajorExtent *
                         pair.Model.Candidate.Elongation)
                     .Take(MaximumRefinedRegions))
        {
            if (CurveFillOutlinedRibbonRasterizer.TryApply(
                    source,
                    destination,
                    item.Model,
                    item.Fit,
                    protectedStrokeColors,
                    out var outlinedChanged))
            {
                outlinedRefined++;
                changed +=
                    outlinedChanged;
                continue;
            }

            changed +=
                LeafPetalArcRasterizer.Apply(
                    source,
                    destination,
                    item.Model,
                    item.Fit,
                    protectedStrokeColors);
        }

        return new RibbonArcRefinementDiagnostics(
            Regions: regions.Count,
            Classified: classified,
            AxisBuilt: axisBuilt,
            MaxSkeletonPixels: maxSkeletonPixels,
            MaxEndpoints: maxEndpoints,
            MaxPrincipalPathPixels: maxPrincipalPathPixels,
            MaxPrincipalPathCoverage: maxPrincipalPathCoverage,
            LastCenterlineReason: lastCenterlineReason,
            RibbonGeometryAccepted: ribbonGeometryAccepted,
            FitSafe: fitSafe,
            CurveToolFits: curveToolFits,
            CubicBezierFits: cubicBezierFits,
            MaxFitDeviation: maxFitDeviation,
            MaxFitCurvatureFlips: maxFitCurvatureFlips,
            Refined: accepted.Count,
            OutlinedRefined: outlinedRefined,
            BoundaryPixelsChanged: changed);
    }

    private static bool LooksLikeDesignerRibbon(
        LeafPetalArcModel model)
    {
        var samples =
            model.Samples;

        if (samples.Count < 8)
            return false;

        var widths =
            samples
                .Select(sample =>
                    Math.Max(
                        0.45,
                        sample.HalfWidth))
                .ToArray();
        var meanWidth =
            widths.Average();

        if (meanWidth <= 0.60)
            return false;

        var variance =
            widths.Average(width =>
            {
                var delta =
                    width -
                    meanWidth;

                return delta *
                       delta;
            });
        var coefficientVariation =
            Math.Sqrt(
                variance) /
            meanWidth;

        if (coefficientVariation >
            MaximumWidthCoefficientVariation)
        {
            return false;
        }

        var terminalWindow =
            Math.Clamp(
                samples.Count /
                8,
                2,
                5);
        var firstWidth =
            widths
                .Take(
                    terminalWindow)
                .Average();
        var lastWidth =
            widths
                .TakeLast(
                    terminalWindow)
                .Average();
        var terminalRatio =
            Math.Min(
                firstWidth,
                lastWidth) /
            Math.Max(
                firstWidth,
                lastWidth);

        // A leaf has a broad base and a narrow apex. A ribbon/oval border keeps comparable width
        // at both ends, even if the raster quantizes one end by a pixel.
        if (terminalRatio <
            MinimumTerminalWidthRatio)
        {
            return false;
        }

        var first =
            samples[0];
        var last =
            samples[^1];
        var chordX =
            last.X -
            first.X;
        var chordY =
            last.Y -
            first.Y;
        var chordLength =
            Math.Sqrt(
                chordX *
                chordX +
                chordY *
                chordY);

        if (chordLength <= 1e-9)
            return false;

        var maximumBend = 0d;

        foreach (var sample in samples)
        {
            var cross =
                Math.Abs(
                    chordX *
                    (sample.Y - first.Y) -
                    chordY *
                    (sample.X - first.X));
            var distance =
                cross /
                chordLength;

            maximumBend =
                Math.Max(
                    maximumBend,
                    distance);
        }

        var requiredBend =
            Math.Max(
                MinimumAbsoluteBend,
                model.Candidate.MajorExtent *
                MinimumRelativeBend);

        return maximumBend >=
               requiredBend;
    }
}


internal readonly record struct RibbonArcRefinementDiagnostics(
    int Regions,
    int Classified,
    int AxisBuilt,
    int MaxSkeletonPixels,
    int MaxEndpoints,
    int MaxPrincipalPathPixels,
    double MaxPrincipalPathCoverage,
    string LastCenterlineReason,
    int RibbonGeometryAccepted,
    int FitSafe,
    int CurveToolFits,
    int CubicBezierFits,
    double MaxFitDeviation,
    int MaxFitCurvatureFlips,
    int Refined,
    int OutlinedRefined,
    int BoundaryPixelsChanged);
