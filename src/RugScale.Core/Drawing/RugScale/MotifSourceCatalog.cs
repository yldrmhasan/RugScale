using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

public enum MotifKind : byte
{
    Branch,
    LeafLike,
    Primitive,
    LargePart,
    Compound,
}

public sealed record MotifCatalogEntry(
    int X,
    int Y,
    int Width,
    int Height,
    int PixelCount,
    MotifKind Kind,
    bool[] Mask,
    MotifDescriptor Descriptor);

/// <summary>
/// Multi-scale source motif catalogue built once per Resize/RugScale session. The catalogue is
/// intentionally broader than MotifShrinkEngine's single-colour reconstruction primitives:
/// individual components classify branches/leaves/large parts, then nearby non-structural
/// components are grouped into bounded compound motifs. That lets feedback search recognize a
/// whole multicolour flower/medallion fragment as well as one vein or leaf.
/// </summary>
public sealed class MotifSourceCatalog
{
    private sealed class Component
    {
        public int Id;
        public byte Color;
        public int Area;
        public int Boundary;
        public int MinX = int.MaxValue;
        public int MinY = int.MaxValue;
        public int MaxX = -1;
        public int MaxY = -1;
        public bool Structural;
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public double Fill => Area / (double)Math.Max(1, Width * Height);
    }

    public required IReadOnlyList<MotifCatalogEntry> Entries { get; init; }

    public static MotifSourceCatalog Build(
        DesignDocument document,
        int warpDensity,
        int weftDensity)
    {
        ArgumentNullException.ThrowIfNull(document);

        var width = document.Width;
        var height = document.Height;
        var total = checked(width * height);
        var pixels = new byte[total];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
                pixels[row + x] = document.GetPixel(x, y);
        }

        var componentIds = new int[total];
        Array.Fill(componentIds, -1);
        var queue = new int[total];
        var components = new List<Component>();

        ReadOnlySpan<int> dx = [-1, 0, 1, -1, 1, -1, 0, 1];
        ReadOnlySpan<int> dy = [-1, -1, -1, 0, 0, 1, 1, 1];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = y * width + x;
                if (componentIds[start] >= 0)
                    continue;

                var id = components.Count;
                var component = new Component
                {
                    Id = id,
                    Color = pixels[start],
                };

                var head = 0;
                var tail = 0;
                queue[tail++] = start;
                componentIds[start] = id;

                while (head < tail)
                {
                    var cell = queue[head++];
                    var cx = cell % width;
                    var cy = cell / width;
                    component.Area++;
                    component.MinX = Math.Min(component.MinX, cx);
                    component.MaxX = Math.Max(component.MaxX, cx);
                    component.MinY = Math.Min(component.MinY, cy);
                    component.MaxY = Math.Max(component.MaxY, cy);

                    var boundary = false;

                    for (var direction = 0; direction < 8; direction++)
                    {
                        var nx = cx + dx[direction];
                        var ny = cy + dy[direction];

                        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        {
                            boundary = true;
                            continue;
                        }

                        var next = ny * width + nx;
                        if (pixels[next] != component.Color)
                        {
                            boundary = true;
                            continue;
                        }

                        if (componentIds[next] >= 0)
                            continue;

                        componentIds[next] = id;
                        queue[tail++] = next;
                    }

                    if (boundary)
                        component.Boundary++;
                }

                component.Structural = IsStructural(
                    component,
                    width,
                    height,
                    total);
                components.Add(component);
            }
        }

        var pixelsByComponent = BuildPixelLists(
            componentIds,
            components);
        var entries = new List<MotifCatalogEntry>();

        // Tiny one-pixel noise is still handled by RugScale's critical-feature path. A catalogue
        // entry begins at 3 pixels so interactive search is about recognizable design structure.
        foreach (var component in components)
        {
            if (component.Structural ||
                component.Area < 3)
            {
                continue;
            }

            var mask = BuildMask(
                [component.Id],
                component,
                pixelsByComponent,
                width);

            entries.Add(new MotifCatalogEntry(
                component.MinX,
                component.MinY,
                component.Width,
                component.Height,
                component.Area,
                Classify(
                    component,
                    width,
                    height,
                    warpDensity,
                    weftDensity),
                mask,
                MotifDescriptor.FromPatch(
                    document,
                    component.MinX,
                    component.MinY,
                    component.Width,
                    component.Height,
                    mask,
                    warpDensity,
                    weftDensity)));
        }

        // Spatial hash for compound discovery. Search only nearby component centres; this keeps
        // catalogue construction O(N)-ish even on 20k+ component carpet rasters.
        var cellSize = Math.Clamp(
            Math.Min(width, height) / 40,
            12,
            64);
        var buckets = new Dictionary<(int X, int Y), List<int>>();

        foreach (var component in components)
        {
            if (component.Structural ||
                component.Area < 3)
            {
                continue;
            }

            var centerX = (component.MinX + component.MaxX) / 2;
            var centerY = (component.MinY + component.MaxY) / 2;
            var key = (centerX / cellSize, centerY / cellSize);

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(component.Id);
        }

        var seenCompounds = new HashSet<(int X, int Y, int W, int H, int Count)>();

        foreach (var seed in components)
        {
            if (seed.Structural ||
                seed.Area < 4)
            {
                continue;
            }

            var centerX = (seed.MinX + seed.MaxX) / 2;
            var centerY = (seed.MinY + seed.MaxY) / 2;
            var bucketX = centerX / cellSize;
            var bucketY = centerY / cellSize;
            var candidates = new List<Component>();

            for (var by = bucketY - 1; by <= bucketY + 1; by++)
            {
                for (var bx = bucketX - 1; bx <= bucketX + 1; bx++)
                {
                    if (!buckets.TryGetValue((bx, by), out var bucketIds))
                        continue;

                    foreach (var id in bucketIds)
                    {
                        var candidate = components[id];
                        if (candidate.Id == seed.Id ||
                            candidate.Structural)
                        {
                            continue;
                        }

                        if (IsCompoundNeighbour(seed, candidate))
                            candidates.Add(candidate);
                    }
                }
            }

            if (candidates.Count == 0)
                continue;

            var selected = candidates
                .OrderBy(candidate => BoxDistance(seed, candidate))
                .Take(7)
                .Prepend(seed)
                .ToArray();

            var minX = selected.Min(component => component.MinX);
            var minY = selected.Min(component => component.MinY);
            var maxX = selected.Max(component => component.MaxX);
            var maxY = selected.Max(component => component.MaxY);
            var compoundWidth = maxX - minX + 1;
            var compoundHeight = maxY - minY + 1;

            if (compoundWidth > width * 0.30 ||
                compoundHeight > height * 0.30)
            {
                continue;
            }

            var key = (
                minX,
                minY,
                compoundWidth,
                compoundHeight,
                selected.Length);
            if (!seenCompounds.Add(key))
                continue;

            var ids = selected
                .Select(component => component.Id)
                .ToArray();
            var bounds = new Component
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Area = selected.Sum(component => component.Area),
            };
            var mask = BuildMask(
                ids,
                bounds,
                pixelsByComponent,
                width);

            // Avoid compounds that are mostly empty space. They are usually accidental proximity
            // between two unrelated border repeats rather than one readable motif.
            var fill = bounds.Area /
                       (double)Math.Max(
                           1,
                           compoundWidth * compoundHeight);
            if (fill < 0.06)
                continue;

            entries.Add(new MotifCatalogEntry(
                minX,
                minY,
                compoundWidth,
                compoundHeight,
                bounds.Area,
                MotifKind.Compound,
                mask,
                MotifDescriptor.FromPatch(
                    document,
                    minX,
                    minY,
                    compoundWidth,
                    compoundHeight,
                    mask,
                    warpDensity,
                    weftDensity)));
        }

        return new MotifSourceCatalog
        {
            Entries = entries,
        };
    }

    private static Dictionary<int, int[]> BuildPixelLists(
        int[] componentIds,
        IReadOnlyList<Component> components)
    {
        var counts = new int[components.Count];
        foreach (var id in componentIds)
            counts[id]++;

        var offsets = new int[components.Count + 1];
        for (var i = 0; i < components.Count; i++)
            offsets[i + 1] = offsets[i] + counts[i];

        var all = new int[componentIds.Length];
        var cursors = offsets[..^1].ToArray();

        for (var pixel = 0; pixel < componentIds.Length; pixel++)
        {
            var id = componentIds[pixel];
            all[cursors[id]++] = pixel;
        }

        var result = new Dictionary<int, int[]>(components.Count);
        for (var id = 0; id < components.Count; id++)
        {
            var length = offsets[id + 1] - offsets[id];
            var pixels = new int[length];
            Array.Copy(
                all,
                offsets[id],
                pixels,
                0,
                length);
            result[id] = pixels;
        }

        return result;
    }

    private static bool[] BuildMask(
        IReadOnlyList<int> componentIds,
        Component bounds,
        IReadOnlyDictionary<int, int[]> pixelsByComponent,
        int sourceWidth)
    {
        var mask = new bool[
            checked(bounds.Width * bounds.Height)];

        foreach (var componentId in componentIds)
        {
            foreach (var pixel in pixelsByComponent[componentId])
            {
                var x = pixel % sourceWidth - bounds.MinX;
                var y = pixel / sourceWidth - bounds.MinY;

                if (x >= 0 && x < bounds.Width &&
                    y >= 0 && y < bounds.Height)
                {
                    mask[y * bounds.Width + x] = true;
                }
            }
        }

        return mask;
    }

    private static bool IsStructural(
        Component component,
        int width,
        int height,
        int totalPixels)
    {
        var areaRatio =
            component.Area / (double)totalPixels;
        var spanX =
            component.Width / (double)width;
        var spanY =
            component.Height / (double)height;

        if ((component.MinX == 0 && component.MaxX == width - 1) ||
            (component.MinY == 0 && component.MaxY == height - 1))
        {
            return true;
        }

        if (areaRatio >= 0.06)
            return true;

        return areaRatio >= 0.015 &&
               (spanX >= 0.55 || spanY >= 0.55);
    }

    private static MotifKind Classify(
        Component component,
        int width,
        int height,
        int warpDensity,
        int weftDensity)
    {
        var physicalAspect =
            (component.Width /
             (double)Math.Max(1, warpDensity)) /
            Math.Max(
                1e-9,
                component.Height /
                (double)Math.Max(1, weftDensity));
        var elongated =
            physicalAspect >= 2.2 ||
            physicalAspect <= 1d / 2.2;
        var boundaryRatio =
            component.Boundary /
            (double)Math.Max(1, component.Area);

        if ((elongated &&
             (Math.Min(component.Width, component.Height) <= 3 ||
              component.Fill <= 0.62)) ||
            (boundaryRatio >= 0.78 &&
             component.Fill <= 0.48))
        {
            return MotifKind.Branch;
        }

        var areaRatio =
            component.Area /
            (double)(width * height);

        // Compact filled parts are usually leaves/petals even when their bounding box is large
        // enough to cross a generic area threshold. Only genuinely large document-scale pieces
        // should be promoted to LargePart.
        if (areaRatio < 0.012 &&
            component.Fill >= 0.32 &&
            physicalAspect is >= 0.42 and <= 2.4)
        {
            return MotifKind.LeafLike;
        }

        if (areaRatio >= 0.0025 ||
            component.Width >= width * 0.14 ||
            component.Height >= height * 0.14)
        {
            return MotifKind.LargePart;
        }

        return MotifKind.Primitive;
    }

    private static bool IsCompoundNeighbour(
        Component first,
        Component second)
    {
        var gap = BoxDistance(first, second);
        var scale = Math.Max(
            3,
            Math.Min(
                18,
                Math.Min(
                    Math.Max(first.Width, first.Height),
                    Math.Max(second.Width, second.Height)) / 3));

        return gap <= scale;
    }

    private static double BoxDistance(
        Component first,
        Component second)
    {
        var dx = Math.Max(
            0,
            Math.Max(
                first.MinX - second.MaxX - 1,
                second.MinX - first.MaxX - 1));
        var dy = Math.Max(
            0,
            Math.Max(
                first.MinY - second.MaxY - 1,
                second.MinY - first.MaxY - 1));

        return Math.Sqrt(dx * dx + dy * dy);
    }
}
