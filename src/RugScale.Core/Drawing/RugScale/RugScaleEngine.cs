using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// RugScale's indexed-pattern resize engine. RugScale treats palette indexes as categorical design
/// information rather than RGB samples. Progressive shrink combines a motif atlas (connected
/// source motif primitives + colour-independent repeated shape families) with contour/branch
/// topology, rapport/repeat phase and designer-symmetry constraints.
///
/// Source-neighbour analysis is periodic (wrap-aware), because a carpet repeat's left/right and
/// top/bottom borders are neighbours in the woven result. The engine never invents a colour:
/// reconstructed motifs reuse palette indexes that already exist in the source design.
/// </summary>
public static class RugScaleEngine
{
    private const int PaletteSlots = 256;
    private const int CandidateLimit = 4;
    private static readonly bool RepeatAware = true;

    // Relative opposite-neighbour pairs used by one-pixel gap repair. This must live outside the
    // per-pixel loops: a Span collection expression inside a large loop can be stack-backed on
    // every iteration and exhaust the thread stack on real 48x50 carpet rasters.
    private static readonly (int Ax, int Ay, int Bx, int By)[] GapNeighbourPairs =
    [
        (-1, 0, 1, 0),
        (0, -1, 0, 1),
        (-1, -1, 1, 1),
        (1, -1, -1, 1),
    ];

    [Flags]
    private enum FeatureFlags : byte
    {
        None = 0,
        Edge = 1 << 0,
        Thin = 1 << 1,
        Corner = 1 << 2,
        Endpoint = 1 << 3,
        Junction = 1 << 4,
    }

    private sealed class RegionInfo(byte color)
    {
        public byte Color { get; } = color;
        public int Area { get; set; }
        public int CriticalCount { get; set; }
    }

    private sealed class BranchSegment(
        byte color,
        List<int> sourcePixels,
        byte startDegree,
        byte endDegree)
    {
        public byte Color { get; } = color;
        public List<int> SourcePixels { get; } = sourcePixels;
        public byte StartDegree { get; } = startDegree;
        public byte EndDegree { get; } = endDegree;

        public bool StartsAtEndpoint => StartDegree <= 1;
        public bool EndsAtEndpoint => EndDegree <= 1;
        public bool TouchesJunction => StartDegree >= 3 || EndDegree >= 3;
    }

    private enum RepeatBandSide : byte
    {
        Top,
        Bottom,
        Left,
        Right,
    }

    private sealed class RepeatBand(
        RepeatBandSide side,
        int crossStart,
        int thickness,
        int period,
        int repeatStart,
        int repeatCount,
        double score,
        bool isOuter)
    {
        public RepeatBandSide Side { get; } = side;

        /// <summary>
        /// Absolute source coordinate of the strip on the cross axis:
        /// Y for horizontal bands, X for vertical bands.
        /// </summary>
        public int CrossStart { get; } = crossStart;

        public int Thickness { get; } = thickness;
        public int Period { get; } = period;
        public int RepeatStart { get; } = repeatStart;
        public int RepeatCount { get; } = repeatCount;
        public double Score { get; } = score;
        public bool IsOuter { get; } = isOuter;

        public bool IsHorizontal =>
            Side is RepeatBandSide.Top or RepeatBandSide.Bottom;
    }

    private readonly record struct SymmetryConstraint(
        bool Enabled,
        int SourceShift,
        double PixelScore,
        double StructuralScore,
        double StructuralCoverage);

    private sealed class Analysis
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required byte[] Pixels { get; init; }
        public required int[] RegionIds { get; init; }
        public required List<RegionInfo> Regions { get; init; }
        public required float[] Importance { get; init; }
        public required FeatureFlags[] Features { get; init; }
        public required byte[] SameNeighbourCounts { get; init; }
        public required int[] ColorCounts { get; init; }

        /// <summary>Mirror across the vertical design axis (left ↔ right).</summary>
        public required SymmetryConstraint LeftRightSymmetry { get; init; }

        /// <summary>Mirror across the horizontal design axis (top ↔ bottom).</summary>
        public required SymmetryConstraint TopBottomSymmetry { get; init; }

        public List<BranchSegment> Branches { get; } = new();
    }

    /// <summary>
    /// Top four source colours for one destination cell, ordered by RugScale's unary feature
    /// score. Keeping the shortlist allows later topology/area passes to change a pixel without
    /// ever introducing a colour that had no support in that source footprint.
    /// </summary>
    private struct CandidateSet
    {
        private byte _c0, _c1, _c2, _c3;
        private float _s0, _s1, _s2, _s3;
        public byte Count { get; private set; }

        public byte TopColor => _c0;
        public float TopScore => _s0;

        public byte ColorAt(int index) => index switch
        {
            0 => _c0,
            1 => _c1,
            2 => _c2,
            3 => _c3,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public float ScoreAt(int index) => index switch
        {
            0 => _s0,
            1 => _s1,
            2 => _s2,
            3 => _s3,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public bool TryGetScore(byte color, out float score)
        {
            for (var i = 0; i < Count; i++)
            {
                if (ColorAt(i) != color)
                    continue;
                score = ScoreAt(i);
                return true;
            }

            score = 0;
            return false;
        }

        public void Add(byte color, float score)
        {
            // A colour is added only once per source footprint, but keep this defensive so later
            // refactors cannot accidentally create duplicate shortlist entries.
            for (var i = 0; i < Count; i++)
            {
                if (ColorAt(i) != color)
                    continue;
                if (score > ScoreAt(i))
                    Set(i, color, score);
                SortDescending();
                return;
            }

            if (Count < CandidateLimit)
            {
                Set(Count, color, score);
                Count++;
                SortDescending();
                return;
            }

            if (score <= _s3)
                return;

            _c3 = color;
            _s3 = score;
            SortDescending();
        }

        private void SortDescending()
        {
            for (var i = 0; i < Count - 1; i++)
            {
                for (var j = i + 1; j < Count; j++)
                {
                    if (ScoreAt(i) >= ScoreAt(j))
                        continue;

                    var ci = ColorAt(i);
                    var si = ScoreAt(i);
                    Set(i, ColorAt(j), ScoreAt(j));
                    Set(j, ci, si);
                }
            }
        }

        private void Set(int index, byte color, float score)
        {
            switch (index)
            {
                case 0: _c0 = color; _s0 = score; break;
                case 1: _c1 = color; _s1 = score; break;
                case 2: _c2 = color; _s2 = score; break;
                case 3: _c3 = color; _s3 = score; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    /// <summary>
    /// Fills an already-created destination document using motif/topology reconstruction.
    /// Curve-heavy outlined artwork is intentionally handled by ScaleMode.CurveFill.
    /// </summary>
    public static void Resize(
        DesignDocument source,
        DesignDocument destination) =>
        Resize(
            source,
            destination,
            sourceWarpDensity: 1,
            sourceWeftDensity: 1,
            targetWarpDensity: 1,
            targetWeftDensity: 1);

    /// <summary>
    /// Quality-aware RugScale entry point. Geometry scales with requested pixel dimensions, while
    /// source-guided curve/stroke thickness scales ONLY with target/source quality. This is the
    /// critical carpet-design distinction between "make the carpet physically larger" and
    /// "increase the weave/pixel density".
    /// </summary>
    public static void Resize(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Width == destination.Width &&
            source.Height == destination.Height)
        {
            Copy(source, destination);
            return;
        }

        if (destination.Width >= source.Width &&
            destination.Height >= source.Height)
        {
            // Motif RugScale stays motif-only. Pure enlargement has no missing motif information
            // to reconstruct, so use categorical centre-nearest here. Outlined/curved artwork is
            // handled explicitly by ScaleMode.CurveFill.
            ScaleNearest(
                source,
                destination);
            return;
        }

        var scaleX = destination.Width / (double)source.Width;
        var scaleY = destination.Height / (double)source.Height;
        var minimumScale = Math.Min(scaleX, scaleY);
        var progressive = minimumScale >= 0.72;

        // Progressive RugScale does not need full connected-component/region bookkeeping; branch
        // topology, feature importance and source-footprint candidates are sufficient. Skipping
        // region BFS on multi-million-pixel carpet rasters removes a large amount of work.
        var analysis = Analyze(
            source,
            includeRegions: !progressive,
            buildBranches: progressive);

        // Real carpet editing is normally progressive: e.g. 200 -> 160 -> 120 rather than one
        // destructive 200 -> 120 jump. For steps that retain at least 72% on both axes, fidelity
        // to the current artwork is more important than aggressively rebuilding every thin line.
        if (progressive)
        {
            ResizeProgressive(
                source,
                destination,
                analysis);
            return;
        }

        ResizeHeavy(
            destination,
            analysis);
    }

    /// <summary>
    /// Conservative path for the normal carpet-design workflow (roughly 75-85% per step).
    /// Start from symmetry-safe centre-nearest sampling and make only high-confidence structural
    /// repairs. No global colour-area forcing is used here: on modest reductions centre sampling
    /// already keeps the source colour ratios very close, while repeated area forcing accumulates
    /// visible motif distortion over 200 -> 160 -> 120 workflows.
    /// </summary>
    private static void ResizeProgressive(DesignDocument source, DesignDocument destination, Analysis analysis)
    {
        var candidates = BuildInitialResult(analysis, destination);

        // BuildInitialResult exists to calculate source-footprint candidate support. Replace its
        // aggressive winner image with the least-destructive baseline before making repairs.
        ScaleNearest(source, destination);

        var locked = new bool[destination.Width * destination.Height];
        PreserveCriticalFeaturesProgressive(analysis, destination, candidates, locked);
        RepairBranchSegmentsProgressive(analysis, destination, candidates, locked);
        RepairOnePixelGapsProgressive(destination, candidates, locked);

        // V5: border/rapport repeats are a structural constraint of the carpet artwork. Detect
        // strong outer repeat bands from the source itself and re-lock their phase after branch
        // repair. Critical branch/keypoint cells remain protected by the lock map.
        var repeatBands = DetectAllRepeatBands(analysis);

        // Snapshot the pre-repeat raster. V7 only protects cells that the proven V5 outer-repeat
        // pass actually changes; everything else remains available to motif reconstruction.
        var beforeRepeat = CapturePixels(destination);
        StabilizeRepeatBandsProgressive(
            analysis,
            destination,
            candidates,
            locked,
            repeatBands);
        var outerRepeatChanges = BuildOuterRepeatChangeMask(
            analysis,
            destination,
            repeatBands,
            beforeRepeat);

        // Designer topology rule: outside a verified repeat zone, do not let local repair create
        // a new multi-way same-colour junction unless the mapped source footprint contains one.
        // This prevents independent ornaments from melting into one large component.
        PruneUnsupportedTopologyMerges(
            analysis,
            destination,
            candidates,
            locked,
            repeatBands);

        // V7 motif-first authority runs after all local topology heuristics. Only target cells
        // that V5's OUTER repeat pass demonstrably corrected are protected from the atlas.
        MotifShrinkEngine.Reconstruct(
            source,
            destination,
            outerRepeatChanges);

        // The motif atlas itself can create a new contact after the first topology pass because
        // two independent source primitives may forward-project onto neighbouring cells in the
        // smaller raster. Validate topology once more after reconstruction. The guard remains
        // source-evidence based and repeat-aware; this is not a generic smoothing operation.
        PruneUnsupportedTopologyMerges(
            analysis,
            destination,
            candidates,
            locked,
            repeatBands);

        EnforceSourceSymmetry(analysis, destination, candidates, locked);
    }

    /// <summary>
    /// Strong topology-preserving path for unusually large one-step reductions. This intentionally
    /// protects complete thin strokes and projected connectivity because the baseline would
    /// otherwise drop them altogether.
    /// </summary>
    private static void ResizeHeavy(DesignDocument destination, Analysis analysis)
    {
        var candidates = BuildInitialResult(analysis, destination);

        CorrectColorArea(analysis, destination, candidates, locked: null, maxPasses: 2);

        var locked = new bool[destination.Width * destination.Height];
        PreserveCriticalFeatures(analysis, destination, candidates, locked, lockThinLines: true);
        PreserveRegions(analysis, destination, candidates, locked);
        RepairProjectedThinConnectivity(analysis, destination, candidates, locked, lockPaintedPixels: true);
        RepairOnePixelGaps(destination, candidates, locked, lockPaintedPixels: true);

        CorrectColorArea(analysis, destination, candidates, locked, maxPasses: 1);
    }

    private static Analysis Analyze(
        DesignDocument source,
        bool includeRegions,
        bool buildBranches)
    {
        var width = source.Width;
        var height = source.Height;
        var count = checked(width * height);
        var pixels = new byte[count];
        var colorCounts = new int[PaletteSlots];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var color = source.GetPixel(x, y);
                pixels[row + x] = color;
                colorCounts[color]++;
            }
        }

        int[] regionIds;
        List<RegionInfo> regions;

        if (includeRegions)
        {
            (regionIds, regions) = BuildRegions(pixels, width, height);
        }
        else
        {
            regionIds = Array.Empty<int>();
            regions = new List<RegionInfo>();
        }

        var features = new FeatureFlags[count];
        var sameNeighbourCounts = new byte[count];
        var importance = new float[count];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var i = row + x;
                var color = pixels[i];

                var n = Same(pixels, width, height, x, y - 1, color);
                var ne = Same(pixels, width, height, x + 1, y - 1, color);
                var e = Same(pixels, width, height, x + 1, y, color);
                var se = Same(pixels, width, height, x + 1, y + 1, color);
                var s = Same(pixels, width, height, x, y + 1, color);
                var sw = Same(pixels, width, height, x - 1, y + 1, color);
                var w = Same(pixels, width, height, x - 1, y, color);
                var nw = Same(pixels, width, height, x - 1, y - 1, color);

                var same8 = BoolInt(n) + BoolInt(ne) + BoolInt(e) + BoolInt(se) +
                            BoolInt(s) + BoolInt(sw) + BoolInt(w) + BoolInt(nw);
                sameNeighbourCounts[i] = (byte)same8;

                // Number of separate same-colour arcs around the 8-neighbour ring. On a one-pixel
                // skeleton this distinguishes an endpoint (one arc), a bend (two arcs) and a
                // junction (three or more arcs).
                var groups = BoolInt(!nw && n) + BoolInt(!n && ne) + BoolInt(!ne && e) +
                             BoolInt(!e && se) + BoolInt(!se && s) + BoolInt(!s && sw) +
                             BoolInt(!sw && w) + BoolInt(!w && nw);

                var edge = !(n && e && s && w);
                var horizontalRun = LocalRun(pixels, width, height, x, y, color, horizontal: true);
                var verticalRun = LocalRun(pixels, width, height, x, y, color, horizontal: false);
                var thin = edge && Math.Min(horizontalRun, verticalRun) <= 2;

                var oppositePair = same8 == 2 && ((n && s) || (ne && sw) || (e && w) || (se && nw));
                var adjacentCardinalOutside =
                    (!n && !e) || (!e && !s) || (!s && !w) || (!w && !n);

                var endpoint = thin && same8 <= 2 && groups == 1;
                var junction = thin && groups >= 3;
                var corner = edge && ((thin && groups == 2 && !oppositePair) || adjacentCardinalOutside);

                var flags = FeatureFlags.None;
                if (edge) flags |= FeatureFlags.Edge;
                if (thin) flags |= FeatureFlags.Thin;
                if (corner) flags |= FeatureFlags.Corner;
                if (endpoint) flags |= FeatureFlags.Endpoint;
                if (junction) flags |= FeatureFlags.Junction;
                features[i] = flags;

                var smallRegionBoost = 0f;
                RegionInfo? region = null;
                if (includeRegions)
                {
                    region = regions[regionIds[i]];
                    smallRegionBoost = Math.Clamp(
                        3.0f / MathF.Sqrt(Math.Max(1, region.Area)),
                        0f,
                        2.5f);
                }

                // Thin strokes deliberately outweigh raw area. That is the central difference
                // from majority resize: a one-pixel contour can beat two or three background
                // pixels when dropping several source cells into one destination cell.
                var score = 1f + smallRegionBoost;
                if (edge) score += 1.0f;
                if (thin) score += 5.0f;
                if (corner) score += 3.0f;
                if (endpoint) score += 4.0f;
                if (junction) score += 5.5f;
                importance[i] = score;

                if (region is not null &&
                    (flags & (FeatureFlags.Corner | FeatureFlags.Endpoint | FeatureFlags.Junction | FeatureFlags.Thin)) != 0)
                {
                    region.CriticalCount++;
                }
            }
        }

        var leftRightSymmetry = DetectMirrorSymmetry(
            pixels,
            features,
            width,
            height,
            leftRight: true);

        var topBottomSymmetry = DetectMirrorSymmetry(
            pixels,
            features,
            width,
            height,
            leftRight: false);

        var analysis = new Analysis
        {
            Width = width,
            Height = height,
            Pixels = pixels,
            RegionIds = regionIds,
            Regions = regions,
            Importance = importance,
            Features = features,
            SameNeighbourCounts = sameNeighbourCounts,
            ColorCounts = colorCounts,
            LeftRightSymmetry = leftRightSymmetry,
            TopBottomSymmetry = topBottomSymmetry,
        };

        if (buildBranches)
            analysis.Branches.AddRange(BuildThinBranchSegments(analysis));

        return analysis;
    }

    /// <summary>
    /// Converts true thin-line pixels into graph segments between topology nodes. Closed contour
    /// loops deliberately have no degree!=2 node and are skipped: V4 is meant to recover branches,
    /// twigs and connectors, not repaint every outline of a filled motif.
    /// </summary>
    /// <summary>
    /// Builds the thin-branch graph in O(N) memory/time without hash-based edge tracking.
    /// Each source pixel stores an 8-bit adjacency mask and an 8-bit visited-edge mask. Detailed
    /// carpet artwork can contain hundreds of thousands of line-like pixels, so avoiding a
    /// HashSet&lt;long&gt; here is critical for interactive resize performance.
    /// </summary>
    private static List<BranchSegment> BuildThinBranchSegments(Analysis analysis)
    {
        var count = analysis.Pixels.Length;
        var branchMask = new bool[count];
        var adjacency = new byte[count];
        var degree = new byte[count];
        var visited = new byte[count];

        // "Thin" alone also catches many ordinary filled-region boundaries. Restrict the branch
        // graph to sparse local neighbourhoods; true 1px stems/twigs normally have 1-4 same-colour
        // neighbours, while broad motif boundaries generally have denser support.
        for (var i = 0; i < count; i++)
        {
            if ((analysis.Features[i] & FeatureFlags.Thin) == 0)
                continue;

            branchMask[i] = analysis.SameNeighbourCounts[i] <= 4;
        }

        // Build every undirected adjacency once using four forward directions and set the reverse
        // bit on the neighbour. The wrap-aware coordinate mapping keeps rapport-border branches.
        ReadOnlySpan<int> forwardDirections = [2, 3, 4, 5];
        for (var i = 0; i < count; i++)
        {
            if (!branchMask[i])
                continue;

            var color = analysis.Pixels[i];
            var x = i % analysis.Width;
            var y = i / analysis.Width;

            foreach (var direction in forwardDirections)
            {
                GetDirectionOffset(direction, out var dx, out var dy);
                var nx = x + dx;
                var ny = y + dy;

                if (RepeatAware)
                {
                    nx = Wrap(nx, analysis.Width);
                    ny = Wrap(ny, analysis.Height);
                }
                else if (nx < 0 || nx >= analysis.Width ||
                         ny < 0 || ny >= analysis.Height)
                {
                    continue;
                }

                var neighbour = ny * analysis.Width + nx;
                if (neighbour == i ||
                    !branchMask[neighbour] ||
                    analysis.Pixels[neighbour] != color)
                {
                    continue;
                }

                adjacency[i] |= (byte)(1 << direction);
                adjacency[neighbour] |= (byte)(1 << OppositeDirection(direction));
            }
        }

        for (var i = 0; i < count; i++)
            degree[i] = (byte)System.Numerics.BitOperations.PopCount((uint)adjacency[i]);

        var segments = new List<BranchSegment>();

        for (var seed = 0; seed < count; seed++)
        {
            if (!branchMask[seed] || degree[seed] == 2)
                continue;

            var seedMask = adjacency[seed];
            for (var direction = 0; direction < 8; direction++)
            {
                var bit = (byte)(1 << direction);
                if ((seedMask & bit) == 0 || (visited[seed] & bit) != 0)
                    continue;

                var next = NeighbourIndex(analysis, seed, direction);
                if (next < 0)
                    continue;

                MarkVisitedEdge(visited, seed, direction, next);

                var path = new List<int>(16) { seed };
                var current = next;
                var incomingDirection = OppositeDirection(direction);
                var guard = 0;

                while (guard++ < count)
                {
                    path.Add(current);

                    if (degree[current] != 2)
                        break;

                    var available = (byte)(adjacency[current] & ~(1 << incomingDirection));
                    if (available == 0)
                        break;

                    var nextDirection = FirstSetDirection(available);
                    var continuation = NeighbourIndex(analysis, current, nextDirection);
                    if (continuation < 0)
                        break;

                    var nextBit = (byte)(1 << nextDirection);
                    if ((visited[current] & nextBit) != 0)
                        break;

                    MarkVisitedEdge(visited, current, nextDirection, continuation);
                    current = continuation;
                    incomingDirection = OppositeDirection(nextDirection);
                }

                // Two-cell node-to-node contacts do not need segment reconstruction and were
                // previously allocated only to be discarded later by RepairBranchSegments.
                if (path.Count < 3)
                    continue;

                var end = path[^1];
                segments.Add(new BranchSegment(
                    analysis.Pixels[seed],
                    path,
                    degree[seed],
                    degree[end]));
            }
        }

        return segments;
    }

    private static int NeighbourIndex(Analysis analysis, int sourceIndex, int direction)
    {
        GetDirectionOffset(direction, out var dx, out var dy);
        var x = sourceIndex % analysis.Width + dx;
        var y = sourceIndex / analysis.Width + dy;

        if (RepeatAware)
        {
            x = Wrap(x, analysis.Width);
            y = Wrap(y, analysis.Height);
        }
        else if (x < 0 || x >= analysis.Width ||
                 y < 0 || y >= analysis.Height)
        {
            return -1;
        }

        return y * analysis.Width + x;
    }

    private static void MarkVisitedEdge(
        byte[] visited,
        int source,
        int direction,
        int destination)
    {
        visited[source] |= (byte)(1 << direction);
        visited[destination] |= (byte)(1 << OppositeDirection(direction));
    }

    private static int FirstSetDirection(byte mask)
    {
        for (var direction = 0; direction < 8; direction++)
            if ((mask & (1 << direction)) != 0)
                return direction;

        return -1;
    }

    // Clockwise from north: N, NE, E, SE, S, SW, W, NW.
    private static void GetDirectionOffset(int direction, out int dx, out int dy)
    {
        switch (direction)
        {
            case 0: dx = 0; dy = -1; break;
            case 1: dx = 1; dy = -1; break;
            case 2: dx = 1; dy = 0; break;
            case 3: dx = 1; dy = 1; break;
            case 4: dx = 0; dy = 1; break;
            case 5: dx = -1; dy = 1; break;
            case 6: dx = -1; dy = 0; break;
            case 7: dx = -1; dy = -1; break;
            default: dx = 0; dy = 0; break;
        }
    }

    private static int OppositeDirection(int direction) => (direction + 4) & 7;

    private static (int[] RegionIds, List<RegionInfo> Regions) BuildRegions(byte[] pixels, int width, int height)
    {
        var regionIds = new int[pixels.Length];
        Array.Fill(regionIds, -1);
        var regions = new List<RegionInfo>();
        var queue = new int[pixels.Length];

        for (var seed = 0; seed < pixels.Length; seed++)
        {
            if (regionIds[seed] >= 0)
                continue;

            var regionId = regions.Count;
            var region = new RegionInfo(pixels[seed]);
            regions.Add(region);

            var head = 0;
            var tail = 0;
            queue[tail++] = seed;
            regionIds[seed] = regionId;

            while (head < tail)
            {
                var current = queue[head++];
                region.Area++;

                var x = current % width;
                var y = current / width;

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        var nx = x + dx;
                        var ny = y + dy;

                        if (RepeatAware)
                        {
                            nx = Wrap(nx, width);
                            ny = Wrap(ny, height);
                        }
                        else if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        {
                            continue;
                        }

                        var next = ny * width + nx;
                        if (regionIds[next] >= 0 || pixels[next] != region.Color)
                            continue;

                        regionIds[next] = regionId;
                        queue[tail++] = next;
                    }
                }
            }
        }

        return (regionIds, regions);
    }

    private static CandidateSet[] BuildInitialResult(Analysis analysis, DesignDocument destination)
    {
        var targetCount = checked(destination.Width * destination.Height);
        var candidateSets = new CandidateSet[targetCount];
        var coverage = new double[PaletteSlots];
        var weighted = new double[PaletteSlots];
        var seen = new List<byte>(16);

        var sourcePerTargetX = analysis.Width / (double)destination.Width;
        var sourcePerTargetY = analysis.Height / (double)destination.Height;

        // RugScale is normally used progressively (for example 200 -> 160 -> 120). A fixed,
        // aggressive detail boost gets applied again at every step and gradually thickens dense
        // carpet ornament. Scale feature pressure with shrink severity instead: moderate steps
        // stay conservative, while a genuinely heavy one-shot shrink gets more protection.
        var footprintScale = Math.Max(1d, sourcePerTargetX * sourcePerTargetY);
        var featurePressure = Math.Clamp(0.42 + (footprintScale - 1d) * 0.36, 0.42, 1.10);

        for (var ty = 0; ty < destination.Height; ty++)
        {
            var sy0 = ty * sourcePerTargetY;
            var sy1 = (ty + 1) * sourcePerTargetY;
            var iy0 = Math.Max(0, (int)Math.Floor(sy0));
            var iy1 = Math.Min(analysis.Height - 1, (int)Math.Ceiling(sy1) - 1);

            for (var tx = 0; tx < destination.Width; tx++)
            {
                var sx0 = tx * sourcePerTargetX;
                var sx1 = (tx + 1) * sourcePerTargetX;
                var ix0 = Math.Max(0, (int)Math.Floor(sx0));
                var ix1 = Math.Min(analysis.Width - 1, (int)Math.Ceiling(sx1) - 1);

                seen.Clear();
                var footprintArea = Math.Max(double.Epsilon, (sx1 - sx0) * (sy1 - sy0));

                for (var sy = iy0; sy <= iy1; sy++)
                {
                    var overlapY = Math.Max(0d, Math.Min(sy + 1d, sy1) - Math.Max(sy, sy0));
                    if (overlapY <= 0)
                        continue;

                    var row = sy * analysis.Width;
                    for (var sx = ix0; sx <= ix1; sx++)
                    {
                        var overlapX = Math.Max(0d, Math.Min(sx + 1d, sx1) - Math.Max(sx, sx0));
                        var area = overlapX * overlapY;
                        if (area <= 0)
                            continue;

                        var sourceIndex = row + sx;
                        var color = analysis.Pixels[sourceIndex];
                        if (coverage[color] == 0)
                            seen.Add(color);

                        coverage[color] += area;
                        weighted[color] += area * analysis.Importance[sourceIndex];
                    }
                }

                var set = new CandidateSet();
                foreach (var color in seen)
                {
                    var coverageRatio = coverage[color] / footprintArea;
                    var importanceRatio = weighted[color] / footprintArea;

                    // Coverage keeps large regions stable; feature-weighted mass allows a real
                    // one-pixel contour or corner to defeat a larger plain background footprint.
                    var score = (float)(coverageRatio * 3.0 + importanceRatio * featurePressure);
                    set.Add(color, score);

                    coverage[color] = 0;
                    weighted[color] = 0;
                }

                if (set.Count == 0)
                {
                    // Defensive fallback for extreme floating-point edge cases.
                    var sx = Math.Min(analysis.Width - 1, (int)((tx + 0.5) * sourcePerTargetX));
                    var sy = Math.Min(analysis.Height - 1, (int)((ty + 0.5) * sourcePerTargetY));
                    set.Add(analysis.Pixels[sy * analysis.Width + sx], 1);
                }

                var targetIndex = ty * destination.Width + tx;
                candidateSets[targetIndex] = set;
                destination.SetPixel(tx, ty, set.TopColor);
            }
        }

        return candidateSets;
    }

    private static void CorrectColorArea(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[]? locked,
        int maxPasses)
    {
        var desired = DesiredTargetColorCounts(analysis.ColorCounts, analysis.Pixels.Length, candidates.Length);
        var current = CountColors(destination);

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var changed = false;

            for (var y = 0; y < destination.Height; y++)
            {
                for (var x = 0; x < destination.Width; x++)
                {
                    var cell = y * destination.Width + x;
                    if (locked is not null && locked[cell])
                        continue;

                    var currentColor = destination.GetPixel(x, y);
                    if (current[currentColor] <= desired[currentColor])
                        continue;

                    // Never satisfy a colour quota by cutting a local bridge. This is the key to
                    // progressive stability: redundant thickness may be removed on each resize,
                    // while a one-pixel connection that holds a motif together survives.
                    if (WouldDisconnectColor(destination, x, y, currentColor))
                        continue;

                    var set = candidates[cell];
                    var topScore = Math.Max(0.0001f, set.TopScore);
                    var bestColor = currentColor;
                    var bestScore = float.MinValue;

                    for (var i = 0; i < set.Count; i++)
                    {
                        var alternate = set.ColorAt(i);
                        if (alternate == currentColor || current[alternate] >= desired[alternate])
                            continue;

                        var score = set.ScoreAt(i);
                        if (score < topScore * 0.28f || score <= bestScore)
                            continue;

                        // Avoid converting area correction into speckle creation. Prefer growing
                        // an already-neighbouring region; only a near-top candidate may seed a new
                        // local island (small motifs are separately protected by PreserveRegions).
                        if (!HasNeighbourColor(destination, x, y, alternate) && score < topScore * 0.82f)
                            continue;

                        bestScore = score;
                        bestColor = alternate;
                    }

                    if (bestColor == currentColor)
                        continue;

                    destination.SetPixel(x, y, bestColor);
                    current[currentColor]--;
                    current[bestColor]++;
                    changed = true;
                }
            }

            if (!changed)
                break;
        }
    }

    private static int[] DesiredTargetColorCounts(int[] sourceCounts, int sourceTotal, int targetTotal)
    {
        var desired = new int[PaletteSlots];
        var fractions = new List<(byte Color, double Fraction)>();
        var assigned = 0;

        for (var i = 0; i < PaletteSlots; i++)
        {
            if (sourceCounts[i] == 0)
                continue;

            var exact = sourceCounts[i] * (double)targetTotal / sourceTotal;
            var floor = (int)Math.Floor(exact);
            desired[i] = floor;
            assigned += floor;
            fractions.Add(((byte)i, exact - floor));
        }

        foreach (var item in fractions.OrderByDescending(f => f.Fraction))
        {
            if (assigned >= targetTotal)
                break;
            desired[item.Color]++;
            assigned++;
        }

        return desired;
    }

    private readonly record struct RepeatProbe(
        int Cross,
        int WindowStart,
        int WindowEnd,
        int WindowId,
        int Period,
        double Score);

    /// <summary>
    /// V6 repeat discovery. Outer borders remain high-confidence anchors, but repeat zones are
    /// also searched inside the design. Inner zones are allowed to cover only part of the long
    /// axis, which is important for carpet borders interrupted by a central field/medallion.
    /// </summary>
    private static List<RepeatBand> DetectAllRepeatBands(Analysis analysis)
    {
        var bands = DetectOuterRepeatBands(analysis);

        DetectInnerRepeatBands(
            analysis,
            horizontal: true,
            bands);

        DetectInnerRepeatBands(
            analysis,
            horizontal: false,
            bands);

        return ConsolidateRepeatBands(bands);
    }

    private static void DetectInnerRepeatBands(
        Analysis analysis,
        bool horizontal,
        List<RepeatBand> output)
    {
        var axisLength = horizontal ? analysis.Width : analysis.Height;
        var crossLength = horizontal ? analysis.Height : analysis.Width;

        if (axisLength < 80 || crossLength < 40)
            return;

        var windows = BuildRepeatSearchWindows(axisLength);
        var crossStep = Math.Clamp(crossLength / 260, 3, 10);
        var probes = new List<RepeatProbe>();

        for (var cross = crossStep;
             cross < crossLength - crossStep;
             cross += crossStep)
        {
            foreach (var (start, end, id) in windows)
            {
                if (!LineHasUsefulVariation(
                        analysis,
                        horizontal,
                        cross,
                        start,
                        end))
                {
                    continue;
                }

                var result = FindFundamentalLinePeriod(
                    analysis,
                    horizontal,
                    cross,
                    start,
                    end);

                if (result.Period <= 0 || result.Score < 0.79)
                    continue;

                probes.Add(new RepeatProbe(
                    cross,
                    start,
                    end,
                    id,
                    result.Period,
                    result.Score));
            }
        }

        if (probes.Count == 0)
            return;

        foreach (var windowGroup in probes.GroupBy(p => p.WindowId))
        {
            var ordered = windowGroup
                .OrderBy(p => p.Cross)
                .ToList();

            var cluster = new List<RepeatProbe>();

            void FlushCluster()
            {
                if (cluster.Count < 2)
                {
                    cluster.Clear();
                    return;
                }

                TryCreateInnerBandFromProbeCluster(
                    analysis,
                    horizontal,
                    crossStep,
                    cluster,
                    output);

                cluster.Clear();
            }

            foreach (var probe in ordered)
            {
                if (cluster.Count == 0)
                {
                    cluster.Add(probe);
                    continue;
                }

                var previous = cluster[^1];
                var periodTolerance = Math.Max(
                    2,
                    (int)Math.Round(
                        Math.Min(previous.Period, probe.Period) * 0.08));

                var contiguous =
                    probe.Cross - previous.Cross <= crossStep * 2;

                var sameFamily =
                    Math.Abs(probe.Period - previous.Period) <= periodTolerance;

                if (!contiguous || !sameFamily)
                    FlushCluster();

                cluster.Add(probe);
            }

            FlushCluster();
        }
    }

    private static List<(int Start, int End, int Id)> BuildRepeatSearchWindows(
        int axisLength)
    {
        var margin = Math.Max(4, axisLength / 40);
        var middle = axisLength / 2;
        var quarter = axisLength / 4;

        var candidates = new[]
        {
            (margin, axisLength - margin),
            (margin, Math.Min(axisLength - margin, middle + axisLength / 20)),
            (Math.Max(margin, middle - axisLength / 20), axisLength - margin),
            (Math.Max(margin, quarter / 2), Math.Min(axisLength - margin, axisLength - quarter / 2)),
        };

        var result = new List<(int Start, int End, int Id)>();
        var id = 0;

        foreach (var candidate in candidates)
        {
            var start = Math.Clamp(candidate.Item1, 0, axisLength - 1);
            var end = Math.Clamp(candidate.Item2, start + 1, axisLength);

            if (end - start < 48)
                continue;

            if (result.Any(w =>
                    Math.Abs(w.Start - start) <= 2 &&
                    Math.Abs(w.End - end) <= 2))
            {
                continue;
            }

            result.Add((start, end, id++));
        }

        return result;
    }

    private static bool LineHasUsefulVariation(
        Analysis analysis,
        bool horizontal,
        int cross,
        int start,
        int end)
    {
        Span<int> counts = stackalloc int[PaletteSlots];
        var length = end - start;
        var step = Math.Max(1, length / 128);
        var total = 0;

        for (var axis = start; axis < end; axis += step)
        {
            var color = GetAbsolutePixel(
                analysis,
                horizontal,
                axis,
                cross);
            counts[color]++;
            total++;
        }

        if (total < 8)
            return false;

        var used = 0;
        var dominant = 0;

        for (var color = 0; color < PaletteSlots; color++)
        {
            if (counts[color] <= 0)
                continue;

            used++;
            dominant = Math.Max(dominant, counts[color]);
        }

        return used >= 2 &&
               dominant / (double)total < 0.93;
    }

    private static (int Period, double Score) FindFundamentalLinePeriod(
        Analysis analysis,
        bool horizontal,
        int cross,
        int start,
        int end)
    {
        var length = end - start;
        var minPeriod = Math.Max(6, length / 120);
        var maxPeriod = Math.Min(length / 4, 384);

        if (maxPeriod < minPeriod)
            return (0, 0);

        // At most ~100 coarse period candidates regardless of raster size, then refine locally.
        var coarseStep = Math.Max(1, (maxPeriod - minPeriod) / 96);
        var bestPeriod = 0;
        var bestScore = 0d;

        for (var period = minPeriod;
             period <= maxPeriod;
             period += coarseStep)
        {
            var score = ScoreLinePeriod(
                analysis,
                horizontal,
                cross,
                start,
                end,
                period);

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestPeriod = period;
        }

        if (bestPeriod <= 0 || bestScore < 0.74)
            return (0, 0);

        var refineStart = Math.Max(minPeriod, bestPeriod - coarseStep);
        var refineEnd = Math.Min(maxPeriod, bestPeriod + coarseStep);

        bestPeriod = 0;
        bestScore = 0;

        for (var period = refineStart; period <= refineEnd; period++)
        {
            var score = ScoreLinePeriod(
                analysis,
                horizontal,
                cross,
                start,
                end,
                period);

            if (score > bestScore)
            {
                bestScore = score;
                bestPeriod = period;
            }
        }

        if (bestPeriod <= 0 || bestScore < 0.76)
            return (0, 0);

        // Prefer a strong fundamental over a harmonic, but do not rescan every smaller
        // integer period. On real 960x2250+ designs that turned inner-zone discovery into tens
        // of millions of redundant correlations per resize. Harmonics can only hide a much
        // smaller set of plausible base periods, so probe those (plus ±1 tolerance) directly.
        var required = Math.Max(0.76, bestScore * 0.95);
        var fundamental = bestPeriod;
        var fundamentalScore = bestScore;

        var harmonicCandidates = new SortedSet<int>();
        for (var divisor = 2; divisor <= 8; divisor++)
        {
            var approximate = bestPeriod / (double)divisor;
            var rounded = (int)Math.Round(approximate);

            for (var delta = -1; delta <= 1; delta++)
            {
                var candidate = rounded + delta;
                if (candidate >= minPeriod && candidate < bestPeriod)
                    harmonicCandidates.Add(candidate);
            }
        }

        foreach (var period in harmonicCandidates)
        {
            var score = ScoreLinePeriod(
                analysis,
                horizontal,
                cross,
                start,
                end,
                period);

            if (score < required)
                continue;

            fundamental = period;
            fundamentalScore = score;
            break;
        }

        return (fundamental, fundamentalScore);
    }

    private static double ScoreLinePeriod(
        Analysis analysis,
        bool horizontal,
        int cross,
        int start,
        int end,
        int period)
    {
        var available = end - start - period;
        if (available <= 0)
            return 0;

        var step = Math.Max(1, available / 128);
        long same = 0;
        long total = 0;

        for (var axis = start;
             axis + period < end;
             axis += step)
        {
            total++;

            if (GetAbsolutePixel(
                    analysis,
                    horizontal,
                    axis,
                    cross) ==
                GetAbsolutePixel(
                    analysis,
                    horizontal,
                    axis + period,
                    cross))
            {
                same++;
            }
        }

        return total == 0 ? 0 : same / (double)total;
    }

    private static void TryCreateInnerBandFromProbeCluster(
        Analysis analysis,
        bool horizontal,
        int crossStep,
        List<RepeatProbe> cluster,
        List<RepeatBand> output)
    {
        var crossLength = horizontal ? analysis.Height : analysis.Width;
        var crossStart = Math.Max(
            0,
            cluster[0].Cross - crossStep / 2);
        var crossEnd = Math.Min(
            crossLength,
            cluster[^1].Cross + crossStep + crossStep / 2);
        var thickness = crossEnd - crossStart;

        if (thickness < Math.Max(6, crossStep * 2))
            return;

        var periods = cluster
            .Select(p => p.Period)
            .OrderBy(p => p)
            .ToArray();

        var seedPeriod = periods[periods.Length / 2];
        var windowStart = cluster[0].WindowStart;
        var windowEnd = cluster[0].WindowEnd;

        var refineRadius = Math.Max(2, seedPeriod / 20);
        var bestPeriod = 0;
        var bestScore = 0d;

        for (var period = Math.Max(6, seedPeriod - refineRadius);
             period <= seedPeriod + refineRadius;
             period++)
        {
            var score = ScoreRepeatZone(
                analysis,
                horizontal,
                crossStart,
                thickness,
                windowStart,
                windowEnd,
                period);

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestPeriod = period;
        }

        if (bestPeriod <= 0 || bestScore < 0.78)
            return;

        var windowLength = windowEnd - windowStart;
        var repeatCount = windowLength / bestPeriod;
        if (repeatCount < 4)
            return;

        var leftover = windowLength - repeatCount * bestPeriod;
        var repeatStart = windowStart + leftover / 2;

        var side = horizontal
            ? RepeatBandSide.Top
            : RepeatBandSide.Left;

        var candidate = new RepeatBand(
            side,
            crossStart,
            thickness,
            bestPeriod,
            repeatStart,
            repeatCount,
            bestScore,
            isOuter: false);

        AddOrReplaceRepeatBand(output, candidate);
    }

    private static double ScoreRepeatZone(
        Analysis analysis,
        bool horizontal,
        int crossStart,
        int thickness,
        int axisStart,
        int axisEnd,
        int period)
    {
        var axisAvailable = axisEnd - axisStart - period;
        if (axisAvailable <= 0 || thickness <= 0)
            return 0;

        var axisStep = Math.Max(1, axisAvailable / 192);
        var crossStep = Math.Max(1, thickness / 20);
        long same = 0;
        long total = 0;

        for (var cross = crossStart;
             cross < crossStart + thickness;
             cross += crossStep)
        {
            for (var axis = axisStart;
                 axis + period < axisEnd;
                 axis += axisStep)
            {
                total++;

                if (GetAbsolutePixel(
                        analysis,
                        horizontal,
                        axis,
                        cross) ==
                    GetAbsolutePixel(
                        analysis,
                        horizontal,
                        axis + period,
                        cross))
                {
                    same++;
                }
            }
        }

        return total == 0 ? 0 : same / (double)total;
    }

    private static byte GetAbsolutePixel(
        Analysis analysis,
        bool horizontal,
        int axis,
        int cross)
    {
        if (horizontal)
        {
            var x = Math.Clamp(axis, 0, analysis.Width - 1);
            var y = Math.Clamp(cross, 0, analysis.Height - 1);
            return analysis.Pixels[y * analysis.Width + x];
        }

        var verticalX = Math.Clamp(cross, 0, analysis.Width - 1);
        var verticalY = Math.Clamp(axis, 0, analysis.Height - 1);
        return analysis.Pixels[verticalY * analysis.Width + verticalX];
    }

    private static void AddOrReplaceRepeatBand(
        List<RepeatBand> bands,
        RepeatBand candidate)
    {
        for (var i = 0; i < bands.Count; i++)
        {
            var existing = bands[i];
            if (existing.IsHorizontal != candidate.IsHorizontal)
                continue;

            if (!RepeatBandsOverlap(existing, candidate))
                continue;

            // Outer borders have a dedicated detector that understands corner blocks and
            // opposite-side harmonization. If an inner probe overlaps that rectangle, period does
            // not matter: allowing a second period family in the same pixels lets the later repeat
            // pass repaint an already-correct outer motif.
            if (existing.IsOuter && !candidate.IsOuter)
                return;

            if (!existing.IsOuter && candidate.IsOuter)
            {
                bands[i] = candidate;
                return;
            }

            var samePeriodFamily =
                Math.Abs(existing.Period - candidate.Period) <=
                Math.Max(
                    2,
                    (int)Math.Round(
                        Math.Min(existing.Period, candidate.Period) * 0.08));

            var existingArea =
                existing.Thickness * existing.RepeatCount * existing.Period;
            var candidateArea =
                candidate.Thickness * candidate.RepeatCount * candidate.Period;

            if (!samePeriodFamily)
            {
                // Two different period hypotheses should not both repaint substantially the same
                // inner rectangle. Replace only when the new evidence is clearly stronger.
                if (candidate.Score > existing.Score + 0.04 &&
                    candidateArea >= existingArea * 0.70)
                {
                    bands[i] = candidate;
                }

                return;
            }

            if (candidate.Score > existing.Score + 0.01 ||
                (Math.Abs(candidate.Score - existing.Score) <= 0.01 &&
                 candidateArea > existingArea))
            {
                bands[i] = candidate;
            }

            return;
        }

        bands.Add(candidate);
    }

    private static bool RepeatBandsOverlap(
        RepeatBand a,
        RepeatBand b)
    {
        var aCrossEnd = a.CrossStart + a.Thickness;
        var bCrossEnd = b.CrossStart + b.Thickness;
        var crossOverlap =
            Math.Min(aCrossEnd, bCrossEnd) -
            Math.Max(a.CrossStart, b.CrossStart);

        if (crossOverlap <= 0)
            return false;

        var aAxisEnd = a.RepeatStart + a.RepeatCount * a.Period;
        var bAxisEnd = b.RepeatStart + b.RepeatCount * b.Period;
        var axisOverlap =
            Math.Min(aAxisEnd, bAxisEnd) -
            Math.Max(a.RepeatStart, b.RepeatStart);

        if (axisOverlap <= 0)
            return false;

        var minCross = Math.Min(a.Thickness, b.Thickness);
        var minAxis = Math.Min(
            a.RepeatCount * a.Period,
            b.RepeatCount * b.Period);

        return crossOverlap >= minCross * 0.45 &&
               axisOverlap >= minAxis * 0.45;
    }

    private static List<RepeatBand> ConsolidateRepeatBands(
        List<RepeatBand> bands)
    {
        var ordered = bands
            .OrderByDescending(b => b.Score)
            .ThenByDescending(b => b.IsOuter)
            .ThenByDescending(b => b.Thickness)
            .ToList();

        var result = new List<RepeatBand>();

        foreach (var band in ordered)
            AddOrReplaceRepeatBand(result, band);

        return result;
    }

    /// <summary>
    /// Detects strong periodic strips at the four outer edges. The detector deliberately prefers
    /// the smallest high-scoring period with at least four complete repeats, so a harmonic such
    /// as 160/240 px cannot hide the actual 80 px motif repeat.
    /// </summary>
    private static List<RepeatBand> DetectOuterRepeatBands(Analysis analysis)
    {
        var bands = new List<RepeatBand>(4);

        TryDetectOuterRepeatBand(analysis, RepeatBandSide.Top, bands);
        TryDetectOuterRepeatBand(analysis, RepeatBandSide.Bottom, bands);
        TryDetectOuterRepeatBand(analysis, RepeatBandSide.Left, bands);
        TryDetectOuterRepeatBand(analysis, RepeatBandSide.Right, bands);

        HarmonizeOppositeRepeatBands(analysis, bands);
        return bands;
    }

    private static void TryDetectOuterRepeatBand(
        Analysis analysis,
        RepeatBandSide side,
        List<RepeatBand> output)
    {
        var horizontal = side is RepeatBandSide.Top or RepeatBandSide.Bottom;
        var axisLength = horizontal ? analysis.Width : analysis.Height;
        var crossLength = horizontal ? analysis.Height : analysis.Width;

        // Below 64 px on the cross axis the detector's minimum 12-24 px strip occupies
        // too much of the whole design and ordinary sparse motifs can masquerade as an outer
        // border. Small rasters are handled by motif/topology logic instead.
        if (axisLength < 64 || crossLength < 64)
            return;

        var maxThickness = Math.Clamp(
            Math.Min(crossLength / 6, axisLength / 5),
            24,
            192);
        var minThickness = Math.Max(12, maxThickness / 5);
        var thicknessStep = Math.Max(4, (maxThickness - minThickness) / 10);

        RepeatBand? bestBand = null;
        var bestUtility = double.MinValue;

        for (var thickness = minThickness;
             thickness <= maxThickness;
             thickness += thicknessStep)
        {
            if (!BandHasColorVariation(analysis, side, thickness))
                continue;

            var minPeriod = Math.Max(8, thickness * 3 / 4);
            var maxPeriod = Math.Min(axisLength / 4, thickness * 4);
            if (maxPeriod < minPeriod)
                continue;

            var bestPeriodScore = 0d;
            var periodScores = new List<(int Period, double Score)>();

            for (var period = minPeriod; period <= maxPeriod; period++)
            {
                var cornerExtent = Math.Max(thickness, period);
                var interiorLength = axisLength - 2 * cornerExtent;
                if (interiorLength < period * 4)
                    continue;

                var score = ScoreRepeatPeriod(
                    analysis,
                    side,
                    thickness,
                    period,
                    cornerExtent);

                periodScores.Add((period, score));
                if (score > bestPeriodScore)
                    bestPeriodScore = score;
            }

            if (periodScores.Count == 0 || bestPeriodScore < 0.72)
                continue;

            // Fundamental before harmonic: the smallest period within 94% of the best correlation.
            var requiredScore = Math.Max(0.72, bestPeriodScore * 0.94);
            var fundamentalPeriod = 0;
            var fundamentalScore = 0d;

            foreach (var candidate in periodScores)
            {
                if (candidate.Score < requiredScore)
                    continue;

                fundamentalPeriod = candidate.Period;
                fundamentalScore = candidate.Score;
                break;
            }

            if (fundamentalPeriod <= 0)
                continue;

            // Repeat extent is defined by motif phase, not by the sampled strip
            // thickness. Tying the corner extent to thickness made the same symmetric rug resolve
            // a different repeat count on opposite sides (e.g. 10 top vs 9 bottom).
            var corner = fundamentalPeriod;
            var sourceInterior = axisLength - 2 * corner;
            var repeatCount = sourceInterior / fundamentalPeriod;
            if (repeatCount < 4)
                continue;

            // Keep any remainder symmetrically between the corner blocks and the repeated strip.
            var leftover = sourceInterior - repeatCount * fundamentalPeriod;
            var repeatStart = corner + leftover / 2;

            // Prefer a sufficiently thick decorative strip over a tiny micro-border that happens
            // to correlate perfectly.
            var thicknessWeight =
                0.45 + 0.55 * Math.Min(1d, thickness / (double)maxThickness);
            var utility = fundamentalScore * thicknessWeight;

            if (utility <= bestUtility)
                continue;

            bestUtility = utility;
            var crossStart = side switch
            {
                RepeatBandSide.Bottom => analysis.Height - thickness,
                RepeatBandSide.Right => analysis.Width - thickness,
                _ => 0,
            };

            bestBand = new RepeatBand(
                side,
                crossStart,
                thickness,
                fundamentalPeriod,
                repeatStart,
                repeatCount,
                fundamentalScore,
                isOuter: true);
        }

        if (bestBand is not null)
            output.Add(bestBand);
    }

    private static void HarmonizeOppositeRepeatBands(
        Analysis analysis,
        List<RepeatBand> bands)
    {
        HarmonizeRepeatPair(
            analysis,
            bands,
            RepeatBandSide.Top,
            RepeatBandSide.Bottom,
            forceSamePeriod: IsVerticallySymmetric(analysis));

        HarmonizeRepeatPair(
            analysis,
            bands,
            RepeatBandSide.Left,
            RepeatBandSide.Right,
            forceSamePeriod: IsHorizontallySymmetric(analysis));
    }

    private static void HarmonizeRepeatPair(
        Analysis analysis,
        List<RepeatBand> bands,
        RepeatBandSide firstSide,
        RepeatBandSide secondSide,
        bool forceSamePeriod)
    {
        var firstIndex = bands.FindIndex(
            b => b.IsOuter && b.Side == firstSide);
        var secondIndex = bands.FindIndex(
            b => b.IsOuter && b.Side == secondSide);
        if (firstIndex < 0 || secondIndex < 0)
            return;

        var first = bands[firstIndex];
        var second = bands[secondIndex];
        var tolerance = Math.Max(
            2,
            (int)Math.Round(Math.Min(first.Period, second.Period) * 0.06));

        if (!forceSamePeriod &&
            Math.Abs(first.Period - second.Period) > tolerance)
        {
            return;
        }

        // Fundamental-period detector already prefers the smallest strong period. For a symmetric
        // pair, use the higher-confidence side's period; for near-equal non-symmetric detections,
        // use the smaller one to avoid accidentally locking onto a harmonic.
        var sharedPeriod = forceSamePeriod
            ? (first.Score >= second.Score ? first.Period : second.Period)
            : Math.Min(first.Period, second.Period);

        var rebuiltFirst = RebuildRepeatBandForPeriod(analysis, first, sharedPeriod);
        var rebuiltSecond = RebuildRepeatBandForPeriod(analysis, second, sharedPeriod);

        if (rebuiltFirst is null || rebuiltSecond is null)
            return;

        bands[firstIndex] = rebuiltFirst;
        bands[secondIndex] = rebuiltSecond;
    }

    private static RepeatBand? RebuildRepeatBandForPeriod(
        Analysis analysis,
        RepeatBand band,
        int period)
    {
        var horizontal = band.IsHorizontal;
        var axisLength = horizontal ? analysis.Width : analysis.Height;
        if (period <= 0 || axisLength < period * 6)
            return null;

        var score = ScoreRepeatPeriod(
            analysis,
            band.Side,
            band.Thickness,
            period,
            Math.Max(band.Thickness, period));

        // A mirrored side can score a few percent lower because of sampling phase, but do not
        // force a genuinely weak/non-repeat strip merely to make the pair look symmetric.
        if (score < 0.68)
            return null;

        var sourceInterior = axisLength - 2 * period;
        var repeatCount = sourceInterior / period;
        if (repeatCount < 4)
            return null;

        var leftover = sourceInterior - repeatCount * period;
        var repeatStart = period + leftover / 2;

        return new RepeatBand(
            band.Side,
            band.CrossStart,
            band.Thickness,
            period,
            repeatStart,
            repeatCount,
            score,
            band.IsOuter);
    }

    private static bool BandHasColorVariation(
        Analysis analysis,
        RepeatBandSide side,
        int thickness)
    {
        var horizontal = side is RepeatBandSide.Top or RepeatBandSide.Bottom;
        var axisLength = horizontal ? analysis.Width : analysis.Height;
        var axisStep = Math.Max(1, axisLength / 96);
        var crossStep = Math.Max(1, thickness / 16);

        var first = GetOuterBandPixel(analysis, side, 0, 0);
        for (var cross = 0; cross < thickness; cross += crossStep)
        {
            for (var axis = 0; axis < axisLength; axis += axisStep)
            {
                if (GetOuterBandPixel(analysis, side, axis, cross) != first)
                    return true;
            }
        }

        return false;
    }

    private static double ScoreRepeatPeriod(
        Analysis analysis,
        RepeatBandSide side,
        int thickness,
        int period,
        int cornerExtent)
    {
        var horizontal = side is RepeatBandSide.Top or RepeatBandSide.Bottom;
        var axisLength = horizontal ? analysis.Width : analysis.Height;

        var start = cornerExtent;
        var endExclusive = axisLength - cornerExtent - period;
        if (endExclusive <= start)
            return 0;

        // V5 outer strips have explicit corner exclusion and opposite-side harmonization. Keep
        // their proven raw correlation semantics; chance-match correction is only needed by the
        // free-scanning V6 inner-band detector.
        var axisStep = Math.Max(1, axisLength / 384);
        var crossStep = Math.Max(1, thickness / 32);

        long same = 0;
        long total = 0;

        for (var cross = 0; cross < thickness; cross += crossStep)
        {
            for (var axis = start; axis < endExclusive; axis += axisStep)
            {
                total++;
                if (GetOuterBandPixel(analysis, side, axis, cross) ==
                    GetOuterBandPixel(analysis, side, axis + period, cross))
                {
                    same++;
                }
            }
        }

        return total == 0
            ? 0
            : same / (double)total;
    }

    /// <summary>
    /// Reconstructs each detected strip from one clean canonical source tile and redistributes the
    /// original repeat count across the resized interior span. This prevents each occurrence from
    /// being independently distorted by resampling. Corner blocks and branch/keypoint repairs are
    /// left untouched.
    /// </summary>
    private static void StabilizeRepeatBandsProgressive(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        List<RepeatBand> bands)
    {
        foreach (var band in bands
                     .OrderByDescending(b => b.Score)
                     .ThenByDescending(b => b.IsOuter))
        {
            StabilizeRepeatBandProgressive(
                analysis,
                destination,
                candidates,
                locked,
                band);
        }
    }

    private static void StabilizeRepeatBandProgressive(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        RepeatBand band)
    {
        var sourceAxisLength = band.IsHorizontal ? analysis.Width : analysis.Height;
        var sourceCrossLength = band.IsHorizontal ? analysis.Height : analysis.Width;
        var targetAxisLength = band.IsHorizontal ? destination.Width : destination.Height;
        var targetCrossLength = band.IsHorizontal ? destination.Height : destination.Width;

        var sourceRepeatEnd = band.RepeatStart + band.RepeatCount * band.Period;
        if (sourceRepeatEnd > sourceAxisLength)
            return;

        var axisScale = targetAxisLength / (double)sourceAxisLength;
        var crossScale = targetCrossLength / (double)sourceCrossLength;

        var targetStart = Math.Clamp(
            (int)Math.Round(band.RepeatStart * axisScale),
            0,
            targetAxisLength - 1);
        var targetEnd = Math.Clamp(
            targetAxisLength -
            (int)Math.Round((sourceAxisLength - sourceRepeatEnd) * axisScale),
            targetStart + 1,
            targetAxisLength);

        var targetSpan = targetEnd - targetStart;
        var targetCrossStart = Math.Clamp(
            (int)Math.Round(band.CrossStart * crossScale),
            0,
            targetCrossLength - 1);
        var targetCrossEnd = Math.Clamp(
            (int)Math.Round((band.CrossStart + band.Thickness) * crossScale),
            targetCrossStart + 1,
            targetCrossLength);
        var targetThickness = targetCrossEnd - targetCrossStart;

        if (targetSpan < band.RepeatCount * 2 || targetThickness <= 0)
            return;

        // Build the canonical motif from all occurrences, not one arbitrary middle tile.
        // A single damaged/hand-adjusted occurrence must not be copied across the whole border.
        var canonicalTile = BuildCanonicalRepeatTile(analysis, band);

        var requiredRatio = band.Score >= 0.88 ? 0.18f : 0.26f;

        for (var targetCross = 0; targetCross < targetThickness; targetCross++)
        {
            var sourceCross = Math.Clamp(
                (int)Math.Floor(
                    (targetCross + 0.5) * band.Thickness / targetThickness),
                0,
                band.Thickness - 1);

            for (var targetAxis = targetStart; targetAxis < targetEnd; targetAxis++)
            {
                var position = targetAxis - targetStart;

                // Distribute the exact source repeat count across the full target span. Individual
                // tiles may differ by one target pixel, but phase never accumulates drift.
                var repeatPosition =
                    (position + 0.5) * band.RepeatCount / targetSpan;
                var phase = repeatPosition - Math.Floor(repeatPosition);
                var sourcePhase = Math.Clamp(
                    (int)Math.Floor(phase * band.Period),
                    0,
                    band.Period - 1);

                var canonicalIndex =
                    sourceCross * band.Period + sourcePhase;
                var desired = canonicalTile.Colors[canonicalIndex];
                var consensus = canonicalTile.Confidence[canonicalIndex];

                GetTargetBandCoordinates(
                    band,
                    targetCrossStart,
                    targetAxis,
                    targetCross,
                    out var x,
                    out var y);

                var cell = y * destination.Width + x;

                // In a verified repeat band, agreement across the source occurrences is stronger
                // evidence than a local branch/keypoint decision. This is essential for carpet
                // rapports: one damaged occurrence must not remain different merely because its
                // pixels were locked by the earlier topology pass.
                var repeatAuthority =
                    band.Score >= 0.80 &&
                    consensus >= 0.72f;

                if (locked[cell] && !repeatAuthority)
                    continue;

                if (destination.GetPixel(x, y) == desired)
                {
                    if (repeatAuthority)
                        locked[cell] = true;
                    continue;
                }

                var set = candidates[cell];
                var hasLocalSupport = set.TryGetScore(desired, out var score);
                var strongRepeat =
                    band.Score >= 0.86 ||
                    repeatAuthority;

                // Phase locking deliberately moves a motif by a fraction of a target pixel in
                // places. On a very strong repeat band the canonical colour may therefore fall
                // just outside this cell's original footprint shortlist. That is allowed because
                // the colour comes from the same verified source repeat tile; weaker detections
                // still require ordinary local candidate support.
                if (!hasLocalSupport && !strongRepeat)
                    continue;

                if (hasLocalSupport && score < set.TopScore * requiredRatio)
                    continue;

                var supportRatio = hasLocalSupport
                    ? score / Math.Max(0.0001f, set.TopScore)
                    : 0.34f;

                var current = destination.GetPixel(x, y);

                // Repeat is strong evidence, but do not casually sever an unrelated one-pixel
                // bridge unless the repeat correlation itself is exceptionally strong.
                if (WouldDisconnectColor(destination, x, y, current) &&
                    supportRatio < 0.48f &&
                    band.Score < 0.90 &&
                    !repeatAuthority)
                {
                    continue;
                }

                destination.SetPixel(x, y, desired);
                locked[cell] = true;
            }
        }
    }

    private static byte[] CapturePixels(
        DesignDocument document)
    {
        var pixels = new byte[
            checked(document.Width * document.Height)];

        for (var y = 0; y < document.Height; y++)
        {
            var row = y * document.Width;
            for (var x = 0; x < document.Width; x++)
                pixels[row + x] = document.GetPixel(x, y);
        }

        return pixels;
    }

    private static bool[] BuildOuterRepeatChangeMask(
        Analysis analysis,
        DesignDocument destination,
        List<RepeatBand> bands,
        byte[] beforeRepeat)
    {
        var protectedCells = new bool[
            checked(destination.Width * destination.Height)];

        foreach (var band in bands)
        {
            if (!band.IsOuter)
                continue;

            var sourceAxisLength =
                band.IsHorizontal ? analysis.Width : analysis.Height;
            var sourceCrossLength =
                band.IsHorizontal ? analysis.Height : analysis.Width;
            var targetAxisLength =
                band.IsHorizontal ? destination.Width : destination.Height;
            var targetCrossLength =
                band.IsHorizontal ? destination.Height : destination.Width;

            var sourceRepeatEnd =
                band.RepeatStart +
                band.RepeatCount * band.Period;
            if (sourceRepeatEnd > sourceAxisLength)
                continue;

            var axisScale =
                targetAxisLength / (double)sourceAxisLength;
            var crossScale =
                targetCrossLength / (double)sourceCrossLength;

            var targetStart = Math.Clamp(
                (int)Math.Round(band.RepeatStart * axisScale),
                0,
                targetAxisLength - 1);
            var targetEnd = Math.Clamp(
                targetAxisLength -
                (int)Math.Round(
                    (sourceAxisLength - sourceRepeatEnd) * axisScale),
                targetStart + 1,
                targetAxisLength);

            var targetCrossStart = Math.Clamp(
                (int)Math.Round(band.CrossStart * crossScale),
                0,
                targetCrossLength - 1);
            var targetCrossEnd = Math.Clamp(
                (int)Math.Round(
                    (band.CrossStart + band.Thickness) * crossScale),
                targetCrossStart + 1,
                targetCrossLength);

            for (var targetCross = targetCrossStart;
                 targetCross < targetCrossEnd;
                 targetCross++)
            {
                for (var targetAxis = targetStart;
                     targetAxis < targetEnd;
                     targetAxis++)
                {
                    int x;
                    int y;

                    if (band.IsHorizontal)
                    {
                        x = targetAxis;
                        y = targetCross;
                    }
                    else
                    {
                        x = targetCross;
                        y = targetAxis;
                    }

                    var cell = y * destination.Width + x;
                    if (beforeRepeat[cell] !=
                        destination.GetPixel(x, y))
                    {
                        protectedCells[cell] = true;
                    }
                }
            }
        }

        return protectedCells;
    }

    private static (byte[] Colors, float[] Confidence) BuildCanonicalRepeatTile(
        Analysis analysis,
        RepeatBand band)
    {
        var length = checked(band.Thickness * band.Period);
        var colors = new byte[length];
        var confidence = new float[length];
        Span<int> counts = stackalloc int[PaletteSlots];

        for (var cross = 0; cross < band.Thickness; cross++)
        {
            for (var phase = 0; phase < band.Period; phase++)
            {
                counts.Clear();

                var middleRepeat = band.RepeatCount / 2;
                var preferredAxis =
                    band.RepeatStart + middleRepeat * band.Period + phase;
                var preferred = GetBandPixel(
                    analysis,
                    band,
                    preferredAxis,
                    cross);

                for (var repeat = 0; repeat < band.RepeatCount; repeat++)
                {
                    var axis =
                        band.RepeatStart + repeat * band.Period + phase;
                    var color = GetBandPixel(
                        analysis,
                        band,
                        axis,
                        cross);
                    counts[color]++;
                }

                var bestColor = preferred;
                var bestCount = counts[preferred];

                for (var color = 0; color < PaletteSlots; color++)
                {
                    if (counts[color] <= bestCount)
                        continue;

                    bestCount = counts[color];
                    bestColor = (byte)color;
                }

                var index = cross * band.Period + phase;
                colors[index] = bestColor;
                confidence[index] =
                    bestCount / (float)Math.Max(1, band.RepeatCount);
            }
        }

        return (colors, confidence);
    }

    private static byte GetBandPixel(
        Analysis analysis,
        RepeatBand band,
        int axis,
        int cross)
    {
        if (band.IsHorizontal)
        {
            var y = Math.Clamp(
                band.CrossStart + cross,
                0,
                analysis.Height - 1);
            var x = Math.Clamp(axis, 0, analysis.Width - 1);
            return analysis.Pixels[y * analysis.Width + x];
        }

        var verticalX = Math.Clamp(
            band.CrossStart + cross,
            0,
            analysis.Width - 1);
        var verticalY = Math.Clamp(axis, 0, analysis.Height - 1);
        return analysis.Pixels[verticalY * analysis.Width + verticalX];
    }

    /// <summary>
    /// Outer-band-only accessor used while detecting a band before a RepeatBand instance exists.
    /// </summary>
    private static byte GetOuterBandPixel(
        Analysis analysis,
        RepeatBandSide side,
        int axis,
        int cross)
    {
        return side switch
        {
            RepeatBandSide.Top =>
                analysis.Pixels[cross * analysis.Width + axis],
            RepeatBandSide.Bottom =>
                analysis.Pixels[(analysis.Height - 1 - cross) * analysis.Width + axis],
            RepeatBandSide.Left =>
                analysis.Pixels[axis * analysis.Width + cross],
            RepeatBandSide.Right =>
                analysis.Pixels[axis * analysis.Width + (analysis.Width - 1 - cross)],
            _ => 0,
        };
    }

    private static void GetTargetBandCoordinates(
        RepeatBand band,
        int targetCrossStart,
        int axis,
        int cross,
        out int x,
        out int y)
    {
        if (band.IsHorizontal)
        {
            x = axis;
            y = targetCrossStart + cross;
            return;
        }

        x = targetCrossStart + cross;
        y = axis;
    }

    /// <summary>
    /// Exact source symmetry is a hard structural constraint, not a visual preference. If a rug
    /// is pixel-identical across one or both mirror axes, progressive RugScale resolves any local
    /// repair back into symmetric destination orbits using only colours supported by every
    /// mirrored source footprint.
    /// </summary>
    private static void PruneUnsupportedTopologyMerges(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        List<RepeatBand> repeatBands)
    {
        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
            {
                var current = destination.GetPixel(x, y);
                var baseline = SourceCentreColor(
                    analysis,
                    destination.Width,
                    destination.Height,
                    x,
                    y);

                if (current == baseline)
                    continue;

                // Verified rapport reconstruction owns its zone. Its canonical tile comes from
                // repeated source evidence and may legitimately create/close local junctions.
                if (IsInsideStrongRepeatZone(
                        analysis,
                        destination,
                        repeatBands,
                        x,
                        y))
                {
                    continue;
                }

                var groups = CountNeighbourGroupsForColor(
                    destination,
                    x,
                    y,
                    current);

                // Two groups are a normal line continuation. Three or more means this pixel is
                // acting as a T/X junction that can merge independent motifs.
                if (groups < 3)
                    continue;

                if (SourceFootprintHasFeature(
                        analysis,
                        destination.Width,
                        destination.Height,
                        x,
                        y,
                        current,
                        FeatureFlags.Junction,
                        radius: 1))
                {
                    continue;
                }

                var cell = y * destination.Width + x;
                var set = candidates[cell];

                if (!set.TryGetScore(baseline, out var baselineScore))
                    continue;

                if (baselineScore < set.TopScore * 0.34f)
                    continue;

                // Reverting to the centre-sampled source colour must not itself create a new
                // unsupported multi-way junction.
                var baselineGroups = CountNeighbourGroupsForColor(
                    destination,
                    x,
                    y,
                    baseline);

                if (baselineGroups >= 3 &&
                    !SourceFootprintHasFeature(
                        analysis,
                        destination.Width,
                        destination.Height,
                        x,
                        y,
                        baseline,
                        FeatureFlags.Junction,
                        radius: 1))
                {
                    continue;
                }

                destination.SetPixel(x, y, baseline);
                locked[cell] = false;
            }
        }
    }

    private static byte SourceCentreColor(
        Analysis analysis,
        int targetWidth,
        int targetHeight,
        int targetX,
        int targetY)
    {
        var sourceX = Math.Clamp(
            (int)Math.Floor(
                (targetX + 0.5) * analysis.Width / targetWidth),
            0,
            analysis.Width - 1);
        var sourceY = Math.Clamp(
            (int)Math.Floor(
                (targetY + 0.5) * analysis.Height / targetHeight),
            0,
            analysis.Height - 1);

        return analysis.Pixels[sourceY * analysis.Width + sourceX];
    }

    private static int CountNeighbourGroupsForColor(
        DesignDocument document,
        int x,
        int y,
        byte color)
    {
        Span<bool> ring =
        [
            document.GetPixel(Wrap(x, document.Width), Wrap(y - 1, document.Height)) == color,
            document.GetPixel(Wrap(x + 1, document.Width), Wrap(y - 1, document.Height)) == color,
            document.GetPixel(Wrap(x + 1, document.Width), Wrap(y, document.Height)) == color,
            document.GetPixel(Wrap(x + 1, document.Width), Wrap(y + 1, document.Height)) == color,
            document.GetPixel(Wrap(x, document.Width), Wrap(y + 1, document.Height)) == color,
            document.GetPixel(Wrap(x - 1, document.Width), Wrap(y + 1, document.Height)) == color,
            document.GetPixel(Wrap(x - 1, document.Width), Wrap(y, document.Height)) == color,
            document.GetPixel(Wrap(x - 1, document.Width), Wrap(y - 1, document.Height)) == color,
        ];

        var groups = 0;
        for (var i = 0; i < ring.Length; i++)
        {
            if (ring[i] &&
                !ring[(i + ring.Length - 1) % ring.Length])
            {
                groups++;
            }
        }

        return groups;
    }

    private static bool SourceFootprintHasFeature(
        Analysis analysis,
        int targetWidth,
        int targetHeight,
        int targetX,
        int targetY,
        byte color,
        FeatureFlags required,
        int radius)
    {
        var sx0 = targetX * analysis.Width / targetWidth;
        var sx1 = Math.Max(
            sx0 + 1,
            (targetX + 1) * analysis.Width / targetWidth);
        var sy0 = targetY * analysis.Height / targetHeight;
        var sy1 = Math.Max(
            sy0 + 1,
            (targetY + 1) * analysis.Height / targetHeight);

        sx0 = Math.Max(0, sx0 - radius);
        sy0 = Math.Max(0, sy0 - radius);
        sx1 = Math.Min(analysis.Width, sx1 + radius);
        sy1 = Math.Min(analysis.Height, sy1 + radius);

        for (var sy = sy0; sy < sy1; sy++)
        {
            var row = sy * analysis.Width;
            for (var sx = sx0; sx < sx1; sx++)
            {
                var index = row + sx;
                if (analysis.Pixels[index] == color &&
                    (analysis.Features[index] & required) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsInsideStrongRepeatZone(
        Analysis analysis,
        DesignDocument destination,
        List<RepeatBand> bands,
        int x,
        int y)
    {
        foreach (var band in bands)
        {
            if (band.Score < 0.80)
                continue;

            var sourceAxisLength =
                band.IsHorizontal ? analysis.Width : analysis.Height;
            var sourceCrossLength =
                band.IsHorizontal ? analysis.Height : analysis.Width;
            var targetAxisLength =
                band.IsHorizontal ? destination.Width : destination.Height;
            var targetCrossLength =
                band.IsHorizontal ? destination.Height : destination.Width;

            var axisScale =
                targetAxisLength / (double)sourceAxisLength;
            var crossScale =
                targetCrossLength / (double)sourceCrossLength;

            var sourceRepeatEnd =
                band.RepeatStart + band.RepeatCount * band.Period;

            var targetAxisStart = Math.Clamp(
                (int)Math.Round(band.RepeatStart * axisScale),
                0,
                targetAxisLength - 1);
            var targetAxisEnd = Math.Clamp(
                targetAxisLength -
                (int)Math.Round(
                    (sourceAxisLength - sourceRepeatEnd) * axisScale),
                targetAxisStart + 1,
                targetAxisLength);

            var targetCrossStart = Math.Clamp(
                (int)Math.Round(band.CrossStart * crossScale),
                0,
                targetCrossLength - 1);
            var targetCrossEnd = Math.Clamp(
                (int)Math.Round(
                    (band.CrossStart + band.Thickness) * crossScale),
                targetCrossStart + 1,
                targetCrossLength);

            var axis = band.IsHorizontal ? x : y;
            var cross = band.IsHorizontal ? y : x;

            if (axis >= targetAxisStart &&
                axis < targetAxisEnd &&
                cross >= targetCrossStart &&
                cross < targetCrossEnd)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnforceSourceSymmetry(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked)
    {
        var hardLeftRight =
            analysis.LeftRightSymmetry.Enabled;
        var hardTopBottom =
            analysis.TopBottomSymmetry.Enabled;

        // A carpet can be strongly designer-symmetric without being pixel-identical: hand-edited
        // ornaments, technical rows and grid phase can leave a few percent of intentional
        // mismatch. Do not promote that to the old whole-document hard mirror. Instead, reconcile
        // only destination pairs for which BOTH source footprints independently support the same
        // palette index. This preserves the symmetric majority while leaving genuinely asymmetric
        // ornament alone.
        //
        // Candidate-backed (soft) axes run FIRST. Any exact hard axis then runs last and remains
        // inviolable. This matters for B163A: left/right is exact while top/bottom is ~97.7%.
        // Running the soft top/bottom pass after the exact left/right pass broke the exact axis.
        if (!hardLeftRight &&
            IsCandidateBackedSymmetry(
                analysis.LeftRightSymmetry))
        {
            EnforceCandidateBackedAxisSymmetry(
                analysis,
                destination,
                candidates,
                locked,
                analysis.LeftRightSymmetry,
                leftRight: true);
        }

        if (!hardTopBottom &&
            IsCandidateBackedSymmetry(
                analysis.TopBottomSymmetry))
        {
            EnforceCandidateBackedAxisSymmetry(
                analysis,
                destination,
                candidates,
                locked,
                analysis.TopBottomSymmetry,
                leftRight: false);
        }

        if (hardLeftRight ||
            hardTopBottom)
        {
            EnforceHardSourceSymmetry(
                analysis,
                destination,
                candidates,
                locked,
                hardLeftRight,
                hardTopBottom);
        }
    }

    private static bool IsCandidateBackedSymmetry(
        SymmetryConstraint constraint) =>
        constraint.PixelScore >= 0.955 &&
        constraint.StructuralScore >= 0.90 &&
        constraint.StructuralCoverage >= 0.18;

    private static void EnforceHardSourceSymmetry(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        bool leftRight,
        bool topBottom)
    {
        // Carpet designers normally re-centre a symmetric motif when changing density/size.
        // Source mirror shifts are therefore evidence that symmetry exists, not an offset that
        // should be copied blindly to the target. The only exception is a technical sentinel
        // edge (e.g. a one-column indexed bitmap seam), which stays outside the design content.
        var lrConstant = leftRight
            ? TargetMirrorConstant(
                analysis,
                destination.Width,
                leftRight: true)
            : -1;

        var tbConstant = topBottom
            ? TargetMirrorConstant(
                analysis,
                destination.Height,
                leftRight: false)
            : -1;

        var visited = new bool[candidates.Length];
        Span<int> orbit = stackalloc int[4];

        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
            {
                var first = y * destination.Width + x;
                if (visited[first])
                    continue;

                var count = 0;
                AddUniqueOrbitCell(orbit, ref count, first);

                var mirrorX = leftRight
                    ? lrConstant - x
                    : x;
                var mirrorY = topBottom
                    ? tbConstant - y
                    : y;

                if (leftRight &&
                    mirrorX >= 0 &&
                    mirrorX < destination.Width)
                {
                    AddUniqueOrbitCell(
                        orbit,
                        ref count,
                        y * destination.Width + mirrorX);
                }

                if (topBottom &&
                    mirrorY >= 0 &&
                    mirrorY < destination.Height)
                {
                    AddUniqueOrbitCell(
                        orbit,
                        ref count,
                        mirrorY * destination.Width + x);
                }

                if (leftRight &&
                    topBottom &&
                    mirrorX >= 0 &&
                    mirrorX < destination.Width &&
                    mirrorY >= 0 &&
                    mirrorY < destination.Height)
                {
                    AddUniqueOrbitCell(
                        orbit,
                        ref count,
                        mirrorY * destination.Width + mirrorX);
                }

                for (var i = 0; i < count; i++)
                    visited[orbit[i]] = true;

                if (count <= 1)
                    continue;

                var chosen = ChooseSymmetricOrbitColor(
                    destination,
                    candidates,
                    locked,
                    orbit[..count]);

                for (var i = 0; i < count; i++)
                {
                    var cell = orbit[i];
                    var px = cell % destination.Width;
                    var py = cell / destination.Width;

                    if (destination.GetPixel(px, py) == chosen)
                        continue;

                    destination.SetPixel(px, py, chosen);
                    locked[cell] = true;
                }
            }
        }
    }

    private static void EnforceCandidateBackedAxisSymmetry(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        SymmetryConstraint constraint,
        bool leftRight)
    {
        var sourceLength =
            leftRight
                ? analysis.Width
                : analysis.Height;
        var targetLength =
            leftRight
                ? destination.Width
                : destination.Height;

        // Hard exact symmetry is re-centred by TargetMirrorConstant. Soft symmetry is different:
        // the measured source phase is part of the evidence and must survive the density change.
        // B163A is the reference case: top/bottom best agreement is at source shift +1; forcing
        // target shift 0 actually reduced symmetry. Scale the measured phase into target pixels.
        var scaledShift =
            (int)Math.Round(
                constraint.SourceShift *
                targetLength /
                (double)Math.Max(1, sourceLength));
        var mirrorConstant =
            targetLength - 1 +
            scaledShift;

        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
            {
                var mirrorX = leftRight
                    ? mirrorConstant - x
                    : x;
                var mirrorY = leftRight
                    ? y
                    : mirrorConstant - y;

                if (mirrorX < 0 ||
                    mirrorX >= destination.Width ||
                    mirrorY < 0 ||
                    mirrorY >= destination.Height)
                {
                    continue;
                }

                var first =
                    y * destination.Width + x;
                var second =
                    mirrorY * destination.Width +
                    mirrorX;

                if (second <= first)
                    continue;

                // Strong-but-not-hard symmetry preserves only source pairs that are themselves
                // equal at the measured source phase. This is the key guard against erasing the
                // intentional ~2-3% asymmetry that may coexist with a 97% symmetric carpet.
                if (!SourcePairAgreesForSoftSymmetry(
                        analysis,
                        destination,
                        x,
                        y,
                        constraint,
                        leftRight))
                {
                    continue;
                }

                var firstColor =
                    destination.GetPixel(x, y);
                var secondColor =
                    destination.GetPixel(
                        mirrorX,
                        mirrorY);

                if (firstColor == secondColor)
                    continue;

                if (locked[first] &&
                    locked[second] &&
                    firstColor != secondColor)
                {
                    continue;
                }

                if (!TryChooseCandidateBackedPairColor(
                        candidates[first],
                        candidates[second],
                        firstColor,
                        secondColor,
                        locked[first],
                        locked[second],
                        out var chosen))
                {
                    continue;
                }

                // A locked structural/repeat decision may act as the authority for its pair, but a
                // soft symmetry pass never overwrites that locked cell with a different colour.
                if ((locked[first] &&
                     firstColor != chosen) ||
                    (locked[second] &&
                     secondColor != chosen))
                {
                    continue;
                }

                if (firstColor != chosen)
                {
                    destination.SetPixel(
                        x,
                        y,
                        chosen);
                }

                if (secondColor != chosen)
                {
                    destination.SetPixel(
                        mirrorX,
                        mirrorY,
                        chosen);
                }
            }
        }
    }

    private static bool SourcePairAgreesForSoftSymmetry(
        Analysis analysis,
        DesignDocument destination,
        int targetX,
        int targetY,
        SymmetryConstraint constraint,
        bool leftRight)
    {
        var sourceAxisLength =
            leftRight
                ? analysis.Width
                : analysis.Height;
        var targetAxisLength =
            leftRight
                ? destination.Width
                : destination.Height;
        var targetAxis =
            leftRight
                ? targetX
                : targetY;

        var sourceAxis =
            Math.Clamp(
                (int)Math.Floor(
                    (targetAxis + 0.5) *
                    sourceAxisLength /
                    targetAxisLength),
                0,
                sourceAxisLength - 1);
        var sourceMirrorAxis =
            sourceAxisLength - 1 +
            constraint.SourceShift -
            sourceAxis;

        if (sourceMirrorAxis < 0 ||
            sourceMirrorAxis >= sourceAxisLength)
        {
            return false;
        }

        var cross =
            leftRight
                ? Math.Clamp(
                    (int)Math.Floor(
                        (targetY + 0.5) *
                        analysis.Height /
                        destination.Height),
                    0,
                    analysis.Height - 1)
                : Math.Clamp(
                    (int)Math.Floor(
                        (targetX + 0.5) *
                        analysis.Width /
                        destination.Width),
                    0,
                    analysis.Width - 1);

        var firstSource =
            leftRight
                ? cross * analysis.Width +
                  sourceAxis
                : sourceAxis * analysis.Width +
                  cross;
        var secondSource =
            leftRight
                ? cross * analysis.Width +
                  sourceMirrorAxis
                : sourceMirrorAxis * analysis.Width +
                  cross;

        return analysis.Pixels[firstSource] ==
               analysis.Pixels[secondSource];
    }

    private static bool TryChooseCandidateBackedPairColor(
        CandidateSet first,
        CandidateSet second,
        byte firstCurrent,
        byte secondCurrent,
        bool firstLocked,
        bool secondLocked,
        out byte chosen)
    {
        chosen = firstCurrent;

        if (first.Count == 0 ||
            second.Count == 0 ||
            first.TopScore <= 0 ||
            second.TopScore <= 0)
        {
            return false;
        }

        // If one side is structurally locked, only its current colour may be propagated. This
        // allows a proven endpoint/repeat pixel to restore its mirrored partner without mutating
        // the proven side itself.
        if (firstLocked ^ secondLocked)
        {
            var authority =
                firstLocked
                    ? firstCurrent
                    : secondCurrent;
            var authoritySet =
                firstLocked
                    ? first
                    : second;
            var partnerSet =
                firstLocked
                    ? second
                    : first;

            if (!authoritySet.TryGetScore(
                    authority,
                    out var authorityScore) ||
                !partnerSet.TryGetScore(
                    authority,
                    out var partnerScore))
            {
                return false;
            }

            var authorityRatio =
                authorityScore /
                Math.Max(
                    0.0001f,
                    authoritySet.TopScore);
            var partnerRatio =
                partnerScore /
                Math.Max(
                    0.0001f,
                    partnerSet.TopScore);

            if (authorityRatio < 0.65f ||
                partnerRatio < 0.42f)
            {
                return false;
            }

            chosen = authority;
            return true;
        }

        var bestScore =
            double.NegativeInfinity;
        var found = false;

        for (var i = 0;
             i < first.Count;
             i++)
        {
            var color =
                first.ColorAt(i);

            if (!second.TryGetScore(
                    color,
                    out var secondScore))
            {
                continue;
            }

            var firstRatio =
                first.ScoreAt(i) /
                Math.Max(
                    0.0001f,
                    first.TopScore);
            var secondRatio =
                secondScore /
                Math.Max(
                    0.0001f,
                    second.TopScore);
            var minimumRatio =
                Math.Min(
                    firstRatio,
                    secondRatio);
            var averageRatio =
                (firstRatio +
                 secondRatio) * 0.5f;

            // Soft symmetry needs independent evidence on both sides. A low-ranked colour that
            // merely exists in both four-entry shortlists is not enough to erase an intentional
            // asymmetric detail.
            if (minimumRatio < 0.45f ||
                averageRatio < 0.60f)
            {
                continue;
            }

            var score =
                minimumRatio * 2.0 +
                averageRatio;

            if (color == firstCurrent)
                score += 0.10;
            if (color == secondCurrent)
                score += 0.10;

            if (score <= bestScore)
                continue;

            bestScore = score;
            chosen = color;
            found = true;
        }

        return found;
    }

    private static void AddUniqueOrbitCell(
        Span<int> orbit,
        ref int count,
        int cell)
    {
        for (var i = 0; i < count; i++)
        {
            if (orbit[i] == cell)
                return;
        }

        orbit[count++] = cell;
    }

    private static byte ChooseSymmetricOrbitColor(
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        ReadOnlySpan<int> orbit)
    {
        Span<bool> considered = stackalloc bool[PaletteSlots];
        Span<byte> colors = stackalloc byte[PaletteSlots];
        var colorCount = 0;

        for (var i = 0; i < orbit.Length; i++)
        {
            var cell = orbit[i];
            var current = destination.GetPixel(
                cell % destination.Width,
                cell / destination.Width);

            if (!considered[current])
            {
                considered[current] = true;
                colors[colorCount++] = current;
            }

            var set = candidates[cell];
            for (var candidateIndex = 0;
                 candidateIndex < set.Count;
                 candidateIndex++)
            {
                var color = set.ColorAt(candidateIndex);
                if (considered[color])
                    continue;

                considered[color] = true;
                colors[colorCount++] = color;
            }
        }

        var bestColor = destination.GetPixel(
            orbit[0] % destination.Width,
            orbit[0] / destination.Width);
        var bestScore = double.NegativeInfinity;

        for (var colorIndex = 0;
             colorIndex < colorCount;
             colorIndex++)
        {
            var color = colors[colorIndex];
            var score = 0d;
            var supportedCells = 0;
            var currentVotes = 0;
            var lockedVotes = 0;

            for (var i = 0; i < orbit.Length; i++)
            {
                var cell = orbit[i];
                var current = destination.GetPixel(
                    cell % destination.Width,
                    cell / destination.Width);
                var set = candidates[cell];

                if (current == color)
                {
                    currentVotes++;
                    score += 0.90;
                    if (locked[cell])
                    {
                        lockedVotes++;
                        score += 1.35;
                    }
                }

                if (!set.TryGetScore(color, out var candidateScore))
                    continue;

                supportedCells++;
                score += 2.75 *
                         candidateScore /
                         Math.Max(0.0001f, set.TopScore);
            }

            // A colour independently supported by every mirrored source footprint is strong
            // evidence that it is the designer-intended symmetric pixel.
            if (supportedCells == orbit.Length)
                score += 2.25;
            else
                score += supportedCells * 0.20;

            score += currentVotes * 0.15 +
                     lockedVotes * 0.20;

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestColor = color;
        }

        return bestColor;
    }

    private static SymmetryConstraint DetectMirrorSymmetry(
        byte[] pixels,
        FeatureFlags[] features,
        int width,
        int height,
        bool leftRight)
    {
        const int MaxShift = 3;
        // Hard symmetry is intentionally strict. Carpet designs often contain large plain fields,
        // so a merely high pixel score can be misleading. B054A/B163A reveal the real rule:
        // grid-phase exact/near-exact designer symmetry should be inviolable; ~97% similarity may
        // still contain intentional asymmetric ornament and must not be mirrored wholesale.
        const double MinPixelScore = 0.992;
        const double MinStructuralScore = 0.985;

        var bestShift = 0;
        var bestPixel = 0d;
        var bestStructural = 0d;
        var bestStructuralCoverage = 0d;
        var bestQuality = 0d;

        var axisLength = leftRight ? width : height;
        var crossLength = leftRight ? height : width;
        var axisStep = Math.Max(1, axisLength / 384);
        var crossStep = Math.Max(1, crossLength / 384);

        for (var shift = -MaxShift; shift <= MaxShift; shift++)
        {
            var mirrorConstant = axisLength - 1 + shift;
            long pixelMatches = 0;
            long pixelTotal = 0;
            long structuralMatches = 0;
            long structuralTotal = 0;

            for (var cross = 0;
                 cross < crossLength;
                 cross += crossStep)
            {
                for (var axis = 0;
                     axis < axisLength;
                     axis += axisStep)
                {
                    var mirrorAxis = mirrorConstant - axis;
                    if (mirrorAxis < 0 ||
                        mirrorAxis >= axisLength)
                    {
                        continue;
                    }

                    var first = leftRight
                        ? cross * width + axis
                        : axis * width + cross;
                    var second = leftRight
                        ? cross * width + mirrorAxis
                        : mirrorAxis * width + cross;

                    var equal = pixels[first] == pixels[second];
                    pixelTotal++;
                    if (equal)
                        pixelMatches++;

                    var structural =
                        ((features[first] | features[second]) &
                         (FeatureFlags.Edge |
                          FeatureFlags.Thin |
                          FeatureFlags.Corner |
                          FeatureFlags.Endpoint |
                          FeatureFlags.Junction)) != 0;

                    if (!structural)
                        continue;

                    structuralTotal++;
                    if (equal)
                        structuralMatches++;
                }
            }

            if (pixelTotal == 0)
                continue;

            var pixelScore =
                pixelMatches / (double)pixelTotal;
            var structuralScore =
                structuralTotal == 0
                    ? pixelScore
                    : structuralMatches / (double)structuralTotal;
            var structuralCoverage =
                structuralTotal / (double)Math.Max(1, pixelTotal);

            // Structural agreement dominates: a large plain field must not make an asymmetric
            // carpet look symmetric merely because most pixels are background. Coverage is also
            // part of the quality signal so an isolated symmetric twig/T-shape cannot trigger a
            // whole-document hard mirror constraint.
            var quality =
                Math.Min(pixelScore, structuralScore) +
                0.02 * Math.Max(pixelScore, structuralScore) +
                0.01 * Math.Min(1d, structuralCoverage * 2d);

            if (quality <= bestQuality)
                continue;

            bestQuality = quality;
            bestShift = shift;
            bestPixel = pixelScore;
            bestStructural = structuralScore;
            bestStructuralCoverage = structuralCoverage;
        }

        const double MinStructuralCoverage = 0.20;

        var enabled =
            bestPixel >= MinPixelScore &&
            bestStructural >= MinStructuralScore &&
            bestStructuralCoverage >= MinStructuralCoverage;

        return new SymmetryConstraint(
            enabled,
            bestShift,
            bestPixel,
            bestStructural,
            bestStructuralCoverage);
    }

    private static int TargetMirrorConstant(
        Analysis analysis,
        int targetLength,
        bool leftRight)
    {
        var defaultConstant = targetLength - 1;

        // Indexed rug BMPs often contain a technical one-pixel seam/sentinel column on one edge.
        // Detect it by uniformity plus global rarity and exclude it from the mirrored content.
        if (HasRareUniformSentinelEdge(
                analysis,
                leftRight,
                highEdge: true))
        {
            return targetLength - 2;
        }

        if (HasRareUniformSentinelEdge(
                analysis,
                leftRight,
                highEdge: false))
        {
            return targetLength;
        }

        return defaultConstant;
    }

    private static bool HasRareUniformSentinelEdge(
        Analysis analysis,
        bool leftRight,
        bool highEdge)
    {
        var axisLength = leftRight
            ? analysis.Width
            : analysis.Height;
        var crossLength = leftRight
            ? analysis.Height
            : analysis.Width;

        var axis = highEdge
            ? axisLength - 1
            : 0;

        var firstIndex = leftRight
            ? axis
            : axis * analysis.Width;
        var color = analysis.Pixels[firstIndex];

        for (var cross = 0;
             cross < crossLength;
             cross++)
        {
            var index = leftRight
                ? cross * analysis.Width + axis
                : axis * analysis.Width + cross;

            if (analysis.Pixels[index] != color)
                return false;
        }

        // One or two full technical scan-lines of a palette index are rare compared with a real
        // decorative background/border colour.
        return analysis.ColorCounts[color] <= crossLength * 2;
    }

    private static bool IsHorizontallySymmetric(Analysis analysis) =>
        analysis.LeftRightSymmetry.Enabled;

    private static bool IsVerticallySymmetric(Analysis analysis) =>
        analysis.TopBottomSymmetry.Enabled;

    /// <summary>
    /// Progressive resize does not repaint every source thin-line sample. It only restores true
    /// keypoints that centre-nearest lost locally, and only when the source footprint strongly
    /// supports that colour and replacing the baseline pixel will not cut another motif.
    /// </summary>
    private static void PreserveCriticalFeaturesProgressive(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked)
    {
        for (var sy = 0; sy < analysis.Height; sy++)
        {
            for (var sx = 0; sx < analysis.Width; sx++)
            {
                var sourceIndex = sy * analysis.Width + sx;
                var flags = analysis.Features[sourceIndex];
                if ((flags & (FeatureFlags.Corner | FeatureFlags.Endpoint | FeatureFlags.Junction)) == 0)
                    continue;

                var color = analysis.Pixels[sourceIndex];
                var tx = MapCoordinate(sx, analysis.Width, destination.Width);
                var ty = MapCoordinate(sy, analysis.Height, destination.Height);
                var cell = ty * destination.Width + tx;

                if (destination.GetPixel(tx, ty) == color)
                    continue;

                var exactKeypoint =
                    (flags & (FeatureFlags.Endpoint | FeatureFlags.Junction)) != 0;

                // A corner can shift by one cell under moderate resampling; an endpoint or
                // junction cannot. Treating "same colour nearby" as good enough shortened real
                // branches and produced the user's visibly unfinished twigs.
                if (!exactKeypoint && HasColorNearby(destination, cell, color, radius: 1))
                    continue;

                var set = candidates[cell];
                var requiredRatio =
                    (flags & FeatureFlags.Junction) != 0 ? 0.54f :
                    (flags & FeatureFlags.Endpoint) != 0 ? 0.58f :
                    0.68f;

                if (!set.TryGetScore(color, out var score) ||
                    score < set.TopScore * requiredRatio)
                {
                    continue;
                }

                var current = destination.GetPixel(tx, ty);
                if (WouldDisconnectColor(destination, tx, ty, current))
                    continue;

                destination.SetPixel(tx, ty, color);
                locked[cell] = true;
            }
        }
    }

    /// <summary>
    /// V4 progressive repair. A whole source branch segment is projected as a one-cell centreline.
    /// Only cells whose source footprint supports the branch colour are restored. This recovers
    /// clipped twigs/branches while avoiding the old behaviour that repainted every thin contour.
    /// </summary>
    private static void RepairBranchSegmentsProgressive(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked)
    {
        if (analysis.Branches.Count == 0)
            return;

        var projected = new List<int>(64);

        foreach (var segment in analysis.Branches)
        {
            if (segment.SourcePixels.Count < 3)
                continue;

            projected.Clear();

            foreach (var sourceIndex in segment.SourcePixels)
            {
                var sx = sourceIndex % analysis.Width;
                var sy = sourceIndex / analysis.Width;
                var tx = MapCoordinate(sx, analysis.Width, destination.Width);
                var ty = MapCoordinate(sy, analysis.Height, destination.Height);
                var cell = ty * destination.Width + tx;

                if (projected.Count == 0 || projected[^1] != cell)
                    projected.Add(cell);
            }

            if (projected.Count < 2)
                continue;

            var surviving = 0;
            foreach (var cell in projected)
            {
                if (destination.GetPixel(
                        cell % destination.Width,
                        cell / destination.Width) == segment.Color)
                    surviving++;
            }

            // Do not resurrect an entirely absent source detail in progressive mode. Keypoint
            // restore runs first, so a meaningful endpoint/junction normally provides an anchor.
            if (surviving == 0)
                continue;

            for (var i = 0; i < projected.Count; i++)
            {
                var cell = projected[i];
                var tx = cell % destination.Width;
                var ty = cell / destination.Width;

                if (destination.GetPixel(tx, ty) == segment.Color)
                    continue;

                if (locked[cell])
                    continue;

                var set = candidates[cell];
                if (!set.TryGetScore(segment.Color, out var score))
                    continue;

                var endpointCell = i == 0 || i == projected.Count - 1;
                var requiredRatio = endpointCell &&
                                    (segment.StartsAtEndpoint || segment.EndsAtEndpoint)
                    ? 0.38f
                    : segment.TouchesJunction
                        ? 0.44f
                        : 0.50f;

                if (score < set.TopScore * requiredRatio)
                    continue;

                var current = destination.GetPixel(tx, ty);
                if (WouldDisconnectColor(destination, tx, ty, current))
                    continue;

                destination.SetPixel(tx, ty, segment.Color);
                locked[cell] = true;
            }

            // A branch can still contain a one-cell hole when the competing colour was itself a
            // protected bridge. Fill only holes bounded by the same segment colour and supported
            // by this exact source footprint.
            for (var i = 1; i < projected.Count - 1; i++)
            {
                var previousCell = projected[i - 1];
                var cell = projected[i];
                var nextCell = projected[i + 1];

                var previousColor = destination.GetPixel(
                    previousCell % destination.Width,
                    previousCell / destination.Width);
                var nextColor = destination.GetPixel(
                    nextCell % destination.Width,
                    nextCell / destination.Width);

                if (previousColor != segment.Color ||
                    nextColor != segment.Color)
                {
                    continue;
                }

                var x = cell % destination.Width;
                var y = cell / destination.Width;
                if (destination.GetPixel(x, y) == segment.Color || locked[cell])
                    continue;

                var set = candidates[cell];
                if (!set.TryGetScore(segment.Color, out var score) ||
                    score < set.TopScore * 0.34f)
                {
                    continue;
                }

                // This is an exact one-cell hole inside the same projected source branch:
                // both neighbouring segment cells already survived and the source footprint
                // explicitly supports the branch colour. In this narrow case branch continuity
                // outranks preserving the competing colour's local bridge.
                destination.SetPixel(x, y, segment.Color);
                locked[cell] = true;
            }
        }
    }

    /// <summary>
    /// Restores missing pieces of a thin source branch without repainting the complete skeleton.
    /// A repair may only propagate from a target cell where the branch colour already survives;
    /// repeated passes extend that surviving branch through strongly-supported projected cells.
    /// This prevents the aggressive thickening of the first RugScale prototype while recovering
    /// branches that centre-nearest sampling clipped short.
    /// </summary>
    private static void RepairProjectedThinConnectivityProgressive(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked)
    {
        ReadOnlySpan<(int Dx, int Dy)> directions =
        [
            (1, 0),
            (0, 1),
            (1, 1),
            (-1, 1),
        ];

        const int MaxPropagationPasses = 5;

        for (var pass = 0; pass < MaxPropagationPasses; pass++)
        {
            var changed = false;

            for (var sy = 0; sy < analysis.Height; sy++)
            {
                for (var sx = 0; sx < analysis.Width; sx++)
                {
                    var aIndex = sy * analysis.Width + sx;
                    if ((analysis.Features[aIndex] & FeatureFlags.Thin) == 0)
                        continue;

                    var color = analysis.Pixels[aIndex];

                    foreach (var (dx, dy) in directions)
                    {
                        var nx = sx + dx;
                        var ny = sy + dy;

                        if (RepeatAware)
                        {
                            nx = Wrap(nx, analysis.Width);
                            ny = Wrap(ny, analysis.Height);
                        }
                        else if (nx < 0 || nx >= analysis.Width ||
                                 ny < 0 || ny >= analysis.Height)
                        {
                            continue;
                        }

                        var bIndex = ny * analysis.Width + nx;
                        if (analysis.Pixels[bIndex] != color ||
                            (analysis.Features[bIndex] & FeatureFlags.Thin) == 0)
                        {
                            continue;
                        }

                        var ax = MapCoordinate(sx, analysis.Width, destination.Width);
                        var ay = MapCoordinate(sy, analysis.Height, destination.Height);
                        var bx = MapCoordinate(nx, analysis.Width, destination.Width);
                        var by = MapCoordinate(ny, analysis.Height, destination.Height);

                        if (ax == bx && ay == by)
                            continue;

                        var aCell = ay * destination.Width + ax;
                        var bCell = by * destination.Width + bx;
                        var aHas = destination.GetPixel(ax, ay) == color;
                        var bHas = destination.GetPixel(bx, by) == color;

                        // Do not resurrect a completely absent fine detail. Only continue a real
                        // branch from an anchor that survived centre-nearest/keypoint repair.
                        if (aHas == bHas)
                            continue;

                        var targetCell = aHas ? bCell : aCell;
                        var tx = targetCell % destination.Width;
                        var ty = targetCell / destination.Width;
                        var current = destination.GetPixel(tx, ty);

                        if (locked[targetCell] && current != color)
                            continue;

                        if (WouldDisconnectColor(destination, tx, ty, current))
                            continue;

                        var set = candidates[targetCell];
                        if (!set.TryGetScore(color, out var score) ||
                            score < set.TopScore * 0.62f)
                        {
                            continue;
                        }

                        destination.SetPixel(tx, ty, color);
                        locked[targetCell] = true;
                        changed = true;
                    }
                }
            }

            if (!changed)
                break;
        }
    }

    private static void RepairOnePixelGapsProgressive(
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked)
    {
        var changes = new List<(int Cell, byte Color)>();

        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
            {
                var cell = y * destination.Width + x;
                if (locked[cell])
                    continue;

                var current = destination.GetPixel(x, y);
                if (WouldDisconnectColor(destination, x, y, current))
                    continue;

                foreach (var pair in GapNeighbourPairs)
                {
                    var a = destination.GetPixel(
                        Wrap(x + pair.Ax, destination.Width),
                        Wrap(y + pair.Ay, destination.Height));
                    var b = destination.GetPixel(
                        Wrap(x + pair.Bx, destination.Width),
                        Wrap(y + pair.By, destination.Height));
                    if (a != b || a == current)
                        continue;

                    var set = candidates[cell];
                    if (!set.TryGetScore(a, out var score) ||
                        score < set.TopScore * 0.82f)
                    {
                        continue;
                    }

                    changes.Add((cell, a));
                    break;
                }
            }
        }

        foreach (var (cell, color) in changes)
        {
            destination.SetPixel(cell % destination.Width, cell / destination.Width, color);
            locked[cell] = true;
        }
    }

    private static void PreserveCriticalFeatures(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        bool lockThinLines)
    {
        var claims = new float[candidates.Length];
        var claimColors = new byte[candidates.Length];
        var claimLocks = new bool[candidates.Length];

        for (var sy = 0; sy < analysis.Height; sy++)
        {
            for (var sx = 0; sx < analysis.Width; sx++)
            {
                var sourceIndex = sy * analysis.Width + sx;
                var flags = analysis.Features[sourceIndex];
                if ((flags & (FeatureFlags.Thin | FeatureFlags.Corner | FeatureFlags.Endpoint | FeatureFlags.Junction)) == 0)
                    continue;

                var tx = MapCoordinate(sx, analysis.Width, destination.Width);
                var ty = MapCoordinate(sy, analysis.Height, destination.Height);
                var cell = ty * destination.Width + tx;
                var color = analysis.Pixels[sourceIndex];
                var set = candidates[cell];

                if (!set.TryGetScore(color, out var candidateScore) ||
                    candidateScore < set.TopScore * 0.32f)
                {
                    continue;
                }

                var severity = FeatureSeverity(flags) * analysis.Importance[sourceIndex];
                if (severity <= claims[cell])
                    continue;

                claims[cell] = severity;
                claimColors[cell] = color;
                // Corners/endpoints/junctions are true keypoints. Ordinary thin-line samples may
                // still be thinned later by area correction, but the topology guard prevents a
                // structural bridge from being removed.
                claimLocks[cell] = lockThinLines ||
                    (flags & (FeatureFlags.Corner | FeatureFlags.Endpoint | FeatureFlags.Junction)) != 0;
            }
        }

        for (var cell = 0; cell < claims.Length; cell++)
        {
            if (claims[cell] <= 0)
                continue;

            var x = cell % destination.Width;
            var y = cell / destination.Width;
            destination.SetPixel(x, y, claimColors[cell]);
            if (claimLocks[cell])
                locked[cell] = true;
        }
    }

    private static void PreserveRegions(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked)
    {
        var regionCount = analysis.Regions.Count;
        var bestCell = new int[regionCount];
        var bestScore = new float[regionCount];
        Array.Fill(bestCell, -1);

        for (var sy = 0; sy < analysis.Height; sy++)
        {
            for (var sx = 0; sx < analysis.Width; sx++)
            {
                var sourceIndex = sy * analysis.Width + sx;
                var regionId = analysis.RegionIds[sourceIndex];
                var color = analysis.Pixels[sourceIndex];
                var tx = MapCoordinate(sx, analysis.Width, destination.Width);
                var ty = MapCoordinate(sy, analysis.Height, destination.Height);
                var cell = ty * destination.Width + tx;

                if (!candidates[cell].TryGetScore(color, out _))
                    continue;

                var score = analysis.Importance[sourceIndex];
                if (score <= bestScore[regionId])
                    continue;

                bestScore[regionId] = score;
                bestCell[regionId] = cell;
            }
        }

        var areaScale = candidates.Length / (double)analysis.Pixels.Length;

        // Smaller/critical regions go first so a large background component cannot claim the only
        // available cell that represents a tiny flower centre or one-pixel ornament.
        foreach (var regionId in Enumerable.Range(0, regionCount)
                     .OrderByDescending(id => analysis.Regions[id].CriticalCount > 0)
                     .ThenBy(id => analysis.Regions[id].Area))
        {
            var region = analysis.Regions[regionId];
            var expectedArea = region.Area * areaScale;
            if (expectedArea < 0.35 && region.CriticalCount == 0)
                continue;

            var representative = bestCell[regionId];
            if (representative < 0)
                continue;

            if (HasColorNearby(destination, representative, region.Color, radius: 1))
                continue;

            var replacement = FindRepresentableCellNearby(destination, candidates, locked, representative, region.Color, radius: 2);
            if (replacement < 0)
                continue;

            destination.SetPixel(replacement % destination.Width, replacement / destination.Width, region.Color);
            locked[replacement] = true;
        }
    }

    private static void RepairProjectedThinConnectivity(
        Analysis analysis,
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        bool lockPaintedPixels)
    {
        // Only forward neighbours are needed; every source adjacency is visited once. The wrap
        // cases on the last row/column intentionally connect to the first row/column.
        ReadOnlySpan<(int Dx, int Dy)> directions =
        [
            (1, 0),
            (0, 1),
            (1, 1),
            (-1, 1),
        ];

        for (var sy = 0; sy < analysis.Height; sy++)
        {
            for (var sx = 0; sx < analysis.Width; sx++)
            {
                var aIndex = sy * analysis.Width + sx;
                if ((analysis.Features[aIndex] & FeatureFlags.Thin) == 0)
                    continue;

                var color = analysis.Pixels[aIndex];

                foreach (var (dx, dy) in directions)
                {
                    var nx = sx + dx;
                    var ny = sy + dy;

                    if (RepeatAware)
                    {
                        nx = Wrap(nx, analysis.Width);
                        ny = Wrap(ny, analysis.Height);
                    }
                    else if (nx < 0 || nx >= analysis.Width || ny < 0 || ny >= analysis.Height)
                    {
                        continue;
                    }

                    var bIndex = ny * analysis.Width + nx;
                    if (analysis.Pixels[bIndex] != color ||
                        (analysis.Features[bIndex] & FeatureFlags.Thin) == 0)
                    {
                        continue;
                    }

                    var ax = MapCoordinate(sx, analysis.Width, destination.Width);
                    var ay = MapCoordinate(sy, analysis.Height, destination.Height);
                    var bx = MapCoordinate(nx, analysis.Width, destination.Width);
                    var by = MapCoordinate(ny, analysis.Height, destination.Height);

                    ConnectCells(destination, candidates, locked, ax, ay, bx, by, color, lockPaintedPixels);
                }
            }
        }
    }

    private static void ConnectCells(
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        int ax,
        int ay,
        int bx,
        int by,
        byte color,
        bool lockPaintedPixels)
    {
        var dx = ShortestWrappedDelta(ax, bx, destination.Width);
        var dy = ShortestWrappedDelta(ay, by, destination.Height);
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        // Adjacent source pixels should collapse into the same or neighbouring destination cells
        // during downscale. A longer path means this is a mixed/enlarging axis; do not draw a
        // speculative long bridge through unrelated motifs.
        if (steps > 3)
            return;

        if (steps == 0)
        {
            TryPaintSupported(destination, candidates, locked, ay * destination.Width + ax, color, lockPaintedPixels);
            return;
        }

        for (var step = 0; step <= steps; step++)
        {
            var x = Wrap((int)Math.Round(ax + dx * (step / (double)steps)), destination.Width);
            var y = Wrap((int)Math.Round(ay + dy * (step / (double)steps)), destination.Height);
            TryPaintSupported(destination, candidates, locked, y * destination.Width + x, color, lockPaintedPixels);
        }
    }

    private static void RepairOnePixelGaps(
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        bool lockPaintedPixels)
    {
        var changes = new List<(int Cell, byte Color)>();

        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
            {
                var cell = y * destination.Width + x;
                if (locked[cell])
                    continue;

                var current = destination.GetPixel(x, y);
                foreach (var pair in GapNeighbourPairs)
                {
                    var a = destination.GetPixel(
                        Wrap(x + pair.Ax, destination.Width),
                        Wrap(y + pair.Ay, destination.Height));
                    var b = destination.GetPixel(
                        Wrap(x + pair.Bx, destination.Width),
                        Wrap(y + pair.By, destination.Height));
                    if (a != b || a == current)
                        continue;

                    var set = candidates[cell];
                    if (!set.TryGetScore(a, out var score) || score < set.TopScore * 0.38f)
                        continue;

                    changes.Add((cell, a));
                    break;
                }
            }
        }

        foreach (var (cell, color) in changes)
        {
            destination.SetPixel(cell % destination.Width, cell / destination.Width, color);
            if (lockPaintedPixels)
                locked[cell] = true;
        }
    }

    private static bool TryPaintSupported(
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        int cell,
        byte color,
        bool lockPixel)
    {
        if (locked[cell] && destination.GetPixel(cell % destination.Width, cell / destination.Width) != color)
            return false;

        var set = candidates[cell];
        if (!set.TryGetScore(color, out var score) || score < set.TopScore * 0.30f)
            return false;

        destination.SetPixel(cell % destination.Width, cell / destination.Width, color);
        if (lockPixel)
            locked[cell] = true;
        return true;
    }

    private static int FindRepresentableCellNearby(
        DesignDocument destination,
        CandidateSet[] candidates,
        bool[] locked,
        int centerCell,
        byte color,
        int radius)
    {
        var cx = centerCell % destination.Width;
        var cy = centerCell / destination.Width;
        var best = -1;
        var bestPenalty = float.MaxValue;

        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = Wrap(cx + dx, destination.Width);
                var y = Wrap(cy + dy, destination.Height);
                var cell = y * destination.Width + x;
                if (locked[cell])
                    continue;

                var set = candidates[cell];
                if (!set.TryGetScore(color, out var score))
                    continue;

                var penalty = set.TopScore - score + (Math.Abs(dx) + Math.Abs(dy)) * 0.15f;
                if (penalty >= bestPenalty)
                    continue;

                bestPenalty = penalty;
                best = cell;
            }
        }

        return best;
    }

    private static bool HasColorNearby(DesignDocument destination, int centerCell, byte color, int radius)
    {
        var cx = centerCell % destination.Width;
        var cy = centerCell / destination.Width;

        for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
                if (destination.GetPixel(Wrap(cx + dx, destination.Width), Wrap(cy + dy, destination.Height)) == color)
                    return true;

        return false;
    }

    /// <summary>
    /// True when removing the centre pixel would split its same-colour 8-neighbourhood into two
    /// or more arcs. This cheap local simple-point test is enough to stop area correction from
    /// severing a one-pixel contour, without freezing every edge pixel in a dense carpet design.
    /// </summary>
    private static bool WouldDisconnectColor(DesignDocument document, int x, int y, byte color)
    {
        Span<bool> ring =
        [
            document.GetPixel(Wrap(x, document.Width), Wrap(y - 1, document.Height)) == color,
            document.GetPixel(Wrap(x + 1, document.Width), Wrap(y - 1, document.Height)) == color,
            document.GetPixel(Wrap(x + 1, document.Width), Wrap(y, document.Height)) == color,
            document.GetPixel(Wrap(x + 1, document.Width), Wrap(y + 1, document.Height)) == color,
            document.GetPixel(Wrap(x, document.Width), Wrap(y + 1, document.Height)) == color,
            document.GetPixel(Wrap(x - 1, document.Width), Wrap(y + 1, document.Height)) == color,
            document.GetPixel(Wrap(x - 1, document.Width), Wrap(y, document.Height)) == color,
            document.GetPixel(Wrap(x - 1, document.Width), Wrap(y - 1, document.Height)) == color,
        ];

        var neighbours = 0;
        var groups = 0;
        for (var i = 0; i < ring.Length; i++)
        {
            if (!ring[i])
                continue;
            neighbours++;
            if (!ring[(i + ring.Length - 1) % ring.Length])
                groups++;
        }

        return neighbours >= 2 && groups >= 2;
    }

    private static bool HasNeighbourColor(DesignDocument document, int x, int y, byte color)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                if (document.GetPixel(
                        Wrap(x + dx, document.Width),
                        Wrap(y + dy, document.Height)) == color)
                    return true;
            }
        }

        return false;
    }

    private static int[] CountColors(DesignDocument document)
    {
        var counts = new int[PaletteSlots];
        for (var y = 0; y < document.Height; y++)
            for (var x = 0; x < document.Width; x++)
                counts[document.GetPixel(x, y)]++;
        return counts;
    }

    private static float FeatureSeverity(FeatureFlags flags)
    {
        if ((flags & FeatureFlags.Junction) != 0) return 6f;
        if ((flags & FeatureFlags.Endpoint) != 0) return 5f;
        if ((flags & FeatureFlags.Corner) != 0) return 4f;
        if ((flags & FeatureFlags.Thin) != 0) return 2.5f;
        if ((flags & FeatureFlags.Edge) != 0) return 1.5f;
        return 1f;
    }

    private static int LocalRun(
        byte[] pixels,
        int width,
        int height,
        int x,
        int y,
        byte color,
        bool horizontal)
    {
        var count = 1;

        for (var direction = -1; direction <= 1; direction += 2)
        {
            for (var distance = 1; distance <= 2; distance++)
            {
                var nx = horizontal ? x + direction * distance : x;
                var ny = horizontal ? y : y + direction * distance;
                if (!Same(pixels, width, height, nx, ny, color))
                    break;
                count++;
            }
        }

        return count;
    }

    private static bool Same(byte[] pixels, int width, int height, int x, int y, byte color)
    {
        if (RepeatAware)
        {
            x = Wrap(x, width);
            y = Wrap(y, height);
        }
        else if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return false;
        }

        return pixels[y * width + x] == color;
    }

    private static int MapCoordinate(int sourceCoordinate, int sourceLength, int targetLength) =>
        Math.Clamp((int)((sourceCoordinate + 0.5) * targetLength / sourceLength), 0, targetLength - 1);

    private static int ShortestWrappedDelta(int from, int to, int length)
    {
        var delta = to - from;
        if (!RepeatAware || length <= 1)
            return delta;

        if (delta > length / 2)
            delta -= length;
        else if (delta < -length / 2)
            delta += length;
        return delta;
    }

    private static int Wrap(int value, int length)
    {
        if (length <= 1)
            return 0;
        value %= length;
        return value < 0 ? value + length : value;
    }

    private static int BoolInt(bool value) => value ? 1 : 0;

    private static void ScaleNearest(DesignDocument source, DesignDocument destination)
    {
        // Sample pixel centres rather than anchoring every non-integer resize at the top-left.
        // The older floor(tx * source/target) mapping gives one side of a symmetric rug the extra
        // duplicated rows/columns. Centre sampling preserves mirror symmetry on enlargement.
        for (var ty = 0; ty < destination.Height; ty++)
        {
            var sy = Math.Clamp(
                (int)Math.Floor((ty + 0.5) * source.Height / destination.Height),
                0,
                source.Height - 1);

            for (var tx = 0; tx < destination.Width; tx++)
            {
                var sx = Math.Clamp(
                    (int)Math.Floor((tx + 0.5) * source.Width / destination.Width),
                    0,
                    source.Width - 1);

                destination.SetPixel(tx, ty, source.GetPixel(sx, sy));
            }
        }
    }

    private static void Copy(DesignDocument source, DesignDocument destination)
    {
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                destination.SetPixel(x, y, source.GetPixel(x, y));
    }
}
