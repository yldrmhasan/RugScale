using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class LeafPetalBoundaryCurveBuilderTests
{
    [Fact]
    public void Build_TaperedOutlinedLeaf_LocksBothDesignerCurvesToRealApex()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(255, 255, 255),
            new RugColor(128, 160, 121),
        });
        var source =
            new DesignDocument(
                80,
                68,
                palette);

        var leftControls = new (int X, int Y)[]
        {
            (23, 57),
            (22, 39),
            (29, 19),
            (40, 7),
        };
        var rightControls = new (int X, int Y)[]
        {
            (47, 57),
            (49, 39),
            (47, 19),
            (40, 7),
        };

        var left =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            leftControls,
                            CurveType.SplineThroughPoints,
                            0.35)))
                .ToArray();
        var right =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            rightControls,
                            CurveType.SplineThroughPoints,
                            0.35)))
                .ToArray();

        var polygon =
            left
                .Select(point =>
                    (
                        X: (double)point.X,
                        Y: (double)point.Y))
                .Concat(
                    right
                        .Reverse()
                        .Select(point =>
                            (
                                X: (double)point.X,
                                Y: (double)point.Y)))
                .ToArray();

        foreach (var (x, y) in RasterizePolygon(
                     polygon,
                     source.Width,
                     source.Height))
        {
            source.SetPixel(
                x,
                y,
                2);
        }

        foreach (var point in left.Concat(right))
        {
            source.SetPixel(
                point.X,
                point.Y,
                1);
        }

        foreach (var point in Rasterizer.Line(
                     left[0].X,
                     left[0].Y,
                     right[0].X,
                     right[0].Y))
        {
            source.SetPixel(
                point.X,
                point.Y,
                1);
        }

        var regions =
            LeafPetalRegionExtractor.Extract(
                source);
        var region =
            regions
                .Where(candidate =>
                    candidate.Color == 2)
                .OrderByDescending(candidate =>
                    candidate.Area)
                .First();

        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                region,
                source.Width,
                out var candidate));
        Assert.True(
            LeafPetalMedialAxisBuilder.TryBuild(
                candidate,
                source.Width,
                out var model));

        var built =
            LeafPetalBoundaryCurveBuilder.TryBuild(
                source,
                model,
                new HashSet<byte>
                {
                    1,
                },
                out var boundary);

        Assert.True(
            built,
            "The fully outlined tapered leaf should recover paired source Curve sides.");
        Assert.True(
            boundary.DrawApexCap,
            "A real tapered source apex should be recognised as a shared curve anchor.");
        Assert.Equal(
            boundary.LeftFit.Controls[^1],
            boundary.RightFit.Controls[^1]);
        Assert.Equal(
            (byte)1,
            source.GetPixel(
                boundary.LeftFit.Controls[^1].X,
                boundary.LeftFit.Controls[^1].Y));
    }

    [Fact]
    public void Build_InnerWhiteSlit_CannotHijackOuterDesignerCurve()
    {
        var palette = new Palette(new[]
        {
            new RugColor(218, 210, 184),
            new RugColor(255, 255, 255),
            new RugColor(128, 160, 121),
        });
        var source =
            new DesignDocument(
                86,
                72,
                palette);

        var leftControls = new (int X, int Y)[]
        {
            (22, 61),
            (20, 43),
            (29, 19),
            (43, 7),
        };
        var rightControls = new (int X, int Y)[]
        {
            (55, 61),
            (57, 43),
            (54, 20),
            (43, 7),
        };

        var left =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            leftControls,
                            CurveType.SplineThroughPoints,
                            0.35)))
                .ToArray();
        var right =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            rightControls,
                            CurveType.SplineThroughPoints,
                            0.35)))
                .ToArray();

        var polygon =
            left
                .Select(point =>
                    (
                        X: (double)point.X,
                        Y: (double)point.Y))
                .Concat(
                    right
                        .Reverse()
                        .Select(point =>
                            (
                                X: (double)point.X,
                                Y: (double)point.Y)))
                .ToArray();

        foreach (var (x, y) in RasterizePolygon(
                     polygon,
                     source.Width,
                     source.Height))
        {
            source.SetPixel(
                x,
                y,
                2);
        }

        foreach (var point in left.Concat(right))
            source.SetPixel(point.X, point.Y, 1);

        var slitControls = new (int X, int Y)[]
        {
            (39, 48),
            (38, 36),
            (41, 25),
            (46, 18),
        };
        var slit =
            ConsecutiveDistinct(
                    Rasterizer.ConnectDiagonalSteps(
                        CurveRasterizer.Draw(
                            slitControls,
                            CurveType.SplineThroughPoints,
                            0.35)))
                .ToHashSet();

        foreach (var point in slit)
            source.SetPixel(point.X, point.Y, 1);

        var region =
            LeafPetalRegionExtractor.Extract(
                    source)
                .Where(candidate =>
                    candidate.Color == 2)
                .OrderByDescending(candidate =>
                    candidate.Area)
                .First();

        Assert.True(
            LeafPetalArcClassifier.TryClassify(
                region,
                source.Width,
                out var candidate));
        Assert.True(
            LeafPetalMedialAxisBuilder.TryBuild(
                candidate,
                source.Width,
                out var model));
        Assert.True(
            LeafPetalBoundaryCurveBuilder.TryBuild(
                source,
                model,
                new HashSet<byte>
                {
                    1,
                },
                out var boundary));

        var outer =
            boundary.LeftSourcePath
                .Concat(
                    boundary.RightSourcePath)
                .ToArray();
        var slitOverlap =
            outer.Count(point =>
                slit.Contains(point));

        Assert.True(
            slitOverlap <= 2,
            $"Inner slit hijacked {slitOverlap} outer-boundary pixels.");
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
            previous =
                point;
        }
    }

    private static IEnumerable<(int X, int Y)> RasterizePolygon(
        IReadOnlyList<(double X, double Y)> polygon,
        int width,
        int height)
    {
        var intersections =
            new List<double>(
                polygon.Count);
        var minY =
            Math.Max(
                0,
                (int)Math.Floor(
                    polygon.Min(point =>
                        point.Y)));
        var maxY =
            Math.Min(
                height - 1,
                (int)Math.Ceiling(
                    polygon.Max(point =>
                        point.Y)));

        for (var y = minY;
             y <= maxY;
             y++)
        {
            intersections.Clear();
            var scanY =
                y +
                0.5;

            for (var i = 0;
                 i < polygon.Count;
                 i++)
            {
                var a =
                    polygon[i];
                var b =
                    polygon[
                        (i + 1) %
                        polygon.Count];

                if (!((a.Y <= scanY &&
                       b.Y > scanY) ||
                      (b.Y <= scanY &&
                       a.Y > scanY)))
                {
                    continue;
                }

                var t =
                    (scanY -
                     a.Y) /
                    (b.Y -
                     a.Y);
                intersections.Add(
                    a.X +
                    (b.X -
                     a.X) *
                    t);
            }

            intersections.Sort();

            for (var i = 0;
                 i + 1 <
                 intersections.Count;
                 i += 2)
            {
                var start =
                    Math.Max(
                        0,
                        (int)Math.Ceiling(
                            intersections[i]));
                var end =
                    Math.Min(
                        width - 1,
                        (int)Math.Floor(
                            intersections[i + 1]));

                for (var x = start;
                     x <= end;
                     x++)
                {
                    yield return (
                        x,
                        y);
                }
            }
        }
    }
}
