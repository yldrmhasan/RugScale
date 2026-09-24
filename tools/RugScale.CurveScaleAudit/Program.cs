using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RugScale.Core.Drawing;
using RugScale.Core.Models;

namespace RugScale.CurveScaleAudit;

internal static class Program
{
    private sealed record Fixture(
        string Name,
        string FileName,
        int Warp,
        int Weft);

    private static readonly Fixture[] Fixtures =
    [
        new("C071C_BEIGE_N69", "C071C_BEIGE_N69.bmp", 40, 60),
        new("B996A_BEIGE_N69", "B996A_BEIGE_N69.bmp", 40, 50),
        new("C004A_BEIGE_N69", "C004A_BEIGE_N69.bmp", 40, 50),
        new("C069A_CREAM_N69", "C069A_CREAM_N69.bmp", 40, 60),
    ];

    private static int Main(string[] args)
    {
        var inputDir =
            GetArg(args, "--input-dir") ??
            throw new ArgumentException("--input-dir <dir> is required.");
        var outputDir =
            GetArg(args, "--output") ??
            Path.Combine(
                Environment.CurrentDirectory,
                "curve-scale-audit");

        Directory.CreateDirectory(outputDir);

        var styleTraining =
            RunCurveStyleSelfTraining(
                outputDir);

        var rows = new List<DesignAuditRow>();

        foreach (var fixture in Fixtures)
        {
            var input =
                Path.Combine(
                    inputDir,
                    fixture.FileName);

            if (!File.Exists(input))
                throw new FileNotFoundException(
                    $"Curve fixture not found: {input}");

            rows.Add(
                AuditFixture(
                    fixture,
                    input,
                    outputDir));
        }

        var report =
            new CurveSuiteReport(
                DateTime.UtcNow,
                rows,
                styleTraining,
                rows.All(row => row.PaletteSafe),
                rows.All(row =>
                    row.ExactLeftRightSource
                        ? row.ExactLeftRightRoundTrip
                        : true),
                rows.All(row =>
                    row.ExactTopBottomSource
                        ? row.ExactTopBottomRoundTrip
                        : true));

        var json =
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy =
                        JsonNamingPolicy.CamelCase,
                });

        File.WriteAllText(
            Path.Combine(
                outputDir,
                "curve-suite-report.json"),
            json);

        var markdown =
            BuildMarkdown(report);

        File.WriteAllText(
            Path.Combine(
                outputDir,
                "curve-suite-report.md"),
            markdown);

        Console.WriteLine(markdown);

        if (!report.PaletteSafe)
        {
            Console.Error.WriteLine(
                "[curve-suite] FAIL: a Curve & Fill output introduced a source-absent palette index.");
            return 2;
        }

        if (!report.ExactLeftRightPreserved ||
            !report.ExactTopBottomPreserved)
        {
            Console.Error.WriteLine(
                "[curve-suite] FAIL: an exact source symmetry was lost.");
            return 3;
        }

        if (!ValidateCurveStyleTraining(
                styleTraining,
                out var trainingFailure))
        {
            Console.Error.WriteLine(
                $"[curve-suite] FAIL: Curve-tool inverse-model training guardrail: {trainingFailure}");
            return 4;
        }

        return 0;
    }

    private static bool ValidateCurveStyleTraining(
        IReadOnlyList<CurveStyleTrainingRow> rows,
        out string failure)
    {
        var throughRows =
            rows
                .Where(row =>
                    row.SourceType ==
                    CurveType.SplineThroughPoints)
                .ToArray();

        var throughAccepted =
            throughRows
                .Where(row =>
                    row.Accepted)
                .ToArray();

        if (throughAccepted.Length < 20)
        {
            failure =
                $"Through-Points coverage fell to {throughAccepted.Length}/{throughRows.Length}.";
            return false;
        }

        var throughFamilyPrecision =
            throughAccepted.Count(row =>
                row.FamilyCorrect) /
            (double)throughAccepted.Length;

        if (throughFamilyPrecision < 0.95)
        {
            failure =
                $"Through-Points family precision fell to {throughFamilyPrecision:P2}.";
            return false;
        }

        var roundnessErrors =
            throughAccepted
                .Where(row =>
                    row.FamilyCorrect &&
                    row.RoundnessError.HasValue)
                .Select(row =>
                    row.RoundnessError!.Value)
                .ToArray();

        var roundnessMae =
            roundnessErrors.Length == 0
                ? double.PositiveInfinity
                : roundnessErrors.Average();

        if (roundnessMae > 0.12)
        {
            failure =
                $"Through-Points roundness MAE rose to {roundnessMae:0.000}.";
            return false;
        }

        var ovalRows =
            rows
                .Where(row =>
                    row.SourceType ==
                    CurveType.SplineThroughPoints &&
                    row.Shape.Contains(
                        "oval",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var ovalAccepted =
            ovalRows
                .Where(row =>
                    row.Accepted)
                .ToArray();

        if (ovalRows.Length == 0 ||
            ovalAccepted.Length <
            Math.Ceiling(
                ovalRows.Length * 0.90))
        {
            failure =
                $"Oval training coverage is too low: {ovalAccepted.Length}/{ovalRows.Length}.";
            return false;
        }

        var ovalFamilyPrecision =
            ovalAccepted.Count(row =>
                row.FamilyCorrect) /
            (double)Math.Max(
                1,
                ovalAccepted.Length);

        if (ovalFamilyPrecision < 0.98)
        {
            failure =
                $"Oval family precision fell to {ovalFamilyPrecision:P2}.";
            return false;
        }

        var ovalLearnedExact =
            ovalRows.Average(row =>
                row.TargetExactF1);
        var ovalGraphExact =
            ovalRows.Average(row =>
                row.FallbackTargetExactF1);

        if (ovalLearnedExact < 0.90)
        {
            failure =
                $"Oval target exact F1 fell to {ovalLearnedExact:P2}.";
            return false;
        }

        if (ovalLearnedExact <
            ovalGraphExact + 0.24)
        {
            failure =
                $"Oval target exact F1 gain is too small: learned={ovalLearnedExact:P2}, graph={ovalGraphExact:P2}.";
            return false;
        }

        var ovalRoundnessErrors =
            ovalAccepted
                .Where(row =>
                    row.FamilyCorrect &&
                    row.RoundnessError.HasValue)
                .Select(row =>
                    row.RoundnessError!.Value)
                .ToArray();
        var ovalRoundnessMae =
            ovalRoundnessErrors.Length == 0
                ? double.PositiveInfinity
                : ovalRoundnessErrors.Average();

        if (ovalRoundnessMae > 0.05)
        {
            failure =
                $"Oval roundness MAE rose to {ovalRoundnessMae:0.000}.";
            return false;
        }

        foreach (var family in Enum.GetValues<CurveType>())
        {
            var familyRows =
                rows
                    .Where(row =>
                        row.SourceType ==
                        family)
                    .ToArray();

            if (familyRows.Length == 0)
                continue;

            var learnedExact =
                familyRows.Average(row =>
                    row.TargetExactF1);
            var graphExact =
                familyRows.Average(row =>
                    row.FallbackTargetExactF1);

            // The point of the inverse model is not merely naming the historical tool; it must
            // extrapolate the original drawing more faithfully than scaling every source pixel
            // edge-for-edge. Every trained family must demonstrate a material exact-raster gain.
            if (learnedExact <
                graphExact + 0.02)
            {
                failure =
                    $"{family} target exact F1 gain is too small: learned={learnedExact:P2}, graph={graphExact:P2}.";
                return false;
            }
        }

        failure =
            string.Empty;
        return true;
    }

    private static IReadOnlyList<CurveStyleTrainingRow> RunCurveStyleSelfTraining(
        string outputDir)
    {
        var rows =
            new List<CurveStyleTrainingRow>();

        var throughShapes =
            new Dictionary<string, (int X, int Y)[]>
            {
                ["arch"] =
                [
                    (5, 42),
                    (12, 13),
                    (30, 6),
                    (50, 16),
                    (58, 42),
                ],
                ["wave"] =
                [
                    (5, 32),
                    (16, 8),
                    (29, 30),
                    (43, 7),
                    (58, 31),
                ],
                ["leaf-arc"] =
                [
                    (5, 40),
                    (17, 13),
                    (32, 9),
                    (49, 23),
                    (58, 41),
                ],
                // Oval-specific training cohort. RugScale curve redraw is expected to preserve
                // the broad, continuous curvature of these motifs instead of collapsing them
                // into polygonal/scalloped chains when the physical carpet size changes.
                ["oval-wide"] =
                [
                    (4, 34),
                    (10, 16),
                    (29, 7),
                    (50, 15),
                    (60, 34),
                ],
                ["oval-tall-side"] =
                [
                    (42, 5),
                    (22, 8),
                    (8, 24),
                    (15, 46),
                    (38, 59),
                ],
                ["oval-soft"] =
                [
                    (6, 39),
                    (13, 17),
                    (28, 8),
                    (46, 12),
                    (59, 32),
                ],
            };

        var throughRoundness =
            new[]
            {
                0.10,
                0.25,
                0.50,
                0.85,
                1.00,
            };

        foreach (var (shapeName, controls) in throughShapes)
        {
            foreach (var roundness in throughRoundness)
            {
                foreach (var pixelCord in new[]
                         {
                             false,
                             true,
                         })
                {
                    rows.Add(
                        EvaluateTrainingExample(
                            $"through-{shapeName}",
                            CurveType.SplineThroughPoints,
                            roundness,
                            controls,
                            pixelCord,
                            evaluateRoundness: true));
                }
            }
        }

        var bezierShapes =
            new Dictionary<string, (int X, int Y)[]>
            {
                ["arch"] =
                [
                    (5, 43),
                    (7, 8),
                    (51, 4),
                    (59, 42),
                ],
                ["sweep"] =
                [
                    (5, 38),
                    (21, 4),
                    (40, 51),
                    (59, 15),
                ],
                ["hook"] =
                [
                    (7, 45),
                    (8, 11),
                    (55, 8),
                    (47, 43),
                ],
            };

        foreach (var (shapeName, controls) in bezierShapes)
        {
            foreach (var roundness in new[]
                     {
                         0.50,
                         0.80,
                         1.00,
                     })
            {
                foreach (var pixelCord in new[]
                         {
                             false,
                             true,
                         })
                {
                    // Bezier handle displacement and the slider roundness are not separately
                    // identifiable from raster alone. The learner therefore recovers an effective
                    // cubic at roundness=1; training scores FAMILY recognition, not historical
                    // slider recovery, for Bezier.
                    rows.Add(
                        EvaluateTrainingExample(
                            $"bezier-{shapeName}",
                            CurveType.Bezier,
                            roundness,
                            controls,
                            pixelCord,
                            evaluateRoundness: false));
                }
            }
        }

        var splineShapes =
            new Dictionary<string, (int X, int Y)[]>
            {
                ["arch"] =
                [
                    (5, 42),
                    (14, 9),
                    (31, 5),
                    (49, 14),
                    (59, 41),
                ],
                ["wave"] =
                [
                    (5, 31),
                    (17, 8),
                    (30, 29),
                    (44, 7),
                    (59, 30),
                ],
            };

        foreach (var (shapeName, controls) in splineShapes)
        {
            foreach (var roundness in new[]
                     {
                         0.45,
                         0.75,
                         1.00,
                     })
            {
                foreach (var pixelCord in new[]
                         {
                             false,
                             true,
                         })
                {
                    rows.Add(
                        EvaluateTrainingExample(
                            $"spline-{shapeName}",
                            CurveType.Spline,
                            roundness,
                            controls,
                            pixelCord,
                            evaluateRoundness: false));
                }
            }
        }

        WriteCurveStyleTrainingCsv(
            Path.Combine(
                outputDir,
                "curve-style-training.csv"),
            rows);

        File.WriteAllText(
            Path.Combine(
                outputDir,
                "curve-style-training.md"),
            BuildCurveStyleTrainingMarkdown(
                rows));

        return rows;
    }

    private static CurveStyleTrainingRow EvaluateTrainingExample(
        string shape,
        CurveType sourceType,
        double sourceRoundness,
        IReadOnlyList<(int X, int Y)> controls,
        bool pixelCord,
        bool evaluateRoundness)
    {
        IEnumerable<(int X, int Y)> rendered =
            CurveRasterizer.Draw(
                controls,
                sourceType,
                sourceRoundness);

        if (pixelCord)
        {
            rendered =
                Rasterizer.ConnectDiagonalSteps(
                    rendered);
        }

        var chain =
            ConsecutiveDistinct(
                rendered)
                .ToArray();

        var fit =
            ToolFaithfulCurveStyleLearner.Fit(
                chain,
                pixelCord);

        var accepted =
            fit.HasValue;
        var predictedType =
            fit?.Type;
        var predictedRoundness =
            fit?.Roundness;
        var familyCorrect =
            accepted &&
            predictedType ==
            sourceType;
        var roundnessError =
            evaluateRoundness &&
            predictedRoundness.HasValue
                ? Math.Abs(
                    predictedRoundness.Value -
                    sourceRoundness)
                : (double?)null;
        var modelMargin =
            accepted
                ? fit!.Value.ModelScore -
                  fit.Value.PolylineBaselineModelScore
                : 0d;

        const double TargetScale = 1.60;

        var expectedTargetControls =
            controls
                .Select(point =>
                    MapTrainingPoint(
                        point,
                        TargetScale))
                .ToArray();

        IEnumerable<(int X, int Y)> expectedTarget =
            CurveRasterizer.Draw(
                expectedTargetControls,
                sourceType,
                sourceRoundness);

        if (pixelCord)
        {
            expectedTarget =
                Rasterizer.ConnectDiagonalSteps(
                    expectedTarget);
        }

        var expectedTargetSet =
            expectedTarget
                .ToHashSet();

        var fallbackTargetSet =
            RenderMappedTrainingGraph(
                    chain,
                    TargetScale,
                    pixelCord)
                .ToHashSet();

        HashSet<(int X, int Y)> learnedTargetSet;

        if (accepted)
        {
            var learnedControls =
                fit!.Value.Controls
                    .Select(point =>
                        MapTrainingPoint(
                            point,
                            TargetScale))
                    .ToArray();

            IEnumerable<(int X, int Y)> learnedTarget =
                CurveRasterizer.Draw(
                    learnedControls,
                    fit.Value.Type,
                    fit.Value.Roundness);

            if (pixelCord)
            {
                learnedTarget =
                    Rasterizer.ConnectDiagonalSteps(
                        learnedTarget);
            }

            learnedTargetSet =
                learnedTarget
                    .ToHashSet();
        }
        else
        {
            learnedTargetSet =
                fallbackTargetSet;
        }

        var targetExactF1 =
            SetF1(
                learnedTargetSet,
                expectedTargetSet);
        var targetNearF1 =
            SetNearF1(
                learnedTargetSet,
                expectedTargetSet,
                radius: 1);
        var fallbackExactF1 =
            SetF1(
                fallbackTargetSet,
                expectedTargetSet);
        var fallbackNearF1 =
            SetNearF1(
                fallbackTargetSet,
                expectedTargetSet,
                radius: 1);

        return new CurveStyleTrainingRow(
            shape,
            sourceType,
            sourceRoundness,
            pixelCord,
            chain.Length,
            accepted,
            predictedType,
            predictedRoundness,
            familyCorrect,
            roundnessError,
            accepted
                ? fit!.Value.Score
                : 0d,
            modelMargin,
            accepted
                ? fit!.Value.Controls.Count
                : 0,
            targetExactF1,
            targetNearF1,
            fallbackExactF1,
            fallbackNearF1,
            targetNearF1 -
            fallbackNearF1);
    }

    private static (int X, int Y) MapTrainingPoint(
        (int X, int Y) point,
        double scale) =>
        (
            X: (int)Math.Round(
                (point.X + 0.5) *
                scale -
                0.5),
            Y: (int)Math.Round(
                (point.Y + 0.5) *
                scale -
                0.5));

    private static IEnumerable<(int X, int Y)> RenderMappedTrainingGraph(
        IReadOnlyList<(int X, int Y)> chain,
        double scale,
        bool pixelCord)
    {
        if (chain.Count == 0)
            yield break;

        var previous =
            MapTrainingPoint(
                chain[0],
                scale);

        yield return previous;

        for (var i = 1;
             i < chain.Count;
             i++)
        {
            var current =
                MapTrainingPoint(
                    chain[i],
                    scale);

            IEnumerable<(int X, int Y)> segment =
                Rasterizer.Line(
                    previous.X,
                    previous.Y,
                    current.X,
                    current.Y);

            if (pixelCord)
            {
                segment =
                    Rasterizer.ConnectDiagonalSteps(
                        segment);
            }

            foreach (var point in segment)
                yield return point;

            previous = current;
        }
    }

    private static double SetF1(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected)
    {
        if (actual.Count == 0 ||
            expected.Count == 0)
        {
            return 0d;
        }

        var intersection =
            actual.Count(
                expected.Contains);
        var precision =
            intersection /
            (double)actual.Count;
        var recall =
            intersection /
            (double)expected.Count;
        var sum =
            precision +
            recall;

        return sum <= 0d
            ? 0d
            : 2d *
              precision *
              recall /
              sum;
    }

    private static double SetNearF1(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected,
        int radius)
    {
        if (actual.Count == 0 ||
            expected.Count == 0)
        {
            return 0d;
        }

        static bool Near(
            IReadOnlySet<(int X, int Y)> points,
            (int X, int Y) point,
            int searchRadius)
        {
            for (var dy = -searchRadius;
                 dy <= searchRadius;
                 dy++)
            {
                for (var dx = -searchRadius;
                     dx <= searchRadius;
                     dx++)
                {
                    if (points.Contains(
                            (
                                point.X + dx,
                                point.Y + dy)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        var supportedActual =
            actual.Count(point =>
                Near(
                    expected,
                    point,
                    radius));
        var supportedExpected =
            expected.Count(point =>
                Near(
                    actual,
                    point,
                    radius));

        var precision =
            supportedActual /
            (double)actual.Count;
        var recall =
            supportedExpected /
            (double)expected.Count;
        var sum =
            precision +
            recall;

        return sum <= 0d
            ? 0d
            : 2d *
              precision *
              recall /
              sum;
    }

    private static IEnumerable<(int X, int Y)> ConsecutiveDistinct(
        IEnumerable<(int X, int Y)> points)
    {
        (int X, int Y)? previous =
            null;

        foreach (var point in points)
        {
            if (previous is not null &&
                previous.Value ==
                point)
            {
                continue;
            }

            yield return point;
            previous = point;
        }
    }

    private static void WriteCurveStyleTrainingCsv(
        string path,
        IReadOnlyList<CurveStyleTrainingRow> rows)
    {
        using var writer =
            new StreamWriter(
                path,
                false,
                new UTF8Encoding(false));

        writer.WriteLine(
            "shape,sourceType,sourceRoundness,pixelCord,sourcePixels,accepted,predictedType,predictedRoundness,familyCorrect,roundnessError,rawScore,modelMargin,controlCount,targetExactF1,targetNearF1,fallbackTargetExactF1,fallbackTargetNearF1,targetNearGain");

        foreach (var row in rows)
        {
            writer.WriteLine(
                string.Join(
                    ",",
                    row.Shape,
                    row.SourceType,
                    row.SourceRoundness.ToString("0.000", CultureInfo.InvariantCulture),
                    row.PixelCord,
                    row.SourcePixels,
                    row.Accepted,
                    row.PredictedType?.ToString() ?? "",
                    row.PredictedRoundness?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                    row.FamilyCorrect,
                    row.RoundnessError?.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                    row.RawScore.ToString("0.0000", CultureInfo.InvariantCulture),
                    row.ModelMargin.ToString("0.0000", CultureInfo.InvariantCulture),
                    row.ControlCount,
                    row.TargetExactF1.ToString("0.0000", CultureInfo.InvariantCulture),
                    row.TargetNearF1.ToString("0.0000", CultureInfo.InvariantCulture),
                    row.FallbackTargetExactF1.ToString("0.0000", CultureInfo.InvariantCulture),
                    row.FallbackTargetNearF1.ToString("0.0000", CultureInfo.InvariantCulture),
                    row.TargetNearGain.ToString("0.0000", CultureInfo.InvariantCulture)));
        }
    }

    private static string BuildCurveStyleTrainingMarkdown(
        IReadOnlyList<CurveStyleTrainingRow> rows)
    {
        var sb =
            new StringBuilder();

        sb.AppendLine(
            "# RugCAD Curve-tool source-style self-training");
        sb.AppendLine();
        sb.AppendLine(
            "Synthetic teacher rasters are generated by RugCAD's own CurveRasterizer, then only " +
            "the raster chain is given back to ToolFaithfulCurveStyleLearner. This measures whether " +
            "the inverse model can recover drawing FAMILY and, where identifiable, Through-Points roundness.");
        sb.AppendLine();

        sb.AppendLine(
            "| Source family | Samples | Accepted | Family correct | Family accuracy | Mean raw | Model margin | Target exact F1 | Graph exact F1 | Exact gain | Target ±1px F1 | Graph ±1px F1 | Near gain |");
        sb.AppendLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var group in rows
                     .GroupBy(row =>
                         row.SourceType)
                     .OrderBy(group =>
                         group.Key))
        {
            var accepted =
                group
                    .Where(row =>
                        row.Accepted)
                    .ToArray();
            var correct =
                accepted.Count(row =>
                    row.FamilyCorrect);

            var learnedExact =
                group.Average(row =>
                    row.TargetExactF1);
            var graphExact =
                group.Average(row =>
                    row.FallbackTargetExactF1);

            sb.AppendLine(
                $"| {group.Key} | {group.Count():N0} | {accepted.Length:N0} | {correct:N0} | " +
                $"{(accepted.Length == 0 ? 0d : correct / (double)accepted.Length):P2} | " +
                $"{(accepted.Length == 0 ? 0d : accepted.Average(row => row.RawScore)):P2} | " +
                $"{(accepted.Length == 0 ? 0d : accepted.Average(row => row.ModelMargin)):0.0000} | " +
                $"{learnedExact:P2} | {graphExact:P2} | {(learnedExact - graphExact):+0.0000;-0.0000;0.0000} | " +
                $"{group.Average(row => row.TargetNearF1):P2} | " +
                $"{group.Average(row => row.FallbackTargetNearF1):P2} | " +
                $"{group.Average(row => row.TargetNearGain):+0.0000;-0.0000;0.0000} |");
        }

        var throughRoundness =
            rows
                .Where(row =>
                    row.SourceType ==
                    CurveType.SplineThroughPoints &&
                    row.FamilyCorrect &&
                    row.RoundnessError.HasValue)
                .Select(row =>
                    row.RoundnessError!.Value)
                .ToArray();

        sb.AppendLine();
        sb.AppendLine(
            $"Through-Points roundness MAE (family-correct accepted samples): **" +
            $"{(throughRoundness.Length == 0 ? 0d : throughRoundness.Average()):0.000}**.");
        sb.AppendLine();
        sb.AppendLine(
            "Bezier/Spline slider roundness is intentionally not scored as a historical parameter: " +
            "control-handle displacement and slider roundness are not uniquely identifiable from final raster geometry. " +
            "For those families the engine learns an equivalent/effective control model that reproduces the source curve.");
        sb.AppendLine();
        sb.AppendLine(
            "**Primary training objective:** target-scale exact raster F1. Family naming is secondary " +
            "when two Curve-tool families are raster-equivalent; the learned compact model must extrapolate " +
            "the designer's curve at 160% more exactly than edge-for-edge pixel graph scaling.");

        return sb.ToString();
    }

    private static DesignAuditRow AuditFixture(
        Fixture fixture,
        string input,
        string outputDir)
    {
        Console.WriteLine(
            $"[curve-suite] {fixture.Name}: loading {input}");

        var bmp = IndexedBmp.Read(input);
        var source = bmp.Document;
        var sourceUsed =
            UsedIndices(source);

        var shrinkWidth =
            Math.Max(
                2,
                (int)Math.Round(
                    source.Width * 0.80));
        var shrinkHeight =
            Math.Max(
                2,
                (int)Math.Round(
                    source.Height * 0.80));

        var directWidth =
            Math.Max(
                source.Width + 1,
                (int)Math.Round(
                    source.Width * 1.60));
        var directHeight =
            Math.Max(
                source.Height + 1,
                (int)Math.Round(
                    source.Height * 1.60));

        var watch = Stopwatch.StartNew();

        var small =
            DesignResizer.Scale(
                source,
                shrinkWidth,
                shrinkHeight,
                ScaleMode.CurveFill,
                fixture.Warp,
                fixture.Weft,
                fixture.Warp,
                fixture.Weft);

        var shrinkSeconds =
            watch.Elapsed.TotalSeconds;

        watch.Restart();

        var roundTrip =
            DesignResizer.Scale(
                small,
                source.Width,
                source.Height,
                ScaleMode.CurveFill,
                fixture.Warp,
                fixture.Weft,
                fixture.Warp,
                fixture.Weft);

        var roundTripSeconds =
            watch.Elapsed.TotalSeconds;

        watch.Restart();

        var roundTripNearest =
            DesignResizer.Scale(
                small,
                source.Width,
                source.Height,
                ScaleMode.NearestNeighbor);

        var nearestRoundTripSeconds =
            watch.Elapsed.TotalSeconds;

        watch.Restart();

        var direct =
            ScaleCurveFillWithDiagnostics(
                source,
                directWidth,
                directHeight,
                fixture.Warp,
                fixture.Weft,
                fixture.Warp,
                fixture.Weft,
                out var directStyleDiagnostics);

        var directSeconds =
            watch.Elapsed.TotalSeconds;

        var ribbonStages =
            CurveFillRibbonArcRefiner.AnalyzeCandidateStages(
                source);

        WriteRibbonCandidateAudit(
            Path.Combine(
                outputDir,
                fixture.Name +
                "_ribbon_candidates.csv"),
            ribbonStages);

        if (string.Equals(
                fixture.Name,
                "C069A_CREAM_N69",
                StringComparison.Ordinal))
        {
            foreach (var stage in ribbonStages
                         .Where(stage =>
                             stage.MinX <= 440 &&
                             stage.MaxX >= 200 &&
                             stage.MinY <= 190)
                         .Take(24))
            {
                Console.WriteLine(
                    $"[ribbon-candidate] C069 color={stage.Color}, " +
                    $"bbox=({stage.MinX},{stage.MinY})-({stage.MaxX},{stage.MaxY}), " +
                    $"area={stage.Area}, elong={stage.Elongation:0.000}, fill={stage.BoundingFillRatio:0.000}, " +
                    $"broad={stage.BroadSparseArch}, centerline={stage.CenterlineBuilt}, " +
                    $"coverage={stage.PrincipalPathCoverage:0.000}, ribbon={stage.DesignerRibbon}, " +
                    $"fit={stage.FitKind}/{stage.CurveFamily}, r={stage.Roundness:0.000}, " +
                    $"safe={stage.FitSafe}, dev={stage.MaximumDeviation:0.000}, " +
                    $"flips={stage.CurvatureSignFlips}, accepted={stage.Accepted}, status={stage.Status}");
            }
        }

        if (string.Equals(
                fixture.Name,
                "B996A_BEIGE_N69",
                StringComparison.Ordinal))
        {
            // Exact user reproduction case from the supplied RugScale comparison.
            var userCase1320 =
                DesignResizer.Scale(
                    source,
                    800,
                    1320,
                    ScaleMode.CurveFill,
                    fixture.Warp,
                    fixture.Weft,
                    fixture.Warp,
                    fixture.Weft);

            IndexedBmp.Write(
                Path.Combine(
                    outputDir,
                    fixture.Name +
                    "_curve_fill_toolfaithful_800x1320.bmp"),
                userCase1320,
                bmp.XPixelsPerMeter,
                bmp.YPixelsPerMeter);

            var leafPetal1320 =
                new DesignDocument(
                    800,
                    1320,
                    source.Palette);
            var leafPetalWatch =
                Stopwatch.StartNew();
            var leafPetalDiagnostics =
                LeafPetalArcScaleEngine.ResizeWithDiagnostics(
                    source,
                    leafPetal1320,
                    fixture.Warp,
                    fixture.Weft,
                    fixture.Warp,
                    fixture.Weft);
            leafPetalWatch.Stop();

            IndexedBmp.Write(
                Path.Combine(
                    outputDir,
                    fixture.Name +
                    "_leaf_petal_arcs_800x1320.bmp"),
                leafPetal1320,
                bmp.XPixelsPerMeter,
                bmp.YPixelsPerMeter);

            // Exact focus region from the user-reported green leaf / petal curve comparison.
            // Keep this calibration crop in CI so whole-design averages cannot hide a locally
            // unattractive arc.
            const int FocusSourceX = 80;
            const int FocusSourceY = 85;
            const int FocusSourceWidth = 114;
            const int FocusSourceHeight = 161;

            var focusSource =
                Crop(
                    source,
                    FocusSourceX,
                    FocusSourceY,
                    FocusSourceWidth,
                    FocusSourceHeight);

            var focusTargetX =
                (int)Math.Floor(
                    FocusSourceX *
                    leafPetal1320.Width /
                    (double)source.Width);
            var focusTargetY =
                (int)Math.Floor(
                    FocusSourceY *
                    leafPetal1320.Height /
                    (double)source.Height);
            var focusTargetRight =
                (int)Math.Ceiling(
                    (FocusSourceX +
                     FocusSourceWidth) *
                    leafPetal1320.Width /
                    (double)source.Width);
            var focusTargetBottom =
                (int)Math.Ceiling(
                    (FocusSourceY +
                     FocusSourceHeight) *
                    leafPetal1320.Height /
                    (double)source.Height);

            var focusCurveFill =
                Crop(
                    userCase1320,
                    focusTargetX,
                    focusTargetY,
                    focusTargetRight -
                    focusTargetX,
                    focusTargetBottom -
                    focusTargetY);
            var focusLeafPetal =
                Crop(
                    leafPetal1320,
                    focusTargetX,
                    focusTargetY,
                    focusTargetRight -
                    focusTargetX,
                    focusTargetBottom -
                    focusTargetY);

            IndexedBmp.Write(
                Path.Combine(
                    outputDir,
                    "B996A_focus_source_green_leaf.bmp"),
                focusSource,
                bmp.XPixelsPerMeter,
                bmp.YPixelsPerMeter);
            IndexedBmp.Write(
                Path.Combine(
                    outputDir,
                    "B996A_focus_curve_fill_800x1320.bmp"),
                focusCurveFill,
                bmp.XPixelsPerMeter,
                bmp.YPixelsPerMeter);
            IndexedBmp.Write(
                Path.Combine(
                    outputDir,
                    "B996A_focus_leaf_petal_800x1320.bmp"),
                focusLeafPetal,
                bmp.XPixelsPerMeter,
                bmp.YPixelsPerMeter);

            var focusDifference =
                PixelDifferenceCount(
                    focusCurveFill,
                    focusLeafPetal);

            Console.WriteLine(
                $"[leaf-petal-focus] B996A green leaf: source={focusSource.Width}x{focusSource.Height}, " +
                $"target={focusLeafPetal.Width}x{focusLeafPetal.Height}, " +
                $"leaf-vs-curvefill changed={focusDifference:N0}px");

            File.WriteAllText(
                Path.Combine(
                    outputDir,
                    "B996A_leaf_petal_800x1320_report.md"),
                BuildLeafPetalCaseReport(
                    leafPetalDiagnostics,
                    leafPetalWatch.Elapsed.TotalSeconds));

            Console.WriteLine(
                $"[leaf-petal] B996A 800x1320: regions={leafPetalDiagnostics.Regions:N0}, " +
                $"candidates={leafPetalDiagnostics.Candidates:N0}, refined={leafPetalDiagnostics.Refined:N0}, " +
                $"boundary={leafPetalDiagnostics.BoundaryCurveRefined:N0}/{leafPetalDiagnostics.BoundaryCurvePixelsChanged:N0}px, " +
                $"center={leafPetalDiagnostics.CenterlineRefined:N0}/{leafPetalDiagnostics.CenterlinePixelsChanged:N0}px, " +
                $"axisReject={leafPetalDiagnostics.RejectedByAxis:N0}, fitReject={leafPetalDiagnostics.RejectedByFit:N0}, " +
                $"changed={leafPetalDiagnostics.BoundaryPixelsChanged:N0}, time={leafPetalWatch.Elapsed.TotalSeconds:0.000}s");

            var leafPetalStages =
                LeafPetalArcScaleEngine.AnalyzeCandidateStages(
                    source,
                    targetWidth: 800,
                    targetHeight: 1320);

            WriteLeafPetalCandidateAudit(
                Path.Combine(
                    outputDir,
                    "B996A_leaf_petal_candidates.csv"),
                leafPetalStages);

            var protectedLeafStrokeColors =
                ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                    source);
            var rawLeafRegions =
                LeafPetalRegionExtractor.Extract(
                    source);
            var rawLeafLobes =
                rawLeafRegions
                    .Where(region =>
                        !protectedLeafStrokeColors.Contains(
                            region.Color))
                    .SelectMany(region =>
                        LeafPetalLobeExtractor.Extract(
                            source,
                            region))
                    .ToArray();

            WriteLeafPetalRawLobeAudit(
                Path.Combine(
                    outputDir,
                    "B996A_leaf_petal_raw_lobes.csv"),
                rawLeafLobes);

            foreach (var stage in leafPetalStages
                         .Where(stage =>
                             stage.IsSubLobe &&
                             stage.MinX <= 190 &&
                             stage.MaxX >= 90 &&
                             stage.MinY <= 245 &&
                             stage.MaxY >= 85)
                         .Take(24))
            {
                Console.WriteLine(
                    $"[leaf-petal-candidate] color={stage.Color}, " +
                    $"bbox=({stage.MinX},{stage.MinY})-({stage.MaxX},{stage.MaxY}), area={stage.Area}, " +
                    $"sub={stage.IsSubLobe}, elong={stage.Elongation:0.000}, axis={stage.AxisBuilt}, " +
                    $"fit={stage.ElegantFitSafe}, boundary={stage.BoundaryCurveBuilt}, " +
                    $"outline={stage.OutlineCoverage:0.000}, controls={stage.LeftControls}/{stage.RightControls}, " +
                    $"path={stage.LeftSourcePathPixels}/{stage.RightSourcePathPixels}, " +
                    $"round={stage.LeftRoundness:0.000}/{stage.RightRoundness:0.000} -> " +
                    $"{stage.TargetLeftRoundness:0.000}/{stage.TargetRightRoundness:0.000}, " +
                    $"pairAdjusted={stage.TargetPairAdjusted}");
            }

            var userCase1800 =
                DesignResizer.Scale(
                    source,
                    800,
                    1800,
                    ScaleMode.CurveFill,
                    fixture.Warp,
                    fixture.Weft,
                    fixture.Warp,
                    fixture.Weft);

            IndexedBmp.Write(
                Path.Combine(
                    outputDir,
                    fixture.Name +
                    "_curve_fill_toolfaithful_800x1800.bmp"),
                userCase1800,
                bmp.XPixelsPerMeter,
                bmp.YPixelsPerMeter);
        }

        var directNearest =
            DesignResizer.Scale(
                source,
                directWidth,
                directHeight,
                ScaleMode.NearestNeighbor);

        var roundTripAgreement =
            PixelAgreement(
                source,
                roundTrip);
        var nearestRoundTripAgreement =
            PixelAgreement(
                source,
                roundTripNearest);

        var roundTripNearAgreement =
            PixelNearAgreement(
                source,
                roundTrip,
                radius: 1);
        var nearestRoundTripNearAgreement =
            PixelNearAgreement(
                source,
                roundTripNearest,
                radius: 1);

        var sourceEdge =
            EdgeDensity(source);
        var smallEdge =
            EdgeDensity(small);
        var roundTripEdge =
            EdgeDensity(roundTrip);
        var nearestRoundTripEdge =
            EdgeDensity(roundTripNearest);
        var directEdge =
            EdgeDensity(direct);
        var directNearestEdge =
            EdgeDensity(directNearest);

        var sourceThin =
            AnalyzeThinStructure(source);
        var roundTripThin =
            AnalyzeThinStructure(roundTrip);
        var nearestRoundTripThin =
            AnalyzeThinStructure(
                roundTripNearest);
        var directThin =
            AnalyzeThinStructure(direct);
        var directNearestThin =
            AnalyzeThinStructure(
                directNearest);

        var exactLrSource =
            HasExactMirror(
                source,
                leftRight: true);
        var exactTbSource =
            HasExactMirror(
                source,
                leftRight: false);
        var exactLrRound =
            HasExactMirror(
                roundTrip,
                leftRight: true);
        var exactTbRound =
            HasExactMirror(
                roundTrip,
                leftRight: false);

        var paletteSafe =
            UsedIndices(small)
                .All(sourceUsed.Contains) &&
            UsedIndices(roundTrip)
                .All(sourceUsed.Contains) &&
            UsedIndices(direct)
                .All(sourceUsed.Contains);

        var colorDrift =
            ColorDistributionDistance(
                source,
                roundTrip);
        var nearestColorDrift =
            ColorDistributionDistance(
                source,
                roundTripNearest);

        var directStrokeInflation =
            SafeRatio(
                directThin.MeanEstimatedThickness,
                sourceThin.MeanEstimatedThickness);
        var nearestStrokeInflation =
            SafeRatio(
                directNearestThin.MeanEstimatedThickness,
                sourceThin.MeanEstimatedThickness);

        var name =
            fixture.Name;

        IndexedBmp.Write(
            Path.Combine(
                outputDir,
                $"{name}_small_80.bmp"),
            small,
            bmp.XPixelsPerMeter,
            bmp.YPixelsPerMeter);
        IndexedBmp.Write(
            Path.Combine(
                outputDir,
                $"{name}_roundtrip_rugscale.bmp"),
            roundTrip,
            bmp.XPixelsPerMeter,
            bmp.YPixelsPerMeter);
        IndexedBmp.Write(
            Path.Combine(
                outputDir,
                $"{name}_roundtrip_nearest.bmp"),
            roundTripNearest,
            bmp.XPixelsPerMeter,
            bmp.YPixelsPerMeter);
        IndexedBmp.Write(
            Path.Combine(
                outputDir,
                $"{name}_enlarge_160_rugscale.bmp"),
            direct,
            bmp.XPixelsPerMeter,
            bmp.YPixelsPerMeter);
        IndexedBmp.Write(
            Path.Combine(
                outputDir,
                $"{name}_enlarge_160_nearest.bmp"),
            directNearest,
            bmp.XPixelsPerMeter,
            bmp.YPixelsPerMeter);

        var row =
            new DesignAuditRow(
                fixture.Name,
                source.Width,
                source.Height,
                fixture.Warp,
                fixture.Weft,
                shrinkWidth,
                shrinkHeight,
                directWidth,
                directHeight,
                shrinkSeconds,
                roundTripSeconds,
                nearestRoundTripSeconds,
                directSeconds,
                paletteSafe,
                roundTripAgreement,
                nearestRoundTripAgreement,
                roundTripNearAgreement,
                nearestRoundTripNearAgreement,
                sourceEdge,
                smallEdge,
                roundTripEdge,
                nearestRoundTripEdge,
                directEdge,
                directNearestEdge,
                colorDrift,
                nearestColorDrift,
                sourceThin,
                roundTripThin,
                nearestRoundTripThin,
                directThin,
                directNearestThin,
                directStrokeInflation,
                nearestStrokeInflation,
                directStyleDiagnostics.AcceptedComponents,
                directStyleDiagnostics.PixelCordComponents,
                directStyleDiagnostics.RedrawnChains,
                directStyleDiagnostics.RedrawnPixels,
                directStyleDiagnostics.LearnedCurves,
                directStyleDiagnostics.LearnedThroughPoints,
                directStyleDiagnostics.LearnedSpline,
                directStyleDiagnostics.LearnedBezier,
                directStyleDiagnostics.LearnedEllipses,
                directStyleDiagnostics.GraphFallbacks,
                directStyleDiagnostics.StyleFitCacheHits,
                directStyleDiagnostics.CurveSafetyFallbacks,
                directStyleDiagnostics.CorridorClippedPixels,
                directStyleDiagnostics.RegionOwnershipCorrections,
                directStyleDiagnostics.BarrierCrossingCorrections,
                directStyleDiagnostics.MeanLearnedRoundness,
                exactLrSource,
                exactLrRound,
                exactTbSource,
                exactTbRound);

        Console.WriteLine(
            $"[curve-suite] {fixture.Name}: exact={roundTripAgreement:P2}, near={roundTripNearAgreement:P2}, " +
            $"stroke x={directStrokeInflation:0.000} (NN {nearestStrokeInflation:0.000}), direct={directSeconds:0.000}s, " +
            $"learned={directStyleDiagnostics.LearnedCurves:N0} " +
            $"(TP {directStyleDiagnostics.LearnedThroughPoints:N0}, S {directStyleDiagnostics.LearnedSpline:N0}, B {directStyleDiagnostics.LearnedBezier:N0}, " +
            $"E {directStyleDiagnostics.LearnedEllipses:N0}), fallback={directStyleDiagnostics.GraphFallbacks:N0}, " +
            $"safety={directStyleDiagnostics.CurveSafetyFallbacks:N0}, clipped={directStyleDiagnostics.CorridorClippedPixels:N0}, " +
            $"ownership={directStyleDiagnostics.RegionOwnershipCorrections:N0}, barriers={directStyleDiagnostics.BarrierCrossingCorrections:N0}, " +
            $"ribbons={directStyleDiagnostics.RibbonArcRefined:N0}/{directStyleDiagnostics.RibbonArcCandidates:N0} " +
            $"({directStyleDiagnostics.RibbonArcPixelsChanged:N0}px; tool={directStyleDiagnostics.RibbonArcCurveToolFits:N0} " +
            $"[TP={directStyleDiagnostics.RibbonArcCurveToolThroughPointsFits:N0}, S={directStyleDiagnostics.RibbonArcCurveToolSplineFits:N0}, " +
            $"B={directStyleDiagnostics.RibbonArcCurveToolBezierFits:N0}, r={directStyleDiagnostics.RibbonArcCurveToolMeanRoundness:0.000}], " +
            $"throughGeo={directStyleDiagnostics.RibbonArcGeometricThroughFits:N0}/{directStyleDiagnostics.RibbonArcGeometricThroughAttempts:N0} " +
            $"r={directStyleDiagnostics.RibbonArcGeometricThroughMeanRoundness:0.000} " +
            $"p95={directStyleDiagnostics.RibbonArcGeometricThroughMaxP95Deviation:0.000}, " +
            $"oval={directStyleDiagnostics.RibbonArcBroadOvalFits:N0}/{directStyleDiagnostics.RibbonArcBroadOvalAttempts:N0}, " +
            $"cubic={directStyleDiagnostics.RibbonArcCubicBezierFits:N0}, outlined={directStyleDiagnostics.RibbonArcOutlinedRefined:N0}), " +
            $"cache={directStyleDiagnostics.StyleFitCacheHits:N0}, round={directStyleDiagnostics.MeanLearnedRoundness:0.000}");

        return row;
    }

    private static DesignDocument ScaleCurveFillWithDiagnostics(
        DesignDocument source,
        int width,
        int height,
        int sourceWarp,
        int sourceWeft,
        int targetWarp,
        int targetWeft,
        out ToolFaithfulOverlayReport diagnostics)
    {
        var result =
            new DesignDocument(
                width,
                height,
                source.Palette);

        diagnostics =
            CurveFillScaleEngine.ResizeWithDiagnostics(
                source,
                result,
                sourceWarp,
                sourceWeft,
                targetWarp,
                targetWeft);

        return result;
    }

    private static void WriteRibbonCandidateAudit(
        string path,
        IReadOnlyList<RibbonArcCandidateStage> stages)
    {
        var sb =
            new StringBuilder();

        sb.AppendLine(
            "color,min_x,min_y,max_x,max_y,area,elongation,boundary_ratio,bounding_fill,broad_sparse_arch,prefilter,centerline,skeleton_pixels,endpoints,principal_path_pixels,path_coverage,designer_ribbon,fit_kind,curve_family,roundness,fit_safe,max_deviation,curvature_flips,accepted,status");

        foreach (var stage in stages)
        {
            sb.Append(stage.Color).Append(',');
            sb.Append(stage.MinX).Append(',');
            sb.Append(stage.MinY).Append(',');
            sb.Append(stage.MaxX).Append(',');
            sb.Append(stage.MaxY).Append(',');
            sb.Append(stage.Area).Append(',');
            sb.Append(stage.Elongation.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.BoundaryRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.BoundingFillRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.BroadSparseArch ? 1 : 0).Append(',');
            sb.Append(stage.PrefilterAccepted ? 1 : 0).Append(',');
            sb.Append(stage.CenterlineBuilt ? 1 : 0).Append(',');
            sb.Append(stage.SkeletonPixels).Append(',');
            sb.Append(stage.Endpoints).Append(',');
            sb.Append(stage.PrincipalPathPixels).Append(',');
            sb.Append(stage.PrincipalPathCoverage.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.DesignerRibbon ? 1 : 0).Append(',');
            sb.Append(stage.FitKind).Append(',');
            sb.Append(stage.CurveFamily).Append(',');
            sb.Append(stage.Roundness.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.FitSafe ? 1 : 0).Append(',');
            sb.Append(stage.MaximumDeviation.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.CurvatureSignFlips).Append(',');
            sb.Append(stage.Accepted ? 1 : 0).Append(',');
            sb.Append(stage.Status).AppendLine();
        }

        File.WriteAllText(
            path,
            sb.ToString());
    }

    private static void WriteLeafPetalRawLobeAudit(
        string path,
        IReadOnlyList<LeafPetalRegion> lobes)
    {
        var sb =
            new StringBuilder();

        sb.AppendLine(
            "color,min_x,min_y,max_x,max_y,area,width,height,boundary_pixels");

        foreach (var lobe in lobes
                     .OrderBy(lobe => lobe.Color)
                     .ThenBy(lobe => lobe.MinY)
                     .ThenBy(lobe => lobe.MinX))
        {
            sb.Append(lobe.Color).Append(',');
            sb.Append(lobe.MinX).Append(',');
            sb.Append(lobe.MinY).Append(',');
            sb.Append(lobe.MaxX).Append(',');
            sb.Append(lobe.MaxY).Append(',');
            sb.Append(lobe.Area).Append(',');
            sb.Append(lobe.Width).Append(',');
            sb.Append(lobe.Height).Append(',');
            sb.Append(lobe.BoundaryPixels.Count).AppendLine();

            if (lobe.Color == 8 &&
                lobe.MinX <= 190 &&
                lobe.MaxX >= 90 &&
                lobe.MinY <= 245 &&
                lobe.MaxY >= 85)
            {
                Console.WriteLine(
                    $"[leaf-petal-raw-lobe] color={lobe.Color}, " +
                    $"bbox=({lobe.MinX},{lobe.MinY})-({lobe.MaxX},{lobe.MaxY}), " +
                    $"area={lobe.Area}, size={lobe.Width}x{lobe.Height}, boundary={lobe.BoundaryPixels.Count}");
            }
        }

        File.WriteAllText(
            path,
            sb.ToString());
    }

    private static void WriteLeafPetalCandidateAudit(
        string path,
        IReadOnlyList<LeafPetalCandidateStage> stages)
    {
        var sb =
            new StringBuilder();

        sb.AppendLine(
            "color,min_x,min_y,max_x,max_y,area,is_sub_lobe,elongation,axis_built,elegant_fit_safe,boundary_curve_built,outline_coverage,left_controls,right_controls,left_source_path_pixels,right_source_path_pixels,left_roundness,right_roundness,target_left_roundness,target_right_roundness,target_pair_adjusted");

        foreach (var stage in stages)
        {
            sb.Append(stage.Color).Append(',');
            sb.Append(stage.MinX).Append(',');
            sb.Append(stage.MinY).Append(',');
            sb.Append(stage.MaxX).Append(',');
            sb.Append(stage.MaxY).Append(',');
            sb.Append(stage.Area).Append(',');
            sb.Append(stage.IsSubLobe ? 1 : 0).Append(',');
            sb.Append(stage.Elongation.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.AxisBuilt ? 1 : 0).Append(',');
            sb.Append(stage.ElegantFitSafe ? 1 : 0).Append(',');
            sb.Append(stage.BoundaryCurveBuilt ? 1 : 0).Append(',');
            sb.Append(stage.OutlineCoverage.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.LeftControls).Append(',');
            sb.Append(stage.RightControls).Append(',');
            sb.Append(stage.LeftSourcePathPixels).Append(',');
            sb.Append(stage.RightSourcePathPixels).Append(',');
            sb.Append(stage.LeftRoundness.ToString("0.000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.RightRoundness.ToString("0.000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.TargetLeftRoundness.ToString("0.000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.TargetRightRoundness.ToString("0.000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(stage.TargetPairAdjusted ? 1 : 0).AppendLine();
        }

        File.WriteAllText(
            path,
            sb.ToString());
    }

    private static string BuildLeafPetalCaseReport(
        LeafPetalArcDiagnostics diagnostics,
        double seconds)
    {
        var sb =
            new StringBuilder();

        sb.AppendLine(
            "# B996A 800x1320 Leaf / Petal Arcs audit");
        sb.AppendLine();
        sb.AppendLine(
            "Specialist mode starts from the safe Curve & Fill result, then only refines elongated filled regions whose centreline, width profile and elegant-arc fit all pass safety checks.");
        sb.AppendLine();
        sb.AppendLine(
            $"- Source regions scanned: **{diagnostics.Regions:N0}**");
        sb.AppendLine(
            $"- Leaf / petal candidates: **{diagnostics.Candidates:N0}**");
        sb.AppendLine(
            $"- Refined regions: **{diagnostics.Refined:N0}**");
        sb.AppendLine(
            $"- Paired source-outline Curve refinements: **{diagnostics.BoundaryCurveRefined:N0}** regions / **{diagnostics.BoundaryCurvePixelsChanged:N0}** changed pixels");
        sb.AppendLine(
            $"- Centreline/width-profile fallback refinements: **{diagnostics.CenterlineRefined:N0}** regions / **{diagnostics.CenterlinePixelsChanged:N0}** changed pixels");
        sb.AppendLine(
            $"- Rejected by centreline construction: **{diagnostics.RejectedByAxis:N0}**");
        sb.AppendLine(
            $"- Rejected by elegant-arc safety: **{diagnostics.RejectedByFit:N0}**");
        sb.AppendLine(
            $"- Boundary pixels changed: **{diagnostics.BoundaryPixelsChanged:N0}**");
        sb.AppendLine(
            $"- Runtime: **{seconds:0.000}s**");
        sb.AppendLine();
        sb.AppendLine(
            "All final pixels still pass Curve & Fill indexed-region ownership and RugCAD Pixel-Cord outline replay.");

        return sb.ToString();
    }

    private static DesignDocument Crop(
        DesignDocument source,
        int x,
        int y,
        int width,
        int height)
    {
        var left =
            Math.Clamp(
                x,
                0,
                source.Width);
        var top =
            Math.Clamp(
                y,
                0,
                source.Height);
        var right =
            Math.Clamp(
                x +
                Math.Max(
                    0,
                    width),
                left,
                source.Width);
        var bottom =
            Math.Clamp(
                y +
                Math.Max(
                    0,
                    height),
                top,
                source.Height);
        var result =
            new DesignDocument(
                Math.Max(
                    1,
                    right -
                    left),
                Math.Max(
                    1,
                    bottom -
                    top),
                source.Palette);

        for (var row = 0;
             row <
             result.Height;
             row++)
        {
            for (var column = 0;
                 column <
                 result.Width;
                 column++)
            {
                result.SetPixel(
                    column,
                    row,
                    source.GetPixel(
                        Math.Min(
                            source.Width - 1,
                            left +
                            column),
                        Math.Min(
                            source.Height - 1,
                            top +
                            row)));
            }
        }

        return result;
    }

    private static int PixelDifferenceCount(
        DesignDocument left,
        DesignDocument right)
    {
        if (left.Width !=
                right.Width ||
            left.Height !=
                right.Height)
        {
            return -1;
        }

        var differences = 0;

        for (var y = 0;
             y < left.Height;
             y++)
        {
            for (var x = 0;
                 x < left.Width;
                 x++)
            {
                if (left.GetPixel(
                        x,
                        y) !=
                    right.GetPixel(
                        x,
                        y))
                {
                    differences++;
                }
            }
        }

        return differences;
    }

    private static ThinStructureStats AnalyzeThinStructure(
        DesignDocument document)
    {
        var width =
            document.Width;
        var height =
            document.Height;
        var total =
            checked(
                width *
                height);
        var visited =
            new bool[total];
        var queue =
            new int[total];

        ReadOnlySpan<int> dx =
            [-1, 0, 1, -1, 1, -1, 0, 1];
        ReadOnlySpan<int> dy =
            [-1, -1, -1, 0, 0, 1, 1, 1];

        var componentCount = 0;
        var thinCount = 0;
        long thinArea = 0;
        double thicknessSum = 0d;
        var curvedCount = 0;

        for (var start = 0;
             start < total;
             start++)
        {
            if (visited[start])
                continue;

            var sx =
                start % width;
            var sy =
                start / width;
            var color =
                document.GetPixel(
                    sx,
                    sy);

            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;

            var area = 0;
            var boundary = 0;
            var minX = sx;
            var maxX = sx;
            var minY = sy;
            var maxY = sy;

            while (head < tail)
            {
                var pixel =
                    queue[head++];
                area++;

                var x =
                    pixel % width;
                var y =
                    pixel / width;
                minX =
                    Math.Min(
                        minX,
                        x);
                maxX =
                    Math.Max(
                        maxX,
                        x);
                minY =
                    Math.Min(
                        minY,
                        y);
                maxY =
                    Math.Max(
                        maxY,
                        y);

                var isBoundary =
                    false;

                for (var direction = 0;
                     direction < 8;
                     direction++)
                {
                    var nx =
                        x +
                        dx[direction];
                    var ny =
                        y +
                        dy[direction];

                    if (nx < 0 ||
                        nx >= width ||
                        ny < 0 ||
                        ny >= height)
                    {
                        isBoundary = true;
                        continue;
                    }

                    var next =
                        ny *
                        width +
                        nx;

                    if (document.GetPixel(
                            nx,
                            ny) !=
                        color)
                    {
                        isBoundary = true;
                        continue;
                    }

                    if (visited[next])
                        continue;

                    visited[next] = true;
                    queue[tail++] =
                        next;
                }

                if (isBoundary)
                    boundary++;
            }

            componentCount++;

            if (area < 4)
                continue;

            var boxWidth =
                maxX -
                minX +
                1;
            var boxHeight =
                maxY -
                minY +
                1;
            var aspect =
                Math.Max(
                    boxWidth,
                    boxHeight) /
                (double)Math.Max(
                    1,
                    Math.Min(
                        boxWidth,
                        boxHeight));
            var fill =
                area /
                (double)Math.Max(
                    1,
                    boxWidth *
                    boxHeight);
            var boundaryRatio =
                boundary /
                (double)Math.Max(
                    1,
                    area);

            // For thin carpet strokes, perimeter/2 is a useful centreline-length estimator.
            // area / length then approximates pixel thickness without requiring an expensive
            // whole-design skeleton just for audit metrics.
            var centerlineEstimate =
                Math.Max(
                    1d,
                    boundary *
                    0.5);
            var thickness =
                area /
                centerlineEstimate;

            var thin =
                thickness <= 5.5 &&
                (boundaryRatio >= 0.36 ||
                 fill <= 0.50 ||
                 aspect >= 2.0);

            if (!thin)
                continue;

            thinCount++;
            thinArea +=
                area;
            thicknessSum +=
                thickness;

            // A compact-but-sparse thin component is usually a curve/scroll rather than a straight
            // horizontal/vertical technical line.
            if (aspect < 5.0 &&
                fill < 0.42 &&
                boxWidth >= 4 &&
                boxHeight >= 4)
            {
                curvedCount++;
            }
        }

        return new ThinStructureStats(
            componentCount,
            thinCount,
            curvedCount,
            thinArea,
            thinCount == 0
                ? 0d
                : thicknessSum /
                  thinCount);
    }

    private static double PixelAgreement(
        DesignDocument expected,
        DesignDocument actual)
    {
        if (expected.Width != actual.Width ||
            expected.Height != actual.Height)
        {
            return 0d;
        }

        long matches = 0;
        var total =
            (long)expected.Width *
            expected.Height;

        for (var y = 0;
             y < expected.Height;
             y++)
        {
            for (var x = 0;
                 x < expected.Width;
                 x++)
            {
                if (expected.GetPixel(x, y) ==
                    actual.GetPixel(x, y))
                {
                    matches++;
                }
            }
        }

        return matches /
               (double)Math.Max(
                   1L,
                   total);
    }

    private static double PixelNearAgreement(
        DesignDocument expected,
        DesignDocument actual,
        int radius)
    {
        if (expected.Width != actual.Width ||
            expected.Height != actual.Height)
        {
            return 0d;
        }

        long matches = 0;
        var total =
            (long)expected.Width *
            expected.Height;

        for (var y = 0;
             y < expected.Height;
             y++)
        {
            for (var x = 0;
                 x < expected.Width;
                 x++)
            {
                var color =
                    expected.GetPixel(
                        x,
                        y);
                var found =
                    false;

                for (var dy = -radius;
                     dy <= radius &&
                     !found;
                     dy++)
                {
                    var py =
                        y +
                        dy;
                    if (py < 0 ||
                        py >= actual.Height)
                    {
                        continue;
                    }

                    for (var dx = -radius;
                         dx <= radius;
                         dx++)
                    {
                        var px =
                            x +
                            dx;
                        if (px < 0 ||
                            px >= actual.Width)
                        {
                            continue;
                        }

                        if (actual.GetPixel(
                                px,
                                py) !=
                            color)
                        {
                            continue;
                        }

                        found = true;
                        break;
                    }
                }

                if (found)
                    matches++;
            }
        }

        return matches /
               (double)Math.Max(
                   1L,
                   total);
    }

    private static double EdgeDensity(
        DesignDocument document)
    {
        long edges = 0;
        long pairs = 0;

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                var value =
                    document.GetPixel(
                        x,
                        y);

                if (x + 1 <
                    document.Width)
                {
                    pairs++;
                    if (value !=
                        document.GetPixel(
                            x + 1,
                            y))
                    {
                        edges++;
                    }
                }

                if (y + 1 <
                    document.Height)
                {
                    pairs++;
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
                   1L,
                   pairs);
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

    private static double ColorDistributionDistance(
        DesignDocument source,
        DesignDocument target)
    {
        Span<long> sourceCounts =
            stackalloc long[256];
        Span<long> targetCounts =
            stackalloc long[256];

        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                sourceCounts[
                    source.GetPixel(
                        x,
                        y)]++;
            }
        }

        for (var y = 0;
             y < target.Height;
             y++)
        {
            for (var x = 0;
                 x < target.Width;
                 x++)
            {
                targetCounts[
                    target.GetPixel(
                        x,
                        y)]++;
            }
        }

        var sourceTotal =
            (double)source.Width *
            source.Height;
        var targetTotal =
            (double)target.Width *
            target.Height;
        var sum = 0d;

        for (var color = 0;
             color < 256;
             color++)
        {
            sum +=
                Math.Abs(
                    sourceCounts[color] /
                    sourceTotal -
                    targetCounts[color] /
                    targetTotal);
        }

        return sum * 0.5;
    }

    private static bool HasExactMirror(
        DesignDocument document,
        bool leftRight)
    {
        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                var mirrorX =
                    leftRight
                        ? document.Width - 1 - x
                        : x;
                var mirrorY =
                    leftRight
                        ? y
                        : document.Height - 1 - y;

                if (document.GetPixel(
                        x,
                        y) !=
                    document.GetPixel(
                        mirrorX,
                        mirrorY))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static double SafeRatio(
        double value,
        double reference) =>
        reference <= 1e-9
            ? 0d
            : value /
              reference;

    private static string BuildMarkdown(
        CurveSuiteReport report)
    {
        var sb =
            new StringBuilder();

        sb.AppendLine(
            "# RugScale Curve & Fill real-raster audit");
        sb.AppendLine();
        sb.AppendLine(
            "The four supplied indexed carpet designs are audited as curve-heavy source ground truth. " +
            "Round-trip = same-quality 80% Curve & Fill shrink followed by Curve & Fill enlargement back to the exact original raster size. " +
            "Direct enlargement = 160% physical size at the SAME warp/weft quality. This deliberately crosses CurveScaleEngine's source-guided redraw threshold, so the audit exercises real curve reconstruction instead of the moderate-size categorical baseline. Stroke weight should stay close to the source instead of block-scaling with physical size.");
        sb.AppendLine();

        sb.AppendLine(
            "| Design | Quality | Source | Roundtrip exact | NN exact | Roundtrip ±1px | NN ±1px | Stroke x | NN stroke x | Palette |");
        sb.AppendLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (var row in report.Designs)
        {
            sb.AppendLine(
                $"| {row.Name} | {row.Warp}×{row.Weft} | {row.SourceWidth}×{row.SourceHeight} | " +
                $"{row.RoundTripPixelAgreement:P2} | {row.NearestRoundTripPixelAgreement:P2} | " +
                $"{row.RoundTripNearAgreement:P2} | {row.NearestRoundTripNearAgreement:P2} | " +
                $"{row.DirectStrokeInflation:0.000} | {row.NearestStrokeInflation:0.000} | " +
                $"{(row.PaletteSafe ? "SAFE" : "FAIL")} |");
        }

        sb.AppendLine();
        sb.AppendLine(
            "## Curve-tool inverse-model self-training");
        sb.AppendLine();

        foreach (var group in report.StyleTraining
                     .GroupBy(row => row.SourceType)
                     .OrderBy(group => group.Key))
        {
            var accepted =
                group
                    .Where(row => row.Accepted)
                    .ToArray();
            var correct =
                accepted.Count(row => row.FamilyCorrect);

            var learnedExact =
                group.Average(row =>
                    row.TargetExactF1);
            var graphExact =
                group.Average(row =>
                    row.FallbackTargetExactF1);

            sb.AppendLine(
                $"- **{group.Key}**: {accepted.Length:N0}/{group.Count():N0} accepted; " +
                $"{correct:N0}/{Math.Max(1, accepted.Length):N0} family-correct; " +
                $"target exact F1 {learnedExact:P2} vs graph {graphExact:P2} " +
                $"({(learnedExact - graphExact):+0.0000;-0.0000;0.0000}); " +
                $"±1px {group.Average(row => row.TargetNearF1):P2}.");
        }

        var roundnessErrors =
            report.StyleTraining
                .Where(row =>
                    row.SourceType == CurveType.SplineThroughPoints &&
                    row.FamilyCorrect &&
                    row.RoundnessError.HasValue)
                .Select(row => row.RoundnessError!.Value)
                .ToArray();

        if (roundnessErrors.Length > 0)
        {
            sb.AppendLine(
                $"- Through-Points roundness MAE: **{roundnessErrors.Average():0.000}**.");
        }

        sb.AppendLine();
        sb.AppendLine(
            "## Source-trained Curve-tool model");
        sb.AppendLine();
        sb.AppendLine(
            "The direct 160% run fits trusted source 1x1 / Pixel-Cord chains back to RugCAD's own " +
            "Curve families. A learned fit is used only when it beats the complexity-regularized " +
            "polyline baseline; otherwise exact source-graph replay remains the fallback.");
        sb.AppendLine();
        sb.AppendLine(
            "| Design | Accepted comps | Pixel Cord comps | Learned open curves | Through Points | Spline | Bezier | Native ellipse | Graph fallback | Safety fallback | Corridor clipped | Ownership fixes | Barrier fixes | Cache hits | Mean roundness |");
        sb.AppendLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var row in report.Designs)
        {
            sb.AppendLine(
                $"| {row.Name} | {row.ToolAcceptedComponents:N0} | {row.ToolPixelCordComponents:N0} | " +
                $"{row.ToolLearnedCurves:N0} | {row.ToolLearnedThroughPoints:N0} | {row.ToolLearnedSpline:N0} | " +
                $"{row.ToolLearnedBezier:N0} | {row.ToolLearnedEllipses:N0} | {row.ToolGraphFallbacks:N0} | " +
                $"{row.ToolCurveSafetyFallbacks:N0} | {row.ToolCorridorClippedPixels:N0} | " +
                $"{row.ToolRegionOwnershipCorrections:N0} | {row.ToolBarrierCrossingCorrections:N0} | " +
                $"{row.ToolStyleFitCacheHits:N0} | {row.ToolMeanLearnedRoundness:0.000} |");
        }

        sb.AppendLine();
        sb.AppendLine(
            "## Thin / curve structure");
        sb.AppendLine();
        sb.AppendLine(
            "| Design | Source thin | Source curved | Source thickness | Roundtrip thin | Roundtrip thickness | Direct thickness | NN direct thickness |");
        sb.AppendLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var row in report.Designs)
        {
            sb.AppendLine(
                $"| {row.Name} | {row.SourceThin.ThinComponents:N0} | {row.SourceThin.CurveLikeComponents:N0} | " +
                $"{row.SourceThin.MeanEstimatedThickness:0.000} | {row.RoundTripThin.ThinComponents:N0} | " +
                $"{row.RoundTripThin.MeanEstimatedThickness:0.000} | {row.DirectThin.MeanEstimatedThickness:0.000} | " +
                $"{row.DirectNearestThin.MeanEstimatedThickness:0.000} |");
        }

        sb.AppendLine();
        sb.AppendLine(
            "## Edge / palette-area consistency");
        sb.AppendLine();
        sb.AppendLine(
            "| Design | Source edge | Roundtrip edge | NN roundtrip edge | Direct edge | NN direct edge | Color drift | NN drift |");
        sb.AppendLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var row in report.Designs)
        {
            sb.AppendLine(
                $"| {row.Name} | {row.SourceEdgeDensity:P2} | {row.RoundTripEdgeDensity:P2} | " +
                $"{row.NearestRoundTripEdgeDensity:P2} | {row.DirectEdgeDensity:P2} | {row.DirectNearestEdgeDensity:P2} | " +
                $"{row.ColorDistributionDrift:P2} | {row.NearestColorDistributionDrift:P2} |");
        }

        sb.AppendLine();
        sb.AppendLine(
            "## Exact designer symmetry");
        sb.AppendLine();
        sb.AppendLine(
            "| Design | LR source → roundtrip | TB source → roundtrip |");
        sb.AppendLine(
            "|---|---|---|");

        foreach (var row in report.Designs)
        {
            sb.AppendLine(
                $"| {row.Name} | {FormatSymmetry(row.ExactLeftRightSource, row.ExactLeftRightRoundTrip)} | " +
                $"{FormatSymmetry(row.ExactTopBottomSource, row.ExactTopBottomRoundTrip)} |");
        }

        sb.AppendLine();
        sb.AppendLine(
            "## Runtime");
        sb.AppendLine();
        sb.AppendLine(
            "| Design | Shrink 80% | RugScale roundtrip enlarge | NN roundtrip enlarge | Curve & Fill direct 160% |");
        sb.AppendLine(
            "|---|---:|---:|---:|---:|");

        foreach (var row in report.Designs)
        {
            sb.AppendLine(
                $"| {row.Name} | {row.ShrinkSeconds:0.000}s | {row.RoundTripSeconds:0.000}s | " +
                $"{row.NearestRoundTripSeconds:0.000}s | {row.DirectEnlargeSeconds:0.000}s |");
        }

        return sb.ToString();
    }

    private static string FormatSymmetry(
        bool source,
        bool target) =>
        !source
            ? "not exact"
            : target
                ? "exact → exact"
                : "exact → LOST";

    private static string? GetArg(
        IReadOnlyList<string> args,
        string name)
    {
        for (var index = 0;
             index + 1 < args.Count;
             index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private sealed record CurveStyleTrainingRow(
        string Shape,
        CurveType SourceType,
        double SourceRoundness,
        bool PixelCord,
        int SourcePixels,
        bool Accepted,
        CurveType? PredictedType,
        double? PredictedRoundness,
        bool FamilyCorrect,
        double? RoundnessError,
        double RawScore,
        double ModelMargin,
        int ControlCount,
        double TargetExactF1,
        double TargetNearF1,
        double FallbackTargetExactF1,
        double FallbackTargetNearF1,
        double TargetNearGain);

    private sealed record ThinStructureStats(
        int AllComponents,
        int ThinComponents,
        int CurveLikeComponents,
        long ThinPixelArea,
        double MeanEstimatedThickness);

    private sealed record DesignAuditRow(
        string Name,
        int SourceWidth,
        int SourceHeight,
        int Warp,
        int Weft,
        int ShrinkWidth,
        int ShrinkHeight,
        int DirectWidth,
        int DirectHeight,
        double ShrinkSeconds,
        double RoundTripSeconds,
        double NearestRoundTripSeconds,
        double DirectEnlargeSeconds,
        bool PaletteSafe,
        double RoundTripPixelAgreement,
        double NearestRoundTripPixelAgreement,
        double RoundTripNearAgreement,
        double NearestRoundTripNearAgreement,
        double SourceEdgeDensity,
        double SmallEdgeDensity,
        double RoundTripEdgeDensity,
        double NearestRoundTripEdgeDensity,
        double DirectEdgeDensity,
        double DirectNearestEdgeDensity,
        double ColorDistributionDrift,
        double NearestColorDistributionDrift,
        ThinStructureStats SourceThin,
        ThinStructureStats RoundTripThin,
        ThinStructureStats NearestRoundTripThin,
        ThinStructureStats DirectThin,
        ThinStructureStats DirectNearestThin,
        double DirectStrokeInflation,
        double NearestStrokeInflation,
        int ToolAcceptedComponents,
        int ToolPixelCordComponents,
        int ToolRedrawnChains,
        int ToolRedrawnPixels,
        int ToolLearnedCurves,
        int ToolLearnedThroughPoints,
        int ToolLearnedSpline,
        int ToolLearnedBezier,
        int ToolLearnedEllipses,
        int ToolGraphFallbacks,
        int ToolStyleFitCacheHits,
        int ToolCurveSafetyFallbacks,
        int ToolCorridorClippedPixels,
        int ToolRegionOwnershipCorrections,
        int ToolBarrierCrossingCorrections,
        double ToolMeanLearnedRoundness,
        bool ExactLeftRightSource,
        bool ExactLeftRightRoundTrip,
        bool ExactTopBottomSource,
        bool ExactTopBottomRoundTrip);

    private sealed record CurveSuiteReport(
        DateTime GeneratedUtc,
        IReadOnlyList<DesignAuditRow> Designs,
        IReadOnlyList<CurveStyleTrainingRow> StyleTraining,
        bool PaletteSafe,
        bool ExactLeftRightPreserved,
        bool ExactTopBottomPreserved);

    private sealed record LoadedBmp(
        DesignDocument Document,
        int XPixelsPerMeter,
        int YPixelsPerMeter);

    private static class IndexedBmp
    {
        public static LoadedBmp Read(
            string path)
        {
            using var stream =
                File.OpenRead(path);
            using var reader =
                new BinaryReader(stream);

            if (reader.ReadUInt16() != 0x4D42)
                throw new InvalidDataException("Not BMP.");

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var pixelOffset =
                reader.ReadUInt32();
            var dibSize =
                reader.ReadUInt32();

            if (dibSize < 40)
                throw new InvalidDataException("Unsupported DIB.");

            var width =
                reader.ReadInt32();
            var signedHeight =
                reader.ReadInt32();
            var planes =
                reader.ReadUInt16();
            var bpp =
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
                bpp != 8 ||
                compression != 0 ||
                width <= 0 ||
                signedHeight == 0)
            {
                throw new InvalidDataException(
                    $"Expected uncompressed 8-bit indexed BMP; got {width}x{signedHeight}, bpp={bpp}, compression={compression}.");
            }

            stream.Position =
                14 +
                dibSize;

            var paletteCount =
                colorsUsed == 0
                    ? 256
                    : Math.Min(
                        256,
                        checked(
                            (int)colorsUsed));
            var colors =
                new List<RugColor>(256);

            for (var index = 0;
                 index < paletteCount;
                 index++)
            {
                var b =
                    reader.ReadByte();
                var g =
                    reader.ReadByte();
                var r =
                    reader.ReadByte();
                _ = reader.ReadByte();

                colors.Add(
                    new RugColor(
                        r,
                        g,
                        b));
            }

            while (colors.Count < 256)
                colors.Add(RugColor.Black);

            var height =
                Math.Abs(
                    signedHeight);
            var topDown =
                signedHeight < 0;
            var stride =
                (width + 3) &
                ~3;
            var document =
                new DesignDocument(
                    width,
                    height,
                    new Palette(colors));

            stream.Position =
                pixelOffset;
            var row =
                new byte[stride];

            for (var fileRow = 0;
                 fileRow < height;
                 fileRow++)
            {
                stream.ReadExactly(row);

                var y =
                    topDown
                        ? fileRow
                        : height - 1 - fileRow;

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

            return new LoadedBmp(
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
            var pixelBytes =
                checked(
                    stride *
                    document.Height);
            var pixelOffset =
                14 +
                40 +
                256 * 4;
            var fileSize =
                pixelOffset +
                pixelBytes;

            using var stream =
                File.Create(path);
            using var writer =
                new BinaryWriter(stream);

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

            for (var index = 0;
                 index < 256;
                 index++)
            {
                var color =
                    document.Palette[index];
                writer.Write(color.B);
                writer.Write(color.G);
                writer.Write(color.R);
                writer.Write((byte)0);
            }

            var row =
                new byte[stride];

            for (var y =
                     document.Height - 1;
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
