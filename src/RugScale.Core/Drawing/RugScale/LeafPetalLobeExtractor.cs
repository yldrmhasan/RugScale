using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Splits a connected filled colour mass into endpoint lobes by using its source skeleton as a
/// Voronoi spine. This is the key case for real carpet florals: several leaves often share one
/// green stem, so ordinary connected-component analysis sees one giant region and misses the
/// individual designer arcs.
/// </summary>
internal static class LeafPetalLobeExtractor
{
    private const int MinimumParentArea = 180;
    private const int MinimumBranchPixels = 10;
    private const int MinimumLobeArea = 48;
    private const int MaximumBranchesPerRegion = 32;
    private const int MaximumTerminalSpurPixels = 9;
    private const int MaximumSpurPrunePasses = 8;

    private static readonly (int X, int Y)[] EightDirections =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0),           (1, 0),
        (-1, 1),  (0, 1),  (1, 1),
    ];

    private static readonly (int X, int Y)[] FourDirections =
    [
        (-1, 0),
        (1, 0),
        (0, -1),
        (0, 1),
    ];

    public static IReadOnlyList<LeafPetalRegion> Extract(
        DesignDocument source,
        LeafPetalRegion parent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parent);

        if (parent.Area <
            MinimumParentArea)
        {
            return Array.Empty<LeafPetalRegion>();
        }

        var width =
            parent.Width;
        var height =
            parent.Height;
        var localCount =
            checked(
                width *
                height);
        var mask =
            new bool[localCount];

        foreach (var pixel in parent.Pixels)
        {
            var x =
                pixel %
                source.Width -
                parent.MinX;
            var y =
                pixel /
                source.Width -
                parent.MinY;

            mask[
                y *
                width +
                x] = true;
        }

        var skeleton =
            CurveScaleEngine.ThinZhangSuen(
                mask,
                width,
                height);

        // Digital thinning can leave a short horizontal/vertical fork at a broad leaf tip. With
        // raw 8-neighbour degree that fork looks like a real branch junction and the actual long
        // base-to-apex lobe is lost. Prune only terminal spurs shorter than the real branch
        // threshold, then compute graph degree on a diagonal-dealiased skeleton.
        PruneShortTerminalSpurs(
            skeleton,
            width,
            height);

        var degree =
            new byte[localCount];
        var junction =
            new bool[localCount];
        var endpoints =
            new List<int>();

        for (var index = 0;
             index < localCount;
             index++)
        {
            if (!skeleton[index])
                continue;

            var neighbors =
                SkeletonNeighbors(
                    skeleton,
                    width,
                    height,
                    index);
            degree[index] =
                (byte)neighbors.Count;

            if (neighbors.Count == 1)
                endpoints.Add(index);

            if (neighbors.Count >= 3)
                junction[index] = true;
        }

        if (endpoints.Count < 2 ||
            !junction.Any(static value =>
                value))
        {
            return Array.Empty<LeafPetalRegion>();
        }

        var branches =
            new List<List<int>>();

        foreach (var endpoint in endpoints)
        {
            var path =
                TraceEndpointBranch(
                    skeleton,
                    junction,
                    width,
                    height,
                    endpoint);

            if (path.Count <
                    MinimumBranchPixels ||
                !junction[path[^1]])
            {
                continue;
            }

            branches.Add(path);
        }

        branches =
            branches
                .OrderByDescending(path =>
                    path.Count)
                .Take(MaximumBranchesPerRegion)
                .ToList();

        if (branches.Count == 0)
            return Array.Empty<LeafPetalRegion>();

        // Multi-source geodesic Voronoi inside the ORIGINAL component. Skeleton pixels belonging
        // to a tip branch are labelled with that branch; the remaining trunk/junction skeleton is
        // label 0. Region pixels inherit the nearest skeleton label.
        var owner =
            Enumerable.Repeat(
                    -1,
                    localCount)
                .ToArray();

        for (var index = 0;
             index < localCount;
             index++)
        {
            if (skeleton[index])
                owner[index] = 0;
        }

        for (var branchIndex = 0;
             branchIndex < branches.Count;
             branchIndex++)
        {
            var label =
                branchIndex +
                1;
            var path =
                branches[branchIndex];

            // The terminal junction remains trunk-owned. The branch itself starts one pixel away,
            // so the artificial lobe cut happens at the natural neck rather than through the stem.
            for (var i = 0;
                 i < path.Count - 1;
                 i++)
            {
                owner[path[i]] =
                    label;
            }
        }

        var queue =
            new Queue<int>(
                localCount);

        // Give branch seeds deterministic priority on equal-distance ties, then seed the trunk.
        for (var label = 1;
             label <= branches.Count;
             label++)
        {
            for (var index = 0;
                 index < localCount;
                 index++)
            {
                if (owner[index] == label)
                    queue.Enqueue(index);
            }
        }

        for (var index = 0;
             index < localCount;
             index++)
        {
            if (owner[index] == 0)
                queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            var current =
                queue.Dequeue();
            var currentOwner =
                owner[current];
            var x =
                current %
                width;
            var y =
                current /
                width;

            foreach (var (dx, dy) in EightDirections)
            {
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

                var next =
                    ny *
                    width +
                    nx;

                if (!mask[next] ||
                    owner[next] >= 0)
                {
                    continue;
                }

                owner[next] =
                    currentOwner;
                queue.Enqueue(next);
            }
        }

        var result =
            new List<LeafPetalRegion>();

        for (var label = 1;
             label <= branches.Count;
             label++)
        {
            var pixels =
                new List<int>();
            var boundary =
                new List<int>();
            var minX =
                int.MaxValue;
            var minY =
                int.MaxValue;
            var maxX =
                int.MinValue;
            var maxY =
                int.MinValue;

            for (var local = 0;
                 local < localCount;
                 local++)
            {
                if (owner[local] !=
                    label)
                {
                    continue;
                }

                var localX =
                    local %
                    width;
                var localY =
                    local /
                    width;
                var globalX =
                    parent.MinX +
                    localX;
                var globalY =
                    parent.MinY +
                    localY;
                var global =
                    globalY *
                    source.Width +
                    globalX;

                pixels.Add(global);
                minX =
                    Math.Min(
                        minX,
                        globalX);
                minY =
                    Math.Min(
                        minY,
                        globalY);
                maxX =
                    Math.Max(
                        maxX,
                        globalX);
                maxY =
                    Math.Max(
                        maxY,
                        globalY);

                var exposed =
                    false;

                foreach (var (dx, dy) in FourDirections)
                {
                    var nx =
                        localX +
                        dx;
                    var ny =
                        localY +
                        dy;

                    if (nx < 0 ||
                        nx >= width ||
                        ny < 0 ||
                        ny >= height ||
                        owner[
                            ny *
                            width +
                            nx] !=
                        label)
                    {
                        exposed = true;
                        break;
                    }
                }

                if (exposed)
                    boundary.Add(global);
            }

            if (pixels.Count <
                    MinimumLobeArea ||
                minX ==
                    int.MaxValue)
            {
                continue;
            }

            result.Add(
                new LeafPetalRegion(
                    parent.Color,
                    pixels,
                    boundary,
                    minX,
                    minY,
                    maxX,
                    maxY,
                    IsSubLobe: true));
        }

        return result;
    }

    private static List<int> TraceEndpointBranch(
        IReadOnlyList<bool> skeleton,
        IReadOnlyList<bool> junction,
        int width,
        int height,
        int endpoint)
    {
        var result =
            new List<int>
            {
                endpoint,
            };
        var previous = -1;
        var current =
            endpoint;
        var guard =
            checked(
                width *
                height);

        while (guard-- > 0)
        {
            if (current !=
                    endpoint &&
                junction[current])
            {
                break;
            }

            var neighbors =
                SkeletonNeighbors(
                    skeleton,
                    width,
                    height,
                    current)
                    .Where(next =>
                        next != previous)
                    .ToArray();

            if (neighbors.Length == 0)
                break;

            if (neighbors.Length > 1)
            {
                // The thinning may represent a junction as a tiny cluster. Treat the current
                // location as the neck instead of selecting an arbitrary outgoing arm.
                break;
            }

            var next =
                neighbors[0];

            previous =
                current;
            current =
                next;
            result.Add(
                current);

            if (junction[current])
                break;
        }

        return result;
    }

    private static void PruneShortTerminalSpurs(
        bool[] skeleton,
        int width,
        int height)
    {
        for (var pass = 0;
             pass < MaximumSpurPrunePasses;
             pass++)
        {
            var degree =
                new byte[skeleton.Length];
            var endpoints =
                new List<int>();

            for (var index = 0;
                 index < skeleton.Length;
                 index++)
            {
                if (!skeleton[index])
                    continue;

                var count =
                    SkeletonNeighbors(
                        skeleton,
                        width,
                        height,
                        index)
                    .Count;

                degree[index] =
                    (byte)count;

                if (count == 1)
                    endpoints.Add(index);
            }

            var remove =
                new HashSet<int>();

            foreach (var endpoint in endpoints)
            {
                var path =
                    new List<int>
                    {
                        endpoint,
                    };
                var previous = -1;
                var current =
                    endpoint;

                while (path.Count <=
                       MaximumTerminalSpurPixels + 1)
                {
                    if (current != endpoint &&
                        degree[current] >= 3)
                    {
                        break;
                    }

                    var neighbors =
                        SkeletonNeighbors(
                                skeleton,
                                width,
                                height,
                                current)
                            .Where(next =>
                                next != previous)
                            .ToArray();

                    if (neighbors.Length != 1)
                        break;

                    var next =
                        neighbors[0];

                    previous =
                        current;
                    current =
                        next;
                    path.Add(
                        current);

                    if (degree[current] >= 3 ||
                        degree[current] == 1)
                    {
                        break;
                    }
                }

                if (path.Count - 1 >
                        MaximumTerminalSpurPixels ||
                    degree[path[^1]] < 3)
                {
                    continue;
                }

                // Keep the junction itself. Only the tiny terminal fork is discarded.
                for (var i = 0;
                     i < path.Count - 1;
                     i++)
                {
                    remove.Add(
                        path[i]);
                }
            }

            if (remove.Count == 0)
                return;

            foreach (var index in remove)
                skeleton[index] = false;
        }
    }

    private static List<int> SkeletonNeighbors(
        IReadOnlyList<bool> skeleton,
        int width,
        int height,
        int index)
    {
        var result =
            new List<int>(8);
        var x =
            index %
            width;
        var y =
            index /
            width;

        foreach (var (dx, dy) in EightDirections)
        {
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

            var next =
                ny *
                width +
                nx;

            if (!skeleton[next])
                continue;

            if (dx != 0 &&
                dy != 0)
            {
                // A diagonal neighbour is redundant when either orthogonal bridge pixel is also
                // on the skeleton. Counting that redundant triangle made ordinary pixel staircases
                // appear to have degree 3/4 and created hundreds of false junctions.
                var horizontal =
                    y *
                    width +
                    (x + dx);
                var vertical =
                    (y + dy) *
                    width +
                    x;

                if (skeleton[horizontal] ||
                    skeleton[vertical])
                {
                    continue;
                }
            }

            result.Add(next);
        }

        return result;
    }
}
