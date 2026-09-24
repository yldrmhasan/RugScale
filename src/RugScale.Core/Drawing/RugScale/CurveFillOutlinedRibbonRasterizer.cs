using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Moves a high-confidence filled ribbon boundary against its own dedicated outline colour.
///
/// Real N69 ornaments often encode a coloured band (for example the C069 green half-oval) fully
/// surrounded by the white 1x1 Pixel-Cord role. The generic specialist correctly treats that role
/// as a hard barrier, but that also means the coloured band can never shed its source staircase.
/// This rasterizer is intentionally narrower: it only swaps the region colour with ONE dominant
/// protected outline colour in the local source-boundary corridor. It never paints over a third
/// colour and never moves the outer side of the outline.
/// </summary>
internal static class CurveFillOutlinedRibbonRasterizer
{
    private const double BoundaryBandRadius = 2.15;
    private const double MinimumOutlineNeighbourShare = 0.78;
    private const int MinimumOutlineContacts = 12;

    public static bool TryApply(
        DesignDocument source,
        DesignDocument destination,
        LeafPetalArcModel model,
        ElegantArcFit fit,
        IReadOnlySet<byte> protectedStrokeColors,
        out int changed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        changed = 0;

        if (!fit.IsSafe ||
            fit.Points.Count < 4 ||
            !TryFindDominantOutlineColor(
                source,
                model.Candidate.Region,
                protectedStrokeColors,
                out var outlineColor))
        {
            return false;
        }

        var scaleX =
            destination.Width /
            (double)source.Width;
        var scaleY =
            destination.Height /
            (double)source.Height;
        var polygon =
            LeafPetalArcRasterizer.BuildTargetPolygon(
                fit.Points,
                scaleX,
                scaleY);

        if (polygon.Count < 6)
            return false;

        var targetMask =
            LeafPetalArcRasterizer.RasterizePolygon(
                polygon,
                destination.Width,
                destination.Height);

        if (targetMask.Count == 0)
            return false;

        var region =
            model.Candidate.Region;
        var boundary =
            region.BoundaryPixels.ToHashSet();
        var minX =
            Math.Max(
                0,
                (int)Math.Floor(
                    polygon.Min(point =>
                        point.X)) -
                3);
        var maxX =
            Math.Min(
                destination.Width - 1,
                (int)Math.Ceiling(
                    polygon.Max(point =>
                        point.X)) +
                3);
        var minY =
            Math.Max(
                0,
                (int)Math.Floor(
                    polygon.Min(point =>
                        point.Y)) -
                3);
        var maxY =
            Math.Min(
                destination.Height - 1,
                (int)Math.Ceiling(
                    polygon.Max(point =>
                        point.Y)) +
                3);

        var regionColor =
            region.Color;

        // Snapshot which target cells already carry the dedicated outline before editing. This
        // prevents scan-order effects when checking that an expansion leaves outline support on
        // the outside of the new smooth boundary.
        var originalOutline =
            new HashSet<int>();

        for (var y = minY;
             y <= maxY;
             y++)
        {
            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                if (destination.GetPixel(
                        x,
                        y) ==
                    outlineColor)
                {
                    originalOutline.Add(
                        y *
                        destination.Width +
                        x);
                }
            }
        }

        for (var y = minY;
             y <= maxY;
             y++)
        {
            var sourceY =
                (y + 0.5) /
                scaleY -
                0.5;

            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                var sourceX =
                    (x + 0.5) /
                    scaleX -
                    0.5;

                if (!IsNearSourceBoundary(
                        boundary,
                        source.Width,
                        source.Height,
                        sourceX,
                        sourceY,
                        BoundaryBandRadius))
                {
                    continue;
                }

                var key =
                    y *
                    destination.Width +
                    x;
                var inside =
                    targetMask.Contains(
                        key);
                var current =
                    destination.GetPixel(
                        x,
                        y);

                if (inside)
                {
                    if (current == regionColor)
                        continue;

                    // Expansion is legal ONLY into this region's own protected outline, and only
                    // when at least one outline cell remains immediately outside the new mask.
                    if (current != outlineColor ||
                        !HasOutsideOutlineSupport(
                            x,
                            y,
                            destination.Width,
                            destination.Height,
                            targetMask,
                            originalOutline))
                    {
                        continue;
                    }

                    destination.SetPixel(
                        x,
                        y,
                        regionColor);
                    changed++;
                    continue;
                }

                // A geometric shave converts old coloured staircase protrusions to the SAME
                // outline colour. No third palette role is ever introduced or crossed.
                if (current != regionColor)
                    continue;

                destination.SetPixel(
                    x,
                    y,
                    outlineColor);
                changed++;
            }
        }

        return true;
    }

    private static bool TryFindDominantOutlineColor(
        DesignDocument source,
        LeafPetalRegion region,
        IReadOnlySet<byte> protectedStrokeColors,
        out byte outlineColor)
    {
        Span<int> contacts =
            stackalloc int[256];
        var total = 0;
        var regionPixels =
            region.Pixels.ToHashSet();

        foreach (var pixel in region.BoundaryPixels)
        {
            var x =
                pixel %
                source.Width;
            var y =
                pixel /
                source.Width;

            foreach (var (dx, dy) in
                     new[]
                     {
                         (-1, 0),
                         (1, 0),
                         (0, -1),
                         (0, 1),
                     })
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
                    continue;
                }

                var next =
                    ny *
                    source.Width +
                    nx;

                if (regionPixels.Contains(
                        next))
                {
                    continue;
                }

                var color =
                    source.GetPixel(
                        nx,
                        ny);

                total++;

                if (protectedStrokeColors.Contains(
                        color))
                {
                    contacts[color]++;
                }
            }
        }

        var bestColor = -1;
        var bestContacts = 0;

        for (var index = 0;
             index < contacts.Length;
             index++)
        {
            if (contacts[index] <=
                bestContacts)
            {
                continue;
            }

            bestContacts =
                contacts[index];
            bestColor =
                index;
        }

        if (bestColor < 0 ||
            bestContacts <
                MinimumOutlineContacts ||
            total <= 0 ||
            bestContacts /
                (double)total <
                MinimumOutlineNeighbourShare)
        {
            outlineColor = 0;
            return false;
        }

        outlineColor =
            (byte)bestColor;
        return true;
    }

    private static bool HasOutsideOutlineSupport(
        int x,
        int y,
        int width,
        int height,
        IReadOnlySet<int> targetMask,
        IReadOnlySet<int> originalOutline)
    {
        for (var dy = -1;
             dy <= 1;
             dy++)
        {
            for (var dx = -1;
                 dx <= 1;
                 dx++)
            {
                if (dx == 0 &&
                    dy == 0)
                {
                    continue;
                }

                var nx =
                    x +
                    dx;
                var ny =
                    y +
                    dy;

                if (nx < 0 ||
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                var key =
                    ny *
                    width +
                    nx;

                if (!targetMask.Contains(
                        key) &&
                    originalOutline.Contains(
                        key))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNearSourceBoundary(
        IReadOnlySet<int> boundary,
        int width,
        int height,
        double sourceX,
        double sourceY,
        double radius)
    {
        var centerX =
            Math.Clamp(
                (int)Math.Round(
                    sourceX),
                0,
                width - 1);
        var centerY =
            Math.Clamp(
                (int)Math.Round(
                    sourceY),
                0,
                height - 1);
        var search =
            (int)Math.Ceiling(
                radius) +
            1;
        var radiusSquared =
            radius *
            radius;

        for (var y =
                 Math.Max(
                     0,
                     centerY - search);
             y <=
             Math.Min(
                 height - 1,
                 centerY + search);
             y++)
        {
            for (var x =
                     Math.Max(
                         0,
                         centerX - search);
                 x <=
                 Math.Min(
                     width - 1,
                     centerX + search);
                 x++)
            {
                if (!boundary.Contains(
                        y *
                        width +
                        x))
                {
                    continue;
                }

                var dx =
                    x -
                    sourceX;
                var dy =
                    y -
                    sourceY;

                if (dx *
                        dx +
                    dy *
                        dy <=
                    radiusSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
