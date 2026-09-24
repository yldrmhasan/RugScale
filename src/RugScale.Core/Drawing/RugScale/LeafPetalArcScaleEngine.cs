using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Specialist RugScale path for elongated, filled floral forms.
///
/// The safe Curve & Fill result is always the baseline. Only regions that independently pass
/// elongated-region classification, centreline construction and elegant-arc safety are refined.
/// Every rejection is a fallback, not an error.
/// </summary>
internal static class LeafPetalArcScaleEngine
{
    private const int MaximumRefinedRegions = 256;

    public static void Resize(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity) =>
        _ = ResizeWithDiagnostics(
            source,
            destination,
            sourceWarpDensity,
            sourceWeftDensity,
            targetWarpDensity,
            targetWeftDensity);

    internal static LeafPetalArcDiagnostics ResizeWithDiagnostics(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        CurveFillScaleEngine.Resize(
            source,
            destination,
            sourceWarpDensity,
            sourceWeftDensity,
            targetWarpDensity,
            targetWeftDensity);

        if (source.Width == destination.Width &&
            source.Height == destination.Height)
        {
            return default;
        }

        var protectedStrokeColors =
            ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                source);

        var candidates =
            BuildCandidates(
                source,
                protectedStrokeColors,
                out var regionCount);

        var refined = 0;
        var rejectedByAxis = 0;
        var rejectedByFit = 0;
        var changed = 0;
        var boundaryCurveRefined = 0;
        var centerlineRefined = 0;
        var boundaryCurveChanged = 0;
        var centerlineChanged = 0;

        foreach (var candidate in candidates)
        {
            if (!LeafPetalMedialAxisBuilder.TryBuild(
                    candidate,
                    source.Width,
                    out var model))
            {
                rejectedByAxis++;
                continue;
            }

            var fit =
                ElegantArcFitter.Fit(
                    model);
            var regionChanges = 0;
            var usedBoundaryCurve = false;

            var hasOutlineEvidence =
                LeafPetalBoundaryCurveBuilder.TryMeasureOutlineEvidence(
                    source,
                    candidate.Region,
                    protectedStrokeColors,
                    out _,
                    out var outlineCoverage);
            var strongDesignerOutline =
                hasOutlineEvidence &&
                outlineCoverage >=
                0.72;

            // An outlined leaf has stronger evidence than its filled centreline: the designer's
            // two visible Curve-tool sides are the actual geometry the user judges. Attempt paired
            // boundary recovery first.
            if (LeafPetalBoundaryCurveBuilder.TryBuild(
                    source,
                    model,
                    protectedStrokeColors,
                    out var boundaryModel))
            {
                regionChanges =
                    LeafPetalBoundaryCurveRasterizer.Apply(
                        source,
                        destination,
                        boundaryModel,
                        protectedStrokeColors,
                        sourceWarpDensity,
                        sourceWeftDensity,
                        targetWarpDensity,
                        targetWeftDensity);

                usedBoundaryCurve =
                    regionChanges > 0;
            }

            if (regionChanges <= 0)
            {
                // Strong source outline + failed paired fit means "do not guess". Keep the already
                // safe Curve & Fill baseline. Generic centreline reconstruction is only for filled
                // shapes without a trustworthy designer outline.
                if (strongDesignerOutline)
                {
                    rejectedByFit++;
                    continue;
                }

                if (!fit.IsSafe)
                {
                    rejectedByFit++;
                    continue;
                }

                regionChanges =
                    LeafPetalArcRasterizer.Apply(
                        source,
                        destination,
                        model,
                        fit,
                        protectedStrokeColors);
            }

            if (regionChanges <= 0)
                continue;

            refined++;
            changed +=
                regionChanges;

            if (usedBoundaryCurve)
            {
                boundaryCurveRefined++;
                boundaryCurveChanged +=
                    regionChanges;
            }
            else
            {
                centerlineRefined++;
                centerlineChanged +=
                    regionChanges;
            }
        }

        // The Curve & Fill baseline already contains strict ownership + RugCAD tool replay.
        // Specialist refinement is deliberately LAST. LeafPetalArcRasterizer protects those
        // separator palette roles itself and allows only a local two-colour boundary displacement;
        // re-running the global ownership guard here would erase the aesthetic correction.

        return new LeafPetalArcDiagnostics(
            regionCount,
            candidates.Count,
            refined,
            rejectedByAxis,
            rejectedByFit,
            changed,
            boundaryCurveRefined,
            centerlineRefined,
            boundaryCurveChanged,
            centerlineChanged);
    }

    internal static IReadOnlyList<LeafPetalCandidateStage> AnalyzeCandidateStages(
        DesignDocument source,
        int targetWidth = 0,
        int targetHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        var protectedStrokeColors =
            ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                source);
        var candidates =
            BuildCandidates(
                source,
                protectedStrokeColors,
                out _);
        var result =
            new List<LeafPetalCandidateStage>(
                candidates.Count);

        foreach (var candidate in candidates)
        {
            var axisBuilt =
                LeafPetalMedialAxisBuilder.TryBuild(
                    candidate,
                    source.Width,
                    out var model);
            var fitSafe = false;
            var boundaryBuilt = false;
            var outlineCoverage = 0d;
            var leftControls = 0;
            var rightControls = 0;
            var leftSourcePathPixels = 0;
            var rightSourcePathPixels = 0;
            var leftRoundness = 0d;
            var rightRoundness = 0d;
            var targetLeftRoundness = 0d;
            var targetRightRoundness = 0d;
            var targetPairAdjusted = false;

            LeafPetalBoundaryCurveBuilder.TryMeasureOutlineEvidence(
                source,
                candidate.Region,
                protectedStrokeColors,
                out _,
                out outlineCoverage);

            if (axisBuilt)
            {
                fitSafe =
                    ElegantArcFitter.Fit(
                        model)
                    .IsSafe;

                if (LeafPetalBoundaryCurveBuilder.TryBuild(
                        source,
                        model,
                        protectedStrokeColors,
                        out var boundary))
                {
                    boundaryBuilt = true;
                    outlineCoverage =
                        boundary.OutlineCoverage;
                    leftControls =
                        boundary.LeftFit.Controls.Count;
                    rightControls =
                        boundary.RightFit.Controls.Count;
                    leftSourcePathPixels =
                        boundary.LeftSourcePath.Count;
                    rightSourcePathPixels =
                        boundary.RightSourcePath.Count;
                    leftRoundness =
                        boundary.LeftFit.Roundness;
                    rightRoundness =
                        boundary.RightFit.Roundness;
                    targetLeftRoundness =
                        leftRoundness;
                    targetRightRoundness =
                        rightRoundness;

                    if (targetWidth > 0 &&
                        targetHeight > 0)
                    {
                        var optimized =
                            LeafPetalBoundaryPairOptimizer.Optimize(
                                boundary,
                                targetWidth /
                                (double)source.Width,
                                targetHeight /
                                (double)source.Height);

                        targetLeftRoundness =
                            optimized.LeftFit.Roundness;
                        targetRightRoundness =
                            optimized.RightFit.Roundness;
                        targetPairAdjusted =
                            Math.Abs(
                                targetLeftRoundness -
                                leftRoundness) >
                            1e-9 ||
                            Math.Abs(
                                targetRightRoundness -
                                rightRoundness) >
                            1e-9;
                    }
                }
            }

            var region =
                candidate.Region;

            result.Add(
                new LeafPetalCandidateStage(
                    region.Color,
                    region.MinX,
                    region.MinY,
                    region.MaxX,
                    region.MaxY,
                    region.Area,
                    region.IsSubLobe,
                    candidate.Elongation,
                    axisBuilt,
                    fitSafe,
                    boundaryBuilt,
                    outlineCoverage,
                    leftControls,
                    rightControls,
                    leftSourcePathPixels,
                    rightSourcePathPixels,
                    leftRoundness,
                    rightRoundness,
                    targetLeftRoundness,
                    targetRightRoundness,
                    targetPairAdjusted));
        }

        return result;
    }

    private static List<LeafPetalArcCandidate> BuildCandidates(
        DesignDocument source,
        IReadOnlySet<byte> protectedStrokeColors,
        out int regionCount)
    {
        var regions =
            LeafPetalRegionExtractor.Extract(
                source);
        regionCount =
            regions.Count;
        var analysisRegions =
            new List<LeafPetalRegion>(
                regions.Count * 2);

        foreach (var region in regions)
        {
            if (!protectedStrokeColors.Contains(
                    region.Color))
            {
                analysisRegions.AddRange(
                    LeafPetalLobeExtractor.Extract(
                        source,
                        region));
            }
        }

        analysisRegions.AddRange(
            regions);

        var candidates =
            new List<LeafPetalArcCandidate>();

        foreach (var region in analysisRegions)
        {
            if (LeafPetalArcClassifier.TryClassify(
                    region,
                    source.Width,
                    out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderByDescending(candidate =>
                candidate.Region.IsSubLobe)
            // Prefer visually meaningful leaf bodies over tiny needle-like slivers. Pure
            // elongation ranking pushed a 31x59 / 762px designer leaf behind hundreds of small
            // 5-8px-wide fragments even though the large lobe is exactly what must be redrawn.
            .ThenByDescending(candidate =>
                candidate.Region.Area *
                Math.Min(
                    4d,
                    candidate.Elongation))
            .ThenByDescending(candidate =>
                candidate.Elongation)
            .Take(MaximumRefinedRegions)
            .ToList();
    }

}
