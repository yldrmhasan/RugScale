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
    private const double MinimumSparseTaperTerminalWidthRatio = 0.25;
    private const double MinimumSparseTaperElongation = 2.50;
    private const double MaximumSparseTaperBoundingFill = 0.20;
    private const double MinimumSparseTaperPathCoverage = 0.64;
    private const double MinimumCompactSpiralTerminalWidthRatio = 0.44;
    private const double MinimumCompactSpiralElongation = 1.35;
    private const double MaximumCompactSpiralElongation = 1.80;
    private const double MinimumCompactSpiralBoundingFill = 0.42;
    private const double MaximumCompactSpiralBoundingFill = 0.56;
    private const double MaximumCompactSpiralBoundaryRatio = 0.25;
    private const double MinimumCompactSpiralPathCoverage = 0.84;
    private const double MaximumCompactSpiralWidthCoefficientVariation = 0.54;
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
        var symmetryRecoveries = 0;
        var mainArcExtractions = 0;
        var maxSymmetrySampleShift = 0d;
        var mirrorSourceFusions = 0;
        var bestMirrorSourceAgreement = 0d;
        var maxMirrorSourceFusionShift = 0d;
        var compoundFits = 0;
        var compoundAttempts = 0;
        var maxCompoundP95Deviation = 0d;
        var maxCompoundDeviation = 0d;
        var lastCompoundReason = "not-attempted";
        var curveToolFits = 0;
        var curveToolThroughPointsFits = 0;
        var curveToolSplineFits = 0;
        var curveToolBezierFits = 0;
        var curveToolRoundnessSum = 0d;
        var geometricThroughFits = 0;
        var geometricThroughAttempts = 0;
        var maxGeometricThroughDeviation = 0d;
        var maxGeometricThroughP95Deviation = 0d;
        var lastGeometricThroughReason = "not-attempted";
        var geometricThroughRoundnessSum = 0d;
        var broadOvalFits = 0;
        var broadOvalAttempts = 0;
        var maxBroadOvalDeviation = 0d;
        var maxBroadOvalP95Deviation = 0d;
        var lastBroadOvalReason = "not-attempted";
        var cubicBezierFits = 0;
        var cubicAttempts = 0;
        var maxCubicDeviation = 0d;
        var maxCubicP95Deviation = 0d;
        var maxCubicHandleRatio = 0d;
        var lastCubicReason = "not-attempted";
        var maxFitDeviation = 0d;
        var maxFitCurvatureFlips = 0;
        var widthRegularized = 0;
        var maxWidthRegularizationShift = 0d;
        var maxWidthVariationReduction = 0d;
        var selfSymmetryNormalizations = 0;
        var bestSelfSymmetryAgreement = 0d;
        var maxSelfSymmetryDeviation = 0d;
        var accepted =
            new List<(LeafPetalArcModel Model, ElegantArcFit Fit)>();
        var mainArcScopedRegions =
            new HashSet<LeafPetalRegion>();
        var compactSpiralRegions =
            new HashSet<LeafPetalRegion>();

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
            var compactSpiralPrefilter =
                candidate.Elongation >=
                    1.25 &&
                boundingFillRatio <=
                    0.58 &&
                candidate.BoundaryRatio <=
                    0.32;

            // A wide U/half-oval has poor PCA elongation because its two shoulders spread across
            // both axes, even though visually it is one long ribbon. Admit only sparse broad
            // arches here; the medial-skeleton, stable-width, bend and no-inflection gates below
            // remain the actual redraw authority.
            if ((candidate.Elongation <
                     MinimumRibbonElongation &&
                 !broadSparseArch &&
                 !compactSpiralPrefilter) ||
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
                    model,
                    out var ribbonShapeDiagnostics))
            {
                continue;
            }

            var sparseTaperSweep =
                string.Equals(
                    ribbonShapeDiagnostics.Reason,
                    "ok-sparse-taper",
                    StringComparison.Ordinal);
            var compactSpiralSweep =
                string.Equals(
                    ribbonShapeDiagnostics.Reason,
                    "ok-compact-spiral",
                    StringComparison.Ordinal);

            // A compact prefilter is only an analysis doorway. Production authority is granted
            // exclusively by the strict ok-compact-spiral classifier after centerline evidence.
            if (compactSpiralPrefilter &&
                !compactSpiralSweep &&
                candidate.Elongation <
                    MinimumRibbonElongation &&
                !broadSparseArch)
            {
                continue;
            }

            var mainArcExtractedForRegion = false;

            if (broadSparseArch &&
                centerlineDiagnostics.Endpoints > 2 &&
                CurveFillRibbonSymmetryRecovery.TryRecover(
                    model,
                    source.Width,
                    out var symmetricModel,
                    out var symmetryDiagnostics))
            {
                model =
                    symmetricModel;
                symmetryRecoveries++;
                maxSymmetrySampleShift =
                    Math.Max(
                        maxSymmetrySampleShift,
                        symmetryDiagnostics.MaximumSampleShift);

                if (CurveFillRibbonMainArcExtractor.TryExtract(
                        model,
                        out var mainArcModel,
                        out _))
                {
                    model =
                        mainArcModel;
                    mainArcExtractedForRegion = true;
                    mainArcExtractions++;
                }
            }

            var mirrorSourceFused = false;

            if (broadSparseArch &&
                CurveFillRibbonMirrorPairRecovery.TryRecover(
                    model,
                    regions,
                    source.Width,
                    out var mirrorFusedModel,
                    out var mirrorFusionDiagnostics))
            {
                model =
                    mirrorFusedModel;
                mirrorSourceFused = true;
                mirrorSourceFusions++;
                bestMirrorSourceAgreement =
                    Math.Max(
                        bestMirrorSourceAgreement,
                        mirrorFusionDiagnostics.MirrorAgreement);
                maxMirrorSourceFusionShift =
                    Math.Max(
                        maxMirrorSourceFusionShift,
                        mirrorFusionDiagnostics.MaximumFusionShift);

                // Mirror-pair fusion is another high-confidence way to suppress one-sided raster
                // phase. Multi-endpoint hooked ribbons may fail self-symmetry recovery yet still
                // have an obvious dominant designer sweep. Extract that sweep AFTER fusion so the
                // main arc can be fitted by the normal Curve-tool family while terminal hooks stay
                // on the categorical baseline.
                if (!mainArcExtractedForRegion &&
                    centerlineDiagnostics.Endpoints > 2 &&
                    CurveFillRibbonMainArcExtractor.TryExtractMirrorFusedSweep(
                        model,
                        out var mirrorMainArcModel,
                        out _))
                {
                    model =
                        mirrorMainArcModel;
                    mainArcExtractedForRegion = true;
                    mainArcExtractions++;
                }
            }

            // Sparse tapered ornaments may have several skeleton endpoints because decorative
            // hooks/branches share the same indexed region. Compound fitting the complete graph can
            // shave valid side structure. Give this class compound authority only after extracting
            // one coherent one-sided sweep (unless it was already a simple two-endpoint path).
            if (sparseTaperSweep &&
                !mainArcExtractedForRegion &&
                centerlineDiagnostics.Endpoints > 2 &&
                CurveFillRibbonMainArcExtractor.TryExtractSparseTaperSweep(
                    model,
                    out var sparseTaperMainArcModel,
                    out _))
            {
                model =
                    sparseTaperMainArcModel;
                mainArcExtractedForRegion = true;
                mainArcExtractions++;
            }

            var sparseTaperCompoundAuthority =
                sparseTaperSweep &&
                (centerlineDiagnostics.Endpoints <= 2 ||
                 mainArcExtractedForRegion);

            ribbonGeometryAccepted++;

            // Ribbon geometry is deliberately NOT apex-tapered. The source width profile already
            // contains the designer's constant/slowly-varying band thickness.
            ElegantArcFit fit;
            var compoundFitSelected = false;

            if (compactSpiralSweep)
            {
                // Compact spirals are complete multi-turn ribbons. The real-raster probe proved
                // generic macro splines unsafe, while the low-frequency compound fitter stayed
                // source-bounded with zero curvature flips. Do not fall back to a generic model:
                // if compound fitting becomes unsafe, leave the categorical baseline untouched.
                compoundAttempts++;

                if (!CurveFillRibbonCompoundFitter.TryFit(
                        model,
                        out var compactCompoundFit,
                        out var compactCompoundDiagnostics))
                {
                    lastCompoundReason =
                        compactCompoundDiagnostics.Reason;
                    maxCompoundDeviation =
                        Math.Max(
                            maxCompoundDeviation,
                            compactCompoundDiagnostics.MaximumDeviation);
                    maxCompoundP95Deviation =
                        Math.Max(
                            maxCompoundP95Deviation,
                            compactCompoundDiagnostics.Percentile95Deviation);
                    continue;
                }

                fit =
                    compactCompoundFit;
                compoundFitSelected = true;
                compoundFits++;
                lastCompoundReason =
                    compactCompoundDiagnostics.Reason;
                maxCompoundDeviation =
                    Math.Max(
                        maxCompoundDeviation,
                        compactCompoundDiagnostics.MaximumDeviation);
                maxCompoundP95Deviation =
                    Math.Max(
                        maxCompoundP95Deviation,
                        compactCompoundDiagnostics.Percentile95Deviation);
            }
            else if (CurveFillRibbonToolFitter.TryFit(
                    model,
                    out var toolFit,
                    out var toolStyle))
            {
                fit =
                    toolFit;
                var selectedToolFit = true;

                // Once a hooked/broad region has been reduced to its dominant designer sweep,
                // compare the source-faithful inverse tool fit with a fixed 5-control
                // Through-Points reconstruction. The alternative may win only when it is
                // substantially smoother AND remains source-bounded; this is the aesthetic
                // correction for the C069 green/gold arches, not a general smoothing override.
                if (broadSparseArch &&
                    mainArcExtractedForRegion)
                {
                    geometricThroughAttempts++;

                    var alternativeSafe =
                        CurveFillRibbonThroughPointsFitter.TryFit(
                            model,
                            out var alternativeFit,
                            out var alternativeDiagnostics);

                    lastGeometricThroughReason =
                        alternativeDiagnostics.Reason;
                    maxGeometricThroughDeviation =
                        Math.Max(
                            maxGeometricThroughDeviation,
                            alternativeDiagnostics.MaximumDeviation);
                    maxGeometricThroughP95Deviation =
                        Math.Max(
                            maxGeometricThroughP95Deviation,
                            alternativeDiagnostics.Percentile95Deviation);

                    if (alternativeSafe)
                    {
                        var toolRoughness =
                            CurveFillRibbonSmoothness.Measure(
                                toolFit.Points);
                        var alternativeRoughness =
                            CurveFillRibbonSmoothness.Measure(
                                alternativeFit.Points);

                        if (CurveFillRibbonFitSelector.PreferGeometricThroughPoints(
                                toolFit,
                                toolRoughness,
                                alternativeFit,
                                alternativeRoughness))
                        {
                            fit =
                                alternativeFit;
                            selectedToolFit = false;
                            geometricThroughFits++;
                            geometricThroughRoundnessSum +=
                                alternativeDiagnostics.Roundness;
                        }
                    }
                }

                if (selectedToolFit)
                {
                    curveToolFits++;
                    curveToolRoundnessSum +=
                        toolStyle.Roundness;

                    switch (toolStyle.Type)
                    {
                        case CurveType.SplineThroughPoints:
                            curveToolThroughPointsFits++;
                            break;
                        case CurveType.Spline:
                            curveToolSplineFits++;
                            break;
                        case CurveType.Bezier:
                            curveToolBezierFits++;
                            break;
                    }
                }
            }
            else if (broadSparseArch)
            {
                geometricThroughAttempts++;

                if (CurveFillRibbonThroughPointsFitter.TryFit(
                        model,
                        out var throughFit,
                        out var throughDiagnostics))
                {
                    fit =
                        throughFit;
                    geometricThroughFits++;
                    geometricThroughRoundnessSum +=
                        throughDiagnostics.Roundness;
                }
                else
                {
                    broadOvalAttempts++;

                    if (CurveFillBroadOvalArcFitter.TryFit(
                            model,
                            out var ovalFit,
                            out var ovalDiagnostics))
                    {
                        fit =
                            ovalFit;
                        broadOvalFits++;
                    }
                    else
                    {
                        cubicAttempts++;

                        if (CurveFillRibbonBezierFitter.TryFit(
                                model,
                                out var bezierFit,
                                out var cubicDiagnostics))
                        {
                            fit =
                                bezierFit;
                            cubicBezierFits++;
                        }
                        else if (mirrorSourceFused ||
                                 sparseTaperCompoundAuthority)
                        {
                            // Mirror-fused source remains the strongest authority. A second,
                            // deliberately narrow authority is the sparse-taper classifier:
                            // elongated, low-fill, high-coverage sweeps whose only reason for not
                            // looking like a constant-width ribbon is a designer taper. These are
                            // exactly the C069 long ornamental curls that previously reached an
                            // unsafe macro spline and were therefore left block-scaled.
                            compoundAttempts++;

                            if (CurveFillRibbonCompoundFitter.TryFit(
                                    model,
                                    out var compoundFit,
                                    out var compoundDiagnostics))
                            {
                                fit =
                                    compoundFit;
                                compoundFitSelected = true;
                                compoundFits++;
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

                            lastCompoundReason =
                                compoundDiagnostics.Reason;
                            maxCompoundDeviation =
                                Math.Max(
                                    maxCompoundDeviation,
                                    compoundDiagnostics.MaximumDeviation);
                            maxCompoundP95Deviation =
                                Math.Max(
                                    maxCompoundP95Deviation,
                                    compoundDiagnostics.Percentile95Deviation);
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

                        lastCubicReason =
                            cubicDiagnostics.Reason;
                        maxCubicDeviation =
                            Math.Max(
                                maxCubicDeviation,
                                cubicDiagnostics.MaximumDeviation);
                        maxCubicP95Deviation =
                            Math.Max(
                                maxCubicP95Deviation,
                                cubicDiagnostics.Percentile95Deviation);
                        maxCubicHandleRatio =
                            Math.Max(
                                maxCubicHandleRatio,
                                cubicDiagnostics.MaximumHandleToChordRatio);
                    }

                    lastBroadOvalReason =
                        ovalDiagnostics.Reason;
                    maxBroadOvalDeviation =
                        Math.Max(
                            maxBroadOvalDeviation,
                            ovalDiagnostics.MaximumDeviation);
                    maxBroadOvalP95Deviation =
                        Math.Max(
                            maxBroadOvalP95Deviation,
                            ovalDiagnostics.Percentile95Deviation);
                }

                lastGeometricThroughReason =
                    throughDiagnostics.Reason;
                maxGeometricThroughDeviation =
                    Math.Max(
                        maxGeometricThroughDeviation,
                        throughDiagnostics.MaximumDeviation);
                maxGeometricThroughP95Deviation =
                    Math.Max(
                        maxGeometricThroughP95Deviation,
                        throughDiagnostics.Percentile95Deviation);
            }
            else
            {
                cubicAttempts++;

                if (CurveFillRibbonBezierFitter.TryFit(
                        model,
                        out var bezierFit,
                        out var cubicDiagnostics))
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

                lastCubicReason =
                    cubicDiagnostics.Reason;
                maxCubicDeviation =
                    Math.Max(
                        maxCubicDeviation,
                        cubicDiagnostics.MaximumDeviation);
                maxCubicP95Deviation =
                    Math.Max(
                        maxCubicP95Deviation,
                        cubicDiagnostics.Percentile95Deviation);
                maxCubicHandleRatio =
                    Math.Max(
                        maxCubicHandleRatio,
                        cubicDiagnostics.MaximumHandleToChordRatio);
            }

            maxFitDeviation =
                Math.Max(
                    maxFitDeviation,
                    fit.MaximumCenterlineDeviation);
            maxFitCurvatureFlips =
                Math.Max(
                    maxFitCurvatureFlips,
                    fit.CurvatureSignFlips);

            var maximumAllowedCurvatureFlips =
                compoundFitSelected
                    ? 2
                    : 1;

            if (!fit.IsSafe ||
                fit.CurvatureSignFlips >
                    maximumAllowedCurvatureFlips)
            {
                continue;
            }

            fit =
                CurveFillRibbonWidthProfileRegularizer.Regularize(
                    model,
                    fit,
                    out var widthDiagnostics);

            if (widthDiagnostics.Applied)
            {
                widthRegularized++;
                maxWidthRegularizationShift =
                    Math.Max(
                        maxWidthRegularizationShift,
                        widthDiagnostics.MaximumHalfWidthShift);
                maxWidthVariationReduction =
                    Math.Max(
                        maxWidthVariationReduction,
                        widthDiagnostics.BeforeAdjacentVariation -
                        widthDiagnostics.AfterAdjacentVariation);
            }

            if (CurveFillRibbonSelfSymmetryNormalizer.TryNormalize(
                    model,
                    fit,
                    source.Width,
                    out var symmetricFit,
                    out var selfSymmetryDiagnostics))
            {
                fit =
                    symmetricFit;
                selfSymmetryNormalizations++;
                bestSelfSymmetryAgreement =
                    Math.Max(
                        bestSelfSymmetryAgreement,
                        selfSymmetryDiagnostics.SourceMirrorAgreement);
                maxSelfSymmetryDeviation =
                    Math.Max(
                        maxSelfSymmetryDeviation,
                        selfSymmetryDiagnostics.MaximumDeviation);
            }

            fitSafe++;

            accepted.Add(
                (model, fit));

            if (compactSpiralSweep)
            {
                compactSpiralRegions.Add(
                    model.Candidate.Region);
            }

            if (mainArcExtractedForRegion)
            {
                mainArcScopedRegions.Add(
                    model.Candidate.Region);
            }
        }

        var mirrorPairDiagnostics =
            CurveFillRibbonMirrorPairNormalizer.Normalize(
                accepted,
                source.Width,
                mainArcScopedRegions);

        var changed = 0;
        var outlinedRefined = 0;

        foreach (var item in accepted
                     .OrderByDescending(pair =>
                         pair.Model.Candidate.MajorExtent *
                         pair.Model.Candidate.Elongation)
                     .Take(MaximumRefinedRegions))
        {
            var scopedMainArc =
                mainArcScopedRegions.Contains(
                    item.Model.Candidate.Region);

            // This is the first genuinely authoritative redraw path in the ribbon pipeline.
            // Ordinary safe fits receive it only when their centreline stays within a very tight
            // source corridor and has no curvature reversal. Extracted main arcs are separately
            // source-scoped, so they may use the same redraw even when a compound fit needs a
            // wider deviation budget.
            var trueRedrawAuthority =
                scopedMainArc ||
                compactSpiralRegions.Contains(
                    item.Model.Candidate.Region) ||
                item.Fit.CurvatureSignFlips == 0 &&
                item.Fit.MaximumCenterlineDeviation <=
                    1.75;

            if (trueRedrawAuthority &&
                CurveFillTrueRibbonRasterizer.TryApply(
                    source,
                    destination,
                    item.Model,
                    item.Fit,
                    protectedStrokeColors,
                    out var trueRedrawChanged))
            {
                outlinedRefined++;
                changed +=
                    trueRedrawChanged;
                continue;
            }

            if (CurveFillOutlinedRibbonRasterizer.TryApply(
                    source,
                    destination,
                    item.Model,
                    item.Fit,
                    protectedStrokeColors,
                    out var outlinedChanged,
                    restrictToFittedSweep:
                        scopedMainArc))
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
                    protectedStrokeColors,
                    restrictToFittedSweep:
                        scopedMainArc);
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
            SymmetryRecoveries: symmetryRecoveries,
            MainArcExtractions: mainArcExtractions,
            MaxSymmetrySampleShift: maxSymmetrySampleShift,
            MirrorSourceFusions: mirrorSourceFusions,
            BestMirrorSourceAgreement: bestMirrorSourceAgreement,
            MaxMirrorSourceFusionShift: maxMirrorSourceFusionShift,
            CompoundFits: compoundFits,
            CompoundAttempts: compoundAttempts,
            MaxCompoundP95Deviation: maxCompoundP95Deviation,
            MaxCompoundDeviation: maxCompoundDeviation,
            LastCompoundReason: lastCompoundReason,
            CurveToolFits: curveToolFits,
            CurveToolThroughPointsFits: curveToolThroughPointsFits,
            CurveToolSplineFits: curveToolSplineFits,
            CurveToolBezierFits: curveToolBezierFits,
            MeanCurveToolRoundness:
                curveToolFits == 0
                    ? 0d
                    : curveToolRoundnessSum /
                      curveToolFits,
            GeometricThroughFits: geometricThroughFits,
            GeometricThroughAttempts: geometricThroughAttempts,
            MaxGeometricThroughDeviation: maxGeometricThroughDeviation,
            MaxGeometricThroughP95Deviation: maxGeometricThroughP95Deviation,
            LastGeometricThroughReason: lastGeometricThroughReason,
            MeanGeometricThroughRoundness:
                geometricThroughFits == 0
                    ? 0d
                    : geometricThroughRoundnessSum /
                      geometricThroughFits,
            BroadOvalFits: broadOvalFits,
            BroadOvalAttempts: broadOvalAttempts,
            MaxBroadOvalDeviation: maxBroadOvalDeviation,
            MaxBroadOvalP95Deviation: maxBroadOvalP95Deviation,
            LastBroadOvalReason: lastBroadOvalReason,
            CubicBezierFits: cubicBezierFits,
            CubicAttempts: cubicAttempts,
            MaxCubicDeviation: maxCubicDeviation,
            MaxCubicP95Deviation: maxCubicP95Deviation,
            MaxCubicHandleRatio: maxCubicHandleRatio,
            LastCubicReason: lastCubicReason,
            MaxFitDeviation: maxFitDeviation,
            MaxFitCurvatureFlips: maxFitCurvatureFlips,
            WidthRegularized: widthRegularized,
            MaxWidthRegularizationShift: maxWidthRegularizationShift,
            MaxWidthVariationReduction: maxWidthVariationReduction,
            SelfSymmetryNormalizations: selfSymmetryNormalizations,
            BestSelfSymmetryAgreement: bestSelfSymmetryAgreement,
            MaxSelfSymmetryDeviation: maxSelfSymmetryDeviation,
            MirrorPairs: mirrorPairDiagnostics.Pairs,
            MirrorPairReplacements: mirrorPairDiagnostics.Replacements,
            BestMirrorPairAgreement: mirrorPairDiagnostics.BestMirrorAgreement,
            MaxMirrorPairDeviation: mirrorPairDiagnostics.MaximumMirroredDeviation,
            Refined: accepted.Count,
            OutlinedRefined: outlinedRefined,
            BoundaryPixelsChanged: changed);
    }

    internal static IReadOnlyList<RibbonArcCandidateStage> AnalyzeCandidateStages(
        DesignDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var protectedStrokeColors =
            ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                source);
        var regions =
            LeafPetalRegionExtractor.Extract(
                source);
        var result =
            new List<RibbonArcCandidateStage>();

        foreach (var region in regions)
        {
            if (protectedStrokeColors.Contains(
                    region.Color) ||
                !LeafPetalArcClassifier.TryClassify(
                    region,
                    source.Width,
                    out var candidate))
            {
                continue;
            }

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
            var prefilterAccepted =
                (candidate.Elongation >=
                     MinimumRibbonElongation ||
                 broadSparseArch) &&
                candidate.BoundaryRatio <=
                    MaximumBoundaryRatio;

            // Diagnostic-only probe for compact spiral ribbons. These shapes can have low PCA
            // elongation because the curve wraps around itself, even though the medial path is a
            // genuine long designer sweep. Do NOT grant production redraw authority here yet;
            // first collect width/terminal/bend/fitter evidence in the real C069 audit.
            var compactSpiralProbe =
                !prefilterAccepted &&
                candidate.Elongation >=
                    1.25 &&
                boundingFillRatio <=
                    0.58 &&
                candidate.BoundaryRatio <=
                    0.32;
            var analysisEligible =
                prefilterAccepted ||
                compactSpiralProbe;

            var centerlineBuilt = false;
            var designerRibbon = false;
            var symmetryRecovered = false;
            var symmetryAxis = "none";
            var mirrorAgreement = 0d;
            var symmetryMeanShift = 0d;
            var symmetryMaxShift = 0d;
            var mirrorSourceRecovered = false;
            var mirrorSourceReason = "not-attempted";
            var mirrorSourceAgreement = 0d;
            var mirrorSourceMeanShift = 0d;
            var mirrorSourceMaxShift = 0d;
            var mainArcExtracted = false;
            var mainArcReason = "not-attempted";
            var mainArcStart = 0;
            var mainArcEnd = 0;
            var mainArcKeptFraction = 0d;
            var fitSafe = false;
            var accepted = false;
            var compoundAttempted = false;
            var compoundReason = "not-attempted";
            var compoundP95Deviation = 0d;
            var compoundMaximumDeviation = 0d;
            var compoundSelectedRoughness = 0d;
            var compoundSelectedAnchors = 0;
            var compoundSelectedSmoothingPasses = 0;
            var compoundSmoothestSafeRoughness = 0d;
            var compoundSmoothestSafeAnchors = 0;
            var compoundSmoothestSafeSmoothingPasses = 0;
            var compoundSmoothestSafeP95Deviation = 0d;
            var compoundSmoothestSafeMaximumDeviation = 0d;
            var compoundSmoothestCurvatureValidRoughness = 0d;
            var compoundSmoothestCurvatureValidAnchors = 0;
            var compoundSmoothestCurvatureValidSmoothingPasses = 0;
            var compoundSmoothestCurvatureValidP95Deviation = 0d;
            var compoundSmoothestCurvatureValidMaximumDeviation = 0d;
            var fitKind = "none";
            var curveFamily = "none";
            var status =
                prefilterAccepted
                    ? "centerline"
                    : compactSpiralProbe
                        ? "compact-probe-centerline"
                        : "prefilter";
            var roundness = 0d;
            var maximumDeviation = 0d;
            var curvatureFlips = 0;
            var selectedSmoothness = double.PositiveInfinity;
            var alternativeThroughSafe = false;
            var alternativeThroughSmoothness = double.PositiveInfinity;
            var alternativeThroughMaximumDeviation = 0d;
            var alternativeThroughP95Deviation = 0d;
            var alternativeThroughRoundness = 0d;
            var widthRegularizerApplied = false;
            var widthSourceCoefficientVariation = 0d;
            var widthTerminalRatio = 0d;
            var widthVariationBefore = 0d;
            var widthVariationAfter = 0d;
            var widthMaximumShift = 0d;
            var skeletonPixels = 0;
            var endpoints = 0;
            var principalPathPixels = 0;
            var principalPathCoverage = 0d;
            var ribbonShapeReason = "not-attempted";
            var ribbonMeanWidth = 0d;
            var ribbonWidthCoefficientVariation = 0d;
            var ribbonTerminalRatio = 0d;
            var ribbonMaximumBend = 0d;
            var ribbonRequiredBend = 0d;

            if (analysisEligible)
            {
                centerlineBuilt =
                    CurveFillRibbonCenterlineBuilder.TryBuild(
                        candidate,
                        source.Width,
                        out var model,
                        out var centerlineDiagnostics);
                skeletonPixels =
                    centerlineDiagnostics.SkeletonPixels;
                endpoints =
                    centerlineDiagnostics.Endpoints;
                principalPathPixels =
                    centerlineDiagnostics.PrincipalPathPixels;
                principalPathCoverage =
                    centerlineDiagnostics.PrincipalPathCoverage;

                if (!centerlineBuilt)
                {
                    status =
                        compactSpiralProbe
                            ? "compact-probe-centerline-" +
                              centerlineDiagnostics.Reason
                            : "centerline-" +
                              centerlineDiagnostics.Reason;
                }
                else
                {
                    designerRibbon =
                        LooksLikeDesignerRibbon(
                            model,
                            out var ribbonShapeDiagnostics);
                    var sparseTaperSweep =
                        string.Equals(
                            ribbonShapeDiagnostics.Reason,
                            "ok-sparse-taper",
                            StringComparison.Ordinal);
                    var compactSpiralSweep =
                        string.Equals(
                            ribbonShapeDiagnostics.Reason,
                            "ok-compact-spiral",
                            StringComparison.Ordinal);
                    ribbonShapeReason =
                        ribbonShapeDiagnostics.Reason;
                    ribbonMeanWidth =
                        ribbonShapeDiagnostics.MeanWidth;
                    ribbonWidthCoefficientVariation =
                        ribbonShapeDiagnostics.WidthCoefficientVariation;
                    ribbonTerminalRatio =
                        ribbonShapeDiagnostics.TerminalRatio;
                    ribbonMaximumBend =
                        ribbonShapeDiagnostics.MaximumBend;
                    ribbonRequiredBend =
                        ribbonShapeDiagnostics.RequiredBend;

                    if (!designerRibbon)
                    {
                        status =
                            compactSpiralProbe
                                ? "compact-probe-ribbon-shape"
                                : "ribbon-shape";
                    }
                    else
                    {
                        if (broadSparseArch &&
                            centerlineDiagnostics.Endpoints > 2 &&
                            CurveFillRibbonSymmetryRecovery.TryRecover(
                                model,
                                source.Width,
                                out var symmetricModel,
                                out var symmetryDiagnostics))
                        {
                            model =
                                symmetricModel;
                            symmetryRecovered = true;
                            symmetryAxis =
                                symmetryDiagnostics.Axis;
                            mirrorAgreement =
                                symmetryDiagnostics.MirrorAgreement;
                            symmetryMeanShift =
                                symmetryDiagnostics.MeanSampleShift;
                            symmetryMaxShift =
                                symmetryDiagnostics.MaximumSampleShift;

                            var selfMainArcExtracted =
                                CurveFillRibbonMainArcExtractor.TryExtract(
                                    model,
                                    out var mainArcModel,
                                    out var mainArcDiagnostics);
                            mainArcReason =
                                mainArcDiagnostics.Reason;
                            mainArcStart =
                                mainArcDiagnostics.StartIndex;
                            mainArcEnd =
                                mainArcDiagnostics.EndIndex;
                            mainArcKeptFraction =
                                mainArcDiagnostics.KeptFraction;

                            if (selfMainArcExtracted)
                            {
                                model =
                                    mainArcModel;
                                mainArcExtracted = true;
                            }
                        }

                        var mirrorSourceFused = false;

                        if (broadSparseArch)
                        {
                            mirrorSourceFused =
                                CurveFillRibbonMirrorPairRecovery.TryRecover(
                                    model,
                                    regions,
                                    source.Width,
                                    out var mirrorFusedModel,
                                    out var mirrorSourceDiagnostics);
                            mirrorSourceRecovered =
                                mirrorSourceFused;
                            mirrorSourceReason =
                                mirrorSourceDiagnostics.Reason;
                            mirrorSourceAgreement =
                                mirrorSourceDiagnostics.MirrorAgreement;
                            mirrorSourceMeanShift =
                                mirrorSourceDiagnostics.MeanFusionShift;
                            mirrorSourceMaxShift =
                                mirrorSourceDiagnostics.MaximumFusionShift;

                            if (mirrorSourceFused)
                            {
                                model =
                                    mirrorFusedModel;

                                if (!mainArcExtracted &&
                                    centerlineDiagnostics.Endpoints > 2)
                                {
                                    var mirrorMainArcExtracted =
                                        CurveFillRibbonMainArcExtractor.TryExtractMirrorFusedSweep(
                                            model,
                                            out var mirrorMainArcModel,
                                            out var mirrorMainArcDiagnostics);
                                    mainArcReason =
                                        mirrorMainArcDiagnostics.Reason;
                                    mainArcStart =
                                        mirrorMainArcDiagnostics.StartIndex;
                                    mainArcEnd =
                                        mirrorMainArcDiagnostics.EndIndex;
                                    mainArcKeptFraction =
                                        mirrorMainArcDiagnostics.KeptFraction;

                                    if (mirrorMainArcExtracted)
                                    {
                                        model =
                                            mirrorMainArcModel;
                                        mainArcExtracted = true;
                                    }
                                }
                            }
                        }

                        if (sparseTaperSweep &&
                            !mainArcExtracted &&
                            centerlineDiagnostics.Endpoints > 2)
                        {
                            var sparseTaperMainArcExtracted =
                                CurveFillRibbonMainArcExtractor.TryExtractSparseTaperSweep(
                                    model,
                                    out var sparseTaperMainArcModel,
                                    out var sparseTaperMainArcDiagnostics);

                            mainArcReason =
                                sparseTaperMainArcDiagnostics.Reason;
                            mainArcStart =
                                sparseTaperMainArcDiagnostics.StartIndex;
                            mainArcEnd =
                                sparseTaperMainArcDiagnostics.EndIndex;
                            mainArcKeptFraction =
                                sparseTaperMainArcDiagnostics.KeptFraction;

                            if (sparseTaperMainArcExtracted)
                            {
                                model =
                                    sparseTaperMainArcModel;
                                mainArcExtracted = true;
                            }
                        }

                        var sparseTaperCompoundAuthority =
                            sparseTaperSweep &&
                            (centerlineDiagnostics.Endpoints <= 2 ||
                             mainArcExtracted);

                        ElegantArcFit fit;

                        if (compactSpiralSweep)
                        {
                            // Diagnostic routing only: compact spirals are multi-turn shapes, so a
                            // single cubic or generic 8-anchor macro spline is the wrong first
                            // model. Test source-faithful Through-Points first, then the
                            // low-frequency compound fitter. Production prefilter still excludes
                            // this family until one of these models proves safe on real raster.
                            if (CurveFillRibbonThroughPointsFitter.TryFit(
                                    model,
                                    out var compactThroughFit,
                                    out var compactThroughDiagnostics))
                            {
                                fit =
                                    compactThroughFit;
                                fitKind =
                                    "compact-through";
                                curveFamily =
                                    CurveType.SplineThroughPoints.ToString();
                                roundness =
                                    compactThroughDiagnostics.Roundness;
                            }
                            else
                            {
                                compoundAttempted = true;

                                if (CurveFillRibbonCompoundFitter.TryFit(
                                        model,
                                        out var compactCompoundFit,
                                        out var compactCompoundDiagnostics))
                                {
                                    fit =
                                        compactCompoundFit;
                                    fitKind =
                                        "compact-compound";
                                    curveFamily =
                                        "CompoundSpline";
                                }
                                else
                                {
                                    fit =
                                        ElegantArcFitter.Fit(
                                            model,
                                            taperApex: false,
                                            maximumAnchors: 8,
                                            smoothingPasses: 2);
                                    fitKind =
                                        "compact-macro-fallback";
                                    curveFamily =
                                        CurveType.Spline.ToString();
                                }

                                compoundReason =
                                    compactCompoundDiagnostics.Reason;
                                compoundP95Deviation =
                                    compactCompoundDiagnostics.Percentile95Deviation;
                                compoundMaximumDeviation =
                                    compactCompoundDiagnostics.MaximumDeviation;
                                compoundSelectedRoughness =
                                    compactCompoundDiagnostics.Roughness;
                                compoundSelectedAnchors =
                                    compactCompoundDiagnostics.Anchors;
                                compoundSelectedSmoothingPasses =
                                    compactCompoundDiagnostics.SmoothingPasses;
                                compoundSmoothestSafeRoughness =
                                    compactCompoundDiagnostics.SmoothestSafeRoughness;
                                compoundSmoothestSafeAnchors =
                                    compactCompoundDiagnostics.SmoothestSafeAnchors;
                                compoundSmoothestSafeSmoothingPasses =
                                    compactCompoundDiagnostics.SmoothestSafeSmoothingPasses;
                                compoundSmoothestSafeP95Deviation =
                                    compactCompoundDiagnostics.SmoothestSafeP95Deviation;
                                compoundSmoothestSafeMaximumDeviation =
                                    compactCompoundDiagnostics.SmoothestSafeMaximumDeviation;
                                compoundSmoothestCurvatureValidRoughness =
                                    compactCompoundDiagnostics.SmoothestCurvatureValidRoughness;
                                compoundSmoothestCurvatureValidAnchors =
                                    compactCompoundDiagnostics.SmoothestCurvatureValidAnchors;
                                compoundSmoothestCurvatureValidSmoothingPasses =
                                    compactCompoundDiagnostics.SmoothestCurvatureValidSmoothingPasses;
                                compoundSmoothestCurvatureValidP95Deviation =
                                    compactCompoundDiagnostics.SmoothestCurvatureValidP95Deviation;
                                compoundSmoothestCurvatureValidMaximumDeviation =
                                    compactCompoundDiagnostics.SmoothestCurvatureValidMaximumDeviation;
                            }
                        }
                        else if (CurveFillRibbonToolFitter.TryFit(
                                model,
                                out var toolFit,
                                out var toolStyle))
                        {
                            fit =
                                toolFit;
                            fitKind =
                                "curve-tool";
                            curveFamily =
                                toolStyle.Type.ToString();
                            roundness =
                                toolStyle.Roundness;

                            if (broadSparseArch &&
                                mainArcExtracted &&
                                CurveFillRibbonThroughPointsFitter.TryFit(
                                    model,
                                    out var preferredThroughFit,
                                    out var preferredThroughDiagnostics))
                            {
                                var toolRoughness =
                                    CurveFillRibbonSmoothness.Measure(
                                        toolFit.Points);
                                var throughRoughness =
                                    CurveFillRibbonSmoothness.Measure(
                                        preferredThroughFit.Points);

                                if (CurveFillRibbonFitSelector.PreferGeometricThroughPoints(
                                        toolFit,
                                        toolRoughness,
                                        preferredThroughFit,
                                        throughRoughness))
                                {
                                    fit =
                                        preferredThroughFit;
                                    fitKind =
                                        "through-geometry-preferred";
                                    curveFamily =
                                        CurveType.SplineThroughPoints.ToString();
                                    roundness =
                                        preferredThroughDiagnostics.Roundness;
                                }
                            }
                        }
                        else if (broadSparseArch)
                        {
                            if (CurveFillRibbonThroughPointsFitter.TryFit(
                                    model,
                                    out var throughFit,
                                    out var throughDiagnostics))
                            {
                                fit =
                                    throughFit;
                                fitKind =
                                    "through-geometry";
                                curveFamily =
                                    CurveType.SplineThroughPoints.ToString();
                                roundness =
                                    throughDiagnostics.Roundness;
                            }
                            else if (CurveFillBroadOvalArcFitter.TryFit(
                                         model,
                                         out var ovalFit,
                                         out _))
                            {
                                fit =
                                    ovalFit;
                                fitKind =
                                    "half-ellipse";
                                curveFamily =
                                    "Ellipse";
                            }
                            else if (CurveFillRibbonBezierFitter.TryFit(
                                         model,
                                         out var bezierFit,
                                         out _))
                            {
                                fit =
                                    bezierFit;
                                fitKind =
                                    "cubic";
                                curveFamily =
                                    CurveType.Bezier.ToString();
                            }
                            else if (mirrorSourceFused ||
                                     sparseTaperCompoundAuthority)
                            {
                                compoundAttempted = true;

                                if (CurveFillRibbonCompoundFitter.TryFit(
                                        model,
                                        out var compoundFit,
                                        out var compoundDiagnostics))
                                {
                                    fit =
                                        compoundFit;
                                    fitKind =
                                        "compound";
                                    curveFamily =
                                        "CompoundSpline";
                                }
                                else
                                {
                                    fit =
                                        ElegantArcFitter.Fit(
                                            model,
                                            taperApex: false,
                                            maximumAnchors: 8,
                                            smoothingPasses: 2);
                                    fitKind =
                                        "macro-spline";
                                    curveFamily =
                                        CurveType.Spline.ToString();
                                }

                                compoundReason =
                                    compoundDiagnostics.Reason;
                                compoundP95Deviation =
                                    compoundDiagnostics.Percentile95Deviation;
                                compoundMaximumDeviation =
                                    compoundDiagnostics.MaximumDeviation;
                                compoundSelectedRoughness =
                                    compoundDiagnostics.Roughness;
                                compoundSelectedAnchors =
                                    compoundDiagnostics.Anchors;
                                compoundSelectedSmoothingPasses =
                                    compoundDiagnostics.SmoothingPasses;
                                compoundSmoothestSafeRoughness =
                                    compoundDiagnostics.SmoothestSafeRoughness;
                                compoundSmoothestSafeAnchors =
                                    compoundDiagnostics.SmoothestSafeAnchors;
                                compoundSmoothestSafeSmoothingPasses =
                                    compoundDiagnostics.SmoothestSafeSmoothingPasses;
                                compoundSmoothestSafeP95Deviation =
                                    compoundDiagnostics.SmoothestSafeP95Deviation;
                                compoundSmoothestSafeMaximumDeviation =
                                    compoundDiagnostics.SmoothestSafeMaximumDeviation;
                                compoundSmoothestCurvatureValidRoughness =
                                    compoundDiagnostics.SmoothestCurvatureValidRoughness;
                                compoundSmoothestCurvatureValidAnchors =
                                    compoundDiagnostics.SmoothestCurvatureValidAnchors;
                                compoundSmoothestCurvatureValidSmoothingPasses =
                                    compoundDiagnostics.SmoothestCurvatureValidSmoothingPasses;
                                compoundSmoothestCurvatureValidP95Deviation =
                                    compoundDiagnostics.SmoothestCurvatureValidP95Deviation;
                                compoundSmoothestCurvatureValidMaximumDeviation =
                                    compoundDiagnostics.SmoothestCurvatureValidMaximumDeviation;
                            }
                            else
                            {
                                fit =
                                    ElegantArcFitter.Fit(
                                        model,
                                        taperApex: false,
                                        maximumAnchors: 8,
                                        smoothingPasses: 2);
                                fitKind =
                                    "macro-spline";
                                curveFamily =
                                    CurveType.Spline.ToString();
                            }
                        }
                        else if (CurveFillRibbonBezierFitter.TryFit(
                                     model,
                                     out var bezierFit,
                                     out _))
                        {
                            fit =
                                bezierFit;
                            fitKind =
                                "cubic";
                            curveFamily =
                                CurveType.Bezier.ToString();
                        }
                        else
                        {
                            fit =
                                ElegantArcFitter.Fit(
                                    model,
                                    taperApex: false,
                                    maximumAnchors: 8,
                                    smoothingPasses: 2);
                            fitKind =
                                "macro-spline";
                            curveFamily =
                                CurveType.Spline.ToString();
                        }

                        var maximumAllowedFlips =
                            fitKind ==
                            "compound"
                                ? 2
                                : 1;
                        fitSafe =
                            fit.IsSafe &&
                            fit.CurvatureSignFlips <=
                                maximumAllowedFlips;
                        accepted =
                            fitSafe &&
                            (prefilterAccepted ||
                             compactSpiralSweep);
                        maximumDeviation =
                            fit.MaximumCenterlineDeviation;
                        curvatureFlips =
                            fit.CurvatureSignFlips;
                        selectedSmoothness =
                            CurveFillRibbonSmoothness.Measure(
                                fit.Points);

                        if (fitSafe)
                        {
                            _ =
                                CurveFillRibbonWidthProfileRegularizer.Regularize(
                                    model,
                                    fit,
                                    out var widthAuditDiagnostics);
                            widthRegularizerApplied =
                                widthAuditDiagnostics.Applied;
                            widthSourceCoefficientVariation =
                                widthAuditDiagnostics.SourceWidthCoefficientVariation;
                            widthTerminalRatio =
                                widthAuditDiagnostics.TerminalWidthRatio;
                            widthVariationBefore =
                                widthAuditDiagnostics.BeforeAdjacentVariation;
                            widthVariationAfter =
                                widthAuditDiagnostics.AfterAdjacentVariation;
                            widthMaximumShift =
                                widthAuditDiagnostics.MaximumHalfWidthShift;
                        }

                        if (broadSparseArch &&
                            mainArcExtracted &&
                            CurveFillRibbonThroughPointsFitter.TryFit(
                                model,
                                out var alternativeThroughFit,
                                out var alternativeThroughDiagnostics))
                        {
                            alternativeThroughSafe = true;
                            alternativeThroughSmoothness =
                                CurveFillRibbonSmoothness.Measure(
                                    alternativeThroughFit.Points);
                            alternativeThroughMaximumDeviation =
                                alternativeThroughDiagnostics.MaximumDeviation;
                            alternativeThroughP95Deviation =
                                alternativeThroughDiagnostics.Percentile95Deviation;
                            alternativeThroughRoundness =
                                alternativeThroughDiagnostics.Roundness;
                        }

                        status =
                            accepted
                                ? compactSpiralSweep &&
                                  !prefilterAccepted
                                    ? "accepted-compact-spiral"
                                    : "accepted"
                                : compactSpiralProbe &&
                                  fitSafe
                                    ? "compact-probe-safe"
                                    : compactSpiralProbe
                                        ? "compact-probe-fit-unsafe"
                                        : "fit-unsafe";
                    }
                }
            }

            result.Add(
                new RibbonArcCandidateStage(
                    region.Color,
                    region.MinX,
                    region.MinY,
                    region.MaxX,
                    region.MaxY,
                    region.Area,
                    candidate.Elongation,
                    candidate.BoundaryRatio,
                    boundingFillRatio,
                    broadSparseArch,
                    prefilterAccepted,
                    centerlineBuilt,
                    skeletonPixels,
                    endpoints,
                    principalPathPixels,
                    principalPathCoverage,
                    designerRibbon,
                    ribbonShapeReason,
                    ribbonMeanWidth,
                    ribbonWidthCoefficientVariation,
                    ribbonTerminalRatio,
                    ribbonMaximumBend,
                    ribbonRequiredBend,
                    symmetryRecovered,
                    symmetryAxis,
                    mirrorAgreement,
                    symmetryMeanShift,
                    symmetryMaxShift,
                    mirrorSourceRecovered,
                    mirrorSourceReason,
                    mirrorSourceAgreement,
                    mirrorSourceMeanShift,
                    mirrorSourceMaxShift,
                    mainArcExtracted,
                    mainArcReason,
                    mainArcStart,
                    mainArcEnd,
                    mainArcKeptFraction,
                    compoundAttempted,
                    compoundReason,
                    compoundP95Deviation,
                    compoundMaximumDeviation,
                    compoundSelectedRoughness,
                    compoundSelectedAnchors,
                    compoundSelectedSmoothingPasses,
                    compoundSmoothestSafeRoughness,
                    compoundSmoothestSafeAnchors,
                    compoundSmoothestSafeSmoothingPasses,
                    compoundSmoothestSafeP95Deviation,
                    compoundSmoothestSafeMaximumDeviation,
                    compoundSmoothestCurvatureValidRoughness,
                    compoundSmoothestCurvatureValidAnchors,
                    compoundSmoothestCurvatureValidSmoothingPasses,
                    compoundSmoothestCurvatureValidP95Deviation,
                    compoundSmoothestCurvatureValidMaximumDeviation,
                    fitKind,
                    curveFamily,
                    roundness,
                    fitSafe,
                    maximumDeviation,
                    curvatureFlips,
                    selectedSmoothness,
                    alternativeThroughSafe,
                    alternativeThroughSmoothness,
                    alternativeThroughMaximumDeviation,
                    alternativeThroughP95Deviation,
                    alternativeThroughRoundness,
                    widthRegularizerApplied,
                    widthSourceCoefficientVariation,
                    widthTerminalRatio,
                    widthVariationBefore,
                    widthVariationAfter,
                    widthMaximumShift,
                    accepted,
                    status));
        }

        return result
            .OrderBy(stage =>
                stage.MinY)
            .ThenBy(stage =>
                stage.MinX)
            .ToArray();
    }

    private static bool LooksLikeDesignerRibbon(
        LeafPetalArcModel model,
        out RibbonShapeDiagnostics diagnostics)
    {
        var samples =
            model.Samples;

        diagnostics =
            new RibbonShapeDiagnostics(
                "not-evaluated",
                0d,
                0d,
                0d,
                0d,
                0d);

        if (samples.Count < 8)
        {
            diagnostics =
                diagnostics with
                {
                    Reason = "too-few-samples",
                };
            return false;
        }

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
        {
            diagnostics =
                new RibbonShapeDiagnostics(
                    "mean-width",
                    meanWidth,
                    0d,
                    0d,
                    0d,
                    0d);
            return false;
        }

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
            diagnostics =
                new RibbonShapeDiagnostics(
                    "width-variation",
                    meanWidth,
                    coefficientVariation,
                    0d,
                    0d,
                    0d);
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

        var region =
            model.Candidate.Region;
        var boundingFillRatio =
            region.Area /
            (double)Math.Max(
                1,
                region.Width *
                region.Height);
        var sparseTaperSweep =
            terminalRatio >=
                MinimumSparseTaperTerminalWidthRatio &&
            model.Candidate.Elongation >=
                MinimumSparseTaperElongation &&
            boundingFillRatio <=
                MaximumSparseTaperBoundingFill &&
            model.Candidate.BoundaryRatio <=
                MaximumBroadArchBoundaryRatio &&
            model.SkeletonCoverage >=
                MinimumSparseTaperPathCoverage;

        // A compact spiral can look nearly square to PCA because the path wraps around itself.
        // Admit a tiny terminal-ratio tolerance ONLY when every other source measurement says
        // "stable compact ribbon": high principal-path coverage, low boundary ratio, stable width
        // and a medium-density annular fill. Production prefilter still excludes this family until
        // its fitter quality is validated by the diagnostic probe.
        var compactSpiral =
            terminalRatio >=
                MinimumCompactSpiralTerminalWidthRatio &&
            model.Candidate.Elongation >=
                MinimumCompactSpiralElongation &&
            model.Candidate.Elongation <=
                MaximumCompactSpiralElongation &&
            boundingFillRatio >=
                MinimumCompactSpiralBoundingFill &&
            boundingFillRatio <=
                MaximumCompactSpiralBoundingFill &&
            model.Candidate.BoundaryRatio <=
                MaximumCompactSpiralBoundaryRatio &&
            model.SkeletonCoverage >=
                MinimumCompactSpiralPathCoverage &&
            coefficientVariation <=
                MaximumCompactSpiralWidthCoefficientVariation;

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
        {
            diagnostics =
                new RibbonShapeDiagnostics(
                    "degenerate-chord",
                    meanWidth,
                    coefficientVariation,
                    terminalRatio,
                    0d,
                    0d);
            return false;
        }

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
        var terminalAccepted =
            terminalRatio >=
                MinimumTerminalWidthRatio ||
            sparseTaperSweep ||
            compactSpiral;
        var bendAccepted =
            maximumBend >=
            requiredBend;
        var accepted =
            terminalAccepted &&
            bendAccepted;

        var reason =
            accepted
                ? compactSpiral &&
                  terminalRatio <
                  MinimumTerminalWidthRatio
                    ? "ok-compact-spiral"
                    : sparseTaperSweep &&
                      terminalRatio <
                      MinimumTerminalWidthRatio
                        ? "ok-sparse-taper"
                        : "ok"
                : !terminalAccepted
                    ? "terminal-width-ratio"
                    : "insufficient-bend";

        diagnostics =
            new RibbonShapeDiagnostics(
                reason,
                meanWidth,
                coefficientVariation,
                terminalRatio,
                maximumBend,
                requiredBend);

        return accepted;
    }
}


internal readonly record struct RibbonShapeDiagnostics(
    string Reason,
    double MeanWidth,
    double WidthCoefficientVariation,
    double TerminalRatio,
    double MaximumBend,
    double RequiredBend);

internal readonly record struct RibbonArcCandidateStage(
    byte Color,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int Area,
    double Elongation,
    double BoundaryRatio,
    double BoundingFillRatio,
    bool BroadSparseArch,
    bool PrefilterAccepted,
    bool CenterlineBuilt,
    int SkeletonPixels,
    int Endpoints,
    int PrincipalPathPixels,
    double PrincipalPathCoverage,
    bool DesignerRibbon,
    string RibbonShapeReason,
    double RibbonMeanWidth,
    double RibbonWidthCoefficientVariation,
    double RibbonTerminalRatio,
    double RibbonMaximumBend,
    double RibbonRequiredBend,
    bool SymmetryRecovered,
    string SymmetryAxis,
    double MirrorAgreement,
    double SymmetryMeanShift,
    double SymmetryMaxShift,
    bool MirrorSourceRecovered,
    string MirrorSourceReason,
    double MirrorSourceAgreement,
    double MirrorSourceMeanShift,
    double MirrorSourceMaxShift,
    bool MainArcExtracted,
    string MainArcReason,
    int MainArcStart,
    int MainArcEnd,
    double MainArcKeptFraction,
    bool CompoundAttempted,
    string CompoundReason,
    double CompoundP95Deviation,
    double CompoundMaximumDeviation,
    double CompoundSelectedRoughness,
    int CompoundSelectedAnchors,
    int CompoundSelectedSmoothingPasses,
    double CompoundSmoothestSafeRoughness,
    int CompoundSmoothestSafeAnchors,
    int CompoundSmoothestSafeSmoothingPasses,
    double CompoundSmoothestSafeP95Deviation,
    double CompoundSmoothestSafeMaximumDeviation,
    double CompoundSmoothestCurvatureValidRoughness,
    int CompoundSmoothestCurvatureValidAnchors,
    int CompoundSmoothestCurvatureValidSmoothingPasses,
    double CompoundSmoothestCurvatureValidP95Deviation,
    double CompoundSmoothestCurvatureValidMaximumDeviation,
    string FitKind,
    string CurveFamily,
    double Roundness,
    bool FitSafe,
    double MaximumDeviation,
    int CurvatureSignFlips,
    double SelectedSmoothness,
    bool AlternativeThroughSafe,
    double AlternativeThroughSmoothness,
    double AlternativeThroughMaximumDeviation,
    double AlternativeThroughP95Deviation,
    double AlternativeThroughRoundness,
    bool WidthRegularizerApplied,
    double WidthSourceCoefficientVariation,
    double WidthTerminalRatio,
    double WidthVariationBefore,
    double WidthVariationAfter,
    double WidthMaximumShift,
    bool Accepted,
    string Status);

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
    int SymmetryRecoveries,
    int MainArcExtractions,
    double MaxSymmetrySampleShift,
    int MirrorSourceFusions,
    double BestMirrorSourceAgreement,
    double MaxMirrorSourceFusionShift,
    int CompoundFits,
    int CompoundAttempts,
    double MaxCompoundP95Deviation,
    double MaxCompoundDeviation,
    string LastCompoundReason,
    int CurveToolFits,
    int CurveToolThroughPointsFits,
    int CurveToolSplineFits,
    int CurveToolBezierFits,
    double MeanCurveToolRoundness,
    int GeometricThroughFits,
    int GeometricThroughAttempts,
    double MaxGeometricThroughDeviation,
    double MaxGeometricThroughP95Deviation,
    string LastGeometricThroughReason,
    double MeanGeometricThroughRoundness,
    int BroadOvalFits,
    int BroadOvalAttempts,
    double MaxBroadOvalDeviation,
    double MaxBroadOvalP95Deviation,
    string LastBroadOvalReason,
    int CubicBezierFits,
    int CubicAttempts,
    double MaxCubicDeviation,
    double MaxCubicP95Deviation,
    double MaxCubicHandleRatio,
    string LastCubicReason,
    double MaxFitDeviation,
    int MaxFitCurvatureFlips,
    int WidthRegularized,
    double MaxWidthRegularizationShift,
    double MaxWidthVariationReduction,
    int SelfSymmetryNormalizations,
    double BestSelfSymmetryAgreement,
    double MaxSelfSymmetryDeviation,
    int MirrorPairs,
    int MirrorPairReplacements,
    double BestMirrorPairAgreement,
    double MaxMirrorPairDeviation,
    int Refined,
    int OutlinedRefined,
    int BoundaryPixelsChanged);
