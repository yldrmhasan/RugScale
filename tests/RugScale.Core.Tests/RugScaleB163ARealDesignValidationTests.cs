using System.Diagnostics;
using System.Text;
using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class RugScaleB163ARealDesignValidationTests
{
    private const int TargetWidth = 768;
    private const int TargetHeight = 1150;
    private const int WarpDensity = 48;
    private const int WeftDensity = 50;

    [Fact]
    [Trait("Category", "RealDesignValidation")]
    public void B163A_MotifScan_And_160x230_Quality()
    {
        if (Environment.GetEnvironmentVariable("RUGSCALE_REAL_FIXTURE") != "1")
            return;

        var fixture = Environment.GetEnvironmentVariable("RUGSCALE_B163A_FIXTURE");
        Assert.True(!string.IsNullOrWhiteSpace(fixture) && File.Exists(fixture));

        var loaded = IndexedBmp.Load(fixture!);
        var source = loaded.Document;
        Assert.Equal(960, source.Width);
        Assert.Equal(1500, source.Height);

        var watch = Stopwatch.StartNew();
        var catalog = MotifSourceCatalog.Build(source, WarpDensity, WeftDensity);
        var catalogMs = watch.ElapsedMilliseconds;
        Assert.NotEmpty(catalog.Entries);
        Assert.All(catalog.Entries, entry =>
        {
            Assert.True(entry.Width > 0 && entry.Height > 0);
            Assert.Equal(entry.Width * entry.Height, entry.Mask.Length);
            Assert.Contains(true, entry.Mask);
            Assert.True(entry.X >= 0 && entry.Y >= 0);
            Assert.True(entry.X + entry.Width <= source.Width);
            Assert.True(entry.Y + entry.Height <= source.Height);
        });

        watch.Restart();
        var rug = DesignResizer.Scale(
            source,
            TargetWidth,
            TargetHeight,
            ScaleMode.RugScale,
            WarpDensity,
            WeftDensity,
            WarpDensity,
            WeftDensity);
        var rugMs = watch.ElapsedMilliseconds;

        watch.Restart();
        var nearest = DesignResizer.Scale(source, TargetWidth, TargetHeight, ScaleMode.NearestNeighbor);
        var nearestMs = watch.ElapsedMilliseconds;

        var sourceColors = UsedColorCounts(source);
        AssertNoNewPaletteIndex(sourceColors, rug);

        watch.Restart();
        var motifRows = ScanEveryMotif(catalog, source, rug, nearest);
        var scanMs = watch.ElapsedMilliseconds;

        watch.Restart();
        var recovery = DetectorRecovery(catalog, source, rug, perKind: 5);
        var recoveryMs = watch.ElapsedMilliseconds;

        var sourceEdge = EdgeDensity(source);
        var rugEdge = EdgeDensity(rug);
        var nearestEdge = EdgeDensity(nearest);
        var sourceLR = BestMirrorAgreement(source, true);
        var sourceTB = BestMirrorAgreement(source, false);
        var rugLR = BestMirrorAgreement(rug, true);
        var rugTB = BestMirrorAgreement(rug, false);
        var nearestLR = BestMirrorAgreement(nearest, true);
        var nearestTB = BestMirrorAgreement(nearest, false);
        var rugDrift = ColorDistributionDistance(source, rug);
        var nearestDrift = ColorDistributionDistance(source, nearest);

        var report = BuildReport(
            loaded,
            catalog,
            motifRows,
            recovery,
            catalogMs,
            rugMs,
            nearestMs,
            scanMs,
            recoveryMs,
            sourceEdge,
            rugEdge,
            nearestEdge,
            sourceLR,
            sourceTB,
            rugLR,
            rugTB,
            nearestLR,
            nearestTB,
            rugDrift,
            nearestDrift);

        Console.WriteLine(report);

        var artifactDir = Environment.GetEnvironmentVariable("RUGSCALE_B163A_ARTIFACT_DIR");
        if (!string.IsNullOrWhiteSpace(artifactDir))
        {
            Directory.CreateDirectory(artifactDir!);
            File.WriteAllText(Path.Combine(artifactDir!, "report.md"), report);
            IndexedBmp.Save(
                Path.Combine(artifactDir!, "B163A_RugScale_160x230_48x50.bmp"),
                rug,
                loaded.XPelsPerMeter,
                loaded.YPelsPerMeter);
            IndexedBmp.Save(
                Path.Combine(artifactDir!, "B163A_Nearest_160x230_48x50.bmp"),
                nearest,
                loaded.XPelsPerMeter,
                loaded.YPelsPerMeter);
        }

        var retentions = motifRows.Select(row => row.RugRetention).Order().ToArray();
        var median = Quantile(retentions, 0.50);
        var p10 = Quantile(retentions, 0.10);

        Assert.True(median >= 0.72, $"Median motif retention too low: {median:P1}");
        Assert.True(p10 >= 0.38, $"P10 motif retention too low: {p10:P1}");
        Assert.True(recovery.Top1Rate >= 0.70, $"Detector top-1 local recovery too low: {recovery.Top1Rate:P1}");
        Assert.True(recovery.Top5Rate >= 0.90, $"Detector top-5 local recovery too low: {recovery.Top5Rate:P1}");
        Assert.True(rugDrift <= 0.075, $"Palette area drift too high: {rugDrift:P2}");
        Assert.InRange(rugEdge / Math.Max(1e-9, sourceEdge), 0.78, 1.22);
        Assert.True(rugLR >= sourceLR - 0.08, $"LR symmetry regressed: {sourceLR:P2} -> {rugLR:P2}");
        Assert.True(rugTB >= sourceTB - 0.10, $"TB symmetry regressed: {sourceTB:P2} -> {rugTB:P2}");
    }

    private static IReadOnlyList<MotifRow> ScanEveryMotif(
        MotifSourceCatalog catalog,
        DesignDocument source,
        DesignDocument rug,
        DesignDocument nearest)
    {
        var rows = new List<MotifRow>(catalog.Entries.Count);

        foreach (var entry in catalog.Entries)
        {
            var active = 0;
            var rugHits = 0;
            var nearestHits = 0;
            var boundary = 0;
            var rugBoundaryHits = 0;
            var nearestBoundaryHits = 0;

            for (var y = 0; y < entry.Height; y++)
            for (var x = 0; x < entry.Width; x++)
            {
                var local = y * entry.Width + x;
                if (!entry.Mask[local])
                    continue;

                active++;
                var sx = entry.X + x;
                var sy = entry.Y + y;
                var color = source.GetPixel(sx, sy);
                var tx = Math.Clamp((int)Math.Floor((sx + 0.5) * rug.Width / source.Width), 0, rug.Width - 1);
                var ty = Math.Clamp((int)Math.Floor((sy + 0.5) * rug.Height / source.Height), 0, rug.Height - 1);

                if (HasColorNear(rug, tx, ty, color, 1))
                    rugHits++;
                if (HasColorNear(nearest, tx, ty, color, 1))
                    nearestHits++;

                if (!IsBoundary(entry.Mask, entry.Width, entry.Height, x, y))
                    continue;

                boundary++;
                if (HasColorNear(rug, tx, ty, color, 1))
                    rugBoundaryHits++;
                if (HasColorNear(nearest, tx, ty, color, 1))
                    nearestBoundaryHits++;
            }

            if (active == 0)
                continue;

            rows.Add(new MotifRow(
                entry.Kind,
                rugHits / (double)active,
                nearestHits / (double)active,
                boundary == 0 ? 1d : rugBoundaryHits / (double)boundary,
                boundary == 0 ? 1d : nearestBoundaryHits / (double)boundary));
        }

        return rows;
    }

    private static RecoverySummary DetectorRecovery(
        MotifSourceCatalog catalog,
        DesignDocument source,
        DesignDocument resized,
        int perKind)
    {
        var sample = RepresentativeEntries(catalog, perKind);
        var tested = 0;
        var top1 = 0;
        var top5 = 0;
        var none = 0;
        var details = new List<string>();

        foreach (var entry in sample)
        {
            var selection = MapSelection(entry, source, resized);
            if (selection.Width < 2 || selection.Height < 2)
                continue;

            tested++;
            var candidates = MotifRepairEngine.FindCandidates(
                source,
                resized,
                selection,
                WarpDensity,
                WeftDensity,
                WarpDensity,
                WeftDensity,
                memory: null,
                maxCandidates: 5,
                sourceCatalog: catalog);

            if (candidates.Count == 0)
            {
                none++;
                details.Add($"{entry.Kind}@{entry.X},{entry.Y}: no candidate");
                continue;
            }

            var first = IsLocal(candidates[0], entry);
            var any = candidates.Take(5).Any(candidate => IsLocal(candidate, entry));
            if (first) top1++;
            if (any) top5++;

            details.Add(
                $"{entry.Kind}@{entry.X},{entry.Y} {entry.Width}x{entry.Height} -> " +
                $"{candidates[0].Kind}@{candidates[0].SourceX},{candidates[0].SourceY} " +
                $"{candidates[0].SourceWidth}x{candidates[0].SourceHeight} " +
                $"score={candidates[0].Similarity:0.000} local={first}");
        }

        return new RecoverySummary(
            tested,
            none,
            tested == 0 ? 0 : top1 / (double)tested,
            tested == 0 ? 0 : top5 / (double)tested,
            details);
    }

    private static IReadOnlyList<MotifCatalogEntry> RepresentativeEntries(
        MotifSourceCatalog catalog,
        int perKind)
    {
        var result = new List<MotifCatalogEntry>();

        foreach (var kind in Enum.GetValues<MotifKind>())
        {
            var entries = catalog.Entries
                .Where(entry =>
                    entry.Kind == kind &&
                    entry.PixelCount >= 5 &&
                    entry.Width <= 170 &&
                    entry.Height <= 260)
                .OrderBy(entry => entry.Y)
                .ThenBy(entry => entry.X)
                .ThenBy(entry => entry.PixelCount)
                .ToArray();

            var count = Math.Min(perKind, entries.Length);
            for (var i = 0; i < count; i++)
            {
                var index = count == 1
                    ? entries.Length / 2
                    : (int)Math.Round(i * (entries.Length - 1d) / (count - 1d));
                if (entries.Length > 0 && !result.Contains(entries[index]))
                    result.Add(entries[index]);
            }
        }

        return result;
    }

    private static MotifSelection MapSelection(
        MotifCatalogEntry entry,
        DesignDocument source,
        DesignDocument target)
    {
        var left = (int)Math.Floor(entry.X * target.Width / (double)source.Width) - 1;
        var top = (int)Math.Floor(entry.Y * target.Height / (double)source.Height) - 1;
        var right = (int)Math.Ceiling((entry.X + entry.Width) * target.Width / (double)source.Width) + 1;
        var bottom = (int)Math.Ceiling((entry.Y + entry.Height) * target.Height / (double)source.Height) + 1;

        left = Math.Clamp(left, 0, target.Width - 1);
        top = Math.Clamp(top, 0, target.Height - 1);
        right = Math.Clamp(right, left + 1, target.Width);
        bottom = Math.Clamp(bottom, top + 1, target.Height);

        var width = right - left;
        var height = bottom - top;
        return new MotifSelection(
            left,
            top,
            width,
            height,
            Enumerable.Repeat(true, checked(width * height)).ToArray());
    }

    private static bool IsLocal(
        MotifRepairCandidate candidate,
        MotifCatalogEntry expected)
    {
        var dx =
            (candidate.SourceX + candidate.SourceWidth * 0.5 -
             (expected.X + expected.Width * 0.5)) /
            Math.Max(4d, expected.Width);
        var dy =
            (candidate.SourceY + candidate.SourceHeight * 0.5 -
             (expected.Y + expected.Height * 0.5)) /
            Math.Max(4d, expected.Height);
        return Math.Sqrt(dx * dx + dy * dy) <= 1.25;
    }

    private static bool HasColorNear(
        DesignDocument document,
        int x,
        int y,
        byte color,
        int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var px = x + dx;
            var py = y + dy;
            if (px >= 0 && px < document.Width &&
                py >= 0 && py < document.Height &&
                document.GetPixel(px, py) == color)
                return true;
        }

        return false;
    }

    private static bool IsBoundary(
        bool[] mask,
        int width,
        int height,
        int x,
        int y)
    {
        ReadOnlySpan<int> dx = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, -1, 1];

        for (var i = 0; i < 4; i++)
        {
            var nx = x + dx[i];
            var ny = y + dy[i];
            if (nx < 0 || nx >= width || ny < 0 || ny >= height ||
                !mask[ny * width + nx])
                return true;
        }

        return false;
    }

    private static double EdgeDensity(DesignDocument document)
    {
        long edges = 0;
        long pairs = 0;

        for (var y = 0; y < document.Height; y++)
        for (var x = 0; x < document.Width; x++)
        {
            var value = document.GetPixel(x, y);
            if (x + 1 < document.Width)
            {
                pairs++;
                if (value != document.GetPixel(x + 1, y)) edges++;
            }

            if (y + 1 < document.Height)
            {
                pairs++;
                if (value != document.GetPixel(x, y + 1)) edges++;
            }
        }

        return edges / (double)Math.Max(1L, pairs);
    }

    private static double BestMirrorAgreement(
        DesignDocument document,
        bool leftRight)
    {
        var best = 0d;

        for (var phase = -3; phase <= 3; phase++)
        {
            long compared = 0;
            long matches = 0;

            for (var y = 0; y < document.Height; y++)
            for (var x = 0; x < document.Width; x++)
            {
                var mx = leftRight ? document.Width - 1 - x + phase : x;
                var my = leftRight ? y : document.Height - 1 - y + phase;
                if (mx < 0 || mx >= document.Width || my < 0 || my >= document.Height)
                    continue;

                compared++;
                if (document.GetPixel(x, y) == document.GetPixel(mx, my))
                    matches++;
            }

            if (compared > 0)
                best = Math.Max(best, matches / (double)compared);
        }

        return best;
    }

    private static int[] UsedColorCounts(DesignDocument document)
    {
        var counts = new int[256];
        for (var y = 0; y < document.Height; y++)
        for (var x = 0; x < document.Width; x++)
            counts[document.GetPixel(x, y)]++;
        return counts;
    }

    private static void AssertNoNewPaletteIndex(
        IReadOnlyList<int> sourceCounts,
        DesignDocument target)
    {
        for (var y = 0; y < target.Height; y++)
        for (var x = 0; x < target.Width; x++)
        {
            var index = target.GetPixel(x, y);
            Assert.True(sourceCounts[index] > 0, $"New palette index {index} at {x},{y}");
        }
    }

    private static double ColorDistributionDistance(
        DesignDocument source,
        DesignDocument target)
    {
        var a = UsedColorCounts(source);
        var b = UsedColorCounts(target);
        var aTotal = (double)(source.Width * source.Height);
        var bTotal = (double)(target.Width * target.Height);
        var sum = 0d;

        for (var i = 0; i < 256; i++)
            sum += Math.Abs(a[i] / aTotal - b[i] / bTotal);

        return sum * 0.5;
    }

    private static double Quantile(
        IReadOnlyList<double> ordered,
        double q)
    {
        if (ordered.Count == 0) return 0;
        var p = Math.Clamp(q, 0, 1) * (ordered.Count - 1);
        var lo = (int)Math.Floor(p);
        var hi = (int)Math.Ceiling(p);
        if (lo == hi) return ordered[lo];
        var f = p - lo;
        return ordered[lo] * (1 - f) + ordered[hi] * f;
    }

    private static string BuildReport(
        LoadedBmp loaded,
        MotifSourceCatalog catalog,
        IReadOnlyList<MotifRow> rows,
        RecoverySummary recovery,
        long catalogMs,
        long rugMs,
        long nearestMs,
        long scanMs,
        long recoveryMs,
        double sourceEdge,
        double rugEdge,
        double nearestEdge,
        double sourceLR,
        double sourceTB,
        double rugLR,
        double rugTB,
        double nearestLR,
        double nearestTB,
        double rugDrift,
        double nearestDrift)
    {
        var ordered = rows.Select(row => row.RugRetention).Order().ToArray();
        var boundaries = rows.Select(row => row.RugBoundaryRetention).Order().ToArray();
        var builder = new StringBuilder();

        builder.AppendLine("# B163A RugScale real-design validation");
        builder.AppendLine();
        builder.AppendLine($"Source: 960x1500 indexed BMP; target 160x230 = {TargetWidth}x{TargetHeight}px at 48x50.");
        builder.AppendLine($"BMP pels/m: {loaded.XPelsPerMeter}/{loaded.YPelsPerMeter}.");
        builder.AppendLine($"Catalogue entries: {catalog.Entries.Count:N0}.");
        builder.AppendLine($"Times: catalogue {catalogMs}ms; RugScale {rugMs}ms; nearest {nearestMs}ms; all-motif scan {scanMs}ms; detector sample {recoveryMs}ms.");
        builder.AppendLine();
        builder.AppendLine("## Catalogue");
        builder.AppendLine("| Kind | Count |");
        builder.AppendLine("|---|---:|");
        foreach (var group in catalog.Entries.GroupBy(entry => entry.Kind).OrderBy(group => group.Key))
            builder.AppendLine($"| {group.Key} | {group.Count():N0} |");

        builder.AppendLine();
        builder.AppendLine("## Every motif projection scan");
        builder.AppendLine($"Mean retention: {rows.Average(row => row.RugRetention):P2}");
        builder.AppendLine($"Median retention: {Quantile(ordered, 0.50):P2}");
        builder.AppendLine($"P10 retention: {Quantile(ordered, 0.10):P2}");
        builder.AppendLine($"Median boundary retention: {Quantile(boundaries, 0.50):P2}");
        builder.AppendLine($"Motifs more than 10 percentage points worse than nearest: {rows.Count(row => row.RugRetention + 0.10 < row.NearestRetention):N0}");
        builder.AppendLine();
        builder.AppendLine("| Kind | Count | Rug mean | Nearest mean | Rug boundary | Nearest boundary |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var group in rows.GroupBy(row => row.Kind).OrderBy(group => group.Key))
            builder.AppendLine($"| {group.Key} | {group.Count():N0} | {group.Average(row => row.RugRetention):P2} | {group.Average(row => row.NearestRetention):P2} | {group.Average(row => row.RugBoundaryRetention):P2} | {group.Average(row => row.NearestBoundaryRetention):P2} |");

        builder.AppendLine();
        builder.AppendLine("## Detector recovery sample");
        builder.AppendLine($"Tested: {recovery.Tested}; no candidate: {recovery.NoCandidate}; top-1 local: {recovery.Top1Rate:P2}; top-5 local: {recovery.Top5Rate:P2}");
        foreach (var line in recovery.Details)
            builder.AppendLine("- " + line);

        builder.AppendLine();
        builder.AppendLine("## Whole-design quality");
        builder.AppendLine("| Metric | Source | RugScale | Nearest |");
        builder.AppendLine("|---|---:|---:|---:|");
        builder.AppendLine($"| Edge density | {sourceEdge:P3} | {rugEdge:P3} | {nearestEdge:P3} |");
        builder.AppendLine($"| Left/right mirror | {sourceLR:P3} | {rugLR:P3} | {nearestLR:P3} |");
        builder.AppendLine($"| Top/bottom mirror | {sourceTB:P3} | {rugTB:P3} | {nearestTB:P3} |");
        builder.AppendLine($"| Palette area TV drift | 0 | {rugDrift:P3} | {nearestDrift:P3} |");

        return builder.ToString();
    }

    private sealed record MotifRow(
        MotifKind Kind,
        double RugRetention,
        double NearestRetention,
        double RugBoundaryRetention,
        double NearestBoundaryRetention);

    private sealed record RecoverySummary(
        int Tested,
        int NoCandidate,
        double Top1Rate,
        double Top5Rate,
        IReadOnlyList<string> Details);

    private sealed record LoadedBmp(
        DesignDocument Document,
        int XPelsPerMeter,
        int YPelsPerMeter);

    private static class IndexedBmp
    {
        public static LoadedBmp Load(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (reader.ReadUInt16() != 0x4D42)
                throw new InvalidDataException("Not BMP.");

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var pixelOffset = reader.ReadUInt32();
            var dibSize = reader.ReadUInt32();
            if (dibSize < 40) throw new InvalidDataException("Unsupported DIB.");

            var width = reader.ReadInt32();
            var signedHeight = reader.ReadInt32();
            var planes = reader.ReadUInt16();
            var bpp = reader.ReadUInt16();
            var compression = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var xppm = reader.ReadInt32();
            var yppm = reader.ReadInt32();
            var colorsUsed = reader.ReadUInt32();
            _ = reader.ReadUInt32();

            if (planes != 1 || bpp != 8 || compression != 0 || width <= 0 || signedHeight == 0)
                throw new InvalidDataException($"Expected uncompressed 8-bit indexed BMP, got {width}x{signedHeight}, bpp={bpp}, compression={compression}.");

            stream.Position = 14 + dibSize;
            var paletteCount = colorsUsed == 0 ? 256 : Math.Min(256, checked((int)colorsUsed));
            var colors = new List<RugColor>(256);
            for (var i = 0; i < paletteCount; i++)
            {
                var b = reader.ReadByte();
                var g = reader.ReadByte();
                var r = reader.ReadByte();
                _ = reader.ReadByte();
                colors.Add(new RugColor(r, g, b));
            }
            while (colors.Count < 256) colors.Add(RugColor.Black);

            var height = Math.Abs(signedHeight);
            var topDown = signedHeight < 0;
            var stride = (width + 3) & ~3;
            var document = new DesignDocument(width, height, new Palette(colors));
            stream.Position = pixelOffset;
            var row = new byte[stride];

            for (var fileRow = 0; fileRow < height; fileRow++)
            {
                stream.ReadExactly(row);
                var y = topDown ? fileRow : height - 1 - fileRow;
                for (var x = 0; x < width; x++)
                    document.SetPixel(x, y, row[x]);
            }

            return new LoadedBmp(document, xppm, yppm);
        }

        public static void Save(
            string path,
            DesignDocument document,
            int xppm,
            int yppm)
        {
            var stride = (document.Width + 3) & ~3;
            var pixelBytes = checked(stride * document.Height);
            var pixelOffset = 14 + 40 + 256 * 4;
            var fileSize = pixelOffset + pixelBytes;

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0x4D42);
            writer.Write((uint)fileSize);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)pixelOffset);
            writer.Write((uint)40);
            writer.Write(document.Width);
            writer.Write(document.Height);
            writer.Write((ushort)1);
            writer.Write((ushort)8);
            writer.Write((uint)0);
            writer.Write((uint)pixelBytes);
            writer.Write(xppm);
            writer.Write(yppm);
            writer.Write((uint)256);
            writer.Write((uint)256);

            for (var i = 0; i < 256; i++)
            {
                var color = document.Palette[i];
                writer.Write(color.B);
                writer.Write(color.G);
                writer.Write(color.R);
                writer.Write((byte)0);
            }

            var row = new byte[stride];
            for (var y = document.Height - 1; y >= 0; y--)
            {
                Array.Clear(row);
                for (var x = 0; x < document.Width; x++)
                    row[x] = document.GetPixel(x, y);
                writer.Write(row);
            }
        }
    }
}
