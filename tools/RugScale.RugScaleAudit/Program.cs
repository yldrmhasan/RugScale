using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RugScale.Core.Drawing;
using RugScale.Core.Models;

namespace RugScale.RugScaleAudit;

internal static class Program
{
    private const int SourceWarp = 48;
    private const int SourceWeft = 50;
    private const int TargetWidth = 768;   // 160 cm at the source's 4.8 px/cm warp density.
    private const int TargetHeight = 1150; // 230 cm at the source's 5.0 px/cm weft density.

    private static int Main(string[] args)
    {
        var input = GetArg(args, "--input")
            ?? throw new ArgumentException("--input <indexed-bmp> is required.");
        var outputDir = GetArg(args, "--output")
            ?? Path.Combine(Environment.CurrentDirectory, "rugscale-audit");

        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"[audit] loading {input}");
        var sourceBmp = IndexedBmp.Read(input);
        var source = sourceBmp.Document;

        Console.WriteLine(
            $"[audit] source={source.Width}x{source.Height}, target={TargetWidth}x{TargetHeight}, " +
            $"quality={SourceWarp}x{SourceWeft}, xppm={sourceBmp.XPixelsPerMeter}, yppm={sourceBmp.YPixelsPerMeter}");

        var usedSource = UsedIndices(source);
        Console.WriteLine($"[audit] used palette indexes: {string.Join(",", usedSource.Order())}");

        var resizeWatch = Stopwatch.StartNew();
        var rugScale = DesignResizer.Scale(
            source,
            TargetWidth,
            TargetHeight,
            ScaleMode.RugScale,
            SourceWarp,
            SourceWeft,
            SourceWarp,
            SourceWeft);
        resizeWatch.Stop();

        var nearest = DesignResizer.Scale(
            source,
            TargetWidth,
            TargetHeight,
            ScaleMode.NearestNeighbor);

        IndexedBmp.Write(
            Path.Combine(outputDir, "B163A_RugScale_160x230.bmp"),
            rugScale,
            sourceBmp.XPixelsPerMeter,
            sourceBmp.YPixelsPerMeter);
        IndexedBmp.Write(
            Path.Combine(outputDir, "B163A_Nearest_160x230.bmp"),
            nearest,
            sourceBmp.XPixelsPerMeter,
            sourceBmp.YPixelsPerMeter);

        Console.WriteLine($"[audit] RugScale resize: {resizeWatch.Elapsed.TotalSeconds:0.000}s");

        var catalogWatch = Stopwatch.StartNew();
        var catalog = MotifSourceCatalog.Build(
            source,
            SourceWarp,
            SourceWeft);
        catalogWatch.Stop();

        Console.WriteLine(
            $"[audit] source catalogue: {catalog.Entries.Count:N0} entries in " +
            $"{catalogWatch.Elapsed.TotalSeconds:0.000}s");

        var auditWatch = Stopwatch.StartNew();
        var rows = AuditEveryCatalogueEntry(
            source,
            rugScale,
            nearest,
            catalog);
        auditWatch.Stop();

        Console.WriteLine(
            $"[audit] motif preservation rows: {rows.Count:N0} in " +
            $"{auditWatch.Elapsed.TotalSeconds:0.000}s");

        // Full motif-by-motif preservation is cheap because descriptors are already catalogued.
        // Interactive detector ranking is much more expensive (each selection compares against the
        // whole source catalogue), so exercise a deterministic set of worst/large/kind-diverse
        // occurrences rather than multiplying a 50k catalogue by itself.
        var detectorWatch = Stopwatch.StartNew();
        var detectorRows = AuditDetectorSamples(
            source,
            rugScale,
            catalog,
            rows,
            maxSamples: 16);
        detectorWatch.Stop();

        Console.WriteLine(
            $"[audit] detector samples: {detectorRows.Count} in " +
            $"{detectorWatch.Elapsed.TotalSeconds:0.000}s");

        var nearestDifference = PixelDifferenceRatio(rugScale, nearest);
        var sourceLr = BestMirrorAgreement(source, leftRight: true);
        var sourceTb = BestMirrorAgreement(source, leftRight: false);
        var rugLr = BestMirrorAgreement(rugScale, leftRight: true);
        var rugTb = BestMirrorAgreement(rugScale, leftRight: false);
        var nearestLr = BestMirrorAgreement(nearest, leftRight: true);
        var nearestTb = BestMirrorAgreement(nearest, leftRight: false);

        var sourceEdge = EdgeDensity(source);
        var rugEdge = EdgeDensity(rugScale);
        var nearestEdge = EdgeDensity(nearest);

        var paletteSafe = UsedIndices(rugScale).All(usedSource.Contains);

        var componentRows = AuditComponents(
            source,
            rugScale,
            nearest,
            usedSource);

        DrawingTrainingReport? drawingTraining = null;
        if (HasFlag(args, "--train-drawing"))
        {
            var trainingWatch = Stopwatch.StartNew();
            drawingTraining = TrainDrawingStyles(
                source,
                rugScale,
                catalog,
                rows,
                maxSamplesPerKind: 60);
            trainingWatch.Stop();

            File.WriteAllText(
                Path.Combine(outputDir, "trained-memory.json"),
                MotifMemorySerializer.Save(
                    drawingTraining.Memory));
            WriteDrawingTrainingCsv(
                Path.Combine(outputDir, "drawing-training.csv"),
                drawingTraining.Rows);
            File.WriteAllText(
                Path.Combine(outputDir, "drawing-training.md"),
                BuildDrawingTrainingMarkdown(
                    drawingTraining,
                    trainingWatch.Elapsed.TotalSeconds));

            Console.WriteLine(
                $"[training] samples={drawingTraining.Rows.Count:N0}, " +
                $"families={drawingTraining.Memory.Families.Count:N0}, " +
                $"time={trainingWatch.Elapsed.TotalSeconds:0.000}s");
        }

        var report = BuildReport(
            rows,
            detectorRows,
            componentRows,
            resizeWatch.Elapsed.TotalSeconds,
            catalogWatch.Elapsed.TotalSeconds,
            auditWatch.Elapsed.TotalSeconds,
            detectorWatch.Elapsed.TotalSeconds,
            nearestDifference,
            paletteSafe,
            sourceLr,
            sourceTb,
            rugLr,
            rugTb,
            nearestLr,
            nearestTb,
            sourceEdge,
            rugEdge,
            nearestEdge);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        File.WriteAllText(
            Path.Combine(outputDir, "report.json"),
            JsonSerializer.Serialize(report, jsonOptions));
        WriteMotifCsv(
            Path.Combine(outputDir, "motifs.csv"),
            rows);
        WriteDetectorCsv(
            Path.Combine(outputDir, "detector.csv"),
            detectorRows);

        var markdown = BuildMarkdown(report);
        File.WriteAllText(
            Path.Combine(outputDir, "report.md"),
            markdown);

        Console.WriteLine(markdown);

        // This first real-raster audit is diagnostic, not a brittle golden-image gate. It fails
        // only on hard invariants; quality thresholds are printed so engine changes can be compared
        // objectively from run to run.
        if (!paletteSafe)
        {
            Console.Error.WriteLine("[audit] FAIL: RugScale introduced a palette index absent from source.");
            return 2;
        }

        if (rugScale.Width != TargetWidth ||
            rugScale.Height != TargetHeight)
        {
            Console.Error.WriteLine("[audit] FAIL: incorrect output dimensions.");
            return 3;
        }

        return 0;
    }

    private static bool HasFlag(
        IReadOnlyList<string> args,
        string flag) =>
        args.Any(value =>
            string.Equals(
                value,
                flag,
                StringComparison.OrdinalIgnoreCase));

    private static DrawingTrainingReport TrainDrawingStyles(
        DesignDocument source,
        DesignDocument resized,
        MotifSourceCatalog catalog,
        IReadOnlyList<MotifAuditRow> auditRows,
        int maxSamplesPerKind)
    {
        var styles = Enum.GetValues<MotifRepairStyle>();
        var rows = new List<DrawingTrainingRow>();
        var memory = new MotifMemoryFile();

        // Training is self-supervised: original source geometry is the teacher. We deliberately
        // oversample weak and median motifs instead of only easy examples, and keep each motif kind
        // spatially distributed across the carpet so style learning does not overfit one corner.
        foreach (var kindGroup in auditRows
                     .Where(row =>
                         row.Area >= 6 &&
                         row.TargetWidth >= 2 &&
                         row.TargetHeight >= 2)
                     .GroupBy(row => row.Kind)
                     .OrderBy(group => group.Key))
        {
            var ordered = kindGroup
                .OrderBy(row => row.RugScore)
                .ThenBy(row => row.SourceY)
                .ThenBy(row => row.SourceX)
                .ToArray();

            var selected = SelectTrainingRows(
                ordered,
                maxSamplesPerKind);

            foreach (var audit in selected)
            {
                var entry = catalog.Entries[
                    audit.CatalogIndex];
                var projected = ProjectEntry(
                    source,
                    resized,
                    entry);

                if (projected.MaskCount < 2)
                    continue;

                var targetSelection = new MotifSelection(
                    projected.X,
                    projected.Y,
                    projected.Width,
                    projected.Height,
                    projected.Mask.ToArray());

                var identityMap = Enumerable
                    .Range(0, 256)
                    .Select(static value => (byte)value)
                    .ToArray();

                var seedCandidate = new MotifRepairCandidate(
                    entry.X,
                    entry.Y,
                    entry.Width,
                    entry.Height,
                    1d,
                    MotifTransform.Identity,
                    null,
                    entry.Kind,
                    MotifRepairStyle.Balanced,
                    identityMap,
                    entry.Mask.ToArray(),
                    new bool[
                        projected.Width *
                        projected.Height],
                    new byte[
                        projected.Width *
                        projected.Height]);

                var family = MotifMemorySerializer.LearnPositive(
                    memory,
                    entry.Descriptor,
                    sourceTag:
                        $"B163A:{entry.Kind}:{entry.X},{entry.Y}:{entry.Width}x{entry.Height}",
                    sourceWidth: entry.Width,
                    sourceHeight: entry.Height,
                    warpDensity: SourceWarp,
                    weftDensity: SourceWeft);

                var styleScores =
                    new Dictionary<MotifRepairStyle, double>();

                foreach (var style in styles)
                {
                    var rendered =
                        MotifRepairEngine.RetargetCandidate(
                            source,
                            targetSelection,
                            seedCandidate,
                            style);
                    styleScores[style] =
                        ScoreDrawing(
                            projected,
                            rendered);
                }

                var bestScore =
                    styleScores.Values.Max();
                var bestStyle =
                    styleScores
                        .OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => pair.Key)
                        .First()
                        .Key;

                foreach (var style in styles)
                {
                    var score =
                        styleScores[style];

                    if (score >= bestScore - 0.015)
                    {
                        MotifMemorySerializer.LearnRepairStyle(
                            memory,
                            family.Id,
                            style,
                            success: true);
                    }
                    else if (score <= bestScore - 0.060)
                    {
                        MotifMemorySerializer.LearnRepairStyle(
                            memory,
                            family.Id,
                            style,
                            success: false);
                    }
                }

                rows.Add(new DrawingTrainingRow(
                    audit.CatalogIndex,
                    audit.Kind,
                    audit.SourceX,
                    audit.SourceY,
                    audit.SourceWidth,
                    audit.SourceHeight,
                    audit.TargetWidth,
                    audit.TargetHeight,
                    audit.RugScore,
                    bestStyle,
                    bestScore,
                    styleScores[MotifRepairStyle.Balanced],
                    styleScores[MotifRepairStyle.PreserveBranches],
                    styleScores[MotifRepairStyle.ContourFirst],
                    styleScores[MotifRepairStyle.ConnectivityFirst],
                    styleScores[MotifRepairStyle.DetailAndConnectivity]));
            }
        }

        return new DrawingTrainingReport(
            memory,
            rows);
    }

    private static IReadOnlyList<MotifAuditRow> SelectTrainingRows(
        IReadOnlyList<MotifAuditRow> ordered,
        int maxSamples)
    {
        if (ordered.Count <= maxSamples)
            return ordered.ToArray();

        var result = new List<MotifAuditRow>(maxSamples);
        var seen = new HashSet<int>();

        void AddAt(int index)
        {
            index = Math.Clamp(
                index,
                0,
                ordered.Count - 1);
            var row = ordered[index];
            if (seen.Add(row.CatalogIndex))
                result.Add(row);
        }

        // Half hard examples, half distribution-spanning examples.
        var hardCount = maxSamples / 2;
        for (var i = 0;
             i < hardCount;
             i++)
        {
            AddAt(i);
        }

        var remaining =
            maxSamples - result.Count;
        for (var i = 0;
             i < remaining;
             i++)
        {
            var index =
                remaining <= 1
                    ? ordered.Count / 2
                    : (int)Math.Round(
                        i *
                        (ordered.Count - 1d) /
                        (remaining - 1d));
            AddAt(index);
        }

        for (var i = 0;
             result.Count < maxSamples &&
             i < ordered.Count;
             i++)
        {
            AddAt(i);
        }

        return result;
    }

    private static double ScoreDrawing(
        ProjectedMotif expected,
        MotifRepairCandidate rendered)
    {
        var count =
            expected.Width *
            expected.Height;
        var intersection = 0;
        var union = 0;
        var expectedCount = 0;
        var renderedCount = 0;
        var exactColor = 0;
        var exactColorTotal = 0;
        var boundaryExpected = 0;
        var boundaryRecovered = 0;

        for (var local = 0;
             local < count;
             local++)
        {
            var e =
                local < expected.Mask.Length &&
                expected.Mask[local];
            var r =
                local < rendered.TargetMask.Length &&
                rendered.TargetMask[local];

            if (e) expectedCount++;
            if (r) renderedCount++;
            if (e && r) intersection++;
            if (e || r) union++;

            if (e)
            {
                exactColorTotal++;
                if (r &&
                    local < rendered.TargetPixels.Length &&
                    rendered.TargetPixels[local] ==
                    expected.ExpectedColors[local])
                {
                    exactColor++;
                }
            }

            if (!e)
                continue;

            var x =
                local % expected.Width;
            var y =
                local / expected.Width;
            if (!ProjectedBoundary(
                    expected.Mask,
                    expected.Width,
                    expected.Height,
                    x,
                    y))
            {
                continue;
            }

            boundaryExpected++;
            if (r)
                boundaryRecovered++;
        }

        var precision =
            renderedCount == 0
                ? 0d
                : intersection /
                  (double)renderedCount;
        var recall =
            expectedCount == 0
                ? 0d
                : intersection /
                  (double)expectedCount;
        var f1 =
            precision + recall <= 0
                ? 0d
                : 2d *
                  precision *
                  recall /
                  (precision + recall);
        var iou =
            union == 0
                ? 1d
                : intersection /
                  (double)union;
        var color =
            exactColorTotal == 0
                ? 1d
                : exactColor /
                  (double)exactColorTotal;
        var boundary =
            boundaryExpected == 0
                ? 1d
                : boundaryRecovered /
                  (double)boundaryExpected;

        return Math.Clamp(
            f1 * 0.38 +
            iou * 0.22 +
            color * 0.28 +
            boundary * 0.12,
            0d,
            1d);
    }

    private static bool ProjectedBoundary(
        bool[] mask,
        int width,
        int height,
        int x,
        int y)
    {
        ReadOnlySpan<int> dx = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, -1, 1];

        for (var i = 0;
             i < 4;
             i++)
        {
            var nx = x + dx[i];
            var ny = y + dy[i];

            if (nx < 0 ||
                nx >= width ||
                ny < 0 ||
                ny >= height ||
                !mask[ny * width + nx])
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteDrawingTrainingCsv(
        string path,
        IReadOnlyList<DrawingTrainingRow> rows)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "catalogIndex,kind,sourceX,sourceY,sourceWidth,sourceHeight,targetWidth,targetHeight," +
            "preTrainingRugScore,bestStyle,bestScore,balanced,preserveBranches,contourFirst," +
            "connectivityFirst,detailAndConnectivity");

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(
                ",",
                row.CatalogIndex,
                row.Kind,
                row.SourceX,
                row.SourceY,
                row.SourceWidth,
                row.SourceHeight,
                row.TargetWidth,
                row.TargetHeight,
                F(row.PreTrainingRugScore),
                row.BestStyle,
                F(row.BestScore),
                F(row.Balanced),
                F(row.PreserveBranches),
                F(row.ContourFirst),
                F(row.ConnectivityFirst),
                F(row.DetailAndConnectivity)));
        }
    }

    private static string BuildDrawingTrainingMarkdown(
        DrawingTrainingReport training,
        double seconds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RugScale drawing-style self-training");
        sb.AppendLine();
        sb.AppendLine(
            "Original B163A source motif geometry is the teacher. Every sampled motif is redrawn " +
            "from the immutable original source with all repair styles; the best reconstruction " +
            "teaches portable motif-memory style preferences.");
        sb.AppendLine();
        sb.AppendLine($"Samples: **{training.Rows.Count:N0}**");
        sb.AppendLine($"Learned families: **{training.Memory.Families.Count:N0}**");
        sb.AppendLine($"Training time: **{seconds:0.000}s**");
        sb.AppendLine();
        sb.AppendLine("| Kind | Samples | Mean best | Balanced wins | Branch wins | Contour wins | Connectivity wins | Detail wins |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var group in training.Rows
                     .GroupBy(row => row.Kind)
                     .OrderBy(group => group.Key))
        {
            sb.AppendLine(
                $"| {group.Key} | {group.Count():N0} | {group.Average(row => row.BestScore):P2} | " +
                $"{group.Count(row => row.BestStyle == MotifRepairStyle.Balanced)} | " +
                $"{group.Count(row => row.BestStyle == MotifRepairStyle.PreserveBranches)} | " +
                $"{group.Count(row => row.BestStyle == MotifRepairStyle.ContourFirst)} | " +
                $"{group.Count(row => row.BestStyle == MotifRepairStyle.ConnectivityFirst)} | " +
                $"{group.Count(row => row.BestStyle == MotifRepairStyle.DetailAndConnectivity)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Overall style score");
        sb.AppendLine();
        sb.AppendLine("| Style | Mean | Wins |");
        sb.AppendLine("|---|---:|---:|");

        foreach (var style in Enum.GetValues<MotifRepairStyle>())
        {
            var values = training.Rows
                .Select(row => row.ScoreFor(style))
                .ToArray();
            sb.AppendLine(
                $"| {style} | {values.Average():P2} | " +
                $"{training.Rows.Count(row => row.BestStyle == style)} |");
        }

        return sb.ToString();
    }

    private sealed record DrawingTrainingRow(
        int CatalogIndex,
        string Kind,
        int SourceX,
        int SourceY,
        int SourceWidth,
        int SourceHeight,
        int TargetWidth,
        int TargetHeight,
        double PreTrainingRugScore,
        MotifRepairStyle BestStyle,
        double BestScore,
        double Balanced,
        double PreserveBranches,
        double ContourFirst,
        double ConnectivityFirst,
        double DetailAndConnectivity)
    {
        public double ScoreFor(
            MotifRepairStyle style) =>
            style switch
            {
                MotifRepairStyle.Balanced => Balanced,
                MotifRepairStyle.PreserveBranches => PreserveBranches,
                MotifRepairStyle.ContourFirst => ContourFirst,
                MotifRepairStyle.ConnectivityFirst => ConnectivityFirst,
                MotifRepairStyle.DetailAndConnectivity => DetailAndConnectivity,
                _ => 0d,
            };
    }

    private sealed record DrawingTrainingReport(
        MotifMemoryFile Memory,
        IReadOnlyList<DrawingTrainingRow> Rows);

    private static List<MotifAuditRow> AuditEveryCatalogueEntry(
        DesignDocument source,
        DesignDocument rugScale,
        DesignDocument nearest,
        MotifSourceCatalog catalog)
    {
        var rows = new List<MotifAuditRow>(catalog.Entries.Count);

        for (var index = 0; index < catalog.Entries.Count; index++)
        {
            var entry = catalog.Entries[index];
            var projected = ProjectEntry(
                source,
                rugScale,
                entry);

            if (projected.MaskCount < 2)
                continue;

            var rugDescriptor = MotifDescriptor.FromPatch(
                rugScale,
                projected.X,
                projected.Y,
                projected.Width,
                projected.Height,
                projected.Mask,
                SourceWarp,
                SourceWeft);
            var nearestDescriptor = MotifDescriptor.FromPatch(
                nearest,
                projected.X,
                projected.Y,
                projected.Width,
                projected.Height,
                projected.Mask,
                SourceWarp,
                SourceWeft);

            var rugFamily = MotifDescriptor.Similarity(
                entry.Descriptor,
                rugDescriptor);
            MotifDescriptor.FindBestRelativeTransform(
                entry.Descriptor,
                rugDescriptor,
                out var rugAlignment);

            var nearestFamily = MotifDescriptor.Similarity(
                entry.Descriptor,
                nearestDescriptor);
            MotifDescriptor.FindBestRelativeTransform(
                entry.Descriptor,
                nearestDescriptor,
                out var nearestAlignment);

            var rugIndex = IndexAgreement(
                rugScale,
                projected);
            var nearestIndex = IndexAgreement(
                nearest,
                projected);

            var rugRole = RoleRetention(
                entry.Descriptor.RoleCount,
                rugDescriptor.RoleCount);
            var nearestRole = RoleRetention(
                entry.Descriptor.RoleCount,
                nearestDescriptor.RoleCount);

            var rugScore =
                rugAlignment * 0.55 +
                rugIndex * 0.30 +
                rugRole * 0.15;
            var nearestScore =
                nearestAlignment * 0.55 +
                nearestIndex * 0.30 +
                nearestRole * 0.15;

            rows.Add(new MotifAuditRow(
                index,
                entry.Kind.ToString(),
                entry.X,
                entry.Y,
                entry.Width,
                entry.Height,
                entry.PixelCount,
                projected.X,
                projected.Y,
                projected.Width,
                projected.Height,
                entry.Descriptor.RoleCount,
                rugDescriptor.RoleCount,
                rugFamily,
                rugAlignment,
                rugIndex,
                rugRole,
                rugScore,
                nearestFamily,
                nearestAlignment,
                nearestIndex,
                nearestRole,
                nearestScore,
                rugScore - nearestScore));
        }

        return rows;
    }

    private static List<DetectorAuditRow> AuditDetectorSamples(
        DesignDocument source,
        DesignDocument resized,
        MotifSourceCatalog catalog,
        IReadOnlyList<MotifAuditRow> rows,
        int maxSamples)
    {
        var selected = new List<MotifAuditRow>();
        var seen = new HashSet<int>();

        void AddRows(IEnumerable<MotifAuditRow> candidates)
        {
            foreach (var row in candidates)
            {
                if (selected.Count >= maxSamples)
                    break;
                if (seen.Add(row.CatalogIndex))
                    selected.Add(row);
            }
        }

        // Start with genuine failures/weakest occurrences.
        AddRows(rows
            .Where(row => row.Area >= 8)
            .OrderBy(row => row.RugScore)
            .Take(6));

        // Force representation from the classes the interactive UI exposes.
        foreach (var kind in Enum.GetNames<MotifKind>())
        {
            AddRows(rows
                .Where(row => row.Kind == kind && row.Area >= 8)
                .OrderByDescending(row => row.Area)
                .Take(2));
        }

        // Spatially distributed large motifs make sure the inverse-location prior is tested away
        // from only one corner/medallion.
        AddRows(rows
            .Where(row => row.Area >= 20)
            .OrderBy(row =>
                ((row.SourceX * 73856093L) ^
                 (row.SourceY * 19349663L) ^
                 row.CatalogIndex) & 0x7fffffff)
            .Take(maxSamples));

        var result = new List<DetectorAuditRow>();

        foreach (var row in selected.Take(maxSamples))
        {
            var selection = new MotifSelection(
                row.TargetX,
                row.TargetY,
                row.TargetWidth,
                row.TargetHeight,
                Enumerable.Repeat(
                    true,
                    row.TargetWidth * row.TargetHeight)
                    .ToArray());

            var candidates = MotifRepairEngine.FindCandidates(
                source,
                resized,
                selection,
                SourceWarp,
                SourceWeft,
                SourceWarp,
                SourceWeft,
                memory: null,
                maxCandidates: 5,
                sourceCatalog: catalog);

            var expected = catalog.Entries[row.CatalogIndex];
            var top = candidates.FirstOrDefault();

            var correct = top is not null &&
                IsLocationMatch(expected, top);

            var expectedRank = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!IsLocationMatch(expected, candidates[i]))
                    continue;
                expectedRank = i + 1;
                break;
            }

            result.Add(new DetectorAuditRow(
                row.CatalogIndex,
                row.Kind,
                row.SourceX,
                row.SourceY,
                row.SourceWidth,
                row.SourceHeight,
                row.TargetX,
                row.TargetY,
                row.TargetWidth,
                row.TargetHeight,
                candidates.Count,
                correct,
                expectedRank,
                top?.Kind.ToString() ?? "",
                top?.SourceX ?? -1,
                top?.SourceY ?? -1,
                top?.SourceWidth ?? 0,
                top?.SourceHeight ?? 0,
                top?.Similarity ?? 0d));
        }

        return result;
    }

    private static bool IsLocationMatch(
        MotifCatalogEntry expected,
        MotifRepairCandidate candidate)
    {
        var expectedCenterX =
            expected.X + expected.Width * 0.5;
        var expectedCenterY =
            expected.Y + expected.Height * 0.5;
        var candidateCenterX =
            candidate.SourceX + candidate.SourceWidth * 0.5;
        var candidateCenterY =
            candidate.SourceY + candidate.SourceHeight * 0.5;

        var distance = Math.Sqrt(
            Math.Pow(candidateCenterX - expectedCenterX, 2) +
            Math.Pow(candidateCenterY - expectedCenterY, 2));
        var tolerance = Math.Max(
            4d,
            Math.Max(expected.Width, expected.Height) * 0.80);

        if (distance <= tolerance)
            return true;

        var ix0 = Math.Max(expected.X, candidate.SourceX);
        var iy0 = Math.Max(expected.Y, candidate.SourceY);
        var ix1 = Math.Min(
            expected.X + expected.Width,
            candidate.SourceX + candidate.SourceWidth);
        var iy1 = Math.Min(
            expected.Y + expected.Height,
            candidate.SourceY + candidate.SourceHeight);

        if (ix1 <= ix0 || iy1 <= iy0)
            return false;

        var intersection =
            (ix1 - ix0) * (iy1 - iy0);
        var union =
            expected.Width * expected.Height +
            candidate.SourceWidth * candidate.SourceHeight -
            intersection;

        return intersection /
            (double)Math.Max(1, union) >= 0.15;
    }

    private static ProjectedMotif ProjectEntry(
        DesignDocument source,
        DesignDocument target,
        MotifCatalogEntry entry)
    {
        var scaleX =
            target.Width /
            (double)source.Width;
        var scaleY =
            target.Height /
            (double)source.Height;

        var x0 = Math.Clamp(
            (int)Math.Floor(entry.X * scaleX),
            0,
            target.Width - 1);
        var y0 = Math.Clamp(
            (int)Math.Floor(entry.Y * scaleY),
            0,
            target.Height - 1);
        var x1 = Math.Clamp(
            (int)Math.Ceiling(
                (entry.X + entry.Width) * scaleX),
            x0 + 1,
            target.Width);
        var y1 = Math.Clamp(
            (int)Math.Ceiling(
                (entry.Y + entry.Height) * scaleY),
            y0 + 1,
            target.Height);

        var width = x1 - x0;
        var height = y1 - y0;
        var mask = new bool[width * height];
        var expected = new byte[width * height];

        var votes =
            new Dictionary<int, int>();

        for (var sy = 0; sy < entry.Height; sy++)
        {
            for (var sx = 0; sx < entry.Width; sx++)
            {
                var sourceLocal =
                    sy * entry.Width + sx;

                if (sourceLocal >= entry.Mask.Length ||
                    !entry.Mask[sourceLocal])
                {
                    continue;
                }

                var sourceX = entry.X + sx;
                var sourceY = entry.Y + sy;

                var targetX = Math.Clamp(
                    (int)Math.Floor(
                        (sourceX + 0.5) * scaleX),
                    x0,
                    x1 - 1);
                var targetY = Math.Clamp(
                    (int)Math.Floor(
                        (sourceY + 0.5) * scaleY),
                    y0,
                    y1 - 1);
                var local =
                    (targetY - y0) * width +
                    (targetX - x0);

                mask[local] = true;

                var color =
                    source.GetPixel(
                        sourceX,
                        sourceY);
                var voteKey =
                    (local << 8) |
                    color;
                votes.TryGetValue(
                    voteKey,
                    out var count);
                votes[voteKey] =
                    count + 1;
            }
        }

        var bestCounts = new int[mask.Length];
        foreach (var pair in votes)
        {
            var local =
                pair.Key >> 8;
            var color =
                (byte)(pair.Key & 0xff);

            if (pair.Value <= bestCounts[local])
                continue;

            bestCounts[local] =
                pair.Value;
            expected[local] =
                color;
        }

        return new ProjectedMotif(
            x0,
            y0,
            width,
            height,
            mask,
            expected,
            mask.Count(static value => value));
    }

    private static double IndexAgreement(
        DesignDocument target,
        ProjectedMotif projected)
    {
        var matches = 0;
        var total = 0;

        for (var y = 0; y < projected.Height; y++)
        {
            for (var x = 0; x < projected.Width; x++)
            {
                var local =
                    y * projected.Width + x;

                if (!projected.Mask[local])
                    continue;

                total++;
                if (target.GetPixel(
                        projected.X + x,
                        projected.Y + y) ==
                    projected.ExpectedColors[local])
                {
                    matches++;
                }
            }
        }

        return total == 0
            ? 0d
            : matches / (double)total;
    }

    private static double RoleRetention(
        int sourceRoles,
        int targetRoles)
    {
        sourceRoles = Math.Max(1, sourceRoles);
        targetRoles = Math.Max(1, targetRoles);

        return Math.Min(sourceRoles, targetRoles) /
            (double)Math.Max(sourceRoles, targetRoles);
    }

    private static AuditReport BuildReport(
        IReadOnlyList<MotifAuditRow> rows,
        IReadOnlyList<DetectorAuditRow> detectorRows,
        IReadOnlyList<ComponentAuditRow> componentRows,
        double resizeSeconds,
        double catalogSeconds,
        double auditSeconds,
        double detectorSeconds,
        double nearestDifference,
        bool paletteSafe,
        double sourceLr,
        double sourceTb,
        double rugLr,
        double rugTb,
        double nearestLr,
        double nearestTb,
        double sourceEdge,
        double rugEdge,
        double nearestEdge)
    {
        var kindSummaries = rows
            .GroupBy(row => row.Kind)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var values = group
                    .Select(row => row.RugScore)
                    .Order()
                    .ToArray();
                var deltas = group
                    .Select(row => row.DeltaVsNearest)
                    .Order()
                    .ToArray();

                return new KindSummary(
                    group.Key,
                    group.Count(),
                    values.Average(),
                    Percentile(values, 0.10),
                    Percentile(values, 0.50),
                    Percentile(values, 0.90),
                    deltas.Average(),
                    group.Count(row =>
                        row.RugScore < 0.60),
                    group.Count(row =>
                        row.RugScore < 0.75));
            })
            .ToArray();

        var scores = rows
            .Select(row => row.RugScore)
            .Order()
            .ToArray();
        var deltasAll = rows
            .Select(row => row.DeltaVsNearest)
            .Order()
            .ToArray();

        return new AuditReport(
            SourceWidth: 960,
            SourceHeight: 1500,
            TargetWidth,
            TargetHeight,
            SourceWarp,
            SourceWeft,
            ResizeSeconds: resizeSeconds,
            CatalogSeconds: catalogSeconds,
            MotifAuditSeconds: auditSeconds,
            DetectorAuditSeconds: detectorSeconds,
            MotifCount: rows.Count,
            MotifMean: scores.Length == 0 ? 0 : scores.Average(),
            MotifP10: Percentile(scores, 0.10),
            MotifMedian: Percentile(scores, 0.50),
            MotifP90: Percentile(scores, 0.90),
            MeanDeltaVsNearest: deltasAll.Length == 0
                ? 0
                : deltasAll.Average(),
            BetterThanNearestCount: rows.Count(row =>
                row.DeltaVsNearest > 0.01),
            WorseThanNearestCount: rows.Count(row =>
                row.DeltaVsNearest < -0.01),
            SevereMotifCount: rows.Count(row =>
                row.RugScore < 0.60),
            WeakMotifCount: rows.Count(row =>
                row.RugScore < 0.75),
            DetectorSampleCount: detectorRows.Count,
            DetectorTop1Correct: detectorRows.Count(row =>
                row.Top1Correct),
            DetectorTop3Found: detectorRows.Count(row =>
                row.ExpectedRank is > 0 and <= 3),
            PixelDifferenceFromNearest: nearestDifference,
            PaletteSafe: paletteSafe,
            SourceLeftRightSymmetry: sourceLr,
            SourceTopBottomSymmetry: sourceTb,
            RugScaleLeftRightSymmetry: rugLr,
            RugScaleTopBottomSymmetry: rugTb,
            NearestLeftRightSymmetry: nearestLr,
            NearestTopBottomSymmetry: nearestTb,
            SourceEdgeDensity: sourceEdge,
            RugScaleEdgeDensity: rugEdge,
            NearestEdgeDensity: nearestEdge,
            KindSummaries: kindSummaries,
            Components: componentRows.ToArray(),
            WorstMotifs: rows
                .OrderBy(row => row.RugScore)
                .Take(40)
                .ToArray(),
            DetectorRows: detectorRows.ToArray());
    }

    private static string BuildMarkdown(
        AuditReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# RugScale B163A real-raster audit");
        sb.AppendLine();
        sb.AppendLine(
            $"Source **{report.SourceWidth}×{report.SourceHeight}** → " +
            $"same-quality 160×230 target **{report.TargetWidth}×{report.TargetHeight}** " +
            $"(quality {report.SourceWarp}×{report.SourceWeft}).");
        sb.AppendLine();
        sb.AppendLine(
            "> This is a structural self-consistency audit because there is no human-drawn " +
            "160×230 ground-truth bitmap in this run. Every catalogue motif is projected to its " +
            "expected target position and compared there; Nearest is included as a baseline.");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Result |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Motifs audited | {report.MotifCount:N0} |");
        sb.AppendLine($"| Motif mean structural score | {report.MotifMean:P2} |");
        sb.AppendLine($"| Motif P10 / median / P90 | {report.MotifP10:P2} / {report.MotifMedian:P2} / {report.MotifP90:P2} |");
        sb.AppendLine($"| Mean delta vs Nearest | {report.MeanDeltaVsNearest:+0.0000;-0.0000;0.0000} |");
        sb.AppendLine($"| Better than Nearest (>1 pt) | {report.BetterThanNearestCount:N0} |");
        sb.AppendLine($"| Worse than Nearest (>1 pt) | {report.WorseThanNearestCount:N0} |");
        sb.AppendLine($"| Weak motifs (<75%) | {report.WeakMotifCount:N0} |");
        sb.AppendLine($"| Severe motifs (<60%) | {report.SevereMotifCount:N0} |");
        sb.AppendLine($"| Detector top-1 local-source accuracy | {report.DetectorTop1Correct}/{report.DetectorSampleCount} |");
        sb.AppendLine($"| Detector expected source in top-3 | {report.DetectorTop3Found}/{report.DetectorSampleCount} |");
        sb.AppendLine($"| RugScale vs Nearest pixel difference | {report.PixelDifferenceFromNearest:P2} |");
        sb.AppendLine($"| Palette safe | {(report.PaletteSafe ? "YES" : "NO")} |");
        sb.AppendLine($"| Resize time | {report.ResizeSeconds:0.000}s |");
        sb.AppendLine();

        sb.AppendLine("## Symmetry / edge density");
        sb.AppendLine();
        sb.AppendLine("| Metric | Source | RugScale | Nearest |");
        sb.AppendLine("|---|---:|---:|---:|");
        sb.AppendLine($"| Left-right best phase agreement | {report.SourceLeftRightSymmetry:P2} | {report.RugScaleLeftRightSymmetry:P2} | {report.NearestLeftRightSymmetry:P2} |");
        sb.AppendLine($"| Top-bottom best phase agreement | {report.SourceTopBottomSymmetry:P2} | {report.RugScaleTopBottomSymmetry:P2} | {report.NearestTopBottomSymmetry:P2} |");
        sb.AppendLine($"| Edge density | {report.SourceEdgeDensity:P2} | {report.RugScaleEdgeDensity:P2} | {report.NearestEdgeDensity:P2} |");
        sb.AppendLine();

        sb.AppendLine("## Motif kinds");
        sb.AppendLine();
        sb.AppendLine("| Kind | Count | Mean | P10 | Median | P90 | Δ vs NN | <60% | <75% |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var kind in report.KindSummaries)
        {
            sb.AppendLine(
                $"| {kind.Kind} | {kind.Count:N0} | {kind.Mean:P2} | {kind.P10:P2} | " +
                $"{kind.Median:P2} | {kind.P90:P2} | {kind.MeanDeltaVsNearest:+0.000;-0.000;0.000} | " +
                $"{kind.Severe:N0} | {kind.Weak:N0} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Same-color connected components");
        sb.AppendLine();
        sb.AppendLine("| Index | Source comps | RugScale comps | Nearest comps | Source largest% | RugScale largest% | Nearest largest% |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var component in report.Components)
        {
            sb.AppendLine(
                $"| {component.ColorIndex} | {component.SourceComponents:N0} | {component.RugScaleComponents:N0} | " +
                $"{component.NearestComponents:N0} | {component.SourceLargestShare:P2} | " +
                $"{component.RugScaleLargestShare:P2} | {component.NearestLargestShare:P2} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Worst projected motifs");
        sb.AppendLine();
        sb.AppendLine("| # | Kind | Source bbox | Target bbox | Area | Score | Align | Index | Roles | NN score | Δ |");
        sb.AppendLine("|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in report.WorstMotifs.Take(25))
        {
            sb.AppendLine(
                $"| {row.CatalogIndex} | {row.Kind} | {row.SourceX},{row.SourceY} {row.SourceWidth}×{row.SourceHeight} | " +
                $"{row.TargetX},{row.TargetY} {row.TargetWidth}×{row.TargetHeight} | {row.Area} | " +
                $"{row.RugScore:P2} | {row.RugAlignment:P2} | {row.RugIndexAgreement:P2} | " +
                $"{row.RugRoleRetention:P2} | {row.NearestScore:P2} | " +
                $"{row.DeltaVsNearest:+0.000;-0.000;0.000} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Interactive detector samples");
        sb.AppendLine();
        sb.AppendLine("| Catalog # | Kind | Expected source | Top-1 source | Match | Expected rank | Score |");
        sb.AppendLine("|---:|---|---|---|---:|---:|---:|");
        foreach (var row in report.DetectorRows)
        {
            sb.AppendLine(
                $"| {row.CatalogIndex} | {row.Kind} | {row.ExpectedSourceX},{row.ExpectedSourceY} " +
                $"{row.ExpectedSourceWidth}×{row.ExpectedSourceHeight} | {row.TopKind}@{row.TopSourceX},{row.TopSourceY} " +
                $"{row.TopSourceWidth}×{row.TopSourceHeight} | {(row.Top1Correct ? "YES" : "NO")} | " +
                $"{row.ExpectedRank} | {row.TopScore:P2} |");
        }

        return sb.ToString();
    }

    private static void WriteMotifCsv(
        string path,
        IReadOnlyList<MotifAuditRow> rows)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine(
            "catalogIndex,kind,sourceX,sourceY,sourceWidth,sourceHeight,area," +
            "targetX,targetY,targetWidth,targetHeight,sourceRoles,rugRoles," +
            "rugFamily,rugAlignment,rugIndexAgreement,rugRoleRetention,rugScore," +
            "nearestFamily,nearestAlignment,nearestIndexAgreement,nearestRoleRetention,nearestScore,deltaVsNearest");

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(
                ",",
                row.CatalogIndex,
                row.Kind,
                row.SourceX,
                row.SourceY,
                row.SourceWidth,
                row.SourceHeight,
                row.Area,
                row.TargetX,
                row.TargetY,
                row.TargetWidth,
                row.TargetHeight,
                row.SourceRoles,
                row.RugRoles,
                F(row.RugFamily),
                F(row.RugAlignment),
                F(row.RugIndexAgreement),
                F(row.RugRoleRetention),
                F(row.RugScore),
                F(row.NearestFamily),
                F(row.NearestAlignment),
                F(row.NearestIndexAgreement),
                F(row.NearestRoleRetention),
                F(row.NearestScore),
                F(row.DeltaVsNearest)));
        }
    }

    private static void WriteDetectorCsv(
        string path,
        IReadOnlyList<DetectorAuditRow> rows)
    {
        using var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false));

        writer.WriteLine(
            "catalogIndex,kind,expectedSourceX,expectedSourceY,expectedSourceWidth,expectedSourceHeight," +
            "targetX,targetY,targetWidth,targetHeight,candidateCount,top1Correct,expectedRank," +
            "topKind,topSourceX,topSourceY,topSourceWidth,topSourceHeight,topScore");

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(
                ",",
                row.CatalogIndex,
                row.Kind,
                row.ExpectedSourceX,
                row.ExpectedSourceY,
                row.ExpectedSourceWidth,
                row.ExpectedSourceHeight,
                row.TargetX,
                row.TargetY,
                row.TargetWidth,
                row.TargetHeight,
                row.CandidateCount,
                row.Top1Correct,
                row.ExpectedRank,
                row.TopKind,
                row.TopSourceX,
                row.TopSourceY,
                row.TopSourceWidth,
                row.TopSourceHeight,
                F(row.TopScore)));
        }
    }

    private static string F(double value) =>
        value.ToString(
            "0.000000",
            CultureInfo.InvariantCulture);

    private static double Percentile(
        IReadOnlyList<double> sorted,
        double p)
    {
        if (sorted.Count == 0)
            return 0d;
        if (sorted.Count == 1)
            return sorted[0];

        var position =
            Math.Clamp(p, 0d, 1d) *
            (sorted.Count - 1);
        var lower =
            (int)Math.Floor(position);
        var upper =
            (int)Math.Ceiling(position);

        if (lower == upper)
            return sorted[lower];

        var fraction =
            position - lower;

        return sorted[lower] *
               (1d - fraction) +
               sorted[upper] *
               fraction;
    }

    private static double PixelDifferenceRatio(
        DesignDocument first,
        DesignDocument second)
    {
        if (first.Width != second.Width ||
            first.Height != second.Height)
        {
            return 1d;
        }

        long different = 0;
        var total =
            (long)first.Width *
            first.Height;

        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                if (first.GetPixel(x, y) !=
                    second.GetPixel(x, y))
                {
                    different++;
                }
            }
        }

        return different /
            (double)Math.Max(1, total);
    }

    private static double BestMirrorAgreement(
        DesignDocument document,
        bool leftRight)
    {
        var best = 0d;

        for (var phase = -3;
             phase <= 3;
             phase++)
        {
            long same = 0;
            long total = 0;

            if (leftRight)
            {
                for (var y = 0;
                     y < document.Height;
                     y++)
                {
                    for (var x = 0;
                         x < document.Width;
                         x++)
                    {
                        var mirror =
                            document.Width -
                            1 -
                            x +
                            phase;

                        if (mirror < 0 ||
                            mirror >= document.Width)
                        {
                            continue;
                        }

                        total++;
                        if (document.GetPixel(x, y) ==
                            document.GetPixel(mirror, y))
                        {
                            same++;
                        }
                    }
                }
            }
            else
            {
                for (var y = 0;
                     y < document.Height;
                     y++)
                {
                    var mirror =
                        document.Height -
                        1 -
                        y +
                        phase;

                    if (mirror < 0 ||
                        mirror >= document.Height)
                    {
                        continue;
                    }

                    for (var x = 0;
                         x < document.Width;
                         x++)
                    {
                        total++;
                        if (document.GetPixel(x, y) ==
                            document.GetPixel(x, mirror))
                        {
                            same++;
                        }
                    }
                }
            }

            if (total > 0)
            {
                best = Math.Max(
                    best,
                    same / (double)total);
            }
        }

        return best;
    }

    private static double EdgeDensity(
        DesignDocument document)
    {
        long edges = 0;
        long comparisons = 0;

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                var value =
                    document.GetPixel(x, y);

                if (x + 1 < document.Width)
                {
                    comparisons++;
                    if (value !=
                        document.GetPixel(
                            x + 1,
                            y))
                    {
                        edges++;
                    }
                }

                if (y + 1 < document.Height)
                {
                    comparisons++;
                    if (value !=
                        document.GetPixel(
                            x,
                            y + 1))
                    {
                        edges++;
                    }
                }
            }
        }

        return edges /
            (double)Math.Max(
                1,
                comparisons);
    }

    private static List<ComponentAuditRow> AuditComponents(
        DesignDocument source,
        DesignDocument rugScale,
        DesignDocument nearest,
        IReadOnlySet<byte> used)
    {
        var result =
            new List<ComponentAuditRow>();

        foreach (var color in used.Order())
        {
            if (color == 0)
                continue;

            var sourceStats =
                ComponentsForColor(
                    source,
                    color);
            var rugStats =
                ComponentsForColor(
                    rugScale,
                    color);
            var nearestStats =
                ComponentsForColor(
                    nearest,
                    color);

            result.Add(new ComponentAuditRow(
                color,
                sourceStats.Count,
                rugStats.Count,
                nearestStats.Count,
                sourceStats.LargestShare,
                rugStats.LargestShare,
                nearestStats.LargestShare));
        }

        return result;
    }

    private static ComponentStats ComponentsForColor(
        DesignDocument document,
        byte color)
    {
        var width =
            document.Width;
        var height =
            document.Height;
        var visited =
            new bool[width * height];
        var queue =
            new int[width * height];

        var components = 0;
        var totalColor = 0;
        var largest = 0;

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                if (document.GetPixel(x, y) ==
                    color)
                {
                    totalColor++;
                }
            }
        }

        ReadOnlySpan<int> dx =
            [-1, 0, 1, -1, 1, -1, 0, 1];
        ReadOnlySpan<int> dy =
            [-1, -1, -1, 0, 0, 1, 1, 1];

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var start =
                    y * width + x;

                if (visited[start] ||
                    document.GetPixel(x, y) !=
                    color)
                {
                    continue;
                }

                components++;
                var head = 0;
                var tail = 0;
                var size = 0;
                queue[tail++] =
                    start;
                visited[start] =
                    true;

                while (head < tail)
                {
                    var current =
                        queue[head++];
                    var cx =
                        current % width;
                    var cy =
                        current / width;
                    size++;

                    for (var direction = 0;
                         direction < 8;
                         direction++)
                    {
                        var nx =
                            cx + dx[direction];
                        var ny =
                            cy + dy[direction];

                        if (nx < 0 ||
                            nx >= width ||
                            ny < 0 ||
                            ny >= height)
                        {
                            continue;
                        }

                        var next =
                            ny * width + nx;

                        if (visited[next] ||
                            document.GetPixel(
                                nx,
                                ny) !=
                            color)
                        {
                            continue;
                        }

                        visited[next] =
                            true;
                        queue[tail++] =
                            next;
                    }
                }

                largest =
                    Math.Max(
                        largest,
                        size);
            }
        }

        return new ComponentStats(
            components,
            totalColor == 0
                ? 0d
                : largest /
                  (double)totalColor);
    }

    private static HashSet<byte> UsedIndices(
        DesignDocument document)
    {
        var result =
            new HashSet<byte>();

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                result.Add(
                    document.GetPixel(
                        x,
                        y));
            }
        }

        return result;
    }

    private static string? GetArg(
        string[] args,
        string name)
    {
        for (var i = 0;
             i + 1 < args.Length;
             i++)
        {
            if (string.Equals(
                    args[i],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private sealed record ProjectedMotif(
        int X,
        int Y,
        int Width,
        int Height,
        bool[] Mask,
        byte[] ExpectedColors,
        int MaskCount);

    private sealed record ComponentStats(
        int Count,
        double LargestShare);

    private sealed record MotifAuditRow(
        int CatalogIndex,
        string Kind,
        int SourceX,
        int SourceY,
        int SourceWidth,
        int SourceHeight,
        int Area,
        int TargetX,
        int TargetY,
        int TargetWidth,
        int TargetHeight,
        int SourceRoles,
        int RugRoles,
        double RugFamily,
        double RugAlignment,
        double RugIndexAgreement,
        double RugRoleRetention,
        double RugScore,
        double NearestFamily,
        double NearestAlignment,
        double NearestIndexAgreement,
        double NearestRoleRetention,
        double NearestScore,
        double DeltaVsNearest);

    private sealed record DetectorAuditRow(
        int CatalogIndex,
        string Kind,
        int ExpectedSourceX,
        int ExpectedSourceY,
        int ExpectedSourceWidth,
        int ExpectedSourceHeight,
        int TargetX,
        int TargetY,
        int TargetWidth,
        int TargetHeight,
        int CandidateCount,
        bool Top1Correct,
        int ExpectedRank,
        string TopKind,
        int TopSourceX,
        int TopSourceY,
        int TopSourceWidth,
        int TopSourceHeight,
        double TopScore);

    private sealed record KindSummary(
        string Kind,
        int Count,
        double Mean,
        double P10,
        double Median,
        double P90,
        double MeanDeltaVsNearest,
        int Severe,
        int Weak);

    private sealed record ComponentAuditRow(
        byte ColorIndex,
        int SourceComponents,
        int RugScaleComponents,
        int NearestComponents,
        double SourceLargestShare,
        double RugScaleLargestShare,
        double NearestLargestShare);

    private sealed record AuditReport(
        int SourceWidth,
        int SourceHeight,
        int TargetWidth,
        int TargetHeight,
        int SourceWarp,
        int SourceWeft,
        double ResizeSeconds,
        double CatalogSeconds,
        double MotifAuditSeconds,
        double DetectorAuditSeconds,
        int MotifCount,
        double MotifMean,
        double MotifP10,
        double MotifMedian,
        double MotifP90,
        double MeanDeltaVsNearest,
        int BetterThanNearestCount,
        int WorseThanNearestCount,
        int SevereMotifCount,
        int WeakMotifCount,
        int DetectorSampleCount,
        int DetectorTop1Correct,
        int DetectorTop3Found,
        double PixelDifferenceFromNearest,
        bool PaletteSafe,
        double SourceLeftRightSymmetry,
        double SourceTopBottomSymmetry,
        double RugScaleLeftRightSymmetry,
        double RugScaleTopBottomSymmetry,
        double NearestLeftRightSymmetry,
        double NearestTopBottomSymmetry,
        double SourceEdgeDensity,
        double RugScaleEdgeDensity,
        double NearestEdgeDensity,
        KindSummary[] KindSummaries,
        ComponentAuditRow[] Components,
        MotifAuditRow[] WorstMotifs,
        DetectorAuditRow[] DetectorRows);

    private static class IndexedBmp
    {
        internal sealed record Loaded(
            DesignDocument Document,
            int XPixelsPerMeter,
            int YPixelsPerMeter);

        public static Loaded Read(
            string path)
        {
            using var stream =
                File.OpenRead(path);
            using var reader =
                new BinaryReader(stream);

            if (reader.ReadUInt16() != 0x4D42)
                throw new InvalidDataException(
                    "Not a BMP file.");

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var pixelOffset =
                reader.ReadUInt32();

            var dibSize =
                reader.ReadUInt32();

            if (dibSize < 40)
                throw new InvalidDataException(
                    "Only BITMAPINFOHEADER-or-newer BMP is supported.");

            var width =
                reader.ReadInt32();
            var rawHeight =
                reader.ReadInt32();
            var planes =
                reader.ReadUInt16();
            var bitsPerPixel =
                reader.ReadUInt16();
            var compression =
                reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var xppm =
                reader.ReadInt32();
            var yppm =
                reader.ReadInt32();
            var colorsUsed =
                reader.ReadUInt32();
            _ = reader.ReadUInt32();

            if (planes != 1 ||
                bitsPerPixel != 8 ||
                compression != 0 ||
                width <= 0 ||
                rawHeight == 0)
            {
                throw new InvalidDataException(
                    $"Expected uncompressed 8-bit indexed BMP; got width={width}, height={rawHeight}, " +
                    $"planes={planes}, bpp={bitsPerPixel}, compression={compression}.");
            }

            if (dibSize > 40)
            {
                reader.ReadBytes(
                    checked(
                        (int)dibSize - 40));
            }

            var paletteEntryBytes =
                checked(
                    (int)pixelOffset -
                    (14 + (int)dibSize));
            var availableEntries =
                Math.Max(
                    0,
                    paletteEntryBytes / 4);
            var requestedEntries =
                colorsUsed == 0
                    ? 256
                    : Math.Min(
                        256,
                        checked((int)colorsUsed));
            var paletteEntries =
                Math.Min(
                    requestedEntries,
                    availableEntries);

            var colors =
                Enumerable.Repeat(
                    RugColor.Black,
                    256)
                    .ToArray();

            for (var i = 0;
                 i < paletteEntries;
                 i++)
            {
                var b =
                    reader.ReadByte();
                var g =
                    reader.ReadByte();
                var r =
                    reader.ReadByte();
                _ = reader.ReadByte();
                colors[i] =
                    new RugColor(
                        r,
                        g,
                        b);
            }

            stream.Position =
                pixelOffset;

            var height =
                Math.Abs(
                    rawHeight);
            var bottomUp =
                rawHeight > 0;
            var stride =
                (width + 3) &
                ~3;
            var document =
                new DesignDocument(
                    width,
                    height,
                    new Palette(colors));

            var row =
                new byte[stride];

            for (var fileRow = 0;
                 fileRow < height;
                 fileRow++)
            {
                var read =
                    stream.Read(
                        row,
                        0,
                        row.Length);

                if (read !=
                    row.Length)
                {
                    throw new EndOfStreamException(
                        "BMP pixel data ended early.");
                }

                var y =
                    bottomUp
                        ? height -
                          1 -
                          fileRow
                        : fileRow;

                for (var x = 0;
                     x < width;
                     x++)
                {
                    document.SetPixel(
                        x,
                        y,
                        row[x]);
                }
            }

            return new Loaded(
                document,
                xppm,
                yppm);
        }

        public static void Write(
            string path,
            DesignDocument document,
            int xppm,
            int yppm)
        {
            var stride =
                (document.Width + 3) &
                ~3;
            var imageBytes =
                checked(
                    stride *
                    document.Height);
            const int pixelOffset =
                14 +
                40 +
                256 * 4;
            var fileBytes =
                checked(
                    pixelOffset +
                    imageBytes);

            using var stream =
                File.Create(path);
            using var writer =
                new BinaryWriter(stream);

            writer.Write(
                (ushort)0x4D42);
            writer.Write(
                (uint)fileBytes);
            writer.Write(
                (ushort)0);
            writer.Write(
                (ushort)0);
            writer.Write(
                (uint)pixelOffset);

            writer.Write(
                (uint)40);
            writer.Write(
                document.Width);
            writer.Write(
                document.Height);
            writer.Write(
                (ushort)1);
            writer.Write(
                (ushort)8);
            writer.Write(
                (uint)0);
            writer.Write(
                (uint)imageBytes);
            writer.Write(xppm);
            writer.Write(yppm);
            writer.Write(
                (uint)256);
            writer.Write(
                (uint)256);

            for (var i = 0;
                 i < 256;
                 i++)
            {
                var color =
                    document.Palette[i];

                writer.Write(color.B);
                writer.Write(color.G);
                writer.Write(color.R);
                writer.Write((byte)0);
            }

            var row =
                new byte[stride];

            for (var y = document.Height - 1;
                 y >= 0;
                 y--)
            {
                Array.Clear(row);

                for (var x = 0;
                     x < document.Width;
                     x++)
                {
                    row[x] =
                        document.GetPixel(
                            x,
                            y);
                }

                writer.Write(row);
            }
        }
    }
}
