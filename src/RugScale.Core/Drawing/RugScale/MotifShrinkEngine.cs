using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Motif-first reconstruction pass used by RugScale while shrinking.
///
/// The normal raster resampler answers "which source colour owns this target pixel?". Carpet
/// artwork needs the inverse question as well: "what happened to every source motif?". This pass
/// therefore catalogues same-colour connected motif parts, groups equal geometry into colour-
/// independent shape families, projects one canonical family mask to the requested target size,
/// and reuses that geometry for every family instance with the instance's own palette index.
///
/// Large field/background/frame regions are deliberately left to RugScale's normal region/repeat
/// engine. Decorative connected parts are removed from the centre-nearest baseline and painted
/// back from their source geometry. Forward projection of an 8-connected source component is
/// monotone, so a connected branch cannot fragment merely because no target sample happened to
/// land on it.
/// </summary>
internal static class MotifShrinkEngine
{
    private const int PaletteSlots = 256;
    private const int SignatureSize = 16;

    private sealed class Region
    {
        public byte Color;
        public int Area;
        public int MinX = int.MaxValue;
        public int MinY = int.MaxValue;
        public int MaxX = -1;
        public int MaxY = -1;
        public int BoundaryPixels;
        public byte BackgroundColor;
        public bool IsStructural;

        public int Width => MaxX >= MinX ? MaxX - MinX + 1 : 0;
        public int Height => MaxY >= MinY ? MaxY - MinY + 1 : 0;
    }

    private readonly record struct FamilyKey(
        int AspectBucket,
        int FillBucket,
        ulong Bits0,
        ulong Bits1,
        ulong Bits2,
        ulong Bits3);

    private readonly record struct TemplateKey(
        FamilyKey Family,
        int Width,
        int Height);

    private sealed class Atlas
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int[] RegionIds { get; init; }
        public required Region[] Regions { get; init; }
        public required int[] RegionOffsets { get; init; }
        public required int[] PixelsByRegion { get; init; }
        public required int[] ColorCounts { get; init; }
        public required bool[] IsMotifRegion { get; init; }
        public required FamilyKey[] Families { get; init; }
        public required int[] FamilyRepresentative { get; init; }
        public required int[] FamilyOccurrenceCount { get; init; }
    }

    public static void Reconstruct(
        DesignDocument source,
        DesignDocument destination,
        bool[]? protectedCells = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Width >= source.Width ||
            destination.Height >= source.Height)
        {
            return;
        }

        var atlas = BuildAtlas(source);
        if (!atlas.IsMotifRegion.Any(static value => value))
            return;

        var scaleX = destination.Width / (double)source.Width;
        var scaleY = destination.Height / (double)source.Height;

        // Remove the centre-nearest remnants of motif components first. Otherwise a clean
        // projected motif can be surrounded by stale one-pixel fragments from the old sampler.
        ClearBaselineMotifResidue(
            source,
            destination,
            atlas,
            protectedCells);

        var cellPriority = new float[
            checked(destination.Width * destination.Height)];
        Array.Fill(cellPriority, float.NegativeInfinity);

        var templateCache =
            new Dictionary<TemplateKey, bool[]>();

        // Structural fills go first; tiny/high-boundary motif parts paint later because their
        // priority is higher. This mirrors how a carpet designer draws a base fill before outline,
        // veins, buds and one-pixel accents.
        var regionOrder = Enumerable.Range(0, atlas.Regions.Length)
            .Where(regionId => atlas.IsMotifRegion[regionId])
            .OrderByDescending(regionId => atlas.Regions[regionId].Area)
            .ToArray();

        foreach (var regionId in regionOrder)
        {
            var region = atlas.Regions[regionId];
            var priority = MotifPriority(
                atlas,
                region);

            // Very small or genuinely narrow motif parts are harmed more by normalizing their
            // bounding box than they are helped by family-template reuse. At 48x50 -> 160x230,
            // examples such as 1x3, 1x4, 12x1 and 2x16 branches were the dominant B163A failures.
            // Project those sparse parts in global source space. They intentionally bypass the
            // earlier repeat-protected mask: canonical repeat establishes phase first, then exact
            // source-backed micro-details may be restored without allowing larger motif geometry
            // to repaint the repeat zone.
            if (ShouldProjectDirectly(
                    region))
            {
                PaintRegionDirectly(
                    source,
                    destination,
                    atlas,
                    regionId,
                    region.Color,
                    priority,
                    cellPriority,
                    protectedCells: null);
                continue;
            }

            var targetWidth = TargetExtent(
                region.Width,
                scaleX,
                region.Area,
                region.BoundaryPixels);
            var targetHeight = TargetExtent(
                region.Height,
                scaleY,
                region.Area,
                region.BoundaryPixels);

            targetWidth = Math.Clamp(
                targetWidth,
                1,
                destination.Width);
            targetHeight = Math.Clamp(
                targetHeight,
                1,
                destination.Height);

            var sourceCenterX =
                (region.MinX + region.MaxX + 1) * 0.5;
            var sourceCenterY =
                (region.MinY + region.MaxY + 1) * 0.5;

            var targetLeft = (int)Math.Round(
                sourceCenterX * scaleX -
                targetWidth * 0.5);
            var targetTop = (int)Math.Round(
                sourceCenterY * scaleY -
                targetHeight * 0.5);

            targetLeft = Math.Clamp(
                targetLeft,
                -targetWidth + 1,
                destination.Width - 1);
            targetTop = Math.Clamp(
                targetTop,
                -targetHeight + 1,
                destination.Height - 1);

            var family = atlas.Families[regionId];
            var representative =
                atlas.FamilyRepresentative[regionId];

            // Family geometry is reusable only when the representative and this instance have
            // effectively the same source extents. The normalized signature is intentionally
            // tolerant, but we never force a slightly different hand-drawn motif into another
            // instance's exact pixel geometry.
            var representativeRegion =
                atlas.Regions[representative];
            var reusable =
                atlas.FamilyOccurrenceCount[regionId] > 1 &&
                SimilarExtent(
                    region.Width,
                    representativeRegion.Width) &&
                SimilarExtent(
                    region.Height,
                    representativeRegion.Height);

            bool[] template;
            if (reusable)
            {
                var key = new TemplateKey(
                    family,
                    targetWidth,
                    targetHeight);

                if (!templateCache.TryGetValue(
                        key,
                        out template!))
                {
                    template = ProjectRegionMask(
                        atlas,
                        representative,
                        targetWidth,
                        targetHeight);
                    templateCache[key] = template;
                }
            }
            else
            {
                template = ProjectRegionMask(
                    atlas,
                    regionId,
                    targetWidth,
                    targetHeight);
            }

            PaintTemplate(
                destination,
                template,
                targetWidth,
                targetHeight,
                targetLeft,
                targetTop,
                region.Color,
                priority,
                cellPriority,
                protectedCells);
        }
    }

    private static Atlas BuildAtlas(
        DesignDocument source)
    {
        var width = source.Width;
        var height = source.Height;
        var pixelCount = checked(width * height);
        var sourcePixels = new byte[pixelCount];
        var colorCounts = new int[PaletteSlots];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var color = source.GetPixel(x, y);
                sourcePixels[row + x] = color;
                colorCounts[color]++;
            }
        }

        var regionIds = new int[pixelCount];
        Array.Fill(regionIds, -1);

        var regions = new List<Region>();
        var queue = new int[pixelCount];

        ReadOnlySpan<int> dx =
            [-1, 0, 1, -1, 1, -1, 0, 1];
        ReadOnlySpan<int> dy =
            [-1, -1, -1, 0, 0, 1, 1, 1];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = y * width + x;
                if (regionIds[start] >= 0)
                    continue;

                var regionId = regions.Count;
                var color = sourcePixels[start];
                var region = new Region
                {
                    Color = color,
                    BackgroundColor = color,
                };

                var head = 0;
                var tail = 0;
                queue[tail++] = start;
                regionIds[start] = regionId;

                while (head < tail)
                {
                    var cell = queue[head++];
                    var cx = cell % width;
                    var cy = cell / width;

                    region.Area++;
                    region.MinX = Math.Min(
                        region.MinX,
                        cx);
                    region.MaxX = Math.Max(
                        region.MaxX,
                        cx);
                    region.MinY = Math.Min(
                        region.MinY,
                        cy);
                    region.MaxY = Math.Max(
                        region.MaxY,
                        cy);

                    var boundary = false;

                    for (var direction = 0;
                         direction < 8;
                         direction++)
                    {
                        var nx = cx + dx[direction];
                        var ny = cy + dy[direction];

                        if (nx < 0 ||
                            nx >= width ||
                            ny < 0 ||
                            ny >= height)
                        {
                            boundary = true;
                            continue;
                        }

                        var next = ny * width + nx;
                        if (sourcePixels[next] != color)
                        {
                            boundary = true;
                            continue;
                        }

                        if (regionIds[next] >= 0)
                            continue;

                        regionIds[next] = regionId;
                        queue[tail++] = next;
                    }

                    if (boundary)
                        region.BoundaryPixels++;
                }

                regions.Add(region);
            }
        }

        var regionArray = regions.ToArray();
        var total = (long)width * height;

        for (var regionId = 0;
             regionId < regionArray.Length;
             regionId++)
        {
            regionArray[regionId].IsStructural =
                IsStructuralRegion(
                    regionArray[regionId],
                    width,
                    height,
                    total);
        }

        var offsets =
            new int[regionArray.Length + 1];
        for (var regionId = 0;
             regionId < regionArray.Length;
             regionId++)
        {
            offsets[regionId + 1] =
                offsets[regionId] +
                regionArray[regionId].Area;
        }

        var pixelsByRegion =
            new int[pixelCount];
        var cursors =
            offsets[..^1].ToArray();

        for (var pixel = 0;
             pixel < pixelCount;
             pixel++)
        {
            var regionId = regionIds[pixel];
            pixelsByRegion[cursors[regionId]++] =
                pixel;
        }

        DetermineBoundaryColours(
            sourcePixels,
            width,
            height,
            regionIds,
            regionArray,
            offsets,
            pixelsByRegion);

        var isMotifRegion =
            new bool[regionArray.Length];
        for (var regionId = 0;
             regionId < regionArray.Length;
             regionId++)
        {
            var region = regionArray[regionId];

            // Every non-structural connected part is remembered, including one-pixel details.
            // Long border/frame/background components stay with RugScale's repeat/region passes.
            isMotifRegion[regionId] =
                !region.IsStructural &&
                region.Area > 0;
        }

        var families =
            new FamilyKey[regionArray.Length];
        var representativeByKey =
            new Dictionary<FamilyKey, int>();
        var occurrenceByKey =
            new Dictionary<FamilyKey, int>();

        for (var regionId = 0;
             regionId < regionArray.Length;
             regionId++)
        {
            if (!isMotifRegion[regionId])
                continue;

            var key = BuildFamilyKey(
                regionId,
                regionArray,
                offsets,
                pixelsByRegion,
                width);
            families[regionId] = key;

            if (!representativeByKey.ContainsKey(key))
                representativeByKey[key] = regionId;

            occurrenceByKey[key] =
                occurrenceByKey.GetValueOrDefault(key) + 1;
        }

        var familyRepresentative =
            new int[regionArray.Length];
        var familyOccurrenceCount =
            new int[regionArray.Length];

        for (var regionId = 0;
             regionId < regionArray.Length;
             regionId++)
        {
            if (!isMotifRegion[regionId])
            {
                familyRepresentative[regionId] =
                    regionId;
                familyOccurrenceCount[regionId] = 1;
                continue;
            }

            var key = families[regionId];
            familyRepresentative[regionId] =
                representativeByKey[key];
            familyOccurrenceCount[regionId] =
                occurrenceByKey[key];
        }

        return new Atlas
        {
            Width = width,
            Height = height,
            RegionIds = regionIds,
            Regions = regionArray,
            RegionOffsets = offsets,
            PixelsByRegion = pixelsByRegion,
            ColorCounts = colorCounts,
            IsMotifRegion = isMotifRegion,
            Families = families,
            FamilyRepresentative =
                familyRepresentative,
            FamilyOccurrenceCount =
                familyOccurrenceCount,
        };
    }

    private static bool IsStructuralRegion(
        Region region,
        int width,
        int height,
        long totalPixels)
    {
        var spanX =
            region.Width / (double)width;
        var spanY =
            region.Height / (double)height;
        var areaRatio =
            region.Area / (double)totalPixels;

        var touchesLeft =
            region.MinX == 0;
        var touchesRight =
            region.MaxX == width - 1;
        var touchesTop =
            region.MinY == 0;
        var touchesBottom =
            region.MaxY == height - 1;

        // Indexed BMP sentinel/seam scan-lines and true full-frame regions are infrastructure,
        // not decorative motifs.
        if ((touchesLeft && touchesRight) ||
            (touchesTop && touchesBottom))
        {
            return true;
        }

        if (areaRatio >= 0.060)
            return true;

        // Large field/frame fills. Central medallion parts normally have substantial area but do
        // not span most of a document axis, so they remain motif candidates.
        if (areaRatio >= 0.015 &&
            (spanX >= 0.55 ||
             spanY >= 0.55))
        {
            return true;
        }

        if (areaRatio >= 0.004 &&
            (spanX >= 0.85 ||
             spanY >= 0.85))
        {
            return true;
        }

        return false;
    }

    private static void DetermineBoundaryColours(
        byte[] sourcePixels,
        int width,
        int height,
        int[] regionIds,
        Region[] regions,
        int[] offsets,
        int[] pixelsByRegion)
    {
        var counts = new int[PaletteSlots];
        var touched = new List<byte>(16);

        ReadOnlySpan<int> dx = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, -1, 1];

        for (var regionId = 0;
             regionId < regions.Length;
             regionId++)
        {
            var region = regions[regionId];
            if (region.IsStructural)
                continue;

            touched.Clear();

            for (var position = offsets[regionId];
                 position < offsets[regionId + 1];
                 position++)
            {
                var cell =
                    pixelsByRegion[position];
                var x = cell % width;
                var y = cell / width;

                for (var direction = 0;
                     direction < 4;
                     direction++)
                {
                    var nx = x + dx[direction];
                    var ny = y + dy[direction];

                    if (nx < 0 ||
                        nx >= width ||
                        ny < 0 ||
                        ny >= height)
                    {
                        continue;
                    }

                    var neighbour =
                        ny * width + nx;
                    if (regionIds[neighbour] ==
                        regionId)
                    {
                        continue;
                    }

                    var color =
                        sourcePixels[neighbour];
                    if (counts[color] == 0)
                        touched.Add(color);
                    counts[color]++;
                }
            }

            var bestColor = region.Color;
            var bestCount = 0;

            foreach (var color in touched)
            {
                if (counts[color] > bestCount)
                {
                    bestCount = counts[color];
                    bestColor = color;
                }

                counts[color] = 0;
            }

            region.BackgroundColor = bestColor;
        }
    }

    private static FamilyKey BuildFamilyKey(
        int regionId,
        Region[] regions,
        int[] offsets,
        int[] pixelsByRegion,
        int sourceWidth)
    {
        var region = regions[regionId];
        Span<ulong> bits = stackalloc ulong[4];

        for (var position = offsets[regionId];
             position < offsets[regionId + 1];
             position++)
        {
            var cell = pixelsByRegion[position];
            var x =
                cell % sourceWidth -
                region.MinX;
            var y =
                cell / sourceWidth -
                region.MinY;

            var bx = Math.Clamp(
                x * SignatureSize /
                Math.Max(1, region.Width),
                0,
                SignatureSize - 1);
            var by = Math.Clamp(
                y * SignatureSize /
                Math.Max(1, region.Height),
                0,
                SignatureSize - 1);

            var bit = by * SignatureSize + bx;
            bits[bit >> 6] |=
                1UL << (bit & 63);
        }

        var aspect = region.Width /
                     (double)Math.Max(1, region.Height);
        var aspectBucket = Math.Clamp(
            (int)Math.Round(
                Math.Log2(
                    Math.Max(1d / 32d,
                        Math.Min(32d, aspect))) * 8d),
            -40,
            40);

        var fill = region.Area /
                   (double)Math.Max(
                       1,
                       region.Width *
                       region.Height);
        var fillBucket = Math.Clamp(
            (int)Math.Round(fill * 24d),
            0,
            24);

        return new FamilyKey(
            aspectBucket,
            fillBucket,
            bits[0],
            bits[1],
            bits[2],
            bits[3]);
    }

    private static bool[] ProjectRegionMask(
        Atlas atlas,
        int regionId,
        int targetWidth,
        int targetHeight)
    {
        var region =
            atlas.Regions[regionId];
        var mask =
            new bool[checked(targetWidth *
                             targetHeight)];

        for (var position =
                 atlas.RegionOffsets[regionId];
             position <
                 atlas.RegionOffsets[regionId + 1];
             position++)
        {
            var cell =
                atlas.PixelsByRegion[position];
            var sourceX =
                cell % atlas.Width -
                region.MinX;
            var sourceY =
                cell / atlas.Width -
                region.MinY;

            // Forward-project source pixel centres. Adjacent source pixels can only land in the
            // same or neighbouring target cell, so source connectivity survives the shrink.
            var targetX = Math.Clamp(
                (int)Math.Floor(
                    (sourceX + 0.5) *
                    targetWidth /
                    Math.Max(1d, region.Width)),
                0,
                targetWidth - 1);
            var targetY = Math.Clamp(
                (int)Math.Floor(
                    (sourceY + 0.5) *
                    targetHeight /
                    Math.Max(1d, region.Height)),
                0,
                targetHeight - 1);

            mask[targetY * targetWidth +
                 targetX] = true;
        }

        // A non-empty source motif must never disappear, even at an unusually aggressive shrink.
        if (!mask.Any(static value => value))
        {
            mask[(targetHeight / 2) *
                 targetWidth +
                 targetWidth / 2] = true;
        }

        return mask;
    }

    private static void ClearBaselineMotifResidue(
        DesignDocument source,
        DesignDocument destination,
        Atlas atlas,
        bool[]? protectedCells)
    {
        for (var y = 0;
             y < destination.Height;
             y++)
        {
            var sourceY = Math.Clamp(
                (int)Math.Floor(
                    (y + 0.5) *
                    source.Height /
                    destination.Height),
                0,
                source.Height - 1);

            for (var x = 0;
                 x < destination.Width;
                 x++)
            {
                var targetCell = y * destination.Width + x;
                if (protectedCells is not null &&
                    protectedCells[targetCell])
                {
                    continue;
                }

                var sourceX = Math.Clamp(
                    (int)Math.Floor(
                        (x + 0.5) *
                        source.Width /
                        destination.Width),
                    0,
                    source.Width - 1);

                var regionId =
                    atlas.RegionIds[
                        sourceY * source.Width +
                        sourceX];

                if (!atlas.IsMotifRegion[regionId])
                    continue;

                destination.SetPixel(
                    x,
                    y,
                    atlas.Regions[regionId]
                        .BackgroundColor);
            }
        }
    }

    private static bool ShouldProjectDirectly(
        Region region)
    {
        var minimumExtent =
            Math.Min(
                region.Width,
                region.Height);
        var maximumExtent =
            Math.Max(
                region.Width,
                region.Height);

        if (region.Area <= 16)
            return true;

        if (minimumExtent <= 2 &&
            region.Area <= 96)
        {
            return true;
        }

        return minimumExtent <= 3 &&
               maximumExtent >=
                   minimumExtent * 4 &&
               region.Area <= 192;
    }

    private static void PaintRegionDirectly(
        DesignDocument source,
        DesignDocument destination,
        Atlas atlas,
        int regionId,
        byte color,
        float priority,
        float[] cellPriority,
        bool[]? protectedCells)
    {
        for (var position =
                 atlas.RegionOffsets[regionId];
             position <
                 atlas.RegionOffsets[regionId + 1];
             position++)
        {
            var sourceCell =
                atlas.PixelsByRegion[position];
            var sourceX =
                sourceCell % source.Width;
            var sourceY =
                sourceCell / source.Width;

            var targetX = Math.Clamp(
                (int)Math.Floor(
                    (sourceX + 0.5) *
                    destination.Width /
                    source.Width),
                0,
                destination.Width - 1);
            var targetY = Math.Clamp(
                (int)Math.Floor(
                    (sourceY + 0.5) *
                    destination.Height /
                    source.Height),
                0,
                destination.Height - 1);
            var targetCell =
                targetY * destination.Width +
                targetX;

            if (protectedCells is not null &&
                protectedCells[targetCell])
            {
                continue;
            }

            if (priority < cellPriority[targetCell])
                continue;

            cellPriority[targetCell] =
                priority;
            destination.SetPixel(
                targetX,
                targetY,
                color);
        }
    }

    private static int TargetExtent(
        int sourceExtent,
        double scale,
        int area,
        int boundaryPixels)
    {
        var target = Math.Max(
            1,
            (int)Math.Round(
                sourceExtent * scale));

        var boundaryRatio =
            boundaryPixels /
            (double)Math.Max(1, area);

        // Tiny/line-like motifs are allowed one extra cell when ordinary rounding would collapse
        // a multi-pixel feature to a single target coordinate. This is intentionally bounded:
        // the motif may resist disappearing, but it may not grow relative to the source.
        if (sourceExtent >= 3 &&
            target == 1 &&
            (area <= 64 ||
             boundaryRatio >= 0.75))
        {
            target = 2;
        }

        return target;
    }

    private static float MotifPriority(
        Atlas atlas,
        Region region)
    {
        var smallBoost = region.Area switch
        {
            <= 4 => 6f,
            <= 16 => 4.5f,
            <= 64 => 3.5f,
            <= 256 => 2.0f,
            <= 1024 => 1.0f,
            _ => 0f,
        };

        var boundaryRatio =
            region.BoundaryPixels /
            (float)Math.Max(1, region.Area);
        var detailBoost =
            Math.Clamp(
                (boundaryRatio - 0.25f) * 4f,
                0f,
                3f);

        var colorCount =
            atlas.ColorCounts[region.Color];
        var rarityBoost =
            colorCount <= 0
                ? 0f
                : Math.Clamp(
                    2.0f -
                    MathF.Log10(
                        Math.Max(1, colorCount)) *
                    0.25f,
                    0f,
                    1.5f);

        return 10f +
               smallBoost +
               detailBoost +
               rarityBoost;
    }

    private static void PaintTemplate(
        DesignDocument destination,
        bool[] template,
        int templateWidth,
        int templateHeight,
        int left,
        int top,
        byte color,
        float priority,
        float[] cellPriority,
        bool[]? protectedCells)
    {
        for (var y = 0;
             y < templateHeight;
             y++)
        {
            var targetY = top + y;
            if (targetY < 0 ||
                targetY >= destination.Height)
            {
                continue;
            }

            for (var x = 0;
                 x < templateWidth;
                 x++)
            {
                if (!template[y * templateWidth + x])
                    continue;

                var targetX = left + x;
                if (targetX < 0 ||
                    targetX >= destination.Width)
                {
                    continue;
                }

                var cell =
                    targetY * destination.Width +
                    targetX;

                if (protectedCells is not null &&
                    protectedCells[cell])
                {
                    continue;
                }

                if (priority < cellPriority[cell])
                    continue;

                cellPriority[cell] = priority;
                destination.SetPixel(
                    targetX,
                    targetY,
                    color);
            }
        }
    }

    private static bool SimilarExtent(
        int first,
        int second)
    {
        if (first == second)
            return true;

        var maximum = Math.Max(
            first,
            second);
        if (maximum <= 2)
            return false;

        return Math.Abs(first - second) /
               (double)maximum <= 0.05;
    }
}
