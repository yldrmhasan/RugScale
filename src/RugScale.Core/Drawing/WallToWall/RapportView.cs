using RugScale.Core.Models;

namespace RugScale.Core.Drawing.WallToWall;

/// <summary>
/// "Rapor görünümü": draws a rapport repeated without end into a view, for the Workbench preview.
/// The view is a window onto an infinite plane of repeats: panning only moves the window, so the
/// design never runs out. A drop (half-drop ...) shifts every next column of repeats down.
/// </summary>
public static class RapportView
{
    /// <summary>Colour of the optional rapport borders (opaque ARGB).</summary>
    public const int BorderColor = unchecked((int)0xFF4FA3FF);

    /// <summary>
    /// The rapport as opaque ARGB pixels (Bgra32 in memory), clamped to the document.
    /// </summary>
    public static (int[] Pixels, int Width, int Height) TilePixels(
        DesignDocument document,
        RapportTile region)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(region);

        var x0 =
            Math.Clamp(region.X, 0, document.Width - 1);
        var y0 =
            Math.Clamp(region.Y, 0, document.Height - 1);
        var width =
            Math.Clamp(region.Width, 1, document.Width - x0);
        var height =
            Math.Clamp(region.Height, 1, document.Height - y0);
        var argb =
            new int[256];

        for (var index = 0; index < document.Palette.Count && index < 256; index++)
        {
            var color =
                document.Palette[index];
            argb[index] = unchecked((int)(0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));
        }

        var pixels =
            new int[width * height];

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = argb[document.GetPixel(x0 + x, y0 + y)];

        return (pixels, width, height);
    }

    /// <summary>
    /// Fills a <paramref name="viewWidth"/> x <paramref name="viewHeight"/> view: view pixel (x, y)
    /// shows design pixel ((x - offsetX) / zoom, (y - offsetY) / zoom) of the endless repeat.
    /// With <paramref name="borders"/> the first view pixel of every repeat is drawn in
    /// <see cref="BorderColor"/> (a tint got lost on busy designs).
    /// </summary>
    public static void Render(
        int[] tile,
        int tileWidth,
        int tileHeight,
        int drop,
        double zoom,
        double offsetX,
        double offsetY,
        bool borders,
        int[] view,
        int viewWidth,
        int viewHeight)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(view);

        if (tileWidth <= 0 ||
            tileHeight <= 0 ||
            zoom <= 0 ||
            view.Length < viewWidth * viewHeight)
        {
            return;
        }

        // Per view column: design column inside the repeat, and how far that repeat drops.
        var column =
            new int[viewWidth];
        var shift =
            new int[viewWidth];
        var columnEdge =
            new bool[viewWidth];
        long previous = long.MinValue;

        for (var x = 0; x < viewWidth; x++)
        {
            var design =
                (long)Math.Floor((x - offsetX) / zoom);
            var repeat =
                FloorDiv(design, tileWidth);
            column[x] = (int)(design - repeat * tileWidth);
            shift[x] = (int)Mod(repeat * drop, tileHeight);
            columnEdge[x] = column[x] == 0 && design != previous;
            previous = design;
        }

        previous = long.MinValue;

        for (var y = 0; y < viewHeight; y++)
        {
            var design =
                (long)Math.Floor((y - offsetY) / zoom);
            var rowChanged =
                design != previous;
            previous = design;
            var row =
                Mod(design, tileHeight);
            var line =
                y * viewWidth;

            for (var x = 0; x < viewWidth; x++)
            {
                var r =
                    row - shift[x];

                if (r < 0)
                    r += tileHeight;

                var pixel =
                    tile[r * tileWidth + column[x]];

                if (borders &&
                    (columnEdge[x] || (rowChanged && r == 0)))
                {
                    pixel = BorderColor;
                }

                view[line + x] = pixel;
            }
        }
    }

    private static long FloorDiv(
        long value,
        long size) =>
        value >= 0
            ? value / size
            : -((-value + size - 1) / size);

    private static long Mod(
        long value,
        long size)
    {
        var m =
            value % size;

        return m < 0
            ? m + size
            : m;
    }
}
