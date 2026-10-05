namespace RugScale.Core.Drawing;

/// <summary>
/// Geometry primitives for jointly redrawing layered ribbon bands around one accepted centreline.
///
/// This class intentionally starts as mask-only infrastructure. Production ownership changes are
/// not allowed until real-raster audits prove that the same source cross-section sequence is stable
/// enough to move the neighbouring bands together.
/// </summary>
internal static class CurveFillLayeredRibbonRasterizer
{
    /// <summary>
    /// Upper bound (source px) on how far stale-layer cleanup may reach beyond the redrawn stack.
    /// The actual reach is the centreline shift the accepted fit measured against the immutable
    /// source medial axis; this bound only caps it so cleanup can never wander with a wild fit.
    /// </summary>
    internal const double MaximumCleanupShiftReach = 3.5;

    /// <summary>
    /// Raster-phase slack (source px) added to the source-stack distance guard. A stale target
    /// pixel is only cleaned when its nearest source pixel lies inside the layered stack around
    /// the SOURCE centreline, so a neighbouring motif that happens to use the same separator or
    /// band colour can never be erased.
    /// </summary>
    private const double SourceStackSlack = 1.0;
    internal static HashSet<int> BuildOneSidedBandMask(
        IReadOnlyList<ElegantArcPoint> points,
        double side,
        double scaleX,
        double scaleY,
        int targetWidth,
        int targetHeight,
        double innerAdditionalTargetPixels,
        double sourceBandWidth,
        double fixedBandWidthTargetPixels = 0d,
        double innerAdditionalSourceWidth = 0d)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result =
            new HashSet<int>();

        if (points.Count < 3 ||
            targetWidth <= 0 ||
            targetHeight <= 0 ||
            Math.Abs(
                side) <
            0.5)
        {
            return result;
        }

        side =
            Math.Sign(
                side);
        var inner =
            new List<(double X, double Y)>(
                points.Count);
        var outer =
            new List<(double X, double Y)>(
                points.Count);

        for (var index = 0;
             index < points.Count;
             index++)
        {
            var previous =
                points[
                    Math.Max(
                        0,
                        index - 1)];
            var next =
                points[
                    Math.Min(
                        points.Count - 1,
                        index + 1)];
            var tangentX =
                next.X -
                previous.X;
            var tangentY =
                next.Y -
                previous.Y;
            var tangentLength =
                Math.Sqrt(
                    tangentX *
                        tangentX +
                    tangentY *
                        tangentY);

            if (tangentLength <= 1e-9)
            {
                if (index > 0)
                {
                    inner.Add(
                        inner[^1]);
                    outer.Add(
                        outer[^1]);
                }

                continue;
            }

            var sourceNormalX =
                -tangentY /
                tangentLength *
                side;
            var sourceNormalY =
                tangentX /
                tangentLength *
                side;
            var targetNormalX =
                sourceNormalX *
                scaleX;
            var targetNormalY =
                sourceNormalY *
                scaleY;
            var normalScale =
                Math.Sqrt(
                    targetNormalX *
                        targetNormalX +
                    targetNormalY *
                        targetNormalY);

            if (normalScale <= 1e-9)
                continue;

            var unitTargetNormalX =
                targetNormalX /
                normalScale;
            var unitTargetNormalY =
                targetNormalY /
                normalScale;
            var centerX =
                (points[index].X +
                 0.5) *
                    scaleX -
                0.5;
            var centerY =
                (points[index].Y +
                 0.5) *
                    scaleY -
                0.5;

            // The fitted half-width is a source-space motif dimension and therefore scales with
            // the local mapped normal. Additional target-pixel offsets (such as a 1x1 Pixel-Cord
            // separator) are deliberately applied AFTER scaling.
            var fillBoundaryX =
                centerX +
                sourceNormalX *
                    points[index].HalfWidth *
                    scaleX;
            var fillBoundaryY =
                centerY +
                sourceNormalY *
                    points[index].HalfWidth *
                    scaleY;
            var innerOffsetTarget =
                innerAdditionalTargetPixels +
                Math.Max(
                    0d,
                    innerAdditionalSourceWidth) *
                normalScale;
            var innerX =
                fillBoundaryX +
                unitTargetNormalX *
                    innerOffsetTarget;
            var innerY =
                fillBoundaryY +
                unitTargetNormalY *
                    innerOffsetTarget;
            var bandWidthTarget =
                fixedBandWidthTargetPixels >
                0d
                    ? fixedBandWidthTargetPixels
                    : Math.Max(
                        0d,
                        sourceBandWidth) *
                      normalScale;

            inner.Add(
                (
                    innerX,
                    innerY
                ));
            outer.Add(
                (
                    innerX +
                    unitTargetNormalX *
                        bandWidthTarget,
                    innerY +
                    unitTargetNormalY *
                        bandWidthTarget
                ));
        }

        if (inner.Count < 3 ||
            outer.Count !=
            inner.Count)
        {
            return result;
        }

        var polygon =
            new List<(double X, double Y)>(
                inner.Count *
                2);
        polygon.AddRange(
            inner);
        polygon.AddRange(
            outer
                .AsEnumerable()
                .Reverse());

        return LeafPetalArcRasterizer.RasterizePolygon(
            polygon,
            targetWidth,
            targetHeight);
    }

    internal static bool TryApplySecondProtectedBandPreview(
        RugScale.Core.Models.DesignDocument source,
        RugScale.Core.Models.DesignDocument destination,
        LeafPetalArcModel model,
        ElegantArcFit fit,
        LayeredRibbonProfileDiagnostics profile,
        IReadOnlySet<byte> protectedStrokeColors,
        out int changed) =>
        TryApplySecondProtectedBandPreview(
            source,
            destination,
            model,
            fit,
            profile,
            protectedStrokeColors,
            out changed,
            out _);

    internal static bool TryApplySecondProtectedBandPreview(
        RugScale.Core.Models.DesignDocument source,
        RugScale.Core.Models.DesignDocument destination,
        LeafPetalArcModel model,
        ElegantArcFit fit,
        LayeredRibbonProfileDiagnostics profile,
        IReadOnlySet<byte> protectedStrokeColors,
        out int changed,
        out LayeredRibbonPreviewDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        changed = 0;
        diagnostics =
            new LayeredRibbonPreviewDiagnostics(
                "not-attempted",
                0,
                0,
                0,
                0);

        if (!fit.IsSafe ||
            fit.Points.Count < 8 ||
            !profile.Detected ||
            Math.Max(
                profile.Coverage,
                profile.BracketedCoverage) < 0.80 ||
            profile.MeanRunWidths is null ||
            profile.MeanRunWidths.Count < 2)
        {
            diagnostics =
                diagnostics with
                {
                    Reason = "input-gate",
                };
            return false;
        }

        var parts =
            profile.Sequence
                .Split(
                    '>',
                    StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 ||
            !byte.TryParse(
                parts[0],
                out var separatorColor) ||
            !byte.TryParse(
                parts[1],
                out var bandColor) ||
            separatorColor ==
                bandColor ||
            !protectedStrokeColors.Contains(
                separatorColor))
        {
            diagnostics =
                diagnostics with
                {
                    Reason = "sequence-gate",
                };
            return false;
        }

        // The first layer must be a known protected Pixel-Cord/outline role. The broader adjacent
        // band may legitimately be a filled colour rather than a protected-stroke role; source
        // cross-section evidence, not palette-role classification, is the authority in preview.

        var separatorSourceWidth =
            profile.MeanRunWidths[0];
        var bandSourceWidth =
            profile.MeanRunWidths[1];
        var bracketed =
            parts.Length >= 3 &&
            string.Equals(
                parts[0],
                parts[2],
                StringComparison.Ordinal) &&
            profile.BracketedCoverage >=
                0.80;
        byte exteriorColor = 0;
        var hasExterior =
            bracketed &&
            profile.ExteriorCoverage >=
                0.60 &&
            byte.TryParse(
                profile.ExteriorColor,
                out exteriorColor) &&
            exteriorColor !=
                separatorColor &&
            exteriorColor !=
                bandColor &&
            exteriorColor !=
                model.Candidate.Region.Color;

        // The first layer is treated as a 1x1 Pixel-Cord only when immutable source evidence says
        // it is genuinely thin. Wider first layers remain source-scaled and need a different model.
        if (separatorSourceWidth >
                1.65 ||
            bandSourceWidth <
                1.75 ||
            bandSourceWidth >
                8.0)
        {
            diagnostics =
                diagnostics with
                {
                    Reason = "width-gate",
                };
            return false;
        }

        var side =
            string.Equals(
                profile.Side,
                "positive",
                StringComparison.Ordinal)
                ? 1d
                : string.Equals(
                    profile.Side,
                    "negative",
                    StringComparison.Ordinal)
                    ? -1d
                    : 0d;

        if (Math.Abs(
                side) <
            0.5)
        {
            diagnostics =
                diagnostics with
                {
                    Reason = "side-gate",
                };
            return false;
        }

        var localExteriorProfile =
            bracketed
                ? CurveFillLayeredRibbonAnalyzer.BuildLocalBracketedExteriorProfile(
                    source,
                    model,
                    protectedStrokeColors,
                    profile.Side,
                    separatorColor,
                    bandColor)
                : Array.Empty<byte?>();

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var separatorMask =
            BuildOneSidedBandMask(
                fit.Points,
                side,
                scaleX,
                scaleY,
                destination.Width,
                destination.Height,
                innerAdditionalTargetPixels: 0d,
                sourceBandWidth: 0d,
                fixedBandWidthTargetPixels: 1d);
        var protectedBandMask =
            BuildOneSidedBandMask(
                fit.Points,
                side,
                scaleX,
                scaleY,
                destination.Width,
                destination.Height,
                innerAdditionalTargetPixels: 1d,
                sourceBandWidth:
                    bandSourceWidth);
        var outerSeparatorMask =
            bracketed
                ? BuildOneSidedBandMask(
                    fit.Points,
                    side,
                    scaleX,
                    scaleY,
                    destination.Width,
                    destination.Height,
                    innerAdditionalTargetPixels: 1d,
                    sourceBandWidth: 0d,
                    fixedBandWidthTargetPixels: 1d,
                    innerAdditionalSourceWidth:
                        bandSourceWidth)
                : new HashSet<int>();
        // The accepted centreline may sit up to MaximumCenterlineDeviation source px away from the
        // immutable source medial axis. The old separator/band therefore can lie that far beyond
        // the redrawn stack; extend cleanup authority by exactly the measured shift (bounded), not
        // by a global constant. Stale pixels beyond this reach stay untouched.
        var centrelineShiftReach =
            Math.Clamp(
                fit.MaximumCenterlineDeviation,
                0d,
                MaximumCleanupShiftReach);
        var outerSeparatorSourceWidth =
            bracketed &&
            profile.MeanRunWidths.Count >= 3
                ? profile.MeanRunWidths[2]
                : 0d;
        var authorityMask =
            BuildOneSidedBandMask(
                fit.Points,
                side,
                scaleX,
                scaleY,
                destination.Width,
                destination.Height,
                innerAdditionalTargetPixels: 0d,
                sourceBandWidth:
                    separatorSourceWidth +
                    bandSourceWidth +
                    2.0 +
                    centrelineShiftReach);

        diagnostics =
            diagnostics with
            {
                SeparatorMaskPixels =
                    separatorMask.Count +
                    outerSeparatorMask.Count,
                BandMaskPixels =
                    protectedBandMask.Count,
                AuthorityMaskPixels =
                    authorityMask.Count,
            };

        if (separatorMask.Count == 0 ||
            protectedBandMask.Count == 0 ||
            authorityMask.Count == 0)
        {
            diagnostics =
                diagnostics with
                {
                    Reason = "mask-gate",
                };
            return false;
        }

        var regionColor =
            model.Candidate.Region.Color;
        var allowStaleCleanup =
            bracketed &&
            (hasExterior ||
             localExteriorProfile.Any(value =>
                 value.HasValue));
        var staleCleaned = 0;

        foreach (var key in authorityMask)
        {
            var x =
                key %
                destination.Width;
            var y =
                key /
                destination.Width;
            var current =
                destination.GetPixel(
                    x,
                    y);
            var sourceX =
                Math.Clamp(
                    (int)Math.Round(
                        (x + 0.5) /
                        scaleX -
                        0.5),
                    0,
                    source.Width - 1);
            var sourceY =
                Math.Clamp(
                    (int)Math.Round(
                        (y + 0.5) /
                        scaleY -
                        0.5),
                    0,
                    source.Height - 1);
            var sourceOwner =
                source.GetPixel(
                    sourceX,
                    sourceY);

            if (separatorMask.Contains(
                    key) ||
                outerSeparatorMask.Contains(
                    key))
            {
                if (current ==
                    separatorColor)
                {
                    continue;
                }

                if (protectedStrokeColors.Contains(
                        current) &&
                    current !=
                        bandColor)
                {
                    continue;
                }

                destination.SetPixel(
                    x,
                    y,
                    separatorColor);
                changed++;
                continue;
            }

            if (protectedBandMask.Contains(
                    key))
            {
                if (current ==
                    bandColor)
                {
                    continue;
                }

                if (protectedStrokeColors.Contains(
                        current) &&
                    current !=
                        separatorColor &&
                    current !=
                        bandColor)
                {
                    continue;
                }

                destination.SetPixel(
                    x,
                    y,
                    bandColor);
                changed++;
                continue;
            }

            // Cleanup requires immutable evidence that the broad band is bracketed by the same
            // separator colour. For unbracketed profiles we intentionally run overlay-only:
            // redraw the learned parallel bands, but never erase stale baseline ownership until an
            // exterior colour is actually proven by source evidence.
            if (!allowStaleCleanup ||
                current !=
                    bandColor &&
                current !=
                    separatorColor ||
                sourceOwner !=
                    bandColor &&
                sourceOwner !=
                    separatorColor)
            {
                continue;
            }

            // Stale ownership must come from THIS ribbon's own source stack. Measure the nearest
            // source pixel against the immutable source centreline: inside
            // half-width + separator + band (+ outer separator) it is the old layer position;
            // farther out it belongs to another motif and is never ours to erase.
            var sourceStackDistance =
                SourceStackDistance(
                    model.Samples,
                    sourceX,
                    sourceY,
                    out var nearestHalfWidth);
            var sourceStackReach =
                nearestHalfWidth +
                separatorSourceWidth +
                bandSourceWidth +
                outerSeparatorSourceWidth +
                SourceStackSlack;

            if (sourceStackDistance >
                sourceStackReach)
            {
                continue;
            }

            var localExterior =
                ResolveLocalExterior(
                    model.Samples,
                    localExteriorProfile,
                    sourceX,
                    sourceY);

            if (!localExterior.HasValue &&
                hasExterior)
            {
                localExterior =
                    exteriorColor;
            }

            if (!localExterior.HasValue)
                continue;

            destination.SetPixel(
                x,
                y,
                localExterior.Value);
            changed++;
            staleCleaned++;
        }

        diagnostics =
            diagnostics with
            {
                Reason =
                    changed > 0
                        ? "ok"
                        : "no-pixel-authority",
                ChangedPixels =
                    changed,
                StaleCleanedPixels =
                    staleCleaned,
            };

        return changed > 0;
    }

    /// <summary>
    /// Euclidean source-space distance from (x, y) to the nearest source centreline sample, with
    /// that sample's fitted half-width.
    /// </summary>
    private static double SourceStackDistance(
        IReadOnlyList<LeafPetalAxisSample> samples,
        double sourceX,
        double sourceY,
        out double nearestHalfWidth)
    {
        nearestHalfWidth = 0d;
        var best =
            double.PositiveInfinity;

        foreach (var sample in samples)
        {
            var dx =
                sample.X -
                sourceX;
            var dy =
                sample.Y -
                sourceY;
            var distanceSquared =
                dx *
                    dx +
                dy *
                    dy;

            if (distanceSquared <
                best)
            {
                best =
                    distanceSquared;
                nearestHalfWidth =
                    sample.HalfWidth;
            }
        }

        return double.IsPositiveInfinity(
            best)
            ? best
            : Math.Sqrt(
                best);
    }

    private static byte? ResolveLocalExterior(
        IReadOnlyList<LeafPetalAxisSample> samples,
        IReadOnlyList<byte?> localExteriorProfile,
        double sourceX,
        double sourceY)
    {
        if (samples.Count == 0 ||
            localExteriorProfile.Count !=
                samples.Count)
        {
            return null;
        }

        var nearestIndex = -1;
        var nearestDistanceSquared =
            double.PositiveInfinity;

        for (var index = 0;
             index < samples.Count;
             index++)
        {
            var dx =
                samples[index].X -
                sourceX;
            var dy =
                samples[index].Y -
                sourceY;
            var distanceSquared =
                dx *
                    dx +
                dy *
                    dy;

            if (distanceSquared <
                nearestDistanceSquared)
            {
                nearestDistanceSquared =
                    distanceSquared;
                nearestIndex =
                    index;
            }
        }

        if (nearestIndex < 0)
            return null;

        if (localExteriorProfile[nearestIndex].HasValue)
            return localExteriorProfile[nearestIndex];

        // Source bracketing can be interrupted for one or two samples by raster phase. Borrow only
        // from a very small axial neighbourhood; never use a distant global exterior just to fill
        // a local evidence gap.
        for (var radius = 1;
             radius <= 3;
             radius++)
        {
            var left =
                nearestIndex -
                radius;
            var right =
                nearestIndex +
                radius;

            if (left >= 0 &&
                localExteriorProfile[left].HasValue)
            {
                return localExteriorProfile[left];
            }

            if (right <
                    localExteriorProfile.Count &&
                localExteriorProfile[right].HasValue)
            {
                return localExteriorProfile[right];
            }
        }

        return null;
    }

}

internal readonly record struct LayeredRibbonPreviewDiagnostics(
    string Reason,
    int SeparatorMaskPixels,
    int BandMaskPixels,
    int AuthorityMaskPixels,
    int ChangedPixels,
    int StaleCleanedPixels = 0);
