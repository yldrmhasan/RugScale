using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class TextureQuiltScaleEngineTests
{
    // 0 white, 1 technical blue, 2 tan, 3 navy.
    private static Palette Palette() =>
        new(
            new[]
            {
                new RugColor(255, 255, 255),
                new RugColor(0, 0, 255),
                new RugColor(213, 190, 122),
                new RugColor(0, 72, 143),
            });

    /// <summary>
    /// Distressed ground in miniature: a white half with one-knot tan speckle and one-row tan
    /// scratch streaks, a navy half with one-knot white speckle, and a technical blue marker column
    /// on the right edge.
    /// </summary>
    private static DesignDocument Source()
    {
        var random =
            new Random(7);
        var source =
            new DesignDocument(
                200,
                200,
                Palette());

        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 199; x++)
            {
                byte color;

                if (x < 100)
                    color = random.NextDouble() < 0.25 ? (byte)2 : (byte)0;
                else
                    color = random.NextDouble() < 0.25 ? (byte)0 : (byte)3;

                source.SetPixel(x, y, color);
            }

            source.SetPixel(199, y, 1);
        }

        for (var y = 3; y < 200; y += 7)
        {
            var start = random.Next(0, 60);

            for (var x = start; x < start + 30; x++)
                source.SetPixel(x, y, 2);
        }

        return source;
    }

    private static double MeanHorizontalRun(
        DesignDocument document)
    {
        var runs = 0;

        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                if (x == 0 || document.GetPixel(x, y) != document.GetPixel(x - 1, y))
                    runs++;
            }
        }

        return document.Width * document.Height / (double)runs;
    }

    private static double Share(
        DesignDocument document,
        byte color,
        int x0,
        int x1)
    {
        var count = 0;

        for (var y = 0; y < document.Height; y++)
            for (var x = x0; x < x1; x++)
                if (document.GetPixel(x, y) == color)
                    count++;

        return count / (double)((x1 - x0) * document.Height);
    }

    [Fact]
    public void Enlarging_KeepsTheGrainFineAndTheStructureInPlace()
    {
        var source =
            Source();
        var texture =
            DesignResizer.Scale(source, 320, 320, ScaleMode.Texture);
        var nearest =
            DesignResizer.Scale(source, 320, 320, ScaleMode.NearestNeighbor);

        var sourceRun = MeanHorizontalRun(source);
        var textureRatio = MeanHorizontalRun(texture) / sourceRun;
        var nearestRatio = MeanHorizontalRun(nearest) / sourceRun;

        // One-knot speckle stays one knot (nearest makes it 1.6x coarser).
        Assert.True(
            textureRatio < 1.2 && nearestRatio > 1.4,
            $"grain ratio texture {textureRatio:F2}, nearest {nearestRatio:F2}");

        // The white/navy halves stay where they were, with their speckle density.
        Assert.True(Share(texture, 3, 0, 150) < 0.03, "navy leaked into the white half");
        Assert.InRange(Share(texture, 3, 170, 318), 0.68, 0.82);
        Assert.InRange(Share(texture, 2, 0, 150), Share(source, 2, 0, 94) - 0.04, Share(source, 2, 0, 94) + 0.04);

        // The technical marker column stays on the edge, and only there.
        for (var y = 0; y < texture.Height; y++)
            Assert.Equal(1, texture.GetPixel(319, y));

        Assert.Equal(0, Share(texture, 1, 0, 319));
    }

    [Fact]
    public void Shrinking_KeepsColourProportions()
    {
        var source =
            Source();
        var texture =
            DesignResizer.Scale(source, 160, 160, ScaleMode.Texture);

        foreach (byte color in new byte[] { 0, 2, 3 })
        {
            Assert.InRange(
                Share(texture, color, 0, 159),
                Share(source, color, 0, 199) - 0.03,
                Share(source, color, 0, 199) + 0.03);
        }
    }
}
