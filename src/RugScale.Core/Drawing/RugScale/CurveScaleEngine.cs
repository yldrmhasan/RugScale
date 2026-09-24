using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Source-guided curve/stroke re-rasterizer used by RugScale when enlarging indexed carpet art.
///
/// Enlargement is NOT implemented by duplicating source pixels. A one-pixel curve in a 40x50
/// design stays approximately one pixel at the same target quality even when the physical carpet
/// becomes larger. Geometry (the curve centreline) scales with the requested dimensions, while
/// stroke thickness scales only with the requested warp/weft quality ratio.
///
/// The pass starts from a categorical nearest-neighbour fill so large fields remain stable, then
/// detects thin connected source components, thins them to a one-pixel skeleton, removes the
/// block-expanded residue, and redraws their skeletons at target coordinates with a quality-aware
/// indexed stroke. No RGB interpolation and no new palette index is ever introduced.
/// </summary>
internal static class CurveScaleEngine
{
    private const int PaletteSlots = 256;
    private const int MinimumStrokePixels = 4;

    private sealed class StrokeComponent
    {
        public required byte Color { get; init; }
        public required List<int> Pixels { get; init; }
        public required int MinX { get; init; }
        public required int MinY { get; init; }
        public required int MaxX { get; init; }
        public required int MaxY { get; init; }
        public required bool[] Mask { get; init; }
        public required bool[] Skeleton { get; init; }
        public required int SkeletonCount { get; init; }

        /// <summary>
        /// Source pen width sampled at each local skeleton pixel. Values come from a padded
        /// chamfer distance transform: a 1px curve remains 1px, a 3px ribbon centre is ~3px, and
        /// intentional width changes along one Curve-tool stroke survive re-rasterization.
        /// Non-skeleton entries are zero.
        /// </summary>
        public required double[] LocalThickness { get; init; }

        public required double EstimatedThickness { get; init; }
        public required double BoundaryRatio { get; init; }
        public required double FillRatio { get; init; }

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public int Area => Pixels.Count;
    }

    public static void RefineShrink(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Width >= source.Width ||
            destination.Height >= source.Height)
        {
            return;
        }

        var strokes = DetectStrokeComponents(
            source,
            sourceWarpDensity,
            sourceWeftDensity)
            // Shrink refinement is intentionally stricter than enlargement. Dense filled ornament
            // remains under MotifShrinkEngine authority; this pass restores high-confidence
            // curve-tool strokes whose pen width should follow QUALITY rather than object scale.
            .Where(stroke =>
                stroke.BoundaryRatio >= 0.40 ||
                stroke.FillRatio <= 0.36 ||
                Math.Max(
                    stroke.Width,
                    stroke.Height) >=
                Math.Min(
                    stroke.Width,
                    stroke.Height) * 2.8)
            .ToArray();

        if (strokes.Length == 0)
            return;

        var strokeArea =
            strokes.Sum(stroke =>
                (long)stroke.Area);
        var designArea =
            (long)source.Width *
            source.Height;

        // Do not run a global curve rewrite on a raster with only incidental line fragments.
        // The four curve-tool training designs sit well above this coverage threshold.
        if (strokeArea <
            designArea * 0.025)
        {
            return;
        }

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var qualityX =
            targetWarpDensity /
            (double)Math.Max(
                1,
                sourceWarpDensity);
        var qualityY =
            targetWeftDensity /
            (double)Math.Max(
                1,
                sourceWeftDensity);

        // Shrink refinement is deliberately ADDITIVE. The generic RugScale shrinker already
        // owns fills, repeat phase, hard symmetry and topology. Erasing its projected stroke
        // residue before redraw caused curve-heavy real designs to fragment into thousands of
        // tiny components on 80% shrink -> enlarge round-trips. Here we only restore source-backed
        // curve centre-lines at the quality-owned pen width, leaving the proven baseline intact.
        // Enlargement is different: there the block-expanded source pen must be removed first, so
        // Enlarge still uses the stroke-free source layer below.
        //
        // Keep a transactional baseline. A strongly designer-symmetric source (B163A is ~97.7%
        // top/bottom) may already have been improved by RugScale's candidate-backed symmetry pass.
        // Curve restoration is not allowed to undo that higher-level evidence.
        var baseline =
            CapturePixels(
                destination);
        var sourceLr =
            BestMirrorAgreement(
                source,
                leftRight: true);
        var sourceTb =
            BestMirrorAgreement(
                source,
                leftRight: false);
        var baselineLr =
            BestMirrorAgreement(
                destination,
                leftRight: true);
        var baselineTb =
            BestMirrorAgreement(
                destination,
                leftRight: false);

        foreach (var stroke in strokes
                     .OrderByDescending(item =>
                         item.EstimatedThickness)
                     .ThenByDescending(item =>
                         item.Area))
        {
            DrawStroke(
                destination,
                stroke,
                scaleX,
                scaleY,
                qualityX,
                qualityY);
        }

        EnforceExactSourceSymmetry(
            source,
            destination);

        var refinedLr =
            BestMirrorAgreement(
                destination,
                leftRight: true);
        var refinedTb =
            BestMirrorAgreement(
                destination,
                leftRight: false);

        const double StrongDesignerSymmetry = 0.955;
        const double AllowedSymmetryLoss = 0.004;

        var damagedStrongAxis =
            (sourceLr >= StrongDesignerSymmetry &&
             refinedLr + AllowedSymmetryLoss < baselineLr) ||
            (sourceTb >= StrongDesignerSymmetry &&
             refinedTb + AllowedSymmetryLoss < baselineTb);

        if (damagedStrongAxis)
        {
            RestorePixels(
                destination,
                baseline);
        }
    }

    public static void Enlarge(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var qualityX =
            targetWarpDensity > 0 &&
            sourceWarpDensity > 0
                ? targetWarpDensity /
                  (double)sourceWarpDensity
                : 1d;
        var qualityY =
            targetWeftDensity > 0 &&
            sourceWeftDensity > 0
                ? targetWeftDensity /
                  (double)sourceWeftDensity
                : 1d;

        // Separate PHYSICAL enlargement from a density/quality increase. At <= ~1.35x physical
        // enlargement, centre-nearest already keeps the four real N69 designs' measured stroke
        // width within roughly 7% of source while preserving connected components exactly. The
        // source-layer redraw is reserved for larger physical growth where nearest would genuinely
        // multiply the pen width. This prevents unnecessary re-vectorization of already-good
        // moderate enlargements such as 160 -> 190/200 cm.
        var physicalScale =
            Math.Max(
                scaleX /
                Math.Max(
                    1e-9,
                    qualityX),
                scaleY /
                Math.Max(
                    1e-9,
                    qualityY));

        var qualityChanged =
            Math.Abs(qualityX - 1d) > 0.02 ||
            Math.Abs(qualityY - 1d) > 0.02;

        var strokes = DetectStrokeComponents(
            source,
            sourceWarpDensity,
            sourceWeftDensity);

        var strokeArea =
            strokes.Sum(stroke =>
                (long)stroke.Area);
        var designArea =
            (long)source.Width *
            source.Height;
        var curveCoverage =
            strokeArea /
            (double)Math.Max(
                1L,
                designArea);

        // The four real N69 fixtures are used as a hard calibration gate. Source-guided
        // redraw is already clearly preferable for large physical enlargement (the 160% audit),
        // but the 125% shrink->roundtrip experiment still scores better with conservative centre
        // sampling. Keep that middle band conservative until the curve fitter beats the baseline;
        // NEVER force a trained-looking redraw merely because it is more sophisticated.
        //
        // A quality change is different: warp/weft changed, so the source pen must be
        // re-rasterized even if physical size barely moves.
        var shouldRedrawCurves =
            strokes.Count > 0 &&
            curveCoverage >= 0.018 &&
            (qualityChanged ||
             physicalScale >= 1.45);

        if (!shouldRedrawCurves)
        {
            ScaleNearest(
                source,
                destination);
            EnforceExactSourceSymmetry(
                source,
                destination);
            return;
        }

        var strokeLayer =
            BuildStrokeFreeSource(
                source,
                strokes);

        // Treat detected curves as a source "pen layer": first scale the coherent field/fill layer,
        // then draw the original curve centreline at target geometry. This avoids the old
        // per-component erase pass that could leave thousands of tiny colour islands.
        ScaleNearest(
            strokeLayer.Background,
            destination);

        // Structural / wider strokes first, fine accents last. That matches the way indexed carpet
        // artwork is drawn and prevents a broad outline from painting over a one-pixel vein.
        foreach (var stroke in strokes
                     .OrderByDescending(item =>
                         item.EstimatedThickness)
                     .ThenByDescending(item =>
                         item.Area))
        {
            DrawStroke(
                destination,
                stroke,
                scaleX,
                scaleY,
                qualityX,
                qualityY);
        }

        // Projection rounding must never destroy an exact designer axis that existed in source.
        // This is intentionally exact-only; near-symmetry remains untouched so deliberate
        // asymmetric ornament is not mirrored into existence.
        EnforceExactSourceSymmetry(
            source,
            destination);
    }

    private static byte[] CapturePixels(
        DesignDocument document)
    {
        var pixels =
            new byte[
                checked(
                    document.Width *
                    document.Height)];

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                pixels[
                    y *
                    document.Width +
                    x] =
                    document.GetPixel(
                        x,
                        y);
            }
        }

        return pixels;
    }

    private static void RestorePixels(
        DesignDocument document,
        IReadOnlyList<byte> pixels)
    {
        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                document.SetPixel(
                    x,
                    y,
                    pixels[
                        y *
                        document.Width +
                        x]);
            }
        }
    }

    private static double BestMirrorAgreement(
        DesignDocument document,
        bool leftRight)
    {
        var best = 0d;
        var axisLength =
            leftRight
                ? document.Width
                : document.Height;
        var crossLength =
            leftRight
                ? document.Height
                : document.Width;

        for (var shift = -3;
             shift <= 3;
             shift++)
        {
            var mirrorConstant =
                axisLength - 1 + shift;
            long compared = 0;
            long matches = 0;

            for (var cross = 0;
                 cross < crossLength;
                 cross++)
            {
                for (var axis = 0;
                     axis < axisLength;
                     axis++)
                {
                    var mirrorAxis =
                        mirrorConstant -
                        axis;
                    if (mirrorAxis < 0 ||
                        mirrorAxis >= axisLength)
                    {
                        continue;
                    }

                    var first =
                        leftRight
                            ? document.GetPixel(
                                axis,
                                cross)
                            : document.GetPixel(
                                cross,
                                axis);
                    var second =
                        leftRight
                            ? document.GetPixel(
                                mirrorAxis,
                                cross)
                            : document.GetPixel(
                                cross,
                                mirrorAxis);

                    compared++;
                    if (first == second)
                        matches++;
                }
            }

            if (compared == 0)
                continue;

            best =
                Math.Max(
                    best,
                    matches /
                    (double)compared);
        }

        return best;
    }

    private static void EnforceExactSourceSymmetry(
        DesignDocument source,
        DesignDocument destination)
    {
        // Exact designer symmetry may be phase-shifted by one technical/sentinel
        // row/column in indexed rug BMPs. RugScale already treats such exact ±3px mirror evidence
        // as a hard constraint; curve redraw must do the same or it can overwrite the final hard
        // symmetry pass. The target is deliberately re-centred after resize.
        var leftRight =
            TryFindExactMirrorShift(
                source,
                leftRight: true,
                out _);
        var topBottom =
            TryFindExactMirrorShift(
                source,
                leftRight: false,
                out _);

        if (leftRight)
        {
            for (var y = 0;
                 y < destination.Height;
                 y++)
            {
                for (var x = 0;
                     x < destination.Width / 2;
                     x++)
                {
                    destination.SetPixel(
                        destination.Width - 1 - x,
                        y,
                        destination.GetPixel(
                            x,
                            y));
                }
            }
        }

        if (topBottom)
        {
            for (var y = 0;
                 y < destination.Height / 2;
                 y++)
            {
                var mirrorY =
                    destination.Height - 1 - y;

                for (var x = 0;
                     x < destination.Width;
                     x++)
                {
                    destination.SetPixel(
                        x,
                        mirrorY,
                        destination.GetPixel(
                            x,
                            y));
                }
            }
        }
    }

    private static bool TryFindExactMirrorShift(
        DesignDocument source,
        bool leftRight,
        out int sourceShift)
    {
        const int MaxShift = 3;
        sourceShift = 0;

        var axisLength =
            leftRight
                ? source.Width
                : source.Height;
        var crossLength =
            leftRight
                ? source.Height
                : source.Width;

        for (var shift = -MaxShift;
             shift <= MaxShift;
             shift++)
        {
            var mirrorConstant =
                axisLength - 1 + shift;
            long compared = 0;
            var exact = true;

            for (var cross = 0;
                 cross < crossLength &&
                 exact;
                 cross++)
            {
                for (var axis = 0;
                     axis < axisLength;
                     axis++)
                {
                    var mirrorAxis =
                        mirrorConstant -
                        axis;

                    if (mirrorAxis < 0 ||
                        mirrorAxis >= axisLength)
                    {
                        continue;
                    }

                    compared++;

                    var first =
                        leftRight
                            ? source.GetPixel(
                                axis,
                                cross)
                            : source.GetPixel(
                                cross,
                                axis);
                    var second =
                        leftRight
                            ? source.GetPixel(
                                mirrorAxis,
                                cross)
                            : source.GetPixel(
                                cross,
                                mirrorAxis);

                    if (first == second)
                        continue;

                    exact = false;
                    break;
                }
            }

            // A ±1..3 phase may intentionally exclude only a technical edge. Never call a tiny
            // overlapping sliver "exact symmetry": require at least 98% axis coverage.
            var coverage =
                (axisLength -
                 Math.Abs(shift)) /
                (double)Math.Max(
                    1,
                    axisLength);

            if (!exact ||
                compared == 0 ||
                coverage < 0.98)
            {
                continue;
            }

            sourceShift = shift;
            return true;
        }

        return false;
    }

    private static List<StrokeComponent> DetectStrokeComponents(
        DesignDocument source,
        int sourceWarpDensity,
        int sourceWeftDensity)
    {
        // IMPORTANT: only narrow pen/curve strokes may be vector-like redrawn.
        // Earlier training used area/skeleton width from whole same-colour components and allowed
        // widths up to 12-14 px. On the four real N69 designs that wrongly classified filled leaf
        // lobes as "curves"; the clear+redraw pass hollowed those motifs. The real thin-structure
        // audit puts source curve strokes around 3.4-4.3 px mean thickness. Use a conservative
        // quality-relative ceiling (~12% of the smaller density, 4..6 px). Wider shapes remain
        // raster/motif authority and are never erased by CurveScaleEngine.
        var minimumDensity =
            Math.Max(
                1,
                Math.Min(
                    sourceWarpDensity,
                    sourceWeftDensity));
        var maximumStrokeThickness =
            Math.Clamp(
                (int)Math.Round(
                    minimumDensity * 0.12),
                4,
                6);
        var width = source.Width;
        var height = source.Height;
        var count = checked(width * height);
        var visited = new bool[count];
        var queue = new int[count];
        var result = new List<StrokeComponent>();

        ReadOnlySpan<int> dx =
            [-1, 0, 1, -1, 1, -1, 0, 1];
        ReadOnlySpan<int> dy =
            [-1, -1, -1, 0, 0, 1, 1, 1];

        var maximumArea =
            Math.Max(
                2500,
                count / 45);

        for (var start = 0;
             start < count;
             start++)
        {
            if (visited[start])
                continue;

            var sx = start % width;
            var sy = start / width;
            var color = source.GetPixel(sx, sy);

            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;

            var pixels = new List<int>();
            var minX = sx;
            var minY = sy;
            var maxX = sx;
            var maxY = sy;
            var boundary = 0;

            while (head < tail)
            {
                var pixel = queue[head++];
                pixels.Add(pixel);

                var x = pixel % width;
                var y = pixel / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);

                var isBoundary = false;

                for (var direction = 0;
                     direction < 8;
                     direction++)
                {
                    var nx = x + dx[direction];
                    var ny = y + dy[direction];

                    if (nx < 0 ||
                        nx >= width ||
                        ny < 0 ||
                        ny >= height)
                    {
                        isBoundary = true;
                        continue;
                    }

                    var next =
                        ny * width + nx;
                    if (source.GetPixel(nx, ny) != color)
                    {
                        isBoundary = true;
                        continue;
                    }

                    if (visited[next])
                        continue;

                    visited[next] = true;
                    queue[tail++] = next;
                }

                if (isBoundary)
                    boundary++;
            }

            if (pixels.Count < MinimumStrokePixels ||
                pixels.Count > maximumArea)
            {
                continue;
            }

            var componentWidth =
                maxX - minX + 1;
            var componentHeight =
                maxY - minY + 1;

            if (Math.Max(
                    componentWidth,
                    componentHeight) < 4)
            {
                continue;
            }

            var localCount =
                checked(
                    componentWidth *
                    componentHeight);
            var mask =
                new bool[localCount];

            foreach (var pixel in pixels)
            {
                var x = pixel % width;
                var y = pixel / width;
                mask[
                    (y - minY) *
                    componentWidth +
                    (x - minX)] = true;
            }

            var skeleton =
                ThinZhangSuen(
                    mask,
                    componentWidth,
                    componentHeight);
            var skeletonCount =
                skeleton.Count(static value => value);

            if (skeletonCount < 3)
                continue;

            // Two different width estimates serve two different jobs:
            //
            // 1) perimeterThickness is a conservative CLASSIFIER shared with the real-raster
            //    audit. It is excellent at rejecting broad filled leaf lobes.
            // 2) skeletonThickness is the REDRAW pen width. For a one-pixel source curve,
            //    area/skeleton-length stays ~1 px; using perimeterThickness for drawing made that
            //    same 1px curve two pixels wide even at unchanged quality.
            var perimeterCenterlineEstimate =
                Math.Max(
                    1d,
                    boundary * 0.5);
            var perimeterThickness =
                pixels.Count /
                perimeterCenterlineEstimate;
            var skeletonThickness =
                pixels.Count /
                (double)Math.Max(
                    1,
                    skeletonCount);
            var boundaryRatio =
                boundary /
                (double)pixels.Count;
            var fillRatio =
                pixels.Count /
                (double)Math.Max(
                    1,
                    localCount);
            var aspect =
                Math.Max(
                    componentWidth,
                    componentHeight) /
                (double)Math.Max(
                    1,
                    Math.Min(
                        componentWidth,
                        componentHeight));

            var curveLike =
                perimeterThickness <= maximumStrokeThickness &&
                boundaryRatio >= 0.38 &&
                (fillRatio <= 0.42 ||
                 aspect >= 2.25);

            if (!curveLike)
                continue;

            // A single component-wide thickness loses exactly the information the carpet designer
            // encoded with the Curve tool: a stroke may taper, widen around a curl, then become
            // thin again. Recover the ORIGINAL local pen width from source pixels instead. The
            // padded chamfer distance transform measures distance from skeleton to source
            // background; 2*d-1 maps digital centre distance back to an indexed-pixel pen width.
            var localThickness =
                EstimateSkeletonThickness(
                    mask,
                    skeleton,
                    componentWidth,
                    componentHeight,
                    maximumStrokeThickness);

            var measuredThickness =
                localThickness
                    .Where(static value => value > 0)
                    .DefaultIfEmpty(
                        Math.Clamp(
                            skeletonThickness,
                            1d,
                            maximumStrokeThickness))
                    .Average();

            result.Add(new StrokeComponent
            {
                Color = color,
                Pixels = pixels,
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Mask = mask,
                Skeleton = skeleton,
                SkeletonCount = skeletonCount,
                LocalThickness = localThickness,
                EstimatedThickness =
                    Math.Clamp(
                        measuredThickness,
                        1d,
                        maximumStrokeThickness),
                BoundaryRatio = boundaryRatio,
                FillRatio = fillRatio,
            });
        }

        return result;
    }

    private sealed record StrokeLayer(
        DesignDocument Background,
        bool[] StrokeMask);

    private static StrokeLayer BuildStrokeFreeSource(
        DesignDocument source,
        IReadOnlyCollection<StrokeComponent> strokes)
    {
        var width = source.Width;
        var height = source.Height;
        var count = checked(
            width *
            height);
        var strokeMask =
            new bool[count];

        foreach (var stroke in strokes)
        {
            foreach (var pixel in stroke.Pixels)
            {
                if ((uint)pixel <
                    (uint)strokeMask.Length)
                {
                    strokeMask[pixel] = true;
                }
            }
        }

        var background =
            new DesignDocument(
                width,
                height,
                source.Palette);
        var owner =
            new byte[count];
        var assigned =
            new bool[count];
        var queue =
            new int[count];
        var head = 0;
        var tail = 0;

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var index =
                    y *
                    width +
                    x;
                var color =
                    source.GetPixel(
                        x,
                        y);
                background.SetPixel(
                    x,
                    y,
                    color);

                if (strokeMask[index])
                    continue;

                var bordersStroke =
                    (x > 0 &&
                     strokeMask[index - 1]) ||
                    (x + 1 < width &&
                     strokeMask[index + 1]) ||
                    (y > 0 &&
                     strokeMask[index - width]) ||
                    (y + 1 < height &&
                     strokeMask[index + width]);

                if (!bordersStroke)
                    continue;

                assigned[index] = true;
                owner[index] = color;
                queue[tail++] = index;
            }
        }

        ReadOnlySpan<int> dx =
            [-1, 1, 0, 0];
        ReadOnlySpan<int> dy =
            [0, 0, -1, 1];

        while (head < tail)
        {
            var pixel =
                queue[head++];
            var x =
                pixel %
                width;
            var y =
                pixel /
                width;
            var color =
                owner[pixel];

            for (var direction = 0;
                 direction < 4;
                 direction++)
            {
                var nx =
                    x +
                    dx[direction];
                var ny =
                    y +
                    dy[direction];

                if (nx < 0 ||
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                var next =
                    ny *
                    width +
                    nx;

                if (!strokeMask[next] ||
                    assigned[next])
                {
                    continue;
                }

                assigned[next] = true;
                owner[next] = color;
                queue[tail++] = next;
            }
        }

        for (var index = 0;
             index < count;
             index++)
        {
            if (!strokeMask[index] ||
                !assigned[index])
            {
                continue;
            }

            background.SetPixel(
                index % width,
                index / width,
                owner[index]);
        }

        return new StrokeLayer(
            background,
            strokeMask);
    }

    private static void RestoreBackgroundUnderStrokeResidue(
        DesignDocument destination,
        DesignDocument background,
        bool[] sourceStrokeMask,
        int sourceWidth,
        int sourceHeight)
    {
        for (var ty = 0;
             ty < destination.Height;
             ty++)
        {
            var sy = Math.Clamp(
                (int)Math.Floor(
                    (ty + 0.5) *
                    sourceHeight /
                    destination.Height),
                0,
                sourceHeight - 1);

            for (var tx = 0;
                 tx < destination.Width;
                 tx++)
            {
                var sx = Math.Clamp(
                    (int)Math.Floor(
                        (tx + 0.5) *
                        sourceWidth /
                        destination.Width),
                    0,
                    sourceWidth - 1);

                if (!sourceStrokeMask[
                        sy *
                        sourceWidth +
                        sx])
                {
                    continue;
                }

                destination.SetPixel(
                    tx,
                    ty,
                    background.GetPixel(
                        tx,
                        ty));
            }
        }
    }

    private static void ClearExpandedStroke(
        DesignDocument source,
        DesignDocument destination,
        StrokeComponent stroke,
        double scaleX,
        double scaleY)
    {
        foreach (var sourcePixel in stroke.Pixels)
        {
            var sx = sourcePixel % source.Width;
            var sy = sourcePixel / source.Width;

            var x0 = Math.Clamp(
                (int)Math.Floor(
                    sx * scaleX),
                0,
                destination.Width - 1);
            var x1 = Math.Clamp(
                (int)Math.Ceiling(
                    (sx + 1) * scaleX),
                x0 + 1,
                destination.Width);
            var y0 = Math.Clamp(
                (int)Math.Floor(
                    sy * scaleY),
                0,
                destination.Height - 1);
            var y1 = Math.Clamp(
                (int)Math.Ceiling(
                    (sy + 1) * scaleY),
                y0 + 1,
                destination.Height);

            for (var ty = y0;
                 ty < y1;
                 ty++)
            {
                for (var tx = x0;
                     tx < x1;
                     tx++)
                {
                    var sourceX = Math.Clamp(
                        (int)Math.Floor(
                            (tx + 0.5) /
                            scaleX),
                        0,
                        source.Width - 1);
                    var sourceY = Math.Clamp(
                        (int)Math.Floor(
                            (ty + 0.5) /
                            scaleY),
                        0,
                        source.Height - 1);

                    destination.SetPixel(
                        tx,
                        ty,
                        FindNearestNonStrokeColor(
                            source,
                            sourceX,
                            sourceY,
                            stroke.Color));
                }
            }
        }
    }

    private static byte FindNearestNonStrokeColor(
        DesignDocument source,
        int centerX,
        int centerY,
        byte strokeColor)
    {
        // Hot path during real-raster enlargement: this can run for many thousands of source
        // stroke pixels. Keep the 256-slot histogram stack-local to avoid one heap allocation per
        // curve pixel.
        Span<ushort> counts =
            stackalloc ushort[PaletteSlots];
        counts.Clear();

        for (var radius = 1;
             radius <= 3;
             radius++)
        {
            var any = false;

            for (var y =
                     Math.Max(
                         0,
                         centerY - radius);
                 y <=
                 Math.Min(
                     source.Height - 1,
                     centerY + radius);
                 y++)
            {
                for (var x =
                         Math.Max(
                             0,
                             centerX - radius);
                     x <=
                     Math.Min(
                         source.Width - 1,
                         centerX + radius);
                     x++)
                {
                    if (Math.Max(
                            Math.Abs(
                                x - centerX),
                            Math.Abs(
                                y - centerY)) != radius)
                    {
                        continue;
                    }

                    var color =
                        source.GetPixel(x, y);
                    if (color == strokeColor)
                        continue;

                    if (counts[color] < ushort.MaxValue)
                        counts[color]++;
                    any = true;
                }
            }

            if (!any)
                continue;

            var bestColor = 0;
            var bestCount = 0;

            for (var color = 0;
                 color < PaletteSlots;
                 color++)
            {
                if (counts[color] <= bestCount)
                    continue;

                bestCount = counts[color];
                bestColor = color;
            }

            return (byte)bestColor;
        }

        // Extremely unusual isolated full-colour island. Keeping the existing colour is safer than
        // inventing a palette role or guessing a distant field.
        return strokeColor;
    }

    private static void DrawStroke(
        DesignDocument destination,
        StrokeComponent stroke,
        double scaleX,
        double scaleY,
        double qualityX,
        double qualityY)
    {
        var localWidth =
            stroke.Width;
        var localHeight =
            stroke.Height;

        ReadOnlySpan<int> neighborX =
            [1, 0, 1, -1];
        ReadOnlySpan<int> neighborY =
            [0, 1, 1, 1];

        for (var localY = 0;
             localY < localHeight;
             localY++)
        {
            for (var localX = 0;
                 localX < localWidth;
                 localX++)
            {
                var local =
                    localY *
                    localWidth +
                    localX;
                if (!stroke.Skeleton[local])
                    continue;

                var sourceX =
                    stroke.MinX +
                    localX;
                var sourceY =
                    stroke.MinY +
                    localY;
                var targetX =
                    MapCenter(
                        sourceX,
                        scaleX,
                        destination.Width);
                var targetY =
                    MapCenter(
                        sourceY,
                        scaleY,
                        destination.Height);

                var sourcePen =
                    local < stroke.LocalThickness.Length &&
                    stroke.LocalThickness[local] > 0
                        ? stroke.LocalThickness[local]
                        : stroke.EstimatedThickness;
                var diameterX =
                    QuantizePenDiameter(
                        sourcePen *
                        qualityX);
                var diameterY =
                    QuantizePenDiameter(
                        sourcePen *
                        qualityY);

                StampEllipse(
                    destination,
                    targetX,
                    targetY,
                    diameterX,
                    diameterY,
                    stroke.Color);

                // Connect source skeleton neighbours AFTER projection. This is the discrete curve
                // equivalent of scaling a vector path: large physical enlargement inserts target
                // pixels along the arc instead of stretching each source pixel into a rectangle.
                for (var direction = 0;
                     direction < neighborX.Length;
                     direction++)
                {
                    var nx =
                        localX +
                        neighborX[direction];
                    var ny =
                        localY +
                        neighborY[direction];

                    if (nx < 0 ||
                        nx >= localWidth ||
                        ny < 0 ||
                        ny >= localHeight ||
                        !stroke.Skeleton[
                            ny *
                            localWidth +
                            nx])
                    {
                        continue;
                    }

                    var targetNeighborX =
                        MapCenter(
                            stroke.MinX + nx,
                            scaleX,
                            destination.Width);
                    var targetNeighborY =
                        MapCenter(
                            stroke.MinY + ny,
                            scaleY,
                            destination.Height);

                    var neighborLocal =
                        ny *
                        localWidth +
                        nx;
                    var neighborPen =
                        neighborLocal < stroke.LocalThickness.Length &&
                        stroke.LocalThickness[neighborLocal] > 0
                            ? stroke.LocalThickness[neighborLocal]
                            : sourcePen;
                    var connectionPen =
                        (sourcePen +
                         neighborPen) *
                        0.5;

                    DrawStampedLine(
                        destination,
                        targetX,
                        targetY,
                        targetNeighborX,
                        targetNeighborY,
                        QuantizePenDiameter(
                            connectionPen *
                            qualityX),
                        QuantizePenDiameter(
                            connectionPen *
                            qualityY),
                        stroke.Color);
                }
            }
        }
    }

    private static int QuantizePenDiameter(
        double desired) =>
        Math.Clamp(
            (int)Math.Round(
                desired,
                MidpointRounding.AwayFromZero),
            1,
            24);

    private static double[] EstimateSkeletonThickness(
        bool[] mask,
        bool[] skeleton,
        int width,
        int height,
        int maximumThickness)
    {
        // Padding makes document/component edges behave like real background rather than giving a
        // border-touching stroke an infinite radius.
        var paddedWidth =
            width + 2;
        var paddedHeight =
            height + 2;
        var distance =
            new double[
                paddedWidth *
                paddedHeight];
        const double Infinity = 1_000_000d;
        var diagonal =
            Math.Sqrt(2d);

        Array.Fill(
            distance,
            0d);

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                if (!mask[
                        y *
                        width +
                        x])
                {
                    continue;
                }

                distance[
                    (y + 1) *
                    paddedWidth +
                    (x + 1)] =
                    Infinity;
            }
        }

        // Forward chamfer pass.
        for (var y = 1;
             y <= height;
             y++)
        {
            for (var x = 1;
                 x <= width;
                 x++)
            {
                var index =
                    y *
                    paddedWidth +
                    x;
                if (distance[index] <= 0)
                    continue;

                distance[index] =
                    Math.Min(
                        distance[index],
                        Math.Min(
                            Math.Min(
                                distance[index - 1] + 1d,
                                distance[index - paddedWidth] + 1d),
                            Math.Min(
                                distance[index - paddedWidth - 1] + diagonal,
                                distance[index - paddedWidth + 1] + diagonal)));
            }
        }

        // Backward chamfer pass.
        for (var y = height;
             y >= 1;
             y--)
        {
            for (var x = width;
                 x >= 1;
                 x--)
            {
                var index =
                    y *
                    paddedWidth +
                    x;
                if (distance[index] <= 0)
                    continue;

                distance[index] =
                    Math.Min(
                        distance[index],
                        Math.Min(
                            Math.Min(
                                distance[index + 1] + 1d,
                                distance[index + paddedWidth] + 1d),
                            Math.Min(
                                distance[index + paddedWidth + 1] + diagonal,
                                distance[index + paddedWidth - 1] + diagonal)));
            }
        }

        var result =
            new double[
                width *
                height];

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var local =
                    y *
                    width +
                    x;
                if (!skeleton[local])
                    continue;

                var d =
                    distance[
                        (y + 1) *
                        paddedWidth +
                        (x + 1)];

                // Digital pen width: 1px centre has d=1 => 1; a 3px centred ribbon has d=2 => 3.
                // Diagonal strokes naturally produce fractional widths, which round only at the
                // final target rasterization step.
                result[local] =
                    Math.Clamp(
                        2d *
                        d -
                        1d,
                        1d,
                        maximumThickness);
            }
        }

        return result;
    }

    private static int MapCenter(
        int sourceCoordinate,
        double scale,
        int targetLength) =>
        Math.Clamp(
            (int)Math.Round(
                (sourceCoordinate + 0.5) *
                scale -
                0.5),
            0,
            targetLength - 1);

    private static void DrawStampedLine(
        DesignDocument destination,
        int x0,
        int y0,
        int x1,
        int y1,
        int diameterX,
        int diameterY,
        byte color)
    {
        var dx =
            x1 - x0;
        var dy =
            y1 - y0;
        var steps =
            Math.Max(
                Math.Abs(dx),
                Math.Abs(dy));

        if (steps <= 0)
        {
            StampEllipse(
                destination,
                x0,
                y0,
                diameterX,
                diameterY,
                color);
            return;
        }

        for (var step = 0;
             step <= steps;
             step++)
        {
            var t =
                step /
                (double)steps;
            var x =
                (int)Math.Round(
                    x0 +
                    dx * t);
            var y =
                (int)Math.Round(
                    y0 +
                    dy * t);

            StampEllipse(
                destination,
                x,
                y,
                diameterX,
                diameterY,
                color);
        }
    }

    private static void StampEllipse(
        DesignDocument destination,
        int centerX,
        int centerY,
        int diameterX,
        int diameterY,
        byte color)
    {
        var halfX =
            (diameterX - 1) * 0.5;
        var halfY =
            (diameterY - 1) * 0.5;
        var radiusX =
            Math.Max(
                0.5,
                diameterX * 0.5);
        var radiusY =
            Math.Max(
                0.5,
                diameterY * 0.5);

        var minX =
            (int)Math.Floor(
                centerX - halfX);
        var maxX =
            (int)Math.Ceiling(
                centerX + halfX);
        var minY =
            (int)Math.Floor(
                centerY - halfY);
        var maxY =
            (int)Math.Ceiling(
                centerY + halfY);

        for (var y = minY;
             y <= maxY;
             y++)
        {
            if (y < 0 ||
                y >= destination.Height)
            {
                continue;
            }

            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                if (x < 0 ||
                    x >= destination.Width)
                {
                    continue;
                }

                var nx =
                    (x - centerX) /
                    radiusX;
                var ny =
                    (y - centerY) /
                    radiusY;

                if (nx * nx +
                    ny * ny <= 1.05)
                {
                    destination.SetPixel(
                        x,
                        y,
                        color);
                }
            }
        }
    }

    internal static bool[] ThinZhangSuen(
        bool[] source,
        int width,
        int height)
    {
        var pixels =
            source.ToArray();

        if (width < 3 ||
            height < 3)
        {
            return pixels;
        }

        var remove =
            new List<int>();
        var changed = true;
        var iterations = 0;
        var maximumIterations =
            Math.Max(
                width,
                height) * 2;

        while (changed &&
               iterations++ <
               maximumIterations)
        {
            changed = false;
            remove.Clear();

            for (var y = 1;
                 y < height - 1;
                 y++)
            {
                for (var x = 1;
                     x < width - 1;
                     x++)
                {
                    var index =
                        y * width + x;
                    if (!pixels[index])
                        continue;

                    Span<bool> n =
                    [
                        pixels[(y - 1) * width + x],
                        pixels[(y - 1) * width + x + 1],
                        pixels[y * width + x + 1],
                        pixels[(y + 1) * width + x + 1],
                        pixels[(y + 1) * width + x],
                        pixels[(y + 1) * width + x - 1],
                        pixels[y * width + x - 1],
                        pixels[(y - 1) * width + x - 1],
                    ];

                    var neighbors =
                        CountTrue(n);
                    var transitions =
                        CountTransitions(n);

                    if (neighbors is >= 2 and <= 6 &&
                        transitions == 1 &&
                        !(n[0] && n[2] && n[4]) &&
                        !(n[2] && n[4] && n[6]))
                    {
                        remove.Add(index);
                    }
                }
            }

            if (remove.Count > 0)
            {
                changed = true;
                foreach (var index in remove)
                    pixels[index] = false;
            }

            remove.Clear();

            for (var y = 1;
                 y < height - 1;
                 y++)
            {
                for (var x = 1;
                     x < width - 1;
                     x++)
                {
                    var index =
                        y * width + x;
                    if (!pixels[index])
                        continue;

                    Span<bool> n =
                    [
                        pixels[(y - 1) * width + x],
                        pixels[(y - 1) * width + x + 1],
                        pixels[y * width + x + 1],
                        pixels[(y + 1) * width + x + 1],
                        pixels[(y + 1) * width + x],
                        pixels[(y + 1) * width + x - 1],
                        pixels[y * width + x - 1],
                        pixels[(y - 1) * width + x - 1],
                    ];

                    var neighbors =
                        CountTrue(n);
                    var transitions =
                        CountTransitions(n);

                    if (neighbors is >= 2 and <= 6 &&
                        transitions == 1 &&
                        !(n[0] && n[2] && n[6]) &&
                        !(n[0] && n[4] && n[6]))
                    {
                        remove.Add(index);
                    }
                }
            }

            if (remove.Count > 0)
            {
                changed = true;
                foreach (var index in remove)
                    pixels[index] = false;
            }
        }

        return pixels;
    }

    private static int CountTrue(
        ReadOnlySpan<bool> values)
    {
        var count = 0;

        foreach (var value in values)
        {
            if (value)
                count++;
        }

        return count;
    }

    private static int CountTransitions(
        ReadOnlySpan<bool> values)
    {
        var transitions = 0;

        for (var i = 0;
             i < values.Length;
             i++)
        {
            if (!values[i] &&
                values[
                    (i + 1) %
                    values.Length])
            {
                transitions++;
            }
        }

        return transitions;
    }

    private static void ScaleNearest(
        DesignDocument source,
        DesignDocument destination)
    {
        for (var ty = 0;
             ty < destination.Height;
             ty++)
        {
            var sy = Math.Clamp(
                (int)Math.Floor(
                    (ty + 0.5) *
                    source.Height /
                    destination.Height),
                0,
                source.Height - 1);

            for (var tx = 0;
                 tx < destination.Width;
                 tx++)
            {
                var sx = Math.Clamp(
                    (int)Math.Floor(
                        (tx + 0.5) *
                        source.Width /
                        destination.Width),
                    0,
                    source.Width - 1);

                destination.SetPixel(
                    tx,
                    ty,
                    source.GetPixel(
                        sx,
                        sy));
            }
        }
    }
}
