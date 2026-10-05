using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Post-stage fidelity guard for Curve &amp; Fill redraw passes.
///
/// Aesthetic redraw stages (Pixel-Cord replay, ribbon/arc refinement) are allowed to move a
/// boundary, but two source facts are not negotiable:
///
/// 1. <b>Separators are hard barriers.</b> When two palette colours are never 4-adjacent in the
///    source, a Pixel-Cord / outline always sits between them. If a stage makes them touch, that
///    is a visible gap in the drawing. <see cref="EnforceSeparatorBarriers"/> repairs every such
///    contact that the stage introduced by redrawing the source separator colour on the stage's
///    own pixel (or, when that is impossible, by reverting the pixel to the stage baseline).
/// 2. <b>Source-backed appendages are content, not staircase.</b> A ribbon fit describes the main
///    sweep only. Teeth, thorns and side lobes that stick out of the fitted silhouette are part of
///    the design. <see cref="RestoreLostAppendages"/> brings back deep, compact pieces of a colour
///    that a stage removed, while shallow or long sliver losses (genuine boundary smoothing and
///    centreline motion) remain the stage's decision.
///
/// Both passes only touch pixels the stage itself changed (plus the one-pixel outline ring of a
/// restored appendage), so a stage that behaved correctly is left byte-identical.
/// </summary>
internal static class CurveFillRibbonFidelityGuard
{
    /// <summary>Losses shallower than this (target px from the new region) are boundary smoothing.</summary>
    internal const double ShaveDepth = 2.0;

    /// <summary>An appendage core must reach at least this deep beyond the new region (target px).</summary>
    internal const double MinimumAppendageDepth = 3.0;

    /// <summary>Core area must stay below CompactFactor * depth^2: compact teeth, not long slivers.</summary>
    internal const double CompactFactor = 2.5;

    /// <summary>Core base width may not exceed BaseWidthFactor * depth + 2: teeth are narrow at the root.</summary>
    internal const double BaseWidthFactor = 2.5;

    /// <summary>Share of appendage pixels whose source owner is the appendage colour.</summary>
    internal const double MinimumSourceSupport = 0.5;

    private const int MaximumRepairsPerPixel = 3;

    internal readonly record struct BarrierRepairReport(
        int BreachesFound,
        int SeparatorRepairs,
        int Reverts,
        int Unresolved);

    internal readonly record struct AppendageRepairReport(
        int Appendages,
        int PixelsRestored);

    internal static byte[] Snapshot(
        DesignDocument document)
    {
        var pixels =
            new byte[document.Width * document.Height];

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                pixels[y * document.Width + x] =
                    document.GetPixel(
                        x,
                        y);
            }
        }

        return pixels;
    }

    internal static int[,] SourceAdjacencyCounts(
        DesignDocument source)
    {
        var counts =
            new int[256, 256];

        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                var color =
                    source.GetPixel(
                        x,
                        y);

                if (x + 1 < source.Width)
                {
                    var right =
                        source.GetPixel(
                            x + 1,
                            y);
                    counts[color, right]++;
                    counts[right, color]++;
                }

                if (y + 1 < source.Height)
                {
                    var below =
                        source.GetPixel(
                            x,
                            y + 1);
                    counts[color, below]++;
                    counts[below, color]++;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// Repairs every 4-adjacency between source-separated colours that involves a pixel this stage
    /// changed relative to <paramref name="baseline"/>.
    /// </summary>
    internal static BarrierRepairReport EnforceSeparatorBarriers(
        DesignDocument source,
        byte[] baseline,
        DesignDocument destination,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        var width =
            destination.Width;
        var height =
            destination.Height;
        var pixels =
            Snapshot(
                destination);
        var adjacency =
            SourceAdjacencyCounts(
                source);
        var contacts =
            new SourceContacts(
                source,
                adjacency,
                width,
                height);
        var separatorFor =
            BuildSeparatorTable(
                adjacency,
                protectedStrokeColors);
        var repairs =
            new byte[pixels.Length];
        var queue =
            new Queue<int>();

        for (var index = 0;
             index < pixels.Length;
             index++)
        {
            if (pixels[index] !=
                baseline[index])
            {
                queue.Enqueue(
                    index);
            }
        }

        var breachesFound = 0;
        var separatorRepairs = 0;
        var reverts = 0;
        var unresolved = 0;
        Span<int> neighbours =
            stackalloc int[4];

        while (queue.Count > 0)
        {
            var p =
                queue.Dequeue();
            var neighbourCount =
                FourNeighbours(
                    p,
                    width,
                    height,
                    neighbours);

            for (var n = 0;
                 n < neighbourCount;
                 n++)
            {
                var q =
                    neighbours[n];
                var a =
                    pixels[p];
                var b =
                    pixels[q];

                if (contacts.Allowed(
                        a,
                        b,
                        p))
                {
                    continue;
                }

                var pChanged =
                    pixels[p] !=
                    baseline[p];
                var qChanged =
                    pixels[q] !=
                    baseline[q];

                if (!pChanged &&
                    !qChanged)
                {
                    continue;
                }

                breachesFound++;

                var first =
                    ChooseRepairTarget(
                        p,
                        q,
                        pChanged,
                        qChanged,
                        pixels,
                        width,
                        height,
                        protectedStrokeColors);
                var second =
                    first == p
                        ? q
                        : p;
                var separator =
                    contacts.LocalSeparator(
                        a,
                        b,
                        p,
                        protectedStrokeColors);

                if (separator < 0)
                    separator = separatorFor[a, b];

                var repaired = false;

                foreach (var target in new[] { first, second })
                {
                    if (target < 0 ||
                        repairs[target] >=
                        MaximumRepairsPerPixel)
                    {
                        continue;
                    }

                    if (separator >= 0 &&
                        pixels[target] != separator &&
                        IsLegalAt(
                            target,
                            (byte)separator,
                            pixels,
                            contacts,
                            width,
                            height))
                    {
                        pixels[target] =
                            (byte)separator;
                        repairs[target]++;
                        separatorRepairs++;
                        repaired = true;
                    }
                    else if (pixels[target] != baseline[target] &&
                             IsLegalAt(
                                 target,
                                 baseline[target],
                                 pixels,
                                 contacts,
                                 width,
                                 height))
                    {
                        pixels[target] =
                            baseline[target];
                        repairs[target]++;
                        reverts++;
                        repaired = true;
                    }

                    if (!repaired)
                        continue;

                    queue.Enqueue(
                        target);

                    var count =
                        FourNeighbours(
                            target,
                            width,
                            height,
                            neighbours);

                    for (var k = 0;
                         k < count;
                         k++)
                    {
                        queue.Enqueue(
                            neighbours[k]);
                    }

                    break;
                }

                if (!repaired)
                    unresolved++;

                // The neighbour span was reused; restart this pixel's scan on the next dequeue.
                if (repaired)
                    break;
            }
        }

        Commit(
            pixels,
            destination);

        return new BarrierRepairReport(
            breachesFound,
            separatorRepairs,
            reverts,
            unresolved);
    }

    /// <summary>
    /// Restores deep, compact, source-backed pieces of a colour that this stage removed.
    /// </summary>
    internal static AppendageRepairReport RestoreLostAppendages(
        DesignDocument source,
        byte[] baseline,
        DesignDocument destination,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        var width =
            destination.Width;
        var height =
            destination.Height;
        var pixels =
            Snapshot(
                destination);
        var lostColors =
            new HashSet<byte>();

        for (var index = 0;
             index < pixels.Length;
             index++)
        {
            if (baseline[index] != pixels[index] &&
                !protectedStrokeColors.Contains(
                    baseline[index]))
            {
                lostColors.Add(
                    baseline[index]);
            }
        }

        var scaleX =
            source.Width /
            (double)width;
        var scaleY =
            source.Height /
            (double)height;
        var appendages = 0;
        var restored = 0;
        var restoredMask =
            new bool[pixels.Length];

        foreach (var color in lostColors.OrderBy(value => value))
        {
            var distance =
                ChamferDistanceTo(
                    pixels,
                    width,
                    height,
                    color);
            var core =
                new bool[pixels.Length];
            var anyCore = false;

            for (var index = 0;
                 index < pixels.Length;
                 index++)
            {
                if (baseline[index] == color &&
                    pixels[index] != color &&
                    distance[index] > ShaveDepth)
                {
                    core[index] = true;
                    anyCore = true;
                }
            }

            if (!anyCore)
                continue;

            var visited =
                new bool[pixels.Length];
            var component =
                new List<int>();
            var queue =
                new Queue<int>();

            for (var start = 0;
                 start < pixels.Length;
                 start++)
            {
                if (!core[start] ||
                    visited[start])
                {
                    continue;
                }

                component.Clear();
                visited[start] = true;
                queue.Enqueue(
                    start);

                while (queue.Count > 0)
                {
                    var current =
                        queue.Dequeue();
                    component.Add(
                        current);

                    ForEachEightNeighbour(
                        current,
                        width,
                        height,
                        neighbour =>
                        {
                            if (!core[neighbour] ||
                                visited[neighbour])
                            {
                                return;
                            }

                            visited[neighbour] = true;
                            queue.Enqueue(
                                neighbour);
                        });
                }

                var depth =
                    component.Max(index =>
                        distance[index]);

                if (depth < MinimumAppendageDepth ||
                    component.Count >
                        CompactFactor *
                        depth *
                        depth)
                {
                    continue;
                }

                var baseWidth =
                    component.Count(index =>
                        distance[index] <=
                        ShaveDepth + 1.0);

                if (baseWidth >
                    BaseWidthFactor *
                    depth +
                    2)
                {
                    continue;
                }

                var supported =
                    component.Count(index =>
                        SourceOwner(
                            source,
                            index,
                            width,
                            scaleX,
                            scaleY) ==
                        color);

                if (supported <
                    MinimumSourceSupport *
                    component.Count)
                {
                    continue;
                }

                // Restore the core, then descend the distance field back to the new region so the
                // appendage stays attached instead of floating beside the redrawn sweep.
                var appendage =
                    new HashSet<int>(
                        component);
                var stem =
                    new Queue<int>(
                        component);

                while (stem.Count > 0)
                {
                    var current =
                        stem.Dequeue();

                    ForEachEightNeighbour(
                        current,
                        width,
                        height,
                        neighbour =>
                        {
                            if (appendage.Contains(neighbour) ||
                                baseline[neighbour] != color ||
                                pixels[neighbour] == color ||
                                distance[neighbour] >=
                                    distance[current])
                            {
                                return;
                            }

                            appendage.Add(
                                neighbour);
                            stem.Enqueue(
                                neighbour);
                        });
                }

                appendages++;

                foreach (var index in appendage)
                {
                    pixels[index] =
                        color;
                    restoredMask[index] = true;
                    restored++;
                }

                // Bring back the appendage's own protected outline ring from the baseline.
                foreach (var index in appendage)
                {
                    ForEachEightNeighbour(
                        index,
                        width,
                        height,
                        neighbour =>
                        {
                            if (restoredMask[neighbour] ||
                                pixels[neighbour] ==
                                    baseline[neighbour] ||
                                !protectedStrokeColors.Contains(
                                    baseline[neighbour]))
                            {
                                return;
                            }

                            pixels[neighbour] =
                                baseline[neighbour];
                            restoredMask[neighbour] = true;
                            restored++;
                        });
                }
            }
        }

        Commit(
            pixels,
            destination);

        return new AppendageRepairReport(
            appendages,
            restored);
    }

    internal readonly record struct CordRepairReport(
        int BridgesRestored,
        int BridgePixels,
        int DustComponentsRemoved,
        int DustPixelsRemoved);

    /// <summary>Longest stretch of old cord path a bridge may revive (target px).</summary>
    internal const int MaximumBridgeLength = 10;

    /// <summary>Cord fragments at most this large that the stage left behind are dust.</summary>
    internal const int MaximumDustSize = 4;

    /// <summary>
    /// Keeps every protected Pixel-Cord colour as continuous as it was before the stage:
    /// (1) where the stage split one cord into pieces, revive the shortest stretch of the old cord
    /// path that joins them again; (2) remove tiny cord fragments the stage left stranded, unless
    /// removing them would open a separator gap.
    /// </summary>
    internal static CordRepairReport RepairCordContinuity(
        DesignDocument source,
        byte[] baseline,
        DesignDocument destination,
        IReadOnlySet<byte> protectedStrokeColors) =>
        RepairColourContinuity(
            source,
            baseline,
            destination,
            protectedStrokeColors);

    /// <summary>
    /// Same continuity repair for an explicit colour set: thin bands (navy stripes, stems) split
    /// by a redraw stage are just as visibly "broken" as Pixel-Cords.
    /// </summary>
    internal static CordRepairReport RepairColourContinuity(
        DesignDocument source,
        byte[] baseline,
        DesignDocument destination,
        IReadOnlySet<byte> colours)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(colours);

        var width =
            destination.Width;
        var height =
            destination.Height;
        var pixels =
            Snapshot(
                destination);
        var contacts =
            new SourceContacts(
                source,
                SourceAdjacencyCounts(
                    source),
                width,
                height);
        var bridges = 0;
        var bridgePixels = 0;
        var dustComponents = 0;
        var dustPixels = 0;

        foreach (var cord in colours.OrderBy(value => value))
        {
            // --- 1. Bridge cord pieces through the old (baseline) cord path. -------------------
            var labels =
                LabelComponents(
                    pixels,
                    width,
                    height,
                    cord,
                    out var componentCount);

            if (componentCount <= 1)
                goto Dust;

            var parent =
                Enumerable.Range(
                        0,
                        componentCount + 1)
                    .ToArray();

            int Find(
                int value)
            {
                while (parent[value] != value)
                {
                    parent[value] =
                        parent[parent[value]];
                    value =
                        parent[value];
                }

                return value;
            }

            // Wave labels on revivable pixels: old cord cells the stage replaced.
            var wave =
                new int[pixels.Length];
            var steps =
                new int[pixels.Length];
            var from =
                new int[pixels.Length];
            var queue =
                new Queue<int>();

            for (var index = 0;
                 index < pixels.Length;
                 index++)
            {
                from[index] = -1;

                if (baseline[index] != cord ||
                    pixels[index] == cord)
                {
                    continue;
                }

                var seeded = false;

                ForEachEightNeighbour(
                    index,
                    width,
                    height,
                    neighbour =>
                    {
                        if (seeded ||
                            labels[neighbour] == 0)
                        {
                            return;
                        }

                        wave[index] =
                            labels[neighbour];
                        steps[index] = 1;
                        seeded = true;
                    });

                if (seeded)
                    queue.Enqueue(
                        index);
            }

            var revive =
                new List<int>();

            while (queue.Count > 0)
            {
                var current =
                    queue.Dequeue();

                ForEachEightNeighbour(
                    current,
                    width,
                    height,
                    neighbour =>
                    {
                        // Meeting a different cord component directly, or another wave.
                        var otherLabel =
                            labels[neighbour] != 0
                                ? labels[neighbour]
                                : wave[neighbour];

                        if (otherLabel == 0)
                        {
                            if (baseline[neighbour] != cord ||
                                pixels[neighbour] == cord ||
                                steps[current] >= MaximumBridgeLength)
                            {
                                return;
                            }

                            wave[neighbour] =
                                wave[current];
                            steps[neighbour] =
                                steps[current] + 1;
                            from[neighbour] =
                                current;
                            queue.Enqueue(
                                neighbour);
                            return;
                        }

                        var a =
                            Find(
                                wave[current]);
                        var b =
                            Find(
                                otherLabel);

                        if (a == b)
                            return;

                        parent[a] = b;
                        bridges++;

                        for (var walk = current;
                             walk >= 0;
                             walk = from[walk])
                        {
                            revive.Add(
                                walk);
                        }

                        if (labels[neighbour] == 0)
                        {
                            for (var walk = neighbour;
                                 walk >= 0;
                                 walk = from[walk])
                            {
                                revive.Add(
                                    walk);
                            }
                        }
                    });
            }

            foreach (var index in revive.Distinct())
            {
                if (pixels[index] == cord ||
                    !IsLegalAt(
                        index,
                        cord,
                        pixels,
                        contacts,
                        width,
                        height))
                {
                    continue;
                }

                pixels[index] =
                    cord;
                bridgePixels++;
            }

            Dust:
            // --- 2. Remove stranded cord dust the stage produced. --------------------------
            var dustLabels =
                LabelComponents(
                    pixels,
                    width,
                    height,
                    cord,
                    out var dustCount);
            var sizes =
                new int[dustCount + 1];
            var touchedByStage =
                new bool[dustCount + 1];

            for (var index = 0;
                 index < pixels.Length;
                 index++)
            {
                var label =
                    dustLabels[index];

                if (label == 0)
                    continue;

                sizes[label]++;

                if (pixels[index] != baseline[index])
                    touchedByStage[label] = true;
            }

            var baselineLabels =
                LabelComponents(
                    baseline,
                    width,
                    height,
                    cord,
                    out var baselineCount);
            var baselineSizes =
                new int[baselineCount + 1];

            foreach (var label in baselineLabels)
            {
                if (label != 0)
                    baselineSizes[label]++;
            }

            var strandedByStage =
                new bool[dustCount + 1];

            for (var index = 0;
                 index < pixels.Length;
                 index++)
            {
                var label =
                    dustLabels[index];

                if (label == 0 ||
                    sizes[label] > MaximumDustSize)
                {
                    continue;
                }

                // Either newly painted by the stage, or a piece of a larger baseline cord that
                // the stage cut off.
                if (touchedByStage[label] ||
                    (baselineLabels[index] != 0 &&
                     baselineSizes[baselineLabels[index]] >
                        MaximumDustSize))
                {
                    strandedByStage[label] = true;
                }
            }

            var removedLabels =
                new HashSet<int>();

            for (var index = 0;
                 index < pixels.Length;
                 index++)
            {
                var label =
                    dustLabels[index];

                if (label == 0 ||
                    !strandedByStage[label] ||
                    sizes[label] > MaximumDustSize)
                {
                    continue;
                }

                var replacement =
                    DustReplacement(
                        index,
                        cord,
                        pixels,
                        baseline,
                        contacts,
                        width,
                        height);

                if (replacement < 0)
                    continue;

                pixels[index] =
                    (byte)replacement;
                dustPixels++;
                removedLabels.Add(
                    label);
            }

            dustComponents +=
                removedLabels.Count;
        }

        Commit(
            pixels,
            destination);

        return new CordRepairReport(
            bridges,
            bridgePixels,
            dustComponents,
            dustPixels);
    }

    private static int DustReplacement(
        int index,
        byte cord,
        byte[] pixels,
        byte[] baseline,
        SourceContacts contacts,
        int width,
        int height)
    {
        if (baseline[index] != cord &&
            IsLegalAt(
                index,
                baseline[index],
                pixels,
                contacts,
                width,
                height))
        {
            return baseline[index];
        }

        var votes =
            new Dictionary<byte, int>();

        ForEachEightNeighbour(
            index,
            width,
            height,
            neighbour =>
            {
                var color =
                    pixels[neighbour];

                if (color != cord)
                    votes[color] =
                        votes.GetValueOrDefault(color) +
                        1;
            });

        foreach (var candidate in votes
                     .OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => pair.Key))
        {
            if (IsLegalAt(
                    index,
                    candidate.Key,
                    pixels,
                    contacts,
                    width,
                    height))
            {
                return candidate.Key;
            }
        }

        return -1;
    }

    private static int[] LabelComponents(
        byte[] pixels,
        int width,
        int height,
        byte color,
        out int count)
    {
        var labels =
            new int[pixels.Length];
        var queue =
            new Queue<int>();
        count = 0;

        for (var start = 0;
             start < pixels.Length;
             start++)
        {
            if (pixels[start] != color ||
                labels[start] != 0)
            {
                continue;
            }

            count++;
            labels[start] =
                count;
            queue.Enqueue(
                start);

            while (queue.Count > 0)
            {
                var current =
                    queue.Dequeue();
                var cx =
                    current % width;
                var cy =
                    current / width;

                for (var dy = -1;
                     dy <= 1;
                     dy++)
                {
                    var ny =
                        cy + dy;

                    if (ny < 0 ||
                        ny >= height)
                    {
                        continue;
                    }

                    for (var dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        var nx =
                            cx + dx;

                        if (nx < 0 ||
                            nx >= width)
                        {
                            continue;
                        }

                        var neighbour =
                            ny * width +
                            nx;

                        if (pixels[neighbour] != color ||
                            labels[neighbour] != 0)
                        {
                            continue;
                        }

                        labels[neighbour] =
                            count;
                        queue.Enqueue(
                            neighbour);
                    }
                }
            }
        }

        return labels;
    }

    /// <summary>
    /// Closes one-pixel notches in protected cords. Source cords are 4-connected staircases; when
    /// a redraw leaves a cord zig-zagging diagonally, single fill cells end up enclosed by the cord
    /// on three or four sides (a pale hole in the line, a navy spur poking through it). Read at
    /// carpet scale that is a comb, not a line. Such a cell becomes cord, unless the source itself
    /// has an enclosed one-cell detail near the mapped location.
    /// </summary>
    internal static int CloseCordNotches(
        DesignDocument source,
        DesignDocument destination,
        IReadOnlySet<byte> protectedStrokeColors,
        int maximumPasses = 3)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(protectedStrokeColors);

        var width =
            destination.Width;
        var height =
            destination.Height;
        var pixels =
            Snapshot(
                destination);
        var sourcePixels =
            Snapshot(
                source);
        var scaleX =
            source.Width /
            (double)width;
        var scaleY =
            source.Height /
            (double)height;
        var closed = 0;
        Span<int> neighbours =
            stackalloc int[4];

        foreach (var cord in protectedStrokeColors.OrderBy(value => value))
        {
            var sourceEnclosed =
                new bool[sourcePixels.Length];

            for (var index = 0;
                 index < sourcePixels.Length;
                 index++)
            {
                if (sourcePixels[index] == cord)
                    continue;

                var count =
                    FourNeighbours(
                        index,
                        source.Width,
                        source.Height,
                        neighbours);
                var cordNeighbours = 0;

                for (var n = 0;
                     n < count;
                     n++)
                {
                    if (sourcePixels[neighbours[n]] == cord)
                        cordNeighbours++;
                }

                sourceEnclosed[index] =
                    cordNeighbours >= 3;
            }

            for (var pass = 0;
                 pass < maximumPasses;
                 pass++)
            {
                var changed =
                    new List<int>();

                for (var index = 0;
                     index < pixels.Length;
                     index++)
                {
                    if (pixels[index] == cord ||
                        protectedStrokeColors.Contains(
                            pixels[index]))
                    {
                        continue;
                    }

                    var count =
                        FourNeighbours(
                            index,
                            width,
                            height,
                            neighbours);
                    var cordNeighbours = 0;

                    for (var n = 0;
                         n < count;
                         n++)
                    {
                        if (pixels[neighbours[n]] == cord)
                            cordNeighbours++;
                    }

                    if (cordNeighbours < 3)
                        continue;

                    var tx =
                        index % width;
                    var ty =
                        index / width;
                    var sx =
                        (int)Math.Floor(
                            (tx + 0.5) *
                            scaleX);
                    var sy =
                        (int)Math.Floor(
                            (ty + 0.5) *
                            scaleY);
                    var keep = false;

                    for (var y = Math.Max(0, sy - 2);
                         y <= Math.Min(source.Height - 1, sy + 2) && !keep;
                         y++)
                    {
                        for (var x = Math.Max(0, sx - 2);
                             x <= Math.Min(source.Width - 1, sx + 2);
                             x++)
                        {
                            if (sourceEnclosed[y * source.Width + x])
                            {
                                keep = true;
                                break;
                            }
                        }
                    }

                    if (!keep)
                        changed.Add(index);
                }

                if (changed.Count == 0)
                    break;

                foreach (var index in changed)
                {
                    pixels[index] =
                        cord;
                }

                closed +=
                    changed.Count;
            }
        }

        Commit(
            pixels,
            destination);

        return closed;
    }

    private static int[,] BuildSeparatorTable(
        int[,] adjacency,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        var table =
            new int[256, 256];

        for (var a = 0;
             a < 256;
             a++)
        {
            for (var b = 0;
                 b < 256;
                 b++)
            {
                table[a, b] = -1;

                if (a == b)
                    continue;

                var best = -1;
                var bestScore = 0L;
                var bestProtected = false;

                for (var s = 0;
                     s < 256;
                     s++)
                {
                    if (s == a ||
                        s == b ||
                        adjacency[a, s] == 0 ||
                        adjacency[b, s] == 0)
                    {
                        continue;
                    }

                    var isProtected =
                        protectedStrokeColors.Contains(
                            (byte)s);
                    var score =
                        Math.Min(
                            (long)adjacency[a, s],
                            adjacency[b, s]);

                    if ((isProtected && !bestProtected) ||
                        (isProtected == bestProtected &&
                         score > bestScore))
                    {
                        best = s;
                        bestScore = score;
                        bestProtected = isProtected;
                    }
                }

                table[a, b] = best;
            }
        }

        return table;
    }

    private static int ChooseRepairTarget(
        int p,
        int q,
        bool pChanged,
        bool qChanged,
        byte[] pixels,
        int width,
        int height,
        IReadOnlySet<byte> protectedStrokeColors)
    {
        var pProtected =
            protectedStrokeColors.Contains(
                pixels[p]);
        var qProtected =
            protectedStrokeColors.Contains(
                pixels[q]);

        if (pProtected != qProtected)
            return pProtected ? q : p;

        // Draw the separator on the locally THICKER side. Painting it on a thin band (a 3-4 px
        // navy stripe beside a large background) would visibly thin that band; the broad side
        // absorbs one pixel without changing the drawing.
        var pThickness =
            LocalSameColourCount(
                p,
                pixels,
                width,
                height);
        var qThickness =
            LocalSameColourCount(
                q,
                pixels,
                width,
                height);

        if (pThickness != qThickness)
            return pThickness > qThickness ? p : q;

        if (pChanged != qChanged)
            return pChanged ? p : q;

        return SameColourEightNeighbours(
                   p,
                   pixels,
                   width,
                   height) <=
               SameColourEightNeighbours(
                   q,
                   pixels,
                   width,
                   height)
            ? p
            : q;
    }

    private static int LocalSameColourCount(
        int index,
        byte[] pixels,
        int width,
        int height)
    {
        var x =
            index % width;
        var y =
            index / width;
        var color =
            pixels[index];
        var count = 0;

        for (var dy = -2;
             dy <= 2;
             dy++)
        {
            var ny =
                y + dy;

            if (ny < 0 ||
                ny >= height)
            {
                continue;
            }

            for (var dx = -2;
                 dx <= 2;
                 dx++)
            {
                var nx =
                    x + dx;

                if (nx >= 0 &&
                    nx < width &&
                    pixels[ny * width + nx] == color)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int SameColourEightNeighbours(
        int index,
        byte[] pixels,
        int width,
        int height)
    {
        var count = 0;
        var color =
            pixels[index];

        ForEachEightNeighbour(
            index,
            width,
            height,
            neighbour =>
            {
                if (pixels[neighbour] == color)
                    count++;
            });

        return count;
    }

    private static bool IsLegalAt(
        int index,
        byte color,
        byte[] pixels,
        SourceContacts contacts,
        int width,
        int height)
    {
        Span<int> neighbours =
            stackalloc int[4];
        var count =
            FourNeighbours(
                index,
                width,
                height,
                neighbours);

        for (var n = 0;
             n < count;
             n++)
        {
            var other =
                pixels[neighbours[n]];

            // Feasibility of a repair uses the design-wide contact table; the strict local rule
            // is for DETECTING gaps. Requiring local evidence for the repair itself would forbid
            // most separator redraws next to a boundary the stage legitimately moved.
            if (other != color &&
                contacts.Global[color, other] == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int FourNeighbours(
        int index,
        int width,
        int height,
        Span<int> result)
    {
        var x =
            index % width;
        var y =
            index / width;
        var count = 0;

        if (x > 0)
            result[count++] = index - 1;

        if (x + 1 < width)
            result[count++] = index + 1;

        if (y > 0)
            result[count++] = index - width;

        if (y + 1 < height)
            result[count++] = index + width;

        return count;
    }

    private static void ForEachEightNeighbour(
        int index,
        int width,
        int height,
        Action<int> action)
    {
        var x =
            index % width;
        var y =
            index / width;

        for (var dy = -1;
             dy <= 1;
             dy++)
        {
            var ny =
                y + dy;

            if (ny < 0 ||
                ny >= height)
            {
                continue;
            }

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
                    x + dx;

                if (nx < 0 ||
                    nx >= width)
                {
                    continue;
                }

                action(
                    ny * width +
                    nx);
            }
        }
    }

    /// <summary>3-4 chamfer distance (in target pixels) to the nearest pixel of <paramref name="color"/>.</summary>
    private static double[] ChamferDistanceTo(
        byte[] pixels,
        int width,
        int height,
        byte color)
    {
        const int Infinite =
            int.MaxValue / 4;
        var d =
            new int[pixels.Length];

        for (var index = 0;
             index < pixels.Length;
             index++)
        {
            d[index] =
                pixels[index] == color
                    ? 0
                    : Infinite;
        }

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var index =
                    y * width +
                    x;
                var best =
                    d[index];

                if (x > 0)
                    best = Math.Min(best, d[index - 1] + 3);

                if (y > 0)
                {
                    best = Math.Min(best, d[index - width] + 3);

                    if (x > 0)
                        best = Math.Min(best, d[index - width - 1] + 4);

                    if (x + 1 < width)
                        best = Math.Min(best, d[index - width + 1] + 4);
                }

                d[index] = best;
            }
        }

        for (var y = height - 1;
             y >= 0;
             y--)
        {
            for (var x = width - 1;
                 x >= 0;
                 x--)
            {
                var index =
                    y * width +
                    x;
                var best =
                    d[index];

                if (x + 1 < width)
                    best = Math.Min(best, d[index + 1] + 3);

                if (y + 1 < height)
                {
                    best = Math.Min(best, d[index + width] + 3);

                    if (x + 1 < width)
                        best = Math.Min(best, d[index + width + 1] + 4);

                    if (x > 0)
                        best = Math.Min(best, d[index + width - 1] + 4);
                }

                d[index] = best;
            }
        }

        var result =
            new double[pixels.Length];

        for (var index = 0;
             index < pixels.Length;
             index++)
        {
            result[index] =
                d[index] >= Infinite
                    ? double.PositiveInfinity
                    : d[index] / 3d;
        }

        return result;
    }

    private static byte SourceOwner(
        DesignDocument source,
        int targetIndex,
        int targetWidth,
        double scaleX,
        double scaleY)
    {
        var tx =
            targetIndex % targetWidth;
        var ty =
            targetIndex / targetWidth;
        var sx =
            Math.Clamp(
                (int)Math.Floor(
                    (tx + 0.5) *
                    scaleX),
                0,
                source.Width - 1);
        var sy =
            Math.Clamp(
                (int)Math.Floor(
                    (ty + 0.5) *
                    scaleY),
                0,
                source.Height - 1);

        return source.GetPixel(
            sx,
            sy);
    }

    private static void Commit(
        byte[] pixels,
        DesignDocument destination)
    {
        for (var y = 0;
             y < destination.Height;
             y++)
        {
            for (var x = 0;
                 x < destination.Width;
                 x++)
            {
                var value =
                    pixels[y * destination.Width + x];

                if (destination.GetPixel(
                        x,
                        y) !=
                    value)
                {
                    destination.SetPixel(
                        x,
                        y,
                        value);
                }
            }
        }
    }

    /// <summary>
    /// Which colour contacts the SOURCE allows at a given target location. A pair that touches
    /// somewhere in the design is not automatically allowed everywhere: navy may meet the field
    /// colour at one motif tip and still always be separated by a cord along a long band. A
    /// contact is legal only when the same pair is 4-adjacent in the source within
    /// <see cref="LocalContactRadius"/> source pixels of the mapped location.
    /// </summary>
    internal sealed class SourceContacts
    {
        private readonly byte[] _source;
        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private readonly int[,] _global;
        private readonly int _targetWidth;
        private readonly double _scaleX;
        private readonly double _scaleY;

        private readonly IReadOnlySet<byte> _cords;

        public SourceContacts(
            DesignDocument source,
            int[,] global,
            int targetWidth,
            int targetHeight,
            IReadOnlySet<byte>? cords = null)
        {
            _cords =
                cords ??
                ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                    source);
            _source =
                Snapshot(
                    source);
            _sourceWidth =
                source.Width;
            _sourceHeight =
                source.Height;
            _global =
                global;
            _targetWidth =
                targetWidth;
            _scaleX =
                source.Width /
                (double)targetWidth;
            _scaleY =
                source.Height /
                (double)targetHeight;
        }

        public int[,] Global =>
            _global;

        public bool Allowed(
            byte a,
            byte b,
            int targetIndex)
        {
            if (a == b)
                return true;

            if (_global[a, b] == 0)
                return false;

            // A contact with the cord itself is always the cord doing its job; only two fills
            // meeting without the cord between them need local source evidence.
            if (_cords.Contains(a) ||
                _cords.Contains(b))
            {
                return true;
            }

            var tx =
                targetIndex % _targetWidth;
            var ty =
                targetIndex / _targetWidth;
            var cx =
                (int)Math.Floor(
                    (tx + 0.5) *
                    _scaleX);
            var cy =
                (int)Math.Floor(
                    (ty + 0.5) *
                    _scaleY);

            for (var y = Math.Max(0, cy - LocalContactRadius);
                 y <= Math.Min(_sourceHeight - 1, cy + LocalContactRadius);
                 y++)
            {
                for (var x = Math.Max(0, cx - LocalContactRadius);
                     x <= Math.Min(_sourceWidth - 1, cx + LocalContactRadius);
                     x++)
                {
                    var here =
                        _source[y * _sourceWidth + x];

                    if (here != a &&
                        here != b)
                    {
                        continue;
                    }

                    var other =
                        here == a
                            ? b
                            : a;

                    if ((x + 1 < _sourceWidth &&
                         _source[y * _sourceWidth + x + 1] == other) ||
                        (y + 1 < _sourceHeight &&
                         _source[(y + 1) * _sourceWidth + x] == other))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Protected colour that touches both sides near this location, if any.</summary>
        public int LocalSeparator(
            byte a,
            byte b,
            int targetIndex,
            IReadOnlySet<byte> protectedStrokeColors)
        {
            foreach (var s in protectedStrokeColors.OrderBy(value => value))
            {
                if (s != a &&
                    s != b &&
                    Allowed(a, s, targetIndex) &&
                    Allowed(b, s, targetIndex))
                {
                    return s;
                }
            }

            return -1;
        }
    }

    /// <summary>Source neighbourhood (pixels) in which a colour contact must also exist.</summary>
    internal const int LocalContactRadius = 2;
}
