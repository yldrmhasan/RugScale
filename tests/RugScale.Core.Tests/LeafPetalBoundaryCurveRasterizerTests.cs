using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class LeafPetalBoundaryCurveRasterizerTests
{
    [Fact]
    public void LeafPetalMode_PairedDesignerCurves_PreserveTargetScaleOuterArc()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(255, 255, 255),
            new RugColor(135, 164, 125),
        });
        var source = new DesignDocument(92, 76, palette);

        var leftControls = new (int X, int Y)[]
        {
            (25, 66),
            (22, 49),
            (28, 28),
            (40, 11),
            (47, 6),
        };
        var rightControls = new (int X, int Y)[]
        {
            (59, 66),
            (62, 48),
            (59, 28),
            (53, 13),
            (47, 6),
        };

        const double SourceRoundness = 0.35;

        var left = OrderedCurve(leftControls, SourceRoundness);
        var right = OrderedCurve(rightControls, SourceRoundness);

        var polygon = left
            .Select(point => ((double)point.X, (double)point.Y))
            .Concat(
                right
                    .Reverse()
                    .Select(point => ((double)point.X, (double)point.Y)))
            .ToArray();

        foreach (var point in RasterizePolygon(
                     polygon,
                     source.Width,
                     source.Height))
        {
            source.SetPixel(point.X, point.Y, 2);
        }

        foreach (var point in left.Concat(right))
            source.SetPixel(point.X, point.Y, 1);

        const int TargetWidth = 147;
        const int TargetHeight = 121;
        var target = new DesignDocument(
            TargetWidth,
            TargetHeight,
            palette);

        var diagnostics =
            LeafPetalArcScaleEngine.ResizeWithDiagnostics(
                source,
                target,
                40,
                50,
                40,
                50);

        Assert.True(
            diagnostics.BoundaryCurveRefined >= 1,
            $"A fully outlined tapered leaf should use paired designer-boundary reconstruction. " +
            $"regions={diagnostics.Regions}, candidates={diagnostics.Candidates}, " +
            $"axisReject={diagnostics.RejectedByAxis}, fitReject={diagnostics.RejectedByFit}, " +
            $"boundaryBuilt={diagnostics.BoundaryCurveBuilt}, " +
            $"rasterReject={diagnostics.BoundaryCurveRasterRejected}.");

        var scaleX = TargetWidth / (double)source.Width;
        var scaleY = TargetHeight / (double)source.Height;
        var expectedLeft = OrderedCurve(
                leftControls
                    .Select(point => Map(point, scaleX, scaleY))
                    .ToArray(),
                SourceRoundness)
            .ToHashSet();
        var expectedRight = OrderedCurve(
                rightControls
                    .Select(point => Map(point, scaleX, scaleY))
                    .ToArray(),
                SourceRoundness)
            .ToHashSet();
        var expectedOutline = expectedLeft
            .Concat(expectedRight)
            .ToHashSet();
        var actualOutline = ColorPixels(target, 1);

        var nearF1 = NearF1(
            actualOutline,
            expectedOutline,
            radius: 1);

        Assert.True(
            nearF1 >= 0.94,
            $"Paired target curves lost designer geometry: ±1px F1={nearF1:P2}.");

        // The specialist pass is allowed to clean digital staircases, but the indexed white
        // separator must remain one connected visual outline around this simple leaf.
        Assert.True(
            CountComponents(target, 1) <= 2,
            "The rebuilt outer curve fragmented into unrelated white pieces.");
    }

    [Fact]
    public void LeafPetalMode_InnerSlit_RemainsIndependentFromOuterPairedCurve()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(255, 255, 255),
            new RugColor(135, 164, 125),
        });
        var source = new DesignDocument(92, 76, palette);

        var leftControls = new (int X, int Y)[]
        {
            (24, 66),
            (21, 48),
            (29, 25),
            (47, 6),
        };
        var rightControls = new (int X, int Y)[]
        {
            (60, 66),
            (63, 48),
            (58, 27),
            (47, 6),
        };
        var left = OrderedCurve(leftControls, 0.35);
        var right = OrderedCurve(rightControls, 0.35);
        var polygon = left
            .Select(point => ((double)point.X, (double)point.Y))
            .Concat(
                right.Reverse()
                    .Select(point => ((double)point.X, (double)point.Y)))
            .ToArray();

        foreach (var point in RasterizePolygon(
                     polygon,
                     source.Width,
                     source.Height))
        {
            source.SetPixel(point.X, point.Y, 2);
        }

        foreach (var point in left.Concat(right))
            source.SetPixel(point.X, point.Y, 1);

        var slitControls = new (int X, int Y)[]
        {
            (43, 53),
            (42, 40),
            (45, 27),
            (51, 18),
        };
        var slit = OrderedCurve(slitControls, 0.35);

        foreach (var point in slit)
            source.SetPixel(point.X, point.Y, 1);

        var result = DesignResizer.Scale(
            source,
            147,
            121,
            ScaleMode.LeafPetalArcs,
            40,
            50,
            40,
            50);

        var scaleX = result.Width / (double)source.Width;
        var scaleY = result.Height / (double)source.Height;
        var expectedSlit = OrderedCurve(
                slitControls
                    .Select(point => Map(point, scaleX, scaleY))
                    .ToArray(),
                0.35)
            .ToHashSet();
        var actualWhite = ColorPixels(result, 1);

        var slitRecall = expectedSlit.Count(point =>
            HasNeighbor(
                actualWhite,
                point,
                radius: 1)) /
            (double)Math.Max(1, expectedSlit.Count);

        Assert.True(
            slitRecall >= 0.90,
            $"Inner separator was damaged while rebuilding outer curves: recall={slitRecall:P2}.");
    }

    private static (int X, int Y)[] OrderedCurve(
        IReadOnlyList<(int X, int Y)> controls,
        double roundness) =>
        Rasterizer.ConnectDiagonalSteps(
                CurveRasterizer.Draw(
                    controls,
                    CurveType.SplineThroughPoints,
                    roundness))
            .Distinct()
            .ToArray();

    private static (int X, int Y) Map(
        (int X, int Y) point,
        double scaleX,
        double scaleY) =>
        (
            X: (int)Math.Round(
                (point.X + 0.5) * scaleX - 0.5),
            Y: (int)Math.Round(
                (point.Y + 0.5) * scaleY - 0.5));

    private static HashSet<(int X, int Y)> ColorPixels(
        DesignDocument document,
        byte color)
    {
        var result = new HashSet<(int X, int Y)>();

        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                if (document.GetPixel(x, y) == color)
                    result.Add((x, y));
            }
        }

        return result;
    }

    private static double NearF1(
        IReadOnlySet<(int X, int Y)> actual,
        IReadOnlySet<(int X, int Y)> expected,
        int radius)
    {
        if (actual.Count == 0 || expected.Count == 0)
            return 0d;

        var actualSupported = actual.Count(point =>
            HasNeighbor(expected, point, radius));
        var expectedSupported = expected.Count(point =>
            HasNeighbor(actual, point, radius));
        var precision = actualSupported / (double)actual.Count;
        var recall = expectedSupported / (double)expected.Count;
        var sum = precision + recall;

        return sum <= 0d
            ? 0d
            : 2d * precision * recall / sum;
    }

    private static bool HasNeighbor(
        IReadOnlySet<(int X, int Y)> points,
        (int X, int Y) point,
        int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (points.Contains((point.X + dx, point.Y + dy)))
                    return true;
            }
        }

        return false;
    }

    private static int CountComponents(
        DesignDocument document,
        byte color)
    {
        var seen = new bool[document.Width * document.Height];
        var queue = new Queue<(int X, int Y)>();
        var count = 0;
        (int X, int Y)[] directions =
        [
            (-1, 0), (1, 0), (0, -1), (0, 1),
            (-1, -1), (1, -1), (-1, 1), (1, 1),
        ];

        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var start = y * document.Width + x;

                if (seen[start] ||
                    document.GetPixel(x, y) != color)
                {
                    continue;
                }

                count++;
                seen[start] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();

                    foreach (var (dx, dy) in directions)
                    {
                        var nx = current.X + dx;
                        var ny = current.Y + dy;

                        if (nx < 0 || nx >= document.Width ||
                            ny < 0 || ny >= document.Height)
                        {
                            continue;
                        }

                        var next = ny * document.Width + nx;

                        if (seen[next] ||
                            document.GetPixel(nx, ny) != color)
                        {
                            continue;
                        }

                        seen[next] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        return count;
    }

    private static IEnumerable<(int X, int Y)> RasterizePolygon(
        IReadOnlyList<(double X, double Y)> polygon,
        int width,
        int height)
    {
        var intersections = new List<double>(polygon.Count);
        var minY = Math.Max(
            0,
            (int)Math.Floor(polygon.Min(point => point.Y)));
        var maxY = Math.Min(
            height - 1,
            (int)Math.Ceiling(polygon.Max(point => point.Y)));

        for (var y = minY; y <= maxY; y++)
        {
            intersections.Clear();
            var scanY = y + 0.5;

            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];

                if (!((a.Y <= scanY && b.Y > scanY) ||
                      (b.Y <= scanY && a.Y > scanY)))
                {
                    continue;
                }

                var t = (scanY - a.Y) / (b.Y - a.Y);
                intersections.Add(
                    a.X + (b.X - a.X) * t);
            }

            intersections.Sort();

            for (var i = 0; i + 1 < intersections.Count; i += 2)
            {
                var start = Math.Max(
                    0,
                    (int)Math.Ceiling(intersections[i]));
                var end = Math.Min(
                    width - 1,
                    (int)Math.Floor(intersections[i + 1]));

                for (var x = start; x <= end; x++)
                    yield return (x, y);
            }
        }
    }
}
