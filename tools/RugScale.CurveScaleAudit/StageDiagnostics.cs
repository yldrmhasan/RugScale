using System.Diagnostics;
using System.Globalization;
using System.Text;
using RugScale.Core.Drawing;
using RugScale.Core.IO;
using RugScale.Core.Models;

namespace RugScale.CurveScaleAudit;

/// <summary>
/// Fast training diagnostic: runs only the 160% same-quality Curve &amp; Fill enlargement and
/// measures, after every engine stage, the two defects users see first in real carpets:
///
/// 1. <b>Separator breaches</b> — two colours that are NEVER 4-adjacent in the source (they are
///    always kept apart by a Pixel-Cord / outline) but touch in the output. Every breach is a
///    visible gap in an outline. Nearest-neighbour enlargement produces zero by construction.
/// 2. <b>Cord thickness</b> — for every protected stroke colour, the share of cord pixels whose
///    thinner local run (min of horizontal / vertical run through the pixel) is 1, 2 or 3+.
///    At the same warp/weft quality a 1 px source cord must stay a continuous 1 px target cord.
///
/// Stage attribution tells which pipeline layer owns each defect, so training changes the
/// narrowest responsible layer instead of adding global smoothing.
/// </summary>
internal static class StageDiagnostics
{
    private sealed record Snapshot(
        string Stage,
        DesignDocument Document);

    internal sealed record CordStats(
        int Pixels,
        int Components,
        double Run1,
        double Run2,
        double Run3Plus,
        int DustComponents = 0);

    public static int Run(
        string inputDir,
        string outputDir,
        IEnumerable<(string Name, string File, int Warp, int Weft)> fixtures,
        bool writeImages)
    {
        Directory.CreateDirectory(
            outputDir);

        var csv =
            new StringBuilder();
        csv.AppendLine(
            "design,stage,breaches,new_breaches,top_pairs,cord_color,cord_pixels,cord_components,cord_run1,cord_run2,cord_run3plus");

        foreach (var fixture in fixtures)
        {
            var path =
                Path.Combine(
                    inputDir,
                    fixture.File);
            var image =
                IndexedBmpCodec.Read(
                    path);
            var source =
                image.Document;
            var width =
                Math.Max(
                    source.Width + 1,
                    (int)Math.Round(
                        source.Width * 1.60));
            var height =
                Math.Max(
                    source.Height + 1,
                    (int)Math.Round(
                        source.Height * 1.60));

            var strokeColors =
                ToolFaithfulPixelCordOverlay.DetectStrokePaletteRoles(
                    source)
                    .OrderBy(color => color)
                    .ToArray();
            var sourceAdjacency =
                Adjacency4(
                    source);

            var snapshots =
                new List<Snapshot>
                {
                    new(
                        "0-nearest",
                        DesignResizer.Scale(
                            source,
                            width,
                            height,
                            ScaleMode.NearestNeighbor)),
                };

            CurveFillScaleEngine.StageObserver =
                (stage, document) =>
                    snapshots.Add(
                        new Snapshot(
                            stage,
                            Clone(
                                document)));

            var watch =
                Stopwatch.StartNew();
            var output =
                new DesignDocument(
                    width,
                    height,
                    source.Palette);

            try
            {
                CurveFillScaleEngine.ResizeWithDiagnostics(
                    source,
                    output,
                    fixture.Warp,
                    fixture.Weft,
                    fixture.Warp,
                    fixture.Weft);
            }
            finally
            {
                CurveFillScaleEngine.StageObserver =
                    null;
            }

            watch.Stop();

            Console.WriteLine(
                $"[stage-diagnostics] {fixture.Name}: {source.Width}x{source.Height} -> {width}x{height}, " +
                $"{watch.Elapsed.TotalSeconds:0.0}s, stroke colours [{string.Join(",", strokeColors)}]");

            var sourceCords =
                strokeColors.ToDictionary(
                    color => color,
                    color => Cord(
                        source,
                        color));

            foreach (var color in strokeColors)
            {
                var stats =
                    sourceCords[color];
                Console.WriteLine(
                    $"  source cord {color}: px={stats.Pixels:N0} comps={stats.Components:N0} " +
                    $"run1={stats.Run1:P1} run2={stats.Run2:P1} run3+={stats.Run3Plus:P1} dust={stats.DustComponents:N0}");
                csv.AppendLine(
                    string.Join(
                        ",",
                        fixture.Name,
                        "source",
                        0,
                        0,
                        "",
                        color,
                        stats.Pixels,
                        stats.Components,
                        F(stats.Run1),
                        F(stats.Run2),
                        F(stats.Run3Plus)));
            }

            var previousBreaches = 0;
            var previousMask =
                new bool[width * height];

            foreach (var snapshot in snapshots)
            {
                var breachMask =
                    BreachMask(
                        snapshot.Document,
                        sourceAdjacency,
                        out var pairCounts);
                var breaches =
                    breachMask.Count(value => value);
                var newBreaches =
                    snapshot.Stage == "0-nearest"
                        ? breaches
                        : breachMask
                            .Where((value, index) =>
                                value &&
                                !previousMask[index])
                            .Count();
                var topPairs =
                    string.Join(
                        " ",
                        pairCounts
                            .OrderByDescending(pair => pair.Value)
                            .Take(5)
                            .Select(pair => $"{pair.Key.A}|{pair.Key.B}:{pair.Value}"));

                var localBreaches =
                    LocalBreaches(
                        source,
                        snapshot.Document);

                Console.WriteLine(
                    $"  {snapshot.Stage,-18} breaches={breaches,7:N0} new={newBreaches,7:N0} local={localBreaches,7:N0}  top {topPairs}");

                foreach (var color in strokeColors)
                {
                    var stats =
                        Cord(
                            snapshot.Document,
                            color);
                    Console.WriteLine(
                        $"      cord {color}: px={stats.Pixels:N0} comps={stats.Components:N0} dust={stats.DustComponents:N0} " +
                        $"run1={stats.Run1:P1} run2={stats.Run2:P1} run3+={stats.Run3Plus:P1}");
                    csv.AppendLine(
                        string.Join(
                            ",",
                            fixture.Name,
                            snapshot.Stage,
                            breaches,
                            newBreaches,
                            topPairs,
                            color,
                            stats.Pixels,
                            stats.Components,
                            F(stats.Run1),
                            F(stats.Run2),
                            F(stats.Run3Plus)));
                }

                if (writeImages &&
                    snapshot.Stage != "0-nearest")
                {
                    IndexedBmpCodec.Write(
                        Path.Combine(
                            outputDir,
                            $"{fixture.Name}_{snapshot.Stage}.bmp"),
                        snapshot.Document,
                        image.XPixelsPerMeter,
                        image.YPixelsPerMeter);
                    IndexedBmpCodec.Write(
                        Path.Combine(
                            outputDir,
                            $"{fixture.Name}_{snapshot.Stage}_breaches.bmp"),
                        MaskDocument(
                            breachMask,
                            width,
                            height),
                        image.XPixelsPerMeter,
                        image.YPixelsPerMeter);
                }

                if (snapshot.Stage != "0-nearest")
                {
                    previousMask =
                        breachMask;
                    previousBreaches =
                        breaches;
                }
            }

            _ = previousBreaches;
        }

        File.WriteAllText(
            Path.Combine(
                outputDir,
                "stage-diagnostics.csv"),
            csv.ToString());

        return 0;
    }

    private static string F(
        double value) =>
        value.ToString(
            "0.0000",
            CultureInfo.InvariantCulture);

    internal static bool[,] Adjacency4(
        DesignDocument document)
    {
        var result =
            new bool[256, 256];

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < document.Width;
                 x++)
            {
                var color =
                    document.GetPixel(
                        x,
                        y);

                if (x + 1 < document.Width)
                {
                    var right =
                        document.GetPixel(
                            x + 1,
                            y);
                    result[color, right] = true;
                    result[right, color] = true;
                }

                if (y + 1 < document.Height)
                {
                    var below =
                        document.GetPixel(
                            x,
                            y + 1);
                    result[color, below] = true;
                    result[below, color] = true;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Local breaches: 4-contacts whose colour pair never touches in the source within
    /// <see cref="CurveFillRibbonFidelityGuard.LocalContactRadius"/> px of the mapped location.
    /// </summary>
    internal static int LocalBreaches(
        DesignDocument source,
        DesignDocument document)
    {
        var contacts =
            new CurveFillRibbonFidelityGuard.SourceContacts(
                source,
                CurveFillRibbonFidelityGuard.SourceAdjacencyCounts(
                    source),
                document.Width,
                document.Height);
        var width =
            document.Width;
        var count = 0;

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var color =
                    document.GetPixel(
                        x,
                        y);
                var index =
                    y * width +
                    x;

                if (x + 1 < width)
                {
                    var right =
                        document.GetPixel(
                            x + 1,
                            y);

                    if (right != color &&
                        !contacts.Allowed(
                            color,
                            right,
                            index))
                    {
                        count++;
                    }
                }

                if (y + 1 < document.Height)
                {
                    var below =
                        document.GetPixel(
                            x,
                            y + 1);

                    if (below != color &&
                        !contacts.Allowed(
                            color,
                            below,
                            index))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    internal static bool[] BreachMask(
        DesignDocument document,
        bool[,] sourceAdjacency,
        out Dictionary<(byte A, byte B), int> pairCounts)
    {
        var width =
            document.Width;
        var mask =
            new bool[width * document.Height];
        var counts =
            new Dictionary<(byte A, byte B), int>();

        void Mark(
            int x,
            int y,
            byte a,
            byte b,
            int nx,
            int ny)
        {
            if (a == b ||
                sourceAdjacency[a, b])
            {
                return;
            }

            mask[y * width + x] = true;
            mask[ny * width + nx] = true;
            var key =
                a < b
                    ? (a, b)
                    : (b, a);
            counts[key] =
                counts.GetValueOrDefault(key) +
                1;
        }

        for (var y = 0;
             y < document.Height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var color =
                    document.GetPixel(
                        x,
                        y);

                if (x + 1 < width)
                {
                    Mark(
                        x,
                        y,
                        color,
                        document.GetPixel(
                            x + 1,
                            y),
                        x + 1,
                        y);
                }

                if (y + 1 < document.Height)
                {
                    Mark(
                        x,
                        y,
                        color,
                        document.GetPixel(
                            x,
                            y + 1),
                        x,
                        y + 1);
                }
            }
        }

        pairCounts =
            counts;

        return mask;
    }

    internal static CordStats Cord(
        DesignDocument document,
        byte color)
    {
        var width =
            document.Width;
        var height =
            document.Height;
        var pixels = 0;
        var run1 = 0;
        var run2 = 0;
        var run3 = 0;
        var visited =
            new bool[width * height];
        var components = 0;
        var dust = 0;
        var queue =
            new Queue<int>();

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                if (document.GetPixel(
                        x,
                        y) !=
                    color)
                {
                    continue;
                }

                pixels++;

                var left = x;
                while (left > 0 &&
                       document.GetPixel(
                           left - 1,
                           y) ==
                       color)
                {
                    left--;
                }

                var right = x;
                while (right + 1 < width &&
                       document.GetPixel(
                           right + 1,
                           y) ==
                       color)
                {
                    right++;
                }

                var top = y;
                while (top > 0 &&
                       document.GetPixel(
                           x,
                           top - 1) ==
                       color)
                {
                    top--;
                }

                var bottom = y;
                while (bottom + 1 < height &&
                       document.GetPixel(
                           x,
                           bottom + 1) ==
                       color)
                {
                    bottom++;
                }

                var run =
                    Math.Min(
                        right - left + 1,
                        bottom - top + 1);

                if (run <= 1)
                    run1++;
                else if (run == 2)
                    run2++;
                else
                    run3++;

                var key =
                    y * width +
                    x;

                if (visited[key])
                    continue;

                components++;
                visited[key] = true;
                queue.Enqueue(
                    key);
                var componentSize = 0;

                while (queue.Count > 0)
                {
                    var current =
                        queue.Dequeue();
                    componentSize++;
                    var cx =
                        current % width;
                    var cy =
                        current / width;

                    for (var dy = -1;
                         dy <= 1;
                         dy++)
                    {
                        for (var dx = -1;
                             dx <= 1;
                             dx++)
                        {
                            var nx =
                                cx + dx;
                            var ny =
                                cy + dy;

                            if (nx < 0 ||
                                ny < 0 ||
                                nx >= width ||
                                ny >= height)
                            {
                                continue;
                            }

                            var neighbour =
                                ny * width +
                                nx;

                            if (visited[neighbour] ||
                                document.GetPixel(
                                    nx,
                                    ny) !=
                                color)
                            {
                                continue;
                            }

                            visited[neighbour] = true;
                            queue.Enqueue(
                                neighbour);
                        }
                    }
                }

                if (componentSize <= 4)
                    dust++;
            }
        }

        var total =
            Math.Max(
                1,
                pixels);

        return new CordStats(
            pixels,
            components,
            run1 / (double)total,
            run2 / (double)total,
            run3 / (double)total,
            dust);
    }

    private static DesignDocument MaskDocument(
        bool[] mask,
        int width,
        int height)
    {
        var palette =
            new Palette(
                new[]
                {
                    new RugColor(255, 255, 255),
                    new RugColor(220, 0, 0),
                });
        var result =
            new DesignDocument(
                width,
                height,
                palette);

        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                result.SetPixel(
                    x,
                    y,
                    mask[y * width + x]
                        ? (byte)1
                        : (byte)0);
            }
        }

        return result;
    }

    private static DesignDocument Clone(
        DesignDocument source)
    {
        var result =
            new DesignDocument(
                source.Width,
                source.Height,
                source.Palette);

        for (var y = 0;
             y < source.Height;
             y++)
        {
            for (var x = 0;
                 x < source.Width;
                 x++)
            {
                result.SetPixel(
                    x,
                    y,
                    source.GetPixel(
                        x,
                        y));
            }
        }

        return result;
    }
}
