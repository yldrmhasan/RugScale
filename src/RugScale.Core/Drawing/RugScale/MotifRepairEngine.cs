using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

public sealed record MotifSelection(
    int X,
    int Y,
    int Width,
    int Height,
    bool[]? Mask = null);

public enum MotifRepairStyle : byte
{
    Balanced,
    PreserveBranches,
    ContourFirst,
    ConnectivityFirst,
    DetailAndConnectivity,
}

public sealed record MotifRepairCandidate(
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    double Similarity,
    MotifTransform Transform,
    string? MemoryFamilyId,
    MotifKind Kind,
    MotifRepairStyle Style,
    byte[] ColorMap,
    bool[] SourceMask,
    bool[] TargetMask,
    byte[] TargetPixels);

/// <summary>
/// Finds an original-source motif corresponding to a broken RugScale preview selection and
/// rasterizes that source motif back into the selected target size. Search starts near the
/// inverse-scaled position (highest prior probability) and then broadens to the whole source.
/// Descriptor matching is palette-independent and rotation/mirror invariant.
/// </summary>
public static class MotifRepairEngine
{
    /// <summary>
    /// Cold-start redraw prior measured by the B163A source-ground-truth self-training run.
    /// User/family feedback still takes precedence in the UI; this protects direct API callers
    /// from assuming Balanced is universally the best first redraw.
    /// </summary>
    public static MotifRepairStyle PreferredColdStartStyle(
        MotifKind kind) =>
        kind switch
        {
            MotifKind.Branch => MotifRepairStyle.Balanced,
            MotifKind.LeafLike => MotifRepairStyle.Balanced,
            MotifKind.Compound => MotifRepairStyle.PreserveBranches,
            MotifKind.LargePart => MotifRepairStyle.DetailAndConnectivity,
            _ => MotifRepairStyle.DetailAndConnectivity,
        };

    public static IReadOnlyList<MotifRepairCandidate> FindCandidates(
        DesignDocument source,
        DesignDocument resizedPreview,
        MotifSelection targetSelection,
        int warpDensity,
        int weftDensity,
        MotifMemoryFile? memory = null,
        int maxCandidates = 8) =>
        FindCandidates(
            source,
            resizedPreview,
            targetSelection,
            warpDensity,
            weftDensity,
            warpDensity,
            weftDensity,
            memory,
            maxCandidates,
            sourceCatalog: null);

    public static IReadOnlyList<MotifRepairCandidate> FindCandidates(
        DesignDocument source,
        DesignDocument resizedPreview,
        MotifSelection targetSelection,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity,
        MotifMemoryFile? memory = null,
        int maxCandidates = 8,
        MotifSourceCatalog? sourceCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resizedPreview);

        if (targetSelection.Width <= 0 ||
            targetSelection.Height <= 0)
        {
            return [];
        }

        var targetMotifMask =
            BuildTargetMotifMask(
                resizedPreview,
                targetSelection);

        var targetDescriptor = MotifDescriptor.FromPatch(
            resizedPreview,
            targetSelection.X,
            targetSelection.Y,
            targetSelection.Width,
            targetSelection.Height,
            targetMotifMask,
            targetWarpDensity,
            targetWeftDensity);

        var targetMotifRoleCount =
            Math.Max(
                1,
                targetDescriptor.RoleCount);
        var targetMotifPixels =
            Math.Max(
                1,
                targetMotifMask.Count(
                    static value => value));
        var sourceToTargetAreaScale =
            source.Width /
            (double)Math.Max(1, resizedPreview.Width) *
            source.Height /
            (double)Math.Max(1, resizedPreview.Height);
        var expectedSourceMotifPixels =
            Math.Max(
                1d,
                targetMotifPixels *
                sourceToTargetAreaScale);

        sourceCatalog ??=
            MotifSourceCatalog.Build(
                source,
                sourceWarpDensity,
                sourceWeftDensity);

        var expectedWidth = Math.Max(
            1,
            (int)Math.Round(
                targetSelection.Width *
                source.Width /
                (double)resizedPreview.Width));
        var expectedHeight = Math.Max(
            1,
            (int)Math.Round(
                targetSelection.Height *
                source.Height /
                (double)resizedPreview.Height));

        var expectedCenterX =
            (targetSelection.X +
             targetSelection.Width * 0.5) *
            source.Width /
            resizedPreview.Width;
        var expectedCenterY =
            (targetSelection.Y +
             targetSelection.Height * 0.5) *
            source.Height /
            resizedPreview.Height;

        // Narrow selections are common when the user repairs a branch/vein. Their rectangular
        // target box often contains one or two unrelated neighbouring colour roles, so treating
        // "fewer source roles than target roles" as a hard rejection hides the correct one-colour
        // source branch entirely. Detect line-like geometry from the inverse-mapped extent and use
        // a softer role rule for that case.
        var targetLineLike =
            Math.Min(expectedWidth, expectedHeight) <= 4 ||
            Math.Max(expectedWidth, expectedHeight) /
                (double)Math.Max(
                    1,
                    Math.Min(expectedWidth, expectedHeight)) >= 3.0;

        var proposals = new List<(int X, int Y, int W, int H, double Prior)>();
        AddLocalProposals(
            proposals,
            source.Width,
            source.Height,
            expectedCenterX,
            expectedCenterY,
            expectedWidth,
            expectedHeight);

        AddGlobalProposals(
            proposals,
            source.Width,
            source.Height,
            expectedWidth,
            expectedHeight);

        // A rotated family may be tall where the target instance is wide (or vice versa). Search
        // the swapped source extent as a second hypothesis instead of assuming orientation.
        if (Math.Abs(expectedWidth - expectedHeight) >= 2)
        {
            AddLocalProposals(
                proposals,
                source.Width,
                source.Height,
                expectedCenterX,
                expectedCenterY,
                expectedHeight,
                expectedWidth);

            AddGlobalProposals(
                proposals,
                source.Width,
                source.Height,
                expectedHeight,
                expectedWidth);
        }

        double targetMemorySimilarity = 0d;
        var targetMemoryFamily = memory is null
            ? null
            : MotifMemorySerializer.FindBestFamily(
                memory,
                targetDescriptor,
                0.72,
                out targetMemorySimilarity);

        var scored = new List<MotifRepairCandidate>();
        // Catalogue masks and rectangular fallback masks are different hypotheses even when their
        // bounding boxes are identical. Keep them separate so an over-grouped Compound entry
        // cannot suppress the user's inverse-mapped source window merely by sharing the same bbox.
        var seen = new HashSet<(
            int X,
            int Y,
            int W,
            int H,
            bool CatalogueEvidence)>();

        MotifRepairCandidate? ScoreCandidate(
            int x,
            int y,
            int width,
            int height,
            double prior,
            MotifDescriptor descriptor,
            MotifKind kind,
            bool[]? exactSourceMask,
            bool catalogueEvidence)
        {
            if (!seen.Add((
                    x,
                    y,
                    width,
                    height,
                    catalogueEvidence)))
            {
                return null;
            }

            var sourceMask =
                exactSourceMask is { Length: > 0 } &&
                exactSourceMask.Length == width * height
                    ? exactSourceMask.ToArray()
                    : BuildFallbackSourceMask(
                        source,
                        x,
                        y,
                        width,
                        height);

            var sourceMotifPixels =
                Math.Max(
                    1,
                    sourceMask.Count(
                        static value => value));
            var massRatio =
                sourceMotifPixels /
                expectedSourceMotifPixels;
            var massScore =
                Math.Exp(
                    -Math.Abs(
                        Math.Log(
                            Math.Max(
                                1e-6,
                                massRatio))));

            var familySimilarity = MotifDescriptor.Similarity(
                targetDescriptor,
                descriptor);
            var relativeTransform =
                MotifDescriptor.FindBestRelativeTransform(
                    descriptor,
                    targetDescriptor,
                    out var alignmentSimilarity);

            MotifFamilyMemory? candidateMemoryFamily = null;
            double candidateMemorySimilarity = 0d;

            if (memory is not null)
            {
                candidateMemoryFamily =
                    MotifMemorySerializer.FindBestFamily(
                        memory,
                        descriptor,
                        0.72,
                        out candidateMemorySimilarity);
            }

            // Source geometry is still the primary signal, but a RugScale repair has one piece of
            // evidence that a generic image matcher does not: the selected target area maps back
            // to a known location in the original source. Keep that inverse-mapped location strong
            // enough to break ties between visually similar repeated/compound motifs instead of
            // allowing a distant lookalike to become candidate #1.
            //
            // Mass is part of the base score rather than a large additive bonus so strong
            // descriptor matches do not all saturate at 1.0 and erase the location signal.
            var similarity =
                familySimilarity * 0.50 +
                alignmentSimilarity * 0.22 +
                massScore * 0.10 +
                prior * 0.12;

            // A rectangular user selection often contains one surrounding field/background role,
            // while an exact source motif mask does not. Compare against both target role counts:
            // raw and raw-1. This keeps a two-colour flower/compound ahead of a one-colour leaf
            // fragment when the selected target clearly contains multiple motif roles.
            var sourceRoleCount =
                Math.Max(1, descriptor.RoleCount);
            var targetRoleCount =
                Math.Max(1, targetDescriptor.RoleCount);

            // A source fragment with fewer colour roles than the selected target motif cannot be
            // the whole motif. This is a hard anti-subcomponent rule: e.g. a one-colour leaf/vein
            // must not outrank the two-colour compound flower that contains it.
            if (targetMotifRoleCount >= 2 &&
                sourceRoleCount < targetMotifRoleCount)
            {
                if (!targetLineLike)
                    return null;

                // A thin branch selected with a little surrounding field may legitimately reduce
                // to one source role. Penalize the missing roles, but keep the spatially correct
                // branch in the candidate set so inverse-source locality can rank it.
                similarity -=
                    Math.Min(
                        0.08,
                        (targetMotifRoleCount -
                         sourceRoleCount) * 0.025);
            }

            var roleDistance =
                Math.Min(
                    Math.Abs(
                        sourceRoleCount -
                        targetRoleCount),
                    Math.Abs(
                        sourceRoleCount -
                        Math.Max(
                            1,
                            targetRoleCount - 1)));

            if (sourceRoleCount == targetMotifRoleCount)
                similarity += 0.040;
            else if (roleDistance == 0)
                similarity += 0.020;
            else
                similarity -=
                    Math.Min(
                        0.15,
                        roleDistance * 0.055);

            // Preserve motif mass across global resize. The positive mass evidence is already in
            // the base score above; these guards only penalize implausibly small/large fragments.
            if (massRatio < 0.58)
                similarity -= 0.12;
            else if (massRatio > 1.75)
                similarity -= 0.08;

            if (targetMemoryFamily is not null &&
                candidateMemoryFamily is not null &&
                targetMemoryFamily.Id ==
                    candidateMemoryFamily.Id)
            {
                var learnedEvidence =
                    Math.Min(
                        targetMemorySimilarity,
                        candidateMemorySimilarity) *
                    targetMemoryFamily.Confidence;

                similarity +=
                    0.04 * learnedEvidence;
            }

            if (catalogueEvidence)
            {
                // Exact masks are useful evidence, but "Compound" is intentionally only a small
                // tie-break. The catalogue can form several nearby compound hypotheses around the
                // same components; a generic compound must not beat the inverse-mapped source just
                // because it contains many colours.
                similarity += 0.025;

                if (targetMotifRoleCount >= 2 &&
                    kind == MotifKind.Compound &&
                    massScore >= 0.50 &&
                    !targetLineLike)
                {
                    similarity += 0.025;
                }
            }

            if (targetLineLike)
            {
                // Geometry is a stronger semantic cue than colour-role count for narrow repairs.
                // Prefer a source-catalogued Branch and slightly demote broad Compound hypotheses.
                if (kind == MotifKind.Branch)
                    similarity += 0.055;
                else if (kind == MotifKind.Compound)
                    similarity -= 0.035;
            }

            similarity = Math.Clamp(
                similarity,
                0d,
                1d);

            var colorMap = BuildSourceToTargetColorMap(
                source,
                resizedPreview,
                targetSelection,
                x,
                y,
                width,
                height,
                relativeTransform,
                sourceMask);

            var coldStartStyle =
                PreferredColdStartStyle(kind);

            var rendered = RenderPatch(
                source,
                x,
                y,
                width,
                height,
                targetSelection.Width,
                targetSelection.Height,
                targetSelection.Mask,
                sourceMask,
                relativeTransform,
                colorMap,
                coldStartStyle);

            var candidate = new MotifRepairCandidate(
                x,
                y,
                width,
                height,
                similarity,
                relativeTransform,
                candidateMemoryFamily?.Id,
                kind,
                coldStartStyle,
                colorMap,
                sourceMask,
                rendered.Mask,
                rendered.Pixels);

            scored.Add(candidate);
            return candidate;
        }

        // Exact source motifs first. These entries know connected-component / compound boundaries
        // and therefore avoid the rectangular-window ambiguity of the fallback scanner.
        if (sourceCatalog is not null)
        {
            foreach (var entry in sourceCatalog.Entries)
            {
                var directSizeError =
                    Math.Abs(Math.Log(
                        Math.Max(1d, entry.Width) /
                        Math.Max(1d, expectedWidth))) +
                    Math.Abs(Math.Log(
                        Math.Max(1d, entry.Height) /
                        Math.Max(1d, expectedHeight)));
                var rotatedSizeError =
                    Math.Abs(Math.Log(
                        Math.Max(1d, entry.Width) /
                        Math.Max(1d, expectedHeight))) +
                    Math.Abs(Math.Log(
                        Math.Max(1d, entry.Height) /
                        Math.Max(1d, expectedWidth)));

                if (Math.Min(
                        directSizeError,
                        rotatedSizeError) > 1.35)
                {
                    continue;
                }

                var centerX =
                    entry.X + entry.Width * 0.5;
                var centerY =
                    entry.Y + entry.Height * 0.5;
                var normalizedDistance = Math.Sqrt(
                    Math.Pow(
                        (centerX - expectedCenterX) /
                        Math.Max(4d, expectedWidth),
                        2) +
                    Math.Pow(
                        (centerY - expectedCenterY) /
                        Math.Max(4d, expectedHeight),
                        2));
                var sizeError =
                    Math.Min(
                        directSizeError,
                        rotatedSizeError);
                var prior =
                    Math.Exp(
                        -normalizedDistance * 0.55) *
                    Math.Exp(
                        -sizeError * 0.85);

                ScoreCandidate(
                    entry.X,
                    entry.Y,
                    entry.Width,
                    entry.Height,
                    prior,
                    entry.Descriptor,
                    entry.Kind,
                    entry.Mask,
                    catalogueEvidence: true);

            }
        }

        // Rectangular windows have two roles. Local windows around the inverse-mapped source
        // position are authoritative fallback evidence and are always evaluated. Coarse GLOBAL
        // windows are the expensive last resort and may be skipped when the hierarchical source
        // catalogue already has a credible exact-mask motif.
        var bestCatalogueSimilarity =
            scored.Count == 0
                ? 0d
                : scored.Max(candidate =>
                    candidate.Similarity);

        // Always distinguish local fallback from global fallback. The inverse-mapped source
        // neighbourhood is authoritative evidence and is cheap enough to score every time. A
        // strong exact catalogue hit may suppress only the expensive GLOBAL window scan; it never
        // suppresses local windows. This also lets a rectangular local hypothesis compete with an
        // over-grouped Compound mask at the same bounding box.
        const bool useLocalFallbackWindows = true;
        var useGlobalFallbackWindows =
            bestCatalogueSimilarity < 0.54;

        if (useLocalFallbackWindows ||
            useGlobalFallbackWindows)
        foreach (var proposal in proposals)
        {
            // Local proposals come from AddLocalProposals and have materially stronger priors;
            // AddGlobalProposals uses 0.10. Swapped-orientation local proposals are local too.
            var isLocalProposal =
                proposal.Prior > 0.20;

            if ((isLocalProposal &&
                 !useLocalFallbackWindows) ||
                (!isLocalProposal &&
                 !useGlobalFallbackWindows))
            {
                continue;
            }
            if (seen.Contains(
                    (proposal.X,
                     proposal.Y,
                     proposal.W,
                     proposal.H,
                     CatalogueEvidence: false)))
            {
                continue;
            }

            var fallbackMask =
                BuildFallbackSourceMask(
                    source,
                    proposal.X,
                    proposal.Y,
                    proposal.W,
                    proposal.H);

            var foregroundPixels =
                fallbackMask.Count(
                    static value => value);

            // A tiny residue extracted from a much larger window is not a motif candidate.
            if (foregroundPixels <
                Math.Max(
                    3,
                    proposal.W * proposal.H / 25))
            {
                continue;
            }

            var descriptor = MotifDescriptor.FromPatch(
                source,
                proposal.X,
                proposal.Y,
                proposal.W,
                proposal.H,
                fallbackMask,
                sourceWarpDensity,
                sourceWeftDensity);

            ScoreCandidate(
                proposal.X,
                proposal.Y,
                proposal.W,
                proposal.H,
                proposal.Prior * 0.78,
                descriptor,
                MotifKind.Primitive,
                exactSourceMask: fallbackMask,
                catalogueEvidence: false);
        }

        double LocalityScore(
            MotifRepairCandidate candidate)
        {
            var centerX =
                candidate.SourceX +
                candidate.SourceWidth * 0.5;
            var centerY =
                candidate.SourceY +
                candidate.SourceHeight * 0.5;
            var dx =
                (centerX - expectedCenterX) /
                Math.Max(4d, expectedWidth);
            var dy =
                (centerY - expectedCenterY) /
                Math.Max(4d, expectedHeight);
            var distance =
                Math.Sqrt(dx * dx + dy * dy);

            return Math.Exp(
                -distance * 0.85);
        }

        return scored
            // Similarity remains primary, but the selected RugScale occurrence has a known
            // inverse-source location. A bounded final locality tie-break prevents a distant
            // lookalike from winning by a few descriptor points, especially for tiny branches.
            .OrderByDescending(candidate =>
                candidate.Similarity +
                0.10 * LocalityScore(candidate))
            .ThenByDescending(candidate =>
                candidate.Similarity)
            .Take(Math.Max(1, maxCandidates))
            .ToArray();
    }

    /// <summary>
    /// Re-rasterizes a detected source motif into a user-selected target area. Detection and
    /// placement are intentionally separate: a motif may occupy a different box after shrink,
    /// while its learned/source geometry and the selected instance's color-role map stay the same.
    /// </summary>
    public static MotifRepairCandidate RetargetCandidate(
        DesignDocument source,
        MotifSelection targetSelection,
        MotifRepairCandidate candidate,
        MotifRepairStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (targetSelection.Width <= 0 ||
            targetSelection.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSelection));
        }

        var colorMap =
            candidate.ColorMap.Length == 256
                ? candidate.ColorMap
                : Enumerable.Range(0, 256)
                    .Select(static value => (byte)value)
                    .ToArray();

        var selectedStyle =
            style ?? candidate.Style;

        var sourceMask =
            candidate.SourceMask.Length ==
            candidate.SourceWidth * candidate.SourceHeight
                ? candidate.SourceMask
                : BuildFallbackSourceMask(
                    source,
                    candidate.SourceX,
                    candidate.SourceY,
                    candidate.SourceWidth,
                    candidate.SourceHeight);

        // Every redraw starts again from the immutable original source motif mask. It never uses
        // the pixels produced by the previous redraw as input.
        var rendered = RenderPatch(
            source,
            candidate.SourceX,
            candidate.SourceY,
            candidate.SourceWidth,
            candidate.SourceHeight,
            targetSelection.Width,
            targetSelection.Height,
            targetSelection.Mask,
            sourceMask,
            candidate.Transform,
            colorMap,
            selectedStyle);

        return candidate with
        {
            Style = selectedStyle,
            ColorMap = colorMap.ToArray(),
            SourceMask = sourceMask.ToArray(),
            TargetMask = rendered.Mask,
            TargetPixels = rendered.Pixels,
        };
    }

    public static DesignDocument ApplyCandidate(
        DesignDocument preview,
        MotifSelection selection,
        MotifRepairCandidate candidate)
    {
        var result = Clone(preview);

        for (var y = 0; y < selection.Height; y++)
        {
            var targetY = selection.Y + y;
            if (targetY < 0 || targetY >= result.Height)
                continue;

            for (var x = 0; x < selection.Width; x++)
            {
                var targetX = selection.X + x;
                if (targetX < 0 || targetX >= result.Width)
                    continue;

                var local = y * selection.Width + x;

                if (selection.Mask is not null &&
                    (local >= selection.Mask.Length ||
                     !selection.Mask[local]))
                {
                    continue;
                }

                if (local >= candidate.TargetMask.Length ||
                    !candidate.TargetMask[local])
                {
                    continue;
                }

                result.SetPixel(
                    targetX,
                    targetY,
                    candidate.TargetPixels[local]);
            }
        }

        return result;
    }

    private static bool[] BuildTargetMotifMask(
        DesignDocument preview,
        MotifSelection selection)
    {
        var count = checked(
            selection.Width *
            selection.Height);
        var mask = new bool[count];
        var colorCounts = new int[256];
        var perimeterCounts = new int[256];
        var total = 0;
        var perimeterTotal = 0;

        for (var y = 0; y < selection.Height; y++)
        {
            var py = selection.Y + y;
            if (py < 0 || py >= preview.Height)
                continue;

            for (var x = 0; x < selection.Width; x++)
            {
                var local =
                    y * selection.Width + x;

                if (selection.Mask is not null &&
                    (local >= selection.Mask.Length ||
                     !selection.Mask[local]))
                {
                    continue;
                }

                var px = selection.X + x;
                if (px < 0 || px >= preview.Width)
                    continue;

                var color = preview.GetPixel(px, py);
                colorCounts[color]++;
                total++;

                // Use both selection bounding perimeter and exposed polygon-mask boundary as field
                // evidence. A rectangular user selection is only a search area; its surrounding
                // field must not become part of the motif descriptor.
                var perimeter =
                    x == 0 ||
                    y == 0 ||
                    x == selection.Width - 1 ||
                    y == selection.Height - 1 ||
                    IsSelectionMaskBoundary(
                        selection.Mask,
                        selection.Width,
                        selection.Height,
                        x,
                        y);

                if (perimeter)
                {
                    perimeterCounts[color]++;
                    perimeterTotal++;
                }
            }
        }

        if (total == 0)
            return mask;

        var backgrounds = new HashSet<byte>();

        foreach (var color in Enumerable.Range(0, 256)
                     .OrderByDescending(color => colorCounts[color]))
        {
            if (colorCounts[color] == 0)
                continue;

            var areaRatio =
                colorCounts[color] /
                (double)total;
            var perimeterRatio =
                perimeterCounts[color] /
                (double)Math.Max(
                    1,
                    perimeterTotal);

            if ((areaRatio >= 0.34 &&
                 perimeterRatio >= 0.18) ||
                (areaRatio >= 0.48 &&
                 perimeterRatio >= 0.10))
            {
                backgrounds.Add((byte)color);
            }

            if (backgrounds.Count >= 2)
                break;
        }

        var active = 0;

        for (var y = 0; y < selection.Height; y++)
        {
            var py = selection.Y + y;
            if (py < 0 || py >= preview.Height)
                continue;

            for (var x = 0; x < selection.Width; x++)
            {
                var local =
                    y * selection.Width + x;

                if (selection.Mask is not null &&
                    (local >= selection.Mask.Length ||
                     !selection.Mask[local]))
                {
                    continue;
                }

                var px = selection.X + x;
                if (px < 0 || px >= preview.Width)
                    continue;

                var color = preview.GetPixel(px, py);

                if (backgrounds.Contains(color))
                    continue;

                mask[local] = true;
                active++;
            }
        }

        // A tightly selected motif may legitimately have no dominant background. If our
        // background inference stripped almost everything, trust the user's selection instead.
        var selectedCount =
            selection.Mask is null
                ? count
                : selection.Mask.Count(
                    static value => value);

        if (active <
            Math.Max(
                2,
                selectedCount / 20))
        {
            for (var i = 0; i < count; i++)
            {
                mask[i] =
                    selection.Mask is null ||
                    (i < selection.Mask.Length &&
                     selection.Mask[i]);
            }
        }

        return mask;
    }

    private static bool IsSelectionMaskBoundary(
        bool[]? selectionMask,
        int width,
        int height,
        int x,
        int y)
    {
        if (selectionMask is null)
            return false;

        var index = y * width + x;
        if (index < 0 ||
            index >= selectionMask.Length ||
            !selectionMask[index])
        {
            return false;
        }

        ReadOnlySpan<int> dx = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, -1, 1];

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
                return true;
            }

            var neighbour =
                ny * width + nx;

            if (neighbour >= selectionMask.Length ||
                !selectionMask[neighbour])
            {
                return true;
            }
        }

        return false;
    }

    private static void AddLocalProposals(
        List<(int X, int Y, int W, int H, double Prior)> output,
        int sourceWidth,
        int sourceHeight,
        double centerX,
        double centerY,
        int expectedWidth,
        int expectedHeight)
    {
        var sizeFactors = new[] { 0.90, 1.00, 1.10 };
        var shifts = new[] { -0.18, -0.09, 0d, 0.09, 0.18 };

        foreach (var factor in sizeFactors)
        {
            var width = Math.Clamp(
                (int)Math.Round(expectedWidth * factor),
                1,
                sourceWidth);
            var height = Math.Clamp(
                (int)Math.Round(expectedHeight * factor),
                1,
                sourceHeight);

            foreach (var shiftY in shifts)
            {
                foreach (var shiftX in shifts)
                {
                    var x = (int)Math.Round(
                        centerX -
                        width * 0.5 +
                        shiftX * expectedWidth);
                    var y = (int)Math.Round(
                        centerY -
                        height * 0.5 +
                        shiftY * expectedHeight);

                    x = Math.Clamp(x, 0, sourceWidth - width);
                    y = Math.Clamp(y, 0, sourceHeight - height);

                    var distance =
                        Math.Sqrt(shiftX * shiftX +
                                  shiftY * shiftY);
                    var prior = Math.Exp(-distance * 4d) *
                                Math.Exp(-Math.Abs(1d - factor) * 2d);

                    output.Add((x, y, width, height, prior));
                }
            }
        }
    }

    private static void AddGlobalProposals(
        List<(int X, int Y, int W, int H, double Prior)> output,
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        if (expectedWidth < 2 || expectedHeight < 2)
            return;

        var width = Math.Min(expectedWidth, sourceWidth);
        var height = Math.Min(expectedHeight, sourceHeight);
        var stepX = Math.Max(4, width / 2);
        var stepY = Math.Max(4, height / 2);

        // Bound global scanning so selecting a tiny motif in a multi-million-pixel carpet stays
        // interactive. At most ~1600 coarse windows are sampled.
        var gridX = Math.Max(1, (sourceWidth - width) / stepX + 1);
        var gridY = Math.Max(1, (sourceHeight - height) / stepY + 1);
        var skip = Math.Max(
            1,
            (int)Math.Ceiling(
                Math.Sqrt(
                    gridX * gridY / 1600d)));

        for (var gy = 0; gy < gridY; gy += skip)
        {
            var y = Math.Min(
                sourceHeight - height,
                gy * stepY);

            for (var gx = 0; gx < gridX; gx += skip)
            {
                var x = Math.Min(
                    sourceWidth - width,
                    gx * stepX);
                output.Add((x, y, width, height, 0.10));
            }
        }
    }

    private static byte[] BuildSourceToTargetColorMap(
        DesignDocument source,
        DesignDocument preview,
        MotifSelection selection,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        MotifTransform transform,
        bool[] sourceMask)
    {
        var map = Enumerable.Range(0, 256)
            .Select(static value => (byte)value)
            .ToArray();
        var counts = new int[256 * 256];
        var sourceTotals = new int[256];
        var targetTotals = new int[256];
        var targetPerimeterCounts = new int[256];
        var targetPerimeterTotal = 0;

        // Background/field detection must describe the WHOLE user-selected target area, not only
        // pixels that happen to overlap the current source motif mask. Otherwise a missing/broken
        // motif can make field grey look like a valid motif colour.
        for (var ty = 0; ty < selection.Height; ty++)
        {
            var py = selection.Y + ty;
            if (py < 0 || py >= preview.Height)
                continue;

            for (var tx = 0; tx < selection.Width; tx++)
            {
                var local = ty * selection.Width + tx;
                if (selection.Mask is not null &&
                    (local >= selection.Mask.Length ||
                     !selection.Mask[local]))
                {
                    continue;
                }

                var px = selection.X + tx;
                if (px < 0 || px >= preview.Width)
                    continue;

                var targetColor = preview.GetPixel(px, py);
                targetTotals[targetColor]++;

                if (tx == 0 ||
                    ty == 0 ||
                    tx == selection.Width - 1 ||
                    ty == selection.Height - 1)
                {
                    targetPerimeterCounts[targetColor]++;
                    targetPerimeterTotal++;
                }
            }
        }

        for (var ty = 0; ty < selection.Height; ty++)
        {
            var py = selection.Y + ty;
            if (py < 0 || py >= preview.Height)
                continue;

            for (var tx = 0; tx < selection.Width; tx++)
            {
                var local = ty * selection.Width + tx;
                if (selection.Mask is not null &&
                    (local >= selection.Mask.Length ||
                     !selection.Mask[local]))
                {
                    continue;
                }

                var px = selection.X + tx;
                if (px < 0 || px >= preview.Width)
                    continue;

                MotifDescriptor.MapOutputToSource(
                    (tx + 0.5) / selection.Width,
                    (ty + 0.5) / selection.Height,
                    transform,
                    out var sourceU,
                    out var sourceV);

                var localSourceX = Math.Clamp(
                    (int)Math.Floor(sourceU * sourceWidth),
                    0,
                    sourceWidth - 1);
                var localSourceY = Math.Clamp(
                    (int)Math.Floor(sourceV * sourceHeight),
                    0,
                    sourceHeight - 1);
                var sourceMaskIndex =
                    localSourceY * sourceWidth +
                    localSourceX;

                if (sourceMaskIndex >= sourceMask.Length ||
                    !sourceMask[sourceMaskIndex])
                {
                    continue;
                }

                var sx = sourceX + localSourceX;
                var sy = sourceY + localSourceY;

                var sourceColor = source.GetPixel(sx, sy);
                var targetColor = preview.GetPixel(px, py);
                counts[sourceColor * 256 + targetColor]++;
                sourceTotals[sourceColor]++;
            }
        }

        // The selected rectangle commonly contains a large field/background around the broken
        // motif. Treat strongly represented perimeter colours as background candidates so a
        // damaged/missing motif cannot teach the colour mapper that "gold leaf == grey field".
        var targetSelectionArea =
            Math.Max(
                1,
                targetTotals.Sum());

        var targetBackgroundCandidates =
            Enumerable.Range(0, 256)
                .Where(color =>
                    targetPerimeterCounts[color] > 0 &&
                    targetPerimeterCounts[color] >=
                        Math.Max(
                            2,
                            (int)Math.Ceiling(
                                targetPerimeterTotal * 0.16)) &&
                    targetTotals[color] >=
                        Math.Max(
                            3,
                            (int)Math.Ceiling(
                                targetSelectionArea * 0.42)))
                .OrderByDescending(color =>
                    targetPerimeterCounts[color])
                .Take(2)
                .ToHashSet();

        // Resolve the strongest source-role/target-role pairs globally first. Independent
        // per-colour majority can collapse two source roles onto one target colour (e.g. both a
        // leaf fill and its vein become the same green after a slightly damaged preview). A
        // one-to-one first pass keeps distinct motif roles distinct whenever the target contains
        // corresponding evidence.
        var pairs = new List<(int Count, int Source, int Target, double Strength)>();
        for (var sourceColor = 0; sourceColor < 256; sourceColor++)
        {
            if (sourceTotals[sourceColor] == 0)
                continue;

            for (var targetColor = 0; targetColor < 256; targetColor++)
            {
                var count = counts[sourceColor * 256 + targetColor];
                if (count <= 0)
                    continue;

                var sourceRatio =
                    count /
                    (double)Math.Max(
                        1,
                        sourceTotals[sourceColor]);
                var targetIsBackground =
                    targetBackgroundCandidates.Contains(
                        targetColor);

                // SourceMask contains motif pixels only. Therefore any mapped source role is a
                // motif role, never field/background. Mapping it to a target background colour is
                // always destructive, regardless of local overlap strength.
                if (targetIsBackground)
                    continue;

                var identityBonus =
                    sourceColor == targetColor
                        ? 0.12
                        : 0d;
                pairs.Add((
                    count,
                    sourceColor,
                    targetColor,
                    sourceRatio + identityBonus));
            }
        }

        var assignedSource = new bool[256];
        var assignedTarget = new bool[256];

        // Same-document resize keeps palette indexes. If a source role is still visibly present
        // in the selected target instance, preserve that identity before considering recolour
        // matching. Different-colour motif instances still work because an absent source role is
        // left free for role-to-role assignment.
        for (var color = 0; color < 256; color++)
        {
            if (sourceTotals[color] <= 0 ||
                targetTotals[color] <= 0)
            {
                continue;
            }

            var identitySupport =
                counts[color * 256 + color];
            var sourceShare =
                identitySupport /
                (double)Math.Max(1, sourceTotals[color]);
            var targetShare =
                identitySupport /
                (double)Math.Max(1, targetTotals[color]);

            if (identitySupport >= 2 ||
                sourceShare >= 0.12 ||
                targetShare >= 0.12)
            {
                map[color] = (byte)color;
                assignedSource[color] = true;
                assignedTarget[color] = true;
            }
        }

        foreach (var pair in pairs
                     .OrderByDescending(pair => pair.Strength)
                     .ThenByDescending(pair => pair.Count))
        {
            if (assignedSource[pair.Source] ||
                assignedTarget[pair.Target])
            {
                continue;
            }

            if (pair.Count <
                Math.Max(
                    2,
                    (int)Math.Ceiling(
                        sourceTotals[pair.Source] * 0.30)))
            {
                continue;
            }

            map[pair.Source] = (byte)pair.Target;
            assignedSource[pair.Source] = true;
            assignedTarget[pair.Target] = true;
        }

        // Overlap can be unreliable when the current resized motif is geometrically damaged.
        // Pair any still-unassigned motif roles by area rank, excluding detected field/background.
        // For recoloured copies of the same geometry this recovers e.g. source main/accent ->
        // target main/accent without depending on exact current pixel alignment.
        var remainingSourceRoles =
            Enumerable.Range(0, 256)
                .Where(color =>
                    sourceTotals[color] > 0 &&
                    !assignedSource[color])
                .OrderByDescending(color =>
                    sourceTotals[color])
                .ToArray();

        var remainingTargetRoles =
            Enumerable.Range(0, 256)
                .Where(color =>
                    targetTotals[color] > 0 &&
                    !assignedTarget[color] &&
                    !targetBackgroundCandidates.Contains(color))
                .OrderByDescending(color =>
                    targetTotals[color])
                .ToArray();

        var rankedPairs =
            Math.Min(
                remainingSourceRoles.Length,
                remainingTargetRoles.Length);

        for (var index = 0;
             index < rankedPairs;
             index++)
        {
            var sourceColor =
                remainingSourceRoles[index];
            var targetColor =
                remainingTargetRoles[index];

            map[sourceColor] =
                (byte)targetColor;
            assignedSource[sourceColor] = true;
            assignedTarget[targetColor] = true;
        }

        // If a damaged target has lost an entire role, a unique assignment may be impossible.
        // Remaining roles may still use their strongest supported NON-BACKGROUND target colour;
        // otherwise they retain the original source index rather than inventing a palette entry.
        for (var sourceColor = 0; sourceColor < 256; sourceColor++)
        {
            if (sourceTotals[sourceColor] == 0 ||
                assignedSource[sourceColor])
            {
                continue;
            }

            var bestTarget = sourceColor;
            var bestCount = 0;

            for (var targetColor = 0; targetColor < 256; targetColor++)
            {
                if (targetBackgroundCandidates.Contains(targetColor))
                    continue;

                var count = counts[sourceColor * 256 + targetColor];
                if (count <= bestCount)
                    continue;

                bestCount = count;
                bestTarget = targetColor;
            }

            var bestRatio =
                bestCount /
                (double)Math.Max(
                    1,
                    sourceTotals[sourceColor]);

            if (bestRatio >= 0.55)
                map[sourceColor] = (byte)bestTarget;
        }

        return map;
    }

    private static void MapSourceToOutput(
        double sourceX,
        double sourceY,
        MotifTransform transform,
        out double outputX,
        out double outputY)
    {
        var inverse = transform switch
        {
            MotifTransform.Rotate90 => MotifTransform.Rotate270,
            MotifTransform.Rotate270 => MotifTransform.Rotate90,
            // Reflections (including reflected rotations) are self-inverse in this convention.
            _ => transform,
        };

        MotifDescriptor.MapOutputToSource(
            sourceX,
            sourceY,
            inverse,
            out outputX,
            out outputY);
    }

    private readonly record struct RepairProfile(
        int SampleGrid,
        float BoundaryBoost,
        float ThinBoost,
        float RareBoost,
        float MaxAreaMultiplier,
        float MinAreaMultiplier,
        bool ConnectivitySupport);

    private sealed record RenderedMotif(
        byte[] Pixels,
        bool[] Mask);

    private static RenderedMotif RenderPatch(
        DesignDocument source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        bool[]? selectionMask,
        bool[] sourceMask,
        MotifTransform transform,
        byte[] colorMap,
        MotifRepairStyle style)
    {
        var profile = RepairProfileFor(style);
        var targetCount = checked(targetWidth * targetHeight);
        var pixels = new byte[targetCount];
        var targetMask = new bool[targetCount];
        var bestScores = new float[targetCount];

        var sourceRoleCounts = new int[256];
        var sourceArea = checked(sourceWidth * sourceHeight);
        var activeSourceArea = 0;
        var sourceBoundary = new bool[sourceArea];
        var sourceThin = new bool[sourceArea];

        // The source mask is the motif. Pixels outside it are never evidence, never colour roles,
        // and never get painted into the target.
        for (var sy = 0; sy < sourceHeight; sy++)
        {
            for (var sx = 0; sx < sourceWidth; sx++)
            {
                var local = sy * sourceWidth + sx;
                if (local >= sourceMask.Length ||
                    !sourceMask[local])
                {
                    continue;
                }

                activeSourceArea++;
                var absoluteX = sourceX + sx;
                var absoluteY = sourceY + sy;
                var sourceColor = source.GetPixel(
                    absoluteX,
                    absoluteY);
                sourceRoleCounts[sourceColor]++;

                sourceBoundary[local] =
                    IsMaskBoundary(
                        sourceMask,
                        sourceWidth,
                        sourceHeight,
                        sx,
                        sy);
                sourceThin[local] =
                    CountMaskNeighbours(
                        sourceMask,
                        sourceWidth,
                        sourceHeight,
                        sx,
                        sy) <= 3;
            }
        }

        if (activeSourceArea == 0)
            return new RenderedMotif(pixels, targetMask);

        Span<byte> evidenceColors = stackalloc byte[32];
        Span<float> evidenceScores = stackalloc float[32];

        // Target-footprint voting, but ONLY from motif pixels.
        for (var ty = 0; ty < targetHeight; ty++)
        {
            for (var tx = 0; tx < targetWidth; tx++)
            {
                var targetIndex = ty * targetWidth + tx;
                if (!TargetCellAllowed(
                        selectionMask,
                        targetIndex))
                {
                    continue;
                }

                var evidenceCount = 0;

                for (var sampleY = 0;
                     sampleY < profile.SampleGrid;
                     sampleY++)
                {
                    for (var sampleX = 0;
                         sampleX < profile.SampleGrid;
                         sampleX++)
                    {
                        var outputU =
                            (tx +
                             (sampleX + 0.5) /
                             profile.SampleGrid) /
                            targetWidth;
                        var outputV =
                            (ty +
                             (sampleY + 0.5) /
                             profile.SampleGrid) /
                            targetHeight;

                        MotifDescriptor.MapOutputToSource(
                            outputU,
                            outputV,
                            transform,
                            out var sourceU,
                            out var sourceV);

                        var sx = Math.Clamp(
                            (int)Math.Floor(
                                sourceU * sourceWidth),
                            0,
                            sourceWidth - 1);
                        var sy = Math.Clamp(
                            (int)Math.Floor(
                                sourceV * sourceHeight),
                            0,
                            sourceHeight - 1);
                        var sourceIndex =
                            sy * sourceWidth + sx;

                        if (sourceIndex >= sourceMask.Length ||
                            !sourceMask[sourceIndex])
                        {
                            continue;
                        }

                        var sourceColor = source.GetPixel(
                            sourceX + sx,
                            sourceY + sy);
                        var mappedColor =
                            colorMap[sourceColor];

                        var weight = 1f;
                        if (sourceBoundary[sourceIndex])
                            weight += profile.BoundaryBoost;
                        if (sourceThin[sourceIndex])
                            weight += profile.ThinBoost;

                        var rarity =
                            sourceRoleCounts[sourceColor] /
                            (float)Math.Max(
                                1,
                                activeSourceArea);
                        if (rarity <= 0.10f)
                        {
                            weight +=
                                profile.RareBoost *
                                (1f - rarity / 0.10f);
                        }

                        AddEvidence(
                            evidenceColors,
                            evidenceScores,
                            ref evidenceCount,
                            mappedColor,
                            weight);
                    }
                }

                if (evidenceCount == 0)
                    continue;

                SelectBestEvidence(
                    evidenceColors,
                    evidenceScores,
                    evidenceCount,
                    out var bestColor,
                    out var bestScore,
                    out _,
                    out _);

                targetMask[targetIndex] = true;
                pixels[targetIndex] = bestColor;
                bestScores[targetIndex] = bestScore;
            }
        }

        // Forward-project EVERY source motif pixel from the immutable source mask. This is the
        // anti-disappearance pass: a leaf tip / branch / rare colour gets at least one target
        // opportunity even when no target-centre sample lands on it.
        for (var sy = 0; sy < sourceHeight; sy++)
        {
            for (var sx = 0; sx < sourceWidth; sx++)
            {
                var sourceIndex =
                    sy * sourceWidth + sx;
                if (sourceIndex >= sourceMask.Length ||
                    !sourceMask[sourceIndex])
                {
                    continue;
                }

                MapSourceToOutput(
                    (sx + 0.5) / sourceWidth,
                    (sy + 0.5) / sourceHeight,
                    transform,
                    out var outputU,
                    out var outputV);

                var tx = Math.Clamp(
                    (int)Math.Floor(
                        outputU * targetWidth),
                    0,
                    targetWidth - 1);
                var ty = Math.Clamp(
                    (int)Math.Floor(
                        outputV * targetHeight),
                    0,
                    targetHeight - 1);
                var targetIndex =
                    ty * targetWidth + tx;

                if (!TargetCellAllowed(
                        selectionMask,
                        targetIndex))
                {
                    continue;
                }

                var sourceColor = source.GetPixel(
                    sourceX + sx,
                    sourceY + sy);
                var mappedColor =
                    colorMap[sourceColor];

                var priority = 1f;
                if (sourceBoundary[sourceIndex])
                    priority += 0.30f +
                                profile.BoundaryBoost;
                if (sourceThin[sourceIndex])
                    priority += 0.55f +
                                profile.ThinBoost;

                var rarity =
                    sourceRoleCounts[sourceColor] /
                    (float)Math.Max(
                        1,
                        activeSourceArea);
                if (rarity <= 0.10f)
                    priority += 0.45f;

                if (!targetMask[targetIndex] ||
                    priority >
                    bestScores[targetIndex] + 0.25f)
                {
                    targetMask[targetIndex] = true;
                    pixels[targetIndex] = mappedColor;
                    bestScores[targetIndex] = priority;
                }
            }
        }

        EnsureEverySourceRoleSurvives(
            source,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            targetWidth,
            targetHeight,
            selectionMask,
            sourceMask,
            transform,
            colorMap,
            sourceRoleCounts,
            pixels,
            targetMask);

        if (profile.ConnectivitySupport)
        {
            RepairMotifMaskConnectivity(
                pixels,
                targetMask,
                targetWidth,
                targetHeight,
                selectionMask);
        }

        return new RenderedMotif(
            pixels,
            targetMask);
    }

    private static bool TargetCellAllowed(
        bool[]? selectionMask,
        int index) =>
        selectionMask is null ||
        (index >= 0 &&
         index < selectionMask.Length &&
         selectionMask[index]);

    private static bool IsMaskBoundary(
        bool[] mask,
        int width,
        int height,
        int x,
        int y)
    {
        if (x == 0 ||
            y == 0 ||
            x == width - 1 ||
            y == height - 1)
        {
            return true;
        }

        var index = y * width + x;
        return
            !mask[index - 1] ||
            !mask[index + 1] ||
            !mask[index - width] ||
            !mask[index + width];
    }

    private static int CountMaskNeighbours(
        bool[] mask,
        int width,
        int height,
        int x,
        int y)
    {
        var count = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 ||
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                if (mask[ny * width + nx])
                    count++;
            }
        }

        return count;
    }

    private static void EnsureEverySourceRoleSurvives(
        DesignDocument source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        bool[]? selectionMask,
        bool[] sourceMask,
        MotifTransform transform,
        byte[] colorMap,
        int[] sourceRoleCounts,
        byte[] pixels,
        bool[] targetMask)
    {
        Span<bool> present =
            stackalloc bool[256];

        for (var i = 0; i < targetMask.Length; i++)
        {
            if (targetMask[i])
                present[pixels[i]] = true;
        }

        for (var sourceColor = 0;
             sourceColor < 256;
             sourceColor++)
        {
            if (sourceRoleCounts[sourceColor] <= 0)
                continue;

            var mappedColor =
                colorMap[sourceColor];
            if (present[mappedColor])
                continue;

            double sumU = 0;
            double sumV = 0;
            var count = 0;

            for (var sy = 0; sy < sourceHeight; sy++)
            {
                for (var sx = 0; sx < sourceWidth; sx++)
                {
                    var sourceIndex =
                        sy * sourceWidth + sx;
                    if (sourceIndex >= sourceMask.Length ||
                        !sourceMask[sourceIndex] ||
                        source.GetPixel(
                            sourceX + sx,
                            sourceY + sy) != sourceColor)
                    {
                        continue;
                    }

                    MapSourceToOutput(
                        (sx + 0.5) / sourceWidth,
                        (sy + 0.5) / sourceHeight,
                        transform,
                        out var outputU,
                        out var outputV);
                    sumU += outputU;
                    sumV += outputV;
                    count++;
                }
            }

            if (count == 0)
                continue;

            var centerX = Math.Clamp(
                (int)Math.Floor(
                    sumU / count * targetWidth),
                0,
                targetWidth - 1);
            var centerY = Math.Clamp(
                (int)Math.Floor(
                    sumV / count * targetHeight),
                0,
                targetHeight - 1);

            var placed = false;

            for (var radius = 0;
                 radius <= 3 && !placed;
                 radius++)
            {
                for (var dy = -radius;
                     dy <= radius && !placed;
                     dy++)
                {
                    for (var dx = -radius;
                         dx <= radius;
                         dx++)
                    {
                        var x = centerX + dx;
                        var y = centerY + dy;
                        if (x < 0 ||
                            x >= targetWidth ||
                            y < 0 ||
                            y >= targetHeight)
                        {
                            continue;
                        }

                        var index =
                            y * targetWidth + x;
                        if (!TargetCellAllowed(
                                selectionMask,
                                index))
                        {
                            continue;
                        }

                        targetMask[index] = true;
                        pixels[index] = mappedColor;
                        present[mappedColor] = true;
                        placed = true;
                        break;
                    }
                }
            }
        }
    }

    private static void RepairMotifMaskConnectivity(
        byte[] pixels,
        bool[] mask,
        int width,
        int height,
        bool[]? selectionMask)
    {
        // Only bridge a one-cell orthogonal gap between already-painted motif pixels. Never flood
        // or thicken large regions; this is deliberately conservative.
        var additions =
            new List<(int Index, byte Color)>();

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = y * width + x;
                if (mask[index] ||
                    !TargetCellAllowed(
                        selectionMask,
                        index))
                {
                    continue;
                }

                var left = index - 1;
                var right = index + 1;
                var up = index - width;
                var down = index + width;

                if (mask[left] &&
                    mask[right] &&
                    pixels[left] == pixels[right])
                {
                    additions.Add(
                        (index, pixels[left]));
                    continue;
                }

                if (mask[up] &&
                    mask[down] &&
                    pixels[up] == pixels[down])
                {
                    additions.Add(
                        (index, pixels[up]));
                }
            }
        }

        foreach (var addition in additions)
        {
            mask[addition.Index] = true;
            pixels[addition.Index] =
                addition.Color;
        }
    }

    private static bool[] BuildFallbackSourceMask(
        DesignDocument source,
        int sourceX,
        int sourceY,
        int width,
        int height)
    {
        var count = checked(width * height);
        var exterior = new bool[count];
        var mask = new bool[count];
        var perimeterCounts = new int[256];
        var cropColorCounts = new int[256];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cropColorCounts[
                    source.GetPixel(
                        sourceX + x,
                        sourceY + y)]++;
            }
        }

        void CountPerimeter(
            int x,
            int y)
        {
            var color = source.GetPixel(
                sourceX + x,
                sourceY + y);
            perimeterCounts[color]++;
        }

        for (var x = 0; x < width; x++)
        {
            CountPerimeter(x, 0);
            if (height > 1)
                CountPerimeter(x, height - 1);
        }

        for (var y = 1; y < height - 1; y++)
        {
            CountPerimeter(0, y);
            if (width > 1)
                CountPerimeter(width - 1, y);
        }

        var perimeterTotal =
            perimeterCounts.Sum();

        var backgroundColors = new HashSet<byte>();

        var perimeterRanked =
            Enumerable.Range(0, 256)
                .Where(color =>
                    perimeterCounts[color] > 0)
                .OrderByDescending(color =>
                    perimeterCounts[color])
                .ThenByDescending(color =>
                    cropColorCounts[color])
                .ToArray();

        if (perimeterRanked.Length > 0)
        {
            var primary = perimeterRanked[0];
            var primaryPerimeterRatio =
                perimeterCounts[primary] /
                (double)Math.Max(1, perimeterTotal);
            var primaryAreaRatio =
                cropColorCounts[primary] /
                (double)Math.Max(1, count);

            if (primaryPerimeterRatio >= 0.32 ||
                primaryAreaRatio >= 0.38)
            {
                backgroundColors.Add((byte)primary);
            }
        }

        if (perimeterRanked.Length > 1)
        {
            var secondary = perimeterRanked[1];
            var secondaryPerimeterRatio =
                perimeterCounts[secondary] /
                (double)Math.Max(1, perimeterTotal);
            var secondaryAreaRatio =
                cropColorCounts[secondary] /
                (double)Math.Max(1, count);

            // A second background role is only accepted with both perimeter and area evidence;
            // this prevents a small motif colour that merely touches the crop edge from vanishing.
            if (secondaryPerimeterRatio >= 0.20 &&
                secondaryAreaRatio >= 0.20)
            {
                backgroundColors.Add((byte)secondary);
            }
        }

        var queue = new int[count];
        var head = 0;
        var tail = 0;

        void Seed(
            int x,
            int y)
        {
            var index = y * width + x;
            if (exterior[index])
                return;

            var color = source.GetPixel(
                sourceX + x,
                sourceY + y);
            if (!backgroundColors.Contains(color))
                return;

            exterior[index] = true;
            queue[tail++] = index;
        }

        for (var x = 0; x < width; x++)
        {
            Seed(x, 0);
            if (height > 1)
                Seed(x, height - 1);
        }

        for (var y = 1; y < height - 1; y++)
        {
            Seed(0, y);
            if (width > 1)
                Seed(width - 1, y);
        }

        ReadOnlySpan<int> dx =
            [-1, 1, 0, 0];
        ReadOnlySpan<int> dy =
            [0, 0, -1, 1];

        while (head < tail)
        {
            var index = queue[head++];
            var x = index % width;
            var y = index / width;
            var currentColor = source.GetPixel(
                sourceX + x,
                sourceY + y);

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

                var next =
                    ny * width + nx;
                if (exterior[next])
                    continue;

                var color = source.GetPixel(
                    sourceX + nx,
                    sourceY + ny);

                // Exterior flood follows only perimeter background colours. Allow transitions
                // between the two common background roles, but never into a motif-only colour.
                if (!backgroundColors.Contains(color))
                    continue;

                exterior[next] = true;
                queue[tail++] = next;
            }
        }

        var active = 0;
        for (var i = 0; i < count; i++)
        {
            var x = i % width;
            var y = i / width;
            var color = source.GetPixel(
                sourceX + x,
                sourceY + y);

            // A perimeter-derived background colour is background even when a motif encloses a
            // hole of that same colour. Keeping enclosed field holes in the source mask lets that
            // colour compete as a motif role and can steal/remap a real accent colour.
            mask[i] =
                !exterior[i] &&
                !backgroundColors.Contains(color);

            if (mask[i])
                active++;
        }

        // If the fallback crop has no separable exterior (or almost the whole crop survives),
        // conservatively keep non-dominant-perimeter colours. Catalogue candidates normally avoid
        // this path entirely.
        if (active == 0 ||
            active >= count * 0.92)
        {
            var dominantBackground =
                backgroundColors.Count > 0
                    ? backgroundColors
                        .OrderByDescending(color =>
                            perimeterCounts[color])
                        .First()
                    : (byte)255;

            active = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index =
                        y * width + x;
                    mask[index] =
                        source.GetPixel(
                            sourceX + x,
                            sourceY + y) !=
                        dominantBackground;
                    if (mask[index])
                        active++;
                }
            }
        }

        if (active == 0)
            Array.Fill(mask, true);

        return mask;
    }

    private static RepairProfile RepairProfileFor(
        MotifRepairStyle style) =>
        style switch
        {
            MotifRepairStyle.Balanced =>
                new RepairProfile(
                    SampleGrid: 4,
                    BoundaryBoost: 0.10f,
                    ThinBoost: 0.10f,
                    RareBoost: 0.10f,
                    MaxAreaMultiplier: 1.30f,
                    MinAreaMultiplier: 0.55f,
                    ConnectivitySupport: false),

            MotifRepairStyle.PreserveBranches =>
                new RepairProfile(
                    SampleGrid: 5,
                    BoundaryBoost: 0.16f,
                    ThinBoost: 0.55f,
                    RareBoost: 0.22f,
                    MaxAreaMultiplier: 1.35f,
                    MinAreaMultiplier: 0.62f,
                    ConnectivitySupport: true),

            MotifRepairStyle.ContourFirst =>
                new RepairProfile(
                    SampleGrid: 5,
                    BoundaryBoost: 0.48f,
                    ThinBoost: 0.08f,
                    RareBoost: 0.08f,
                    MaxAreaMultiplier: 1.22f,
                    MinAreaMultiplier: 0.58f,
                    ConnectivitySupport: false),

            MotifRepairStyle.ConnectivityFirst =>
                new RepairProfile(
                    SampleGrid: 5,
                    BoundaryBoost: 0.18f,
                    ThinBoost: 0.34f,
                    RareBoost: 0.14f,
                    MaxAreaMultiplier: 1.28f,
                    MinAreaMultiplier: 0.60f,
                    ConnectivitySupport: true),

            MotifRepairStyle.DetailAndConnectivity =>
                new RepairProfile(
                    SampleGrid: 6,
                    BoundaryBoost: 0.24f,
                    ThinBoost: 0.48f,
                    RareBoost: 0.30f,
                    MaxAreaMultiplier: 1.24f,
                    MinAreaMultiplier: 0.68f,
                    ConnectivitySupport: true),

            _ =>
                new RepairProfile(
                    4,
                    0.10f,
                    0.10f,
                    0.10f,
                    1.30f,
                    0.55f,
                    false),
        };

    private static void AddEvidence(
        Span<byte> colors,
        Span<float> scores,
        ref int count,
        byte color,
        float score)
    {
        for (var i = 0; i < count; i++)
        {
            if (colors[i] != color)
                continue;

            scores[i] += score;
            return;
        }

        if (count >= colors.Length)
            return;

        colors[count] = color;
        scores[count] = score;
        count++;
    }

    private static void SelectBestEvidence(
        Span<byte> colors,
        Span<float> scores,
        int count,
        out byte bestColor,
        out float bestScore,
        out byte secondColor,
        out float secondScore)
    {
        bestColor = 0;
        secondColor = 0;
        bestScore = float.NegativeInfinity;
        secondScore = float.NegativeInfinity;

        for (var i = 0; i < count; i++)
        {
            var score = scores[i];
            var color = colors[i];

            if (score > bestScore)
            {
                secondScore = bestScore;
                secondColor = bestColor;
                bestScore = score;
                bestColor = color;
            }
            else if (score > secondScore)
            {
                secondScore = score;
                secondColor = color;
            }
        }

        if (float.IsNegativeInfinity(secondScore))
        {
            secondColor = bestColor;
            secondScore = bestScore;
        }
    }

    private static void AddForwardDetailEvidence(
        DesignDocument source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        int targetX,
        int targetY,
        MotifTransform transform,
        byte[] colorMap,
        bool[] sourceBoundary,
        bool[] sourceThin,
        int[] sourceRoleCounts,
        RepairProfile profile,
        Span<byte> colors,
        Span<float> scores,
        ref int evidenceCount)
    {
        // Invert the target cell to a conservative source neighbourhood and inspect only source
        // detail that genuinely projects into THIS target cell.
        var radiusX = Math.Max(
            1,
            (int)Math.Ceiling(
                sourceWidth /
                (double)targetWidth));
        var radiusY = Math.Max(
            1,
            (int)Math.Ceiling(
                sourceHeight /
                (double)targetHeight));

        MotifDescriptor.MapOutputToSource(
            (targetX + 0.5) / targetWidth,
            (targetY + 0.5) / targetHeight,
            transform,
            out var centerU,
            out var centerV);

        var centerX = Math.Clamp(
            (int)Math.Floor(centerU * sourceWidth),
            0,
            sourceWidth - 1);
        var centerY = Math.Clamp(
            (int)Math.Floor(centerV * sourceHeight),
            0,
            sourceHeight - 1);

        for (var sy = Math.Max(0, centerY - radiusY);
             sy <= Math.Min(sourceHeight - 1, centerY + radiusY);
             sy++)
        {
            for (var sx = Math.Max(0, centerX - radiusX);
                 sx <= Math.Min(sourceWidth - 1, centerX + radiusX);
                 sx++)
            {
                MapSourceToOutput(
                    (sx + 0.5) / sourceWidth,
                    (sy + 0.5) / sourceHeight,
                    transform,
                    out var outputU,
                    out var outputV);

                var projectedX = Math.Clamp(
                    (int)Math.Floor(outputU * targetWidth),
                    0,
                    targetWidth - 1);
                var projectedY = Math.Clamp(
                    (int)Math.Floor(outputV * targetHeight),
                    0,
                    targetHeight - 1);

                if (projectedX != targetX ||
                    projectedY != targetY)
                {
                    continue;
                }

                var local = sy * sourceWidth + sx;
                if (!sourceBoundary[local] &&
                    !sourceThin[local])
                {
                    continue;
                }

                var sourceColor = source.GetPixel(
                    sourceX + sx,
                    sourceY + sy);
                var mappedColor = colorMap[sourceColor];
                var rarity =
                    sourceRoleCounts[sourceColor] /
                    (float)Math.Max(
                        1,
                        sourceWidth * sourceHeight);

                var boost =
                    (sourceBoundary[local]
                        ? profile.BoundaryBoost
                        : 0f) +
                    (sourceThin[local]
                        ? profile.ThinBoost
                        : 0f);

                if (rarity <= 0.08f)
                    boost += profile.RareBoost * 0.6f;

                if (boost > 0f)
                {
                    AddEvidence(
                        colors,
                        scores,
                        ref evidenceCount,
                        mappedColor,
                        boost);
                }
            }
        }
    }

    private static void EnforceRoleAreaBounds(
        byte[] pixels,
        byte[] secondColors,
        float[] bestScores,
        float[] secondScores,
        int width,
        int height,
        bool[]? mask,
        int[] mappedRoleCounts,
        int sourceArea,
        int activeTargetCount,
        RepairProfile profile)
    {
        if (activeTargetCount <= 0 ||
            sourceArea <= 0)
        {
            return;
        }

        var counts = new int[256];
        var minCounts = new int[256];
        var maxCounts = new int[256];

        for (var color = 0; color < 256; color++)
        {
            if (mappedRoleCounts[color] <= 0)
                continue;

            var expected =
                mappedRoleCounts[color] /
                (double)sourceArea *
                activeTargetCount;

            // A real source role that survives at least one expected target pixel should not
            // disappear completely. Very tiny roles remain allowed to collapse if target geometry
            // simply cannot represent them.
            minCounts[color] =
                expected >= 1d
                    ? Math.Max(
                        1,
                        (int)Math.Floor(
                            expected *
                            profile.MinAreaMultiplier))
                    : 0;

            maxCounts[color] = Math.Max(
                minCounts[color],
                (int)Math.Ceiling(
                    expected *
                    profile.MaxAreaMultiplier +
                    1d));
        }

        for (var i = 0; i < pixels.Length; i++)
        {
            if (mask is not null &&
                (i >= mask.Length ||
                 !mask[i]))
            {
                continue;
            }

            counts[pixels[i]]++;
        }

        // First trim colours that expanded far beyond their source role. Replace the least certain
        // cells with their second supported colour.
        for (var color = 0; color < 256; color++)
        {
            var excess =
                counts[color] -
                maxCounts[color];
            if (excess <= 0 ||
                mappedRoleCounts[color] <= 0)
            {
                continue;
            }

            var candidates = Enumerable.Range(
                    0,
                    pixels.Length)
                .Where(index =>
                    pixels[index] == color &&
                    secondColors[index] != color &&
                    (mask is null ||
                     (index < mask.Length &&
                      mask[index])))
                .OrderBy(index =>
                    bestScores[index] -
                    secondScores[index])
                .ToArray();

            foreach (var index in candidates)
            {
                if (excess <= 0)
                    break;

                var replacement =
                    secondColors[index];

                if (maxCounts[replacement] > 0 &&
                    counts[replacement] >=
                    maxCounts[replacement])
                {
                    continue;
                }

                pixels[index] = replacement;
                counts[color]--;
                counts[replacement]++;
                excess--;
            }
        }

        // Then rescue underrepresented roles from cells where that role was the second-best source
        // supported candidate. This is especially important for one-pixel veins and accent colours.
        for (var color = 0; color < 256; color++)
        {
            var deficit =
                minCounts[color] -
                counts[color];
            if (deficit <= 0)
                continue;

            var candidates = Enumerable.Range(
                    0,
                    pixels.Length)
                .Where(index =>
                    secondColors[index] == color &&
                    pixels[index] != color &&
                    (mask is null ||
                     (index < mask.Length &&
                      mask[index])))
                .OrderBy(index =>
                    bestScores[index] -
                    secondScores[index])
                .ToArray();

            foreach (var index in candidates)
            {
                if (deficit <= 0)
                    break;

                var current =
                    pixels[index];

                if (mappedRoleCounts[current] > 0 &&
                    counts[current] <=
                    minCounts[current])
                {
                    continue;
                }

                pixels[index] = (byte)color;
                counts[current]--;
                counts[color]++;
                deficit--;
            }
        }
    }

    private static void RepairConnectivityWithCandidateSupport(
        byte[] pixels,
        byte[] secondColors,
        float[] bestScores,
        float[] secondScores,
        int width,
        int height,
        bool[]? mask)
    {
        var source = pixels.ToArray();

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = y * width + x;

                if (mask is not null &&
                    (index >= mask.Length ||
                     !mask[index]))
                {
                    continue;
                }

                var candidate =
                    secondColors[index];

                if (candidate == source[index] ||
                    candidate == 0 && secondScores[index] <= 0f)
                {
                    continue;
                }

                // Connectivity repair is legal only when the source-footprint already listed the
                // colour as the second-best candidate and the score is reasonably close.
                if (secondScores[index] <
                    bestScores[index] * 0.58f)
                {
                    continue;
                }

                var left =
                    source[index - 1] == candidate;
                var right =
                    source[index + 1] == candidate;
                var up =
                    source[index - width] == candidate;
                var down =
                    source[index + width] == candidate;
                var diagonalA =
                    source[index - width - 1] == candidate &&
                    source[index + width + 1] == candidate;
                var diagonalB =
                    source[index - width + 1] == candidate &&
                    source[index + width - 1] == candidate;

                if ((left && right) ||
                    (up && down) ||
                    diagonalA ||
                    diagonalB)
                {
                    pixels[index] = candidate;
                }
            }
        }
    }

    private static void BridgeSinglePixelGaps(
        byte[] pixels,
        int width,
        int height,
        bool[]? mask,
        bool diagonal,
        int passes)
    {
        var scratch = pixels.ToArray();
        Span<byte> candidates = stackalloc byte[8];

        for (var pass = 0; pass < passes; pass++)
        {
            Array.Copy(
                pixels,
                scratch,
                pixels.Length);

            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var index = y * width + x;
                    if (mask is not null &&
                        (index >= mask.Length ||
                         !mask[index]))
                    {
                        continue;
                    }

                    var current = pixels[index];
                    candidates[0] = pixels[index - 1];
                    candidates[1] = pixels[index + 1];
                    candidates[2] = pixels[index - width];
                    candidates[3] = pixels[index + width];
                    candidates[4] = diagonal ? pixels[index - width - 1] : current;
                    candidates[5] = diagonal ? pixels[index - width + 1] : current;
                    candidates[6] = diagonal ? pixels[index + width - 1] : current;
                    candidates[7] = diagonal ? pixels[index + width + 1] : current;

                    byte bestColor = current;
                    var bestOppositePairs = 0;

                    foreach (var color in candidates)
                    {
                        if (color == current)
                            continue;

                        var pairs = 0;
                        if (pixels[index - 1] == color &&
                            pixels[index + 1] == color)
                            pairs++;
                        if (pixels[index - width] == color &&
                            pixels[index + width] == color)
                            pairs++;

                        if (diagonal)
                        {
                            if (pixels[index - width - 1] == color &&
                                pixels[index + width + 1] == color)
                                pairs++;
                            if (pixels[index - width + 1] == color &&
                                pixels[index + width - 1] == color)
                                pairs++;
                        }

                        if (pairs > bestOppositePairs)
                        {
                            bestOppositePairs = pairs;
                            bestColor = color;
                        }
                    }

                    if (bestOppositePairs > 0)
                        scratch[index] = bestColor;
                }
            }

            Array.Copy(
                scratch,
                pixels,
                pixels.Length);
        }
    }

    private static void RemoveUnsupportedSpurs(
        byte[] pixels,
        int width,
        int height,
        bool[]? mask,
        bool preserveEndpoints = false)
    {
        var source = pixels.ToArray();
        Span<byte> neighbours = stackalloc byte[4];

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = y * width + x;
                if (mask is not null &&
                    (index >= mask.Length ||
                     !mask[index]))
                {
                    continue;
                }

                var color = source[index];
                var same = CountSameNeighbours(
                    source,
                    width,
                    height,
                    x,
                    y,
                    color);

                if (same >= 2 ||
                    (preserveEndpoints && same == 1))
                {
                    continue;
                }

                neighbours[0] = source[index - 1];
                neighbours[1] = source[index + 1];
                neighbours[2] = source[index - width];
                neighbours[3] = source[index + width];

                byte best = color;
                var bestCount = 0;

                foreach (var candidate in neighbours)
                {
                    if (candidate == color)
                        continue;

                    var count = 0;
                    foreach (var neighbour in neighbours)
                    {
                        if (neighbour == candidate)
                            count++;
                    }

                    if (count > bestCount)
                    {
                        bestCount = count;
                        best = candidate;
                    }
                }

                if (bestCount >= 2)
                    pixels[index] = best;
            }
        }
    }

    private static int CountSameNeighbours(
        DesignDocument document,
        int x,
        int y,
        byte color)
    {
        var count = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= document.Width ||
                    ny < 0 || ny >= document.Height)
                {
                    continue;
                }

                if (document.GetPixel(nx, ny) == color)
                    count++;
            }
        }

        return count;
    }

    private static int CountSameNeighbours(
        byte[] pixels,
        int width,
        int height,
        int x,
        int y,
        byte color)
    {
        var count = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= width ||
                    ny < 0 || ny >= height)
                {
                    continue;
                }

                if (pixels[ny * width + nx] == color)
                    count++;
            }
        }

        return count;
    }

    private static bool IsBoundary(
        DesignDocument document,
        int x,
        int y,
        byte color)
    {
        return
            x == 0 ||
            y == 0 ||
            x == document.Width - 1 ||
            y == document.Height - 1 ||
            document.GetPixel(
                Math.Max(0, x - 1),
                y) != color ||
            document.GetPixel(
                Math.Min(document.Width - 1, x + 1),
                y) != color ||
            document.GetPixel(
                x,
                Math.Max(0, y - 1)) != color ||
            document.GetPixel(
                x,
                Math.Min(document.Height - 1, y + 1)) != color;
    }

    private static DesignDocument Clone(
        DesignDocument source)
    {
        var result = new DesignDocument(
            source.Width,
            source.Height,
            source.Palette);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
                result.SetPixel(x, y, source.GetPixel(x, y));
        }

        return result;
    }
}
