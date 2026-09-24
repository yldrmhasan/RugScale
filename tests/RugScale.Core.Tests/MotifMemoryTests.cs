using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class MotifMemoryTests
{
    [Fact]
    public void MotifMemory_RoundTripsFeedbackAndPortableDescriptor()
    {
        var document = CreatePaletteDocument(14, 10);
        DrawAsymmetricMotif(document, 3, 2, 1, 2);

        var descriptor = MotifDescriptor.FromPatch(
            document,
            3,
            2,
            7,
            6,
            selectionMask: null,
            warpDensity: 40,
            weftDensity: 63);

        var memory = new MotifMemoryFile();
        var family = MotifMemorySerializer.LearnPositive(
            memory,
            descriptor,
            "seed_0014",
            7,
            6,
            40,
            63);
        MotifMemorySerializer.LearnNegative(memory, family.Id);

        var json = MotifMemorySerializer.Save(memory);
        var restored = MotifMemorySerializer.Load(json);

        var restoredFamily = Assert.Single(restored.Families);
        Assert.Equal(1, restoredFamily.PositiveFeedback);
        Assert.Equal(1, restoredFamily.NegativeFeedback);
        Assert.Single(restoredFamily.Samples);
        Assert.Equal(descriptor.Roles, restoredFamily.CanonicalRoles);
        Assert.Equal(descriptor.Edges, restoredFamily.CanonicalEdges);
    }

    [Fact]
    public void MotifDescriptor_MatchesSameGeometryAcrossPaletteAndRotation()
    {
        var source = CreatePaletteDocument(16, 16);
        DrawAsymmetricMotif(source, 3, 5, 1, 2);

        var target = CreatePaletteDocument(16, 16);
        DrawRotatedMotif(target, 8, 4, 3, 4);

        var sourceDescriptor = MotifDescriptor.FromPatch(
            source,
            3,
            5,
            7,
            6,
            selectionMask: null,
            warpDensity: 40,
            weftDensity: 40);
        var targetDescriptor = MotifDescriptor.FromPatch(
            target,
            8,
            4,
            6,
            7,
            selectionMask: null,
            warpDensity: 40,
            weftDensity: 40);

        var familySimilarity = MotifDescriptor.Similarity(
            sourceDescriptor,
            targetDescriptor);
        var transform = MotifDescriptor.FindBestRelativeTransform(
            sourceDescriptor,
            targetDescriptor,
            out var alignmentSimilarity);

        Assert.True(
            familySimilarity >= 0.90,
            $"Expected palette-independent family match, got {familySimilarity:0.000}.");
        Assert.True(
            alignmentSimilarity >= 0.88,
            $"Expected rotated alignment match, got {alignmentSimilarity:0.000}.");
        Assert.NotEqual(MotifTransform.Identity, transform);
    }

    [Fact]
    public void MotifDescriptor_UsesWarpWeftPhysicalAspect()
    {
        var document = CreatePaletteDocument(12, 12);
        DrawAsymmetricMotif(document, 2, 3, 1, 2);

        var tallPixelDescriptor = MotifDescriptor.FromPatch(
            document,
            2,
            3,
            7,
            6,
            selectionMask: null,
            warpDensity: 40,
            weftDensity: 80);
        var widePixelDescriptor = MotifDescriptor.FromPatch(
            document,
            2,
            3,
            7,
            6,
            selectionMask: null,
            warpDensity: 80,
            weftDensity: 40);

        Assert.True(
            tallPixelDescriptor.RawPhysicalAspect >
            widePixelDescriptor.RawPhysicalAspect * 3.5,
            "Warp/Weft must materially change the motif's physical aspect descriptor.");
    }

    [Fact]
    public void MotifRepair_RotatesGeometryButKeepsTargetColorVariant()
    {
        var source = CreatePaletteDocument(20, 20);
        DrawAsymmetricMotif(source, 7, 8, 1, 2);

        // Same geometry, 90-degree rotated, but with different palette roles.
        var preview = CreatePaletteDocument(20, 20);
        DrawRotatedMotif(preview, 8, 7, 3, 4);

        var selection = new MotifSelection(
            8,
            7,
            6,
            7,
            Enumerable.Repeat(true, 6 * 7).ToArray());

        var candidates = MotifRepairEngine.FindCandidates(
            source,
            preview,
            selection,
            sourceWarpDensity: 40,
            sourceWeftDensity: 40,
            targetWarpDensity: 40,
            targetWeftDensity: 40,
            memory: null,
            maxCandidates: 5);

        Assert.NotEmpty(candidates);
        var candidate = candidates[0];

        var candidateDiagnostics = string.Join(
            " | ",
            candidates.Take(8).Select((item, index) =>
                $"{index + 1}:{item.Kind}@{item.SourceX},{item.SourceY} {item.SourceWidth}x{item.SourceHeight} " +
                $"score={item.Similarity:0.000} mask={item.SourceMask.Count(value => value)} " +
                $"tx={item.Transform} map={item.ColorMap[1]}/{item.ColorMap[2]}"));

        // Candidate scoring was rebalanced so inverse-mapped source location remains visible
        // instead of descriptor/catalogue bonuses saturating strong matches at 1.0. The absolute
        // score scale therefore moved down; geometry/transform/color-role assertions below remain
        // the authoritative correctness checks.
        Assert.True(
            candidate.Similarity >= 0.68,
            $"Expected a strong source motif candidate, got {candidate.Similarity:0.000}. Candidates: {candidateDiagnostics}");
        Assert.NotEqual(MotifTransform.Identity, candidate.Transform);

        var used = candidate.TargetPixels.ToHashSet();

        Assert.DoesNotContain((byte)1, used);
        Assert.DoesNotContain((byte)2, used);
        Assert.Contains((byte)3, used);
        Assert.True(
            used.Contains((byte)4),
            $"Expected role 4. Kind={candidate.Kind}; source={candidate.SourceX},{candidate.SourceY} {candidate.SourceWidth}x{candidate.SourceHeight}; " +
            $"map1={candidate.ColorMap[1]}, map2={candidate.ColorMap[2]}; " +
            $"sourceMask={candidate.SourceMask.Count(value => value)}; targetMask={candidate.TargetMask.Count(value => value)}; " +
            $"colors={string.Join(",", used.Order())}");
        Assert.All(
            used,
            color => Assert.Contains(
                color,
                new byte[] { 0, 3, 4 }));
    }

    [Fact]
    public void MotifSourceCatalog_FindsBranchLeafLargePartAndCompound()
    {
        var document = CreatePaletteDocument(80, 60);

        // Thin elongated branch.
        for (var x = 5; x <= 28; x++)
            document.SetPixel(x, 8, 1);
        for (var y = 8; y <= 15; y++)
            document.SetPixel(28, y, 1);

        // Compact leaf-like component.
        for (var y = 23; y <= 27; y++)
        {
            for (var x = 12; x <= 18; x++)
            {
                if (Math.Abs(x - 15) + Math.Abs(y - 25) <= 5)
                    document.SetPixel(x, y, 2);
            }
        }

        // A sizeable, but not document-structural, motif part.
        for (var y = 31; y < 43; y++)
            for (var x = 42; x < 58; x++)
                document.SetPixel(x, y, 3);

        // Nearby second colour makes the large motif multi-colour and should yield a Compound.
        for (var y = 34; y < 40; y++)
            for (var x = 59; x < 64; x++)
                document.SetPixel(x, y, 4);

        var catalog = MotifSourceCatalog.Build(
            document,
            warpDensity: 40,
            weftDensity: 63);

        Assert.Contains(
            catalog.Entries,
            entry => entry.Kind == MotifKind.Branch);
        Assert.Contains(
            catalog.Entries,
            entry => entry.Kind == MotifKind.LeafLike);
        Assert.Contains(
            catalog.Entries,
            entry => entry.Kind == MotifKind.LargePart);
        Assert.Contains(
            catalog.Entries,
            entry => entry.Kind == MotifKind.Compound);

        Assert.All(
            catalog.Entries,
            entry =>
            {
                Assert.True(entry.Width > 0);
                Assert.True(entry.Height > 0);
                Assert.Equal(
                    entry.Width * entry.Height,
                    entry.Mask.Length);
            });
    }

    [Theory]
    [InlineData(MotifKind.Branch, MotifRepairStyle.Balanced)]
    [InlineData(MotifKind.LeafLike, MotifRepairStyle.Balanced)]
    [InlineData(MotifKind.Compound, MotifRepairStyle.PreserveBranches)]
    [InlineData(MotifKind.LargePart, MotifRepairStyle.DetailAndConnectivity)]
    [InlineData(MotifKind.Primitive, MotifRepairStyle.DetailAndConnectivity)]
    public void MotifRepair_UsesB163ATrainedColdStartStyle(
        MotifKind kind,
        MotifRepairStyle expected)
    {
        Assert.Equal(
            expected,
            MotifRepairEngine.PreferredColdStartStyle(kind));
    }

    [Fact]
    public void MotifRepair_DistantCatalogueLookalikeCannotHideInverseMappedSourceArea()
    {
        var source = CreatePaletteDocument(64, 48);
        DrawAsymmetricMotif(source, 16, 12, 1, 2);
        DrawAsymmetricMotif(source, 48, 34, 1, 2);

        // The damaged/preview instance still lives at the same normalized RugScale location, but
        // uses a different palette variant. This is the strongest spatial evidence available to
        // repair: target (16,12) maps back to source (16,12).
        var preview = CreatePaletteDocument(64, 48);
        DrawAsymmetricMotif(preview, 16, 12, 3, 4);

        var remoteMask = new bool[7 * 6];
        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                remoteMask[y * 7 + x] =
                    source.GetPixel(48 + x, 34 + y) != 0;
            }
        }

        // Deliberately provide only the distant lookalike as an exact catalogue entry. The
        // detector must still inspect local fallback windows around the inverse-mapped source
        // location instead of allowing this remote catalogue hit to suppress them all.
        var remoteDescriptor = MotifDescriptor.FromPatch(
            source,
            48,
            34,
            7,
            6,
            remoteMask,
            40,
            40);
        var sourceCatalog = new MotifSourceCatalog
        {
            Entries =
            [
                new MotifCatalogEntry(
                    48,
                    34,
                    7,
                    6,
                    remoteMask.Count(value => value),
                    MotifKind.Compound,
                    remoteMask,
                    remoteDescriptor),
            ],
        };

        var selection = new MotifSelection(
            16,
            12,
            7,
            6,
            Enumerable.Repeat(true, 7 * 6).ToArray());

        var candidates = MotifRepairEngine.FindCandidates(
            source,
            preview,
            selection,
            sourceWarpDensity: 40,
            sourceWeftDensity: 40,
            targetWarpDensity: 40,
            targetWeftDensity: 40,
            memory: null,
            maxCandidates: 6,
            sourceCatalog: sourceCatalog);

        Assert.NotEmpty(candidates);

        var best = candidates[0];
        Assert.True(
            best.SourceX < 30 && best.SourceY < 25,
            $"Expected inverse-mapped local source first, got {best.Kind}@{best.SourceX},{best.SourceY} " +
            $"{best.SourceWidth}x{best.SourceHeight} score={best.Similarity:0.000}. " +
            $"Candidates: {string.Join(" | ", candidates.Select(candidate =>
                $"{candidate.Kind}@{candidate.SourceX},{candidate.SourceY} {candidate.SourceWidth}x{candidate.SourceHeight}={candidate.Similarity:0.000}"))}");
    }

    [Fact]
    public void MotifRepair_RetargetsDetectedMotifIntoDifferentAreaWithoutLosingColorRoles()
    {
        var source = CreatePaletteDocument(24, 24);
        DrawAsymmetricMotif(source, 4, 6, 1, 2);

        var preview = CreatePaletteDocument(24, 24);
        DrawRotatedMotif(preview, 9, 8, 3, 4);

        var detection = new MotifSelection(
            9,
            8,
            6,
            7,
            Enumerable.Repeat(true, 6 * 7).ToArray());

        var candidates = MotifRepairEngine.FindCandidates(
            source,
            preview,
            detection,
            40,
            40,
            40,
            40,
            memory: null,
            maxCandidates: 4);

        Assert.NotEmpty(candidates);
        var detected = candidates[0];

        var target = new MotifSelection(
            2,
            2,
            9,
            5,
            Enumerable.Repeat(true, 9 * 5).ToArray());

        var retargeted = MotifRepairEngine.RetargetCandidate(
            source,
            target,
            detected);

        Assert.Equal(9 * 5, retargeted.TargetPixels.Length);

        var colors = retargeted.TargetPixels.ToHashSet();
        Assert.DoesNotContain((byte)1, colors);
        Assert.DoesNotContain((byte)2, colors);
        Assert.True(
            colors.Contains((byte)3),
            $"Retarget lost role 3. Colors={string.Join(",", colors.Order())}; map1={retargeted.ColorMap[1]}, map2={retargeted.ColorMap[2]}; masked={retargeted.TargetMask.Count(value => value)}");
        Assert.True(
            colors.Contains((byte)4),
            $"Retarget lost role 4. Colors={string.Join(",", colors.Order())}; map1={retargeted.ColorMap[1]}, map2={retargeted.ColorMap[2]}; masked={retargeted.TargetMask.Count(value => value)}");

        var applied = MotifRepairEngine.ApplyCandidate(
            preview,
            target,
            retargeted);

        var appliedTargetColors = Enumerable.Range(target.Y, target.Height)
            .SelectMany(y => Enumerable.Range(target.X, target.Width)
                .Select(x => applied.GetPixel(x, y)))
            .ToHashSet();

        Assert.Contains((byte)3, appliedTargetColors);
        Assert.Contains((byte)4, appliedTargetColors);
    }

    [Fact]
    public void MotifRepair_RefinementStylesKeepSameSourceCandidate()
    {
        var source = CreatePaletteDocument(28, 24);
        DrawAsymmetricMotif(source, 5, 7, 1, 2);

        var preview = CreatePaletteDocument(28, 24);
        DrawRotatedMotif(preview, 12, 8, 3, 4);

        var selection = new MotifSelection(
            12,
            8,
            6,
            7,
            Enumerable.Repeat(true, 6 * 7).ToArray());

        var candidates = MotifRepairEngine.FindCandidates(
            source,
            preview,
            selection,
            40,
            40,
            40,
            40,
            memory: null,
            maxCandidates: 4);

        Assert.NotEmpty(candidates);
        var detected = candidates[0];

        var balanced = MotifRepairEngine.RetargetCandidate(
            source,
            selection,
            detected,
            MotifRepairStyle.Balanced);
        var branches = MotifRepairEngine.RetargetCandidate(
            source,
            selection,
            detected,
            MotifRepairStyle.PreserveBranches);
        var connectivity = MotifRepairEngine.RetargetCandidate(
            source,
            selection,
            detected,
            MotifRepairStyle.ConnectivityFirst);

        foreach (var refined in new[] { balanced, branches, connectivity })
        {
            Assert.Equal(detected.SourceX, refined.SourceX);
            Assert.Equal(detected.SourceY, refined.SourceY);
            Assert.Equal(detected.SourceWidth, refined.SourceWidth);
            Assert.Equal(detected.SourceHeight, refined.SourceHeight);
            Assert.Equal(detected.Transform, refined.Transform);
            Assert.Equal(selection.Width * selection.Height, refined.TargetPixels.Length);

            Assert.All(
                refined.TargetPixels,
                color => Assert.InRange(color, (byte)0, (byte)4));
        }

        Assert.Equal(MotifRepairStyle.Balanced, balanced.Style);
        Assert.Equal(MotifRepairStyle.PreserveBranches, branches.Style);
        Assert.Equal(MotifRepairStyle.ConnectivityFirst, connectivity.Style);
    }

    [Fact]
    public void MotifMemory_RedrawStyleFeedbackChangesPreference()
    {
        var document = CreatePaletteDocument(14, 10);
        DrawAsymmetricMotif(document, 3, 2, 1, 2);

        var descriptor = MotifDescriptor.FromPatch(
            document,
            3,
            2,
            7,
            6,
            null,
            40,
            63);

        var memory = new MotifMemoryFile();
        var family = MotifMemorySerializer.LearnPositive(
            memory,
            descriptor,
            "feedback",
            7,
            6,
            40,
            63);

        var initial = MotifMemorySerializer.RepairStyleConfidence(
            memory,
            family.Id,
            MotifRepairStyle.PreserveBranches);

        MotifMemorySerializer.LearnRepairStyle(
            memory,
            family.Id,
            MotifRepairStyle.PreserveBranches,
            success: false);
        var afterBad = MotifMemorySerializer.RepairStyleConfidence(
            memory,
            family.Id,
            MotifRepairStyle.PreserveBranches);

        MotifMemorySerializer.LearnRepairStyle(
            memory,
            family.Id,
            MotifRepairStyle.PreserveBranches,
            success: true);
        MotifMemorySerializer.LearnRepairStyle(
            memory,
            family.Id,
            MotifRepairStyle.PreserveBranches,
            success: true);
        var afterGood = MotifMemorySerializer.RepairStyleConfidence(
            memory,
            family.Id,
            MotifRepairStyle.PreserveBranches);

        Assert.True(afterBad < initial);
        Assert.True(afterGood > afterBad);
    }

    [Fact]
    public void MotifRepair_AllRefinementStylesPreserveMeaningfulColorRoles()
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(35, 65, 95),
            new RugColor(170, 120, 80),
            new RugColor(90, 150, 105),
            new RugColor(190, 95, 125),
        });
        var source = new DesignDocument(20, 20, palette);

        // Four meaningful motif roles plus background. The shapes intentionally touch and cross
        // so connectivity repair has opportunities to be aggressive.
        for (var y = 3; y <= 16; y++)
        {
            for (var x = 3; x <= 7; x++)
                source.SetPixel(x, y, 1);
            for (var x = 8; x <= 11; x++)
                source.SetPixel(x, y, 2);
            for (var x = 12; x <= 16; x++)
                source.SetPixel(x, y, 3);
        }

        for (var y = 7; y <= 12; y++)
        {
            for (var x = 5; x <= 14; x++)
                source.SetPixel(x, y, 4);
        }

        var identityMap = Enumerable.Range(0, 256)
            .Select(static value => (byte)value)
            .ToArray();

        var candidate = new MotifRepairCandidate(
            0,
            0,
            20,
            20,
            1.0,
            MotifTransform.Identity,
            null,
            MotifKind.Compound,
            MotifRepairStyle.Balanced,
            identityMap,
            Enumerable.Repeat(true, 20 * 20).ToArray(),
            Enumerable.Repeat(true, 16 * 16).ToArray(),
            new byte[16 * 16]);

        var selection = new MotifSelection(
            0,
            0,
            16,
            16,
            Enumerable.Repeat(true, 16 * 16).ToArray());

        var sourceCounts = new int[5];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
                sourceCounts[source.GetPixel(x, y)]++;
        }

        foreach (var style in Enum.GetValues<MotifRepairStyle>())
        {
            var rendered = MotifRepairEngine.RetargetCandidate(
                source,
                selection,
                candidate,
                style);

            var targetCounts = new int[5];
            foreach (var color in rendered.TargetPixels)
            {
                if (color < targetCounts.Length)
                    targetCounts[color]++;
            }

            for (byte color = 1; color <= 4; color++)
            {
                Assert.True(
                    targetCounts[color] > 0,
                    $"{style} removed source color role {color}.");

                var expected =
                    sourceCounts[color] /
                    (double)(source.Width * source.Height) *
                    rendered.TargetPixels.Length;

                Assert.True(
                    targetCounts[color] <= expected * 1.55 + 3,
                    $"{style} expanded color role {color} from expected {expected:0.0} to {targetCounts[color]} pixels.");
            }
        }
    }

    [Fact]
    public void MotifRepair_IsolatedSourceMaskNeverPaintsCropBackground()
    {
        var palette = new Palette(new[]
        {
            new RugColor(230, 225, 210), // crop/background role
            new RugColor(40, 70, 105),
            new RugColor(170, 120, 80),
            new RugColor(95, 145, 110),
        });

        var source = new DesignDocument(16, 16, palette);

        // Deliberately put the motif in a much larger rectangular crop.
        for (var y = 5; y <= 10; y++)
            source.SetPixel(8, y, 1);
        for (var x = 6; x <= 10; x++)
            source.SetPixel(x, 8, 2);
        source.SetPixel(9, 7, 3);

        var sourceMask = new bool[12 * 12];
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                var sx = 2 + x;
                var sy = 2 + y;
                sourceMask[y * 12 + x] =
                    source.GetPixel(sx, sy) != 0;
            }
        }

        var identityMap = Enumerable.Range(0, 256)
            .Select(static value => (byte)value)
            .ToArray();

        var candidate = new MotifRepairCandidate(
            2,
            2,
            12,
            12,
            1.0,
            MotifTransform.Identity,
            null,
            MotifKind.Compound,
            MotifRepairStyle.Balanced,
            identityMap,
            sourceMask,
            Array.Empty<bool>(),
            Array.Empty<byte>());

        var target = new MotifSelection(
            0,
            0,
            9,
            9,
            Enumerable.Repeat(true, 9 * 9).ToArray());

        foreach (var style in Enum.GetValues<MotifRepairStyle>())
        {
            var rendered = MotifRepairEngine.RetargetCandidate(
                source,
                target,
                candidate,
                style);

            Assert.Equal(9 * 9, rendered.TargetMask.Length);
            Assert.Equal(9 * 9, rendered.TargetPixels.Length);

            for (var i = 0; i < rendered.TargetMask.Length; i++)
            {
                if (!rendered.TargetMask[i])
                    continue;

                Assert.NotEqual(
                    (byte)0,
                    rendered.TargetPixels[i]);
            }

            var colors = rendered.TargetPixels
                .Where((_, index) => rendered.TargetMask[index])
                .ToHashSet();

            Assert.Contains((byte)1, colors);
            Assert.Contains((byte)2, colors);
            Assert.Contains((byte)3, colors);
        }
    }

    [Fact]
    public void MotifRepair_ApplyCandidateLeavesPixelsOutsideGeneratedMotifMaskUntouched()
    {
        var document = CreatePaletteDocument(12, 12);

        // Existing resized design contains unrelated content inside the user's rectangular target.
        for (var y = 2; y < 10; y++)
            for (var x = 2; x < 10; x++)
                document.SetPixel(x, y, 4);

        var target = new MotifSelection(
            2,
            2,
            8,
            8,
            Enumerable.Repeat(true, 8 * 8).ToArray());

        var targetPixels = new byte[8 * 8];
        var targetMask = new bool[8 * 8];

        // New motif is only a small plus shape.
        for (var i = 1; i < 7; i++)
        {
            targetMask[3 * 8 + i] = true;
            targetPixels[3 * 8 + i] = 1;
            targetMask[i * 8 + 3] = true;
            targetPixels[i * 8 + 3] = 2;
        }

        var candidate = new MotifRepairCandidate(
            0,
            0,
            1,
            1,
            1.0,
            MotifTransform.Identity,
            null,
            MotifKind.Primitive,
            MotifRepairStyle.Balanced,
            Enumerable.Range(0, 256)
                .Select(static value => (byte)value)
                .ToArray(),
            [true],
            targetMask,
            targetPixels);

        var applied = MotifRepairEngine.ApplyCandidate(
            document,
            target,
            candidate);

        // Corner is inside the rectangular target but outside generated motif mask: preserve it.
        Assert.Equal((byte)4, applied.GetPixel(2, 2));

        // Painted motif cells change.
        Assert.Equal((byte)1, applied.GetPixel(2 + 4, 2 + 3));
        Assert.Equal((byte)2, applied.GetPixel(2 + 3, 2 + 4));
    }

    [Fact]
    public void MotifMemory_PositiveAndNegativeFeedbackChangeConfidence()
    {
        var document = CreatePaletteDocument(14, 10);
        DrawAsymmetricMotif(document, 3, 2, 1, 2);
        var descriptor = MotifDescriptor.FromPatch(
            document,
            3,
            2,
            7,
            6,
            null,
            40,
            63);

        var memory = new MotifMemoryFile();
        var family = MotifMemorySerializer.LearnPositive(
            memory,
            descriptor,
            "accepted",
            7,
            6,
            40,
            63);
        var afterPositive = family.Confidence;

        MotifMemorySerializer.LearnNegative(
            memory,
            family.Id);

        Assert.True(afterPositive > family.Confidence);
    }

    private static DesignDocument CreatePaletteDocument(
        int width,
        int height)
    {
        var palette = new Palette(new[]
        {
            new RugColor(245, 240, 230),
            new RugColor(35, 65, 95),
            new RugColor(170, 120, 80),
            new RugColor(90, 150, 105),
            new RugColor(190, 95, 125),
        });

        return new DesignDocument(
            width,
            height,
            palette);
    }

    private static void DrawAsymmetricMotif(
        DesignDocument document,
        int ox,
        int oy,
        byte main,
        byte accent)
    {
        // 7x6 patch. Deliberately asymmetric so rotation can be recovered unambiguously.
        var mainPoints = new[]
        {
            (0, 1), (1, 1), (2, 1), (2, 2), (2, 3),
            (3, 3), (4, 3), (5, 3), (5, 4), (5, 5),
            (6, 5),
        };
        var accentPoints = new[]
        {
            (3, 0), (4, 0), (4, 1), (6, 2),
        };

        foreach (var (x, y) in mainPoints)
            document.SetPixel(ox + x, oy + y, main);
        foreach (var (x, y) in accentPoints)
            document.SetPixel(ox + x, oy + y, accent);
    }

    private static void DrawRotatedMotif(
        DesignDocument document,
        int ox,
        int oy,
        byte main,
        byte accent)
    {
        // Clockwise rotation of DrawAsymmetricMotif's 7x6 patch -> 6x7.
        var mainPoints = new[]
        {
            (4, 0), (4, 1), (4, 2), (3, 2), (2, 2),
            (2, 3), (2, 4), (2, 5), (1, 5), (0, 5),
            (0, 6),
        };
        var accentPoints = new[]
        {
            (5, 3), (5, 4), (4, 4), (3, 6),
        };

        foreach (var (x, y) in mainPoints)
            document.SetPixel(ox + x, oy + y, main);
        foreach (var (x, y) in accentPoints)
            document.SetPixel(ox + x, oy + y, accent);
    }
}
