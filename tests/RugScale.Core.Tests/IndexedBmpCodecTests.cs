using RugScale.Core.IO;
using RugScale.Core.Models;

namespace RugScale.Core.Tests;

public sealed class IndexedBmpCodecTests
{
    [Fact]
    public void EightBitIndexedBmp_RoundTripsPaletteIndexesAndResolution()
    {
        var palette =
            new Palette(
            [
                new RugColor(10, 20, 30),
                new RugColor(200, 120, 40),
            ]);
        var document =
            new DesignDocument(
                3,
                2,
                palette);

        document.SetPixel(0, 0, 0);
        document.SetPixel(1, 0, 1);
        document.SetPixel(2, 0, 0);
        document.SetPixel(0, 1, 1);
        document.SetPixel(1, 1, 0);
        document.SetPixel(2, 1, 1);

        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"rugscale-{Guid.NewGuid():N}.bmp");

        try
        {
            IndexedBmpCodec.Write(
                path,
                document,
                18898,
                19685);

            var loaded =
                IndexedBmpCodec.Read(
                    path);

            Assert.Equal(
                18898,
                loaded.XPixelsPerMeter);
            Assert.Equal(
                19685,
                loaded.YPixelsPerMeter);
            Assert.Equal(
                document.Width,
                loaded.Document.Width);
            Assert.Equal(
                document.Height,
                loaded.Document.Height);

            for (var y = 0;
                 y < document.Height;
                 y++)
            {
                for (var x = 0;
                     x < document.Width;
                     x++)
                {
                    Assert.Equal(
                        document.GetPixel(x, y),
                        loaded.Document.GetPixel(x, y));
                }
            }

            Assert.Equal(
                document.Palette[0],
                loaded.Document.Palette[0]);
            Assert.Equal(
                document.Palette[1],
                loaded.Document.Palette[1]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
