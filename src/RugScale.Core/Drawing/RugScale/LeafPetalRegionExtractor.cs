using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Extracts 4-connected indexed fill regions that are large enough to carry a meaningful
/// leaf/petal shape but small enough not to be the global field/background.
///
/// The extractor is intentionally colour-agnostic. Classification of "leafness" belongs to
/// <see cref="LeafPetalArcClassifier"/>; this stage only provides stable categorical components.
/// </summary>
internal static class LeafPetalRegionExtractor
{
    private const int MinimumArea = 48;
    // Synthetic/unit fixtures and close-cropped production fragments can contain one dominant
    // leaf that occupies well above 18% of the image. Background rejection belongs to the shape
    // classifier (elongation/fill/boundary rules), not to a hard document-area cutoff.
    private const double MaximumDocumentAreaRatio = 0.45;

    private static readonly (int X, int Y)[] Directions =
    [
        (-1, 0),
        (1, 0),
        (0, -1),
        (0, 1),
    ];

    public static IReadOnlyList<LeafPetalRegion> Extract(
        DesignDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var total =
            checked(
                source.Width *
                source.Height);
        var maximumArea =
            Math.Max(
                MinimumArea,
                (int)Math.Round(
                    total *
                    MaximumDocumentAreaRatio));

        var visited =
            new bool[total];
        var queue =
            new int[total];
        var regions =
            new List<LeafPetalRegion>();

        for (var start = 0;
             start < total;
             start++)
        {
            if (visited[start])
                continue;

            var startX =
                start %
                source.Width;
            var startY =
                start /
                source.Width;
            var color =
                source.GetPixel(
                    startX,
                    startY);

            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;

            var pixels =
                new List<int>();
            var boundary =
                new List<int>();
            var minX = startX;
            var minY = startY;
            var maxX = startX;
            var maxY = startY;

            while (head < tail)
            {
                var pixel =
                    queue[head++];
                pixels.Add(pixel);

                var x =
                    pixel %
                    source.Width;
                var y =
                    pixel /
                    source.Width;

                minX =
                    Math.Min(
                        minX,
                        x);
                minY =
                    Math.Min(
                        minY,
                        y);
                maxX =
                    Math.Max(
                        maxX,
                        x);
                maxY =
                    Math.Max(
                        maxY,
                        y);

                var exposed = false;

                foreach (var (dx, dy) in Directions)
                {
                    var nx =
                        x +
                        dx;
                    var ny =
                        y +
                        dy;

                    if (nx < 0 ||
                        nx >= source.Width ||
                        ny < 0 ||
                        ny >= source.Height)
                    {
                        exposed = true;
                        continue;
                    }

                    if (source.GetPixel(
                            nx,
                            ny) != color)
                    {
                        exposed = true;
                        continue;
                    }

                    var next =
                        ny *
                        source.Width +
                        nx;

                    if (visited[next])
                        continue;

                    visited[next] = true;
                    queue[tail++] = next;
                }

                if (exposed)
                    boundary.Add(pixel);
            }

            if (pixels.Count <
                    MinimumArea ||
                pixels.Count >
                    maximumArea)
            {
                continue;
            }

            var width =
                maxX -
                minX +
                1;
            var height =
                maxY -
                minY +
                1;

            // Ignore technical/sentinel edge strips.
            var technicalVertical =
                width <= 2 &&
                height >=
                source.Height *
                0.80 &&
                (minX == 0 ||
                 maxX ==
                 source.Width - 1);
            var technicalHorizontal =
                height <= 2 &&
                width >=
                source.Width *
                0.80 &&
                (minY == 0 ||
                 maxY ==
                 source.Height - 1);

            if (technicalVertical ||
                technicalHorizontal)
            {
                continue;
            }

            regions.Add(
                new LeafPetalRegion(
                    color,
                    pixels,
                    boundary,
                    minX,
                    minY,
                    maxX,
                    maxY));
        }

        return regions;
    }
}
