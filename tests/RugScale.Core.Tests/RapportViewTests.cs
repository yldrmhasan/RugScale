using RugScale.Core.Drawing.WallToWall;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class RapportViewTests
{
    // 3 x 2 tile with distinct values.
    private static readonly int[] Tile = [10, 11, 12, 20, 21, 22];

    private static int[] View(int drop, double zoom, double ox, double oy, int w, int h, bool borders = false)
    {
        var view = new int[w * h];
        RapportView.Render(Tile, 3, 2, drop, zoom, ox, oy, borders, view, w, h);
        return view;
    }

    private static int Expected(int x, int y, int drop)
    {
        var col = (int)Math.Floor(x / 3.0);
        var lx = x - col * 3;
        var ly = (((y - col * drop) % 2) + 2) % 2;
        return Tile[ly * 3 + lx];
    }

    [Fact]
    public void RepeatsWithoutEnd()
    {
        var view = View(0, 1, 0, 0, 10, 7);

        for (var y = 0; y < 7; y++)
            for (var x = 0; x < 10; x++)
                Assert.Equal(Expected(x, y, 0), view[y * 10 + x]);
    }

    [Fact]
    public void PanningAnyDistanceStillShowsTheRepeat()
    {
        // Offset -1000.5 px: design pixel (x + 1000, y + 1000) at zoom 1 (floor of x + 1000.5).
        var view = View(0, 1, -1000.5, -999.5, 9, 6);

        for (var y = 0; y < 6; y++)
            for (var x = 0; x < 9; x++)
                Assert.Equal(Expected(x + 1000, y + 999, 0), view[y * 9 + x]);
    }

    [Fact]
    public void ZoomScalesDesignPixels()
    {
        var view = View(0, 2, 0, 0, 12, 8);

        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 12; x++)
                Assert.Equal(Expected(x / 2, y / 2, 0), view[y * 12 + x]);
    }

    [Fact]
    public void DropShiftsEveryNextColumnOfRepeats()
    {
        var view = View(1, 1, 0, 0, 12, 6);

        for (var y = 0; y < 6; y++)
            for (var x = 0; x < 12; x++)
                Assert.Equal(Expected(x, y, 1), view[y * 12 + x]);

        // Second column of repeats starts one row down: its top-left shows tile row 1.
        Assert.Equal(20, view[3]);
    }

    [Fact]
    public void BordersTintTheFirstPixelOfEveryRepeat()
    {
        var view = View(0, 1, 0, 0, 6, 4, borders: true);

        Assert.NotEqual(Tile[0], view[0]);            // corner
        Assert.NotEqual(Tile[0], view[3]);            // next repeat across
        Assert.NotEqual(Tile[0], view[2 * 6]);        // next repeat along
        Assert.Equal(Tile[4], view[1 * 6 + 1]);       // inside: untouched
    }

    [Fact]
    public void TilePixelsTakesTheRapportRegion()
    {
        var palette = new Palette([new RugColor(1, 2, 3), new RugColor(200, 100, 50)]);
        var design = new DesignDocument(4, 3, palette);
        design.SetPixel(2, 1, 1);

        var (pixels, width, height) =
            RapportView.TilePixels(design, new RapportTile(1, 1, 2, 2));

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(unchecked((int)0xFFC86432), pixels[1]);
        Assert.Equal(unchecked((int)0xFF010203), pixels[0]);
    }
}
