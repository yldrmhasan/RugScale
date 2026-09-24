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
    private const double MaximumBoundaryRatio = 0.66;
    private const double MaximumWidthCoefficientVariation = 0.55;
    private const double MinimumTerminalWidthRatio = 0.45;
    private const double MinimumAbsoluteBend = 1.35;
    private const double MinimumRelativeBend = 0.035;

    public static int Apply(
        DesignDocument source,
        DesignDocument destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Width == destination.Width &&
            source.Height == destination.Height)
        {
            return 0;
        }

        var protectedStrokeColors =
            ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                source);
        var regions =
            LeafPetalRegionExtractor.Extract(
                source);

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
                    out var candidate) ||
                candidate.Elongation <
                    MinimumRibbonElongation ||
                candidate.BoundaryRatio >
                    MaximumBoundaryRatio)
            {
                continue;
            }

            if (!LeafPetalMedialAxisBuilder.TryBuild(
                    candidate,
                    source.Width,
                    out var model) ||
                !LooksLikeDesignerRibbon(
                    model))
            {
                continue;
            }

            // Ribbon geometry is deliberately NOT apex-tapered. The source width profile already
            // contains the designer's constant/slowly-varying band thickness.
            var fit =
                ElegantArcFitter.Fit(
                    model,
                    taperApex: false);

            if (!fit.IsSafe ||
                fit.CurvatureSignFlips > 1)
            {
                continue;
            }

            accepted.Add(
                (model, fit));
        }

        var changed = 0;

        foreach (var item in accepted
                     .OrderByDescending(pair =>
                         pair.Model.Candidate.MajorExtent *
                         pair.Model.Candidate.Elongation)
                     .Take(MaximumRefinedRegions))
        {
            changed +=
                LeafPetalArcRasterizer.Apply(
                    source,
                    destination,
                    item.Model,
                    item.Fit,
                    protectedStrokeColors);
        }

        return changed;
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
