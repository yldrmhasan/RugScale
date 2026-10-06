using RugScale.Core.Models;

namespace RugScale.Core.Drawing.WallToWall;

/// <summary>Which way a rapport is repeated: across the roll (en), along it (boy), or both.</summary>
public enum RapportDirection
{
    /// <summary>Enden: repeat across the width; rows come from the source.</summary>
    Width,

    /// <summary>Boydan: repeat along the length; columns come from the source.</summary>
    Length,

    /// <summary>Her ikisi: tile in both directions.</summary>
    Both,
}

/// <summary>
/// One repeat unit (rapport) of a roll / wall-to-wall design, in source pixels. <see cref="Drop"/>
/// is the vertical shift of every next repeat across the width (half-drop = Height / 2, 0 = straight).
/// </summary>
public sealed record RapportTile(
    int X,
    int Y,
    int Width,
    int Height,
    int Drop = 0);

/// <summary>Technical marker columns (one colour that appears nowhere else) on the design edges.</summary>
public sealed record EdgeMarkers(
    int Left,
    int Right,
    byte Color);

/// <summary>
/// The repeated design, the rapport period actually used (a whole-design seamless join shortens
/// it by the band) and how visible the joins are: mismatch across a join divided by the mean
/// mismatch between neighbouring columns / rows inside the rapport (about 1 = reads like the
/// texture, above <see cref="WallToWallRepeat.VisibleSeam"/> = a visible line). NaN for an axis
/// that is not repeated.
/// </summary>
public sealed record RapportResult(
    DesignDocument Design,
    int PeriodWidth,
    int PeriodHeight,
    double SeamAcross,
    double SeamAlong);

public sealed record RapportOptions(
    RapportDirection Direction = RapportDirection.Both,
    bool Seamless = false,
    int SeamBand = 12,
    bool KeepEdgeMarkers = true);

/// <summary>
/// Fills a roll / wall-to-wall target by repeating one rapport, instead of scaling the design: on
/// a roll the pattern keeps its size and a wider or longer carpet simply shows more repeats.
///
/// Along a repeating axis the target walks through the rapport cyclically (with the drop between
/// neighbouring repeats); along a non-repeating axis it reads the source straight from the rapport
/// start. With <see cref="RapportOptions.Seamless"/> a rapport whose opposite edges do not meet is
/// joined along the minimum-mismatch path inside a band at its end, using the source content just
/// outside the rapport (or the rapport's own start when the rapport is the whole design), so the
/// join reads as part of the texture instead of a straight line. The rapport period is unchanged.
/// </summary>
public static class WallToWallRepeat
{
    public static DesignDocument Repeat(
        DesignDocument source,
        RapportTile tile,
        int width,
        int height,
        RapportOptions? options = null) =>
        Render(
            source,
            tile,
            width,
            height,
            options).Design;

    /// <summary>Repeats the rapport and reports the period actually used (a whole-design seamless join shortens it by the band).</summary>
    public static RapportResult Render(
        DesignDocument source,
        RapportTile tile,
        int width,
        int height,
        RapportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tile);
        options ??= new RapportOptions();

        if (width <= 0 ||
            height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");
        }

        var w =
            source.Width;
        var h =
            source.Height;
        var tx =
            Math.Clamp(tile.X, 0, w - 1);
        var ty =
            Math.Clamp(tile.Y, 0, h - 1);
        var tw =
            Math.Clamp(tile.Width, 1, w - tx);
        var th =
            Math.Clamp(tile.Height, 1, h - ty);
        var markers =
            options.KeepEdgeMarkers
                ? RapportDetector.FindEdgeMarkers(source)
                : null;

        var src =
            new byte[w * h];

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                src[y * w + x] = source.GetPixel(x, y);

        var repeatX =
            options.Direction != RapportDirection.Length;
        var repeatY =
            options.Direction != RapportDirection.Width;

        // The rapport as its own image, made seamless on the repeating axes if asked.
        var unit =
            new byte[tw * th];

        for (var y = 0; y < th; y++)
            for (var x = 0; x < tw; x++)
                unit[y * tw + x] = src[(ty + y) * w + tx + x];

        var fullWidth =
            tw;
        var fullHeight =
            th;

        if (options.Seamless)
        {
            if (repeatX)
            {
                (unit, tw) =
                    JoinHorizontally(
                        src,
                        w,
                        h,
                        tx,
                        ty,
                        tw,
                        th,
                        repeatY ? tile.Drop : 0,
                        unit,
                        Math.Min(options.SeamBand, tw / 3));
            }

            if (repeatY)
            {
                (unit, th) =
                    JoinVertically(
                        src,
                        w,
                        h,
                        tx,
                        ty,
                        tw,
                        th,
                        unit,
                        Math.Min(options.SeamBand, th / 3));
            }
        }


        var result =
            new DesignDocument(
                width,
                height,
                source.Palette);
        var drop =
            repeatX && repeatY
                ? tile.Drop
                : 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                byte color;

                if (repeatX &&
                    repeatY)
                {
                    var column =
                        x / tw;
                    var ux =
                        x % tw;
                    var uy =
                        Mod(y - column * drop, th);
                    color = unit[uy * tw + ux];
                }
                else if (repeatX)
                {
                    // Enden: rows straight from the source, columns cycle through the rapport.
                    var ux =
                        x % tw;
                    var sy =
                        ty + y;

                    color =
                        sy < h
                            ? (sy < ty + th ? unit[(sy - ty) * tw + ux] : src[sy * w + tx + ux])
                            : unit[Mod(y, th) * tw + ux];
                }
                else
                {
                    // Boydan: columns straight from the source, rows cycle through the rapport.
                    var uy =
                        y % th;
                    var sx =
                        tx + x;

                    color =
                        sx < w
                            ? (sx < tx + tw ? unit[uy * tw + sx - tx] : src[(ty + uy) * w + sx])
                            : unit[uy * tw + Mod(x, tw)];
                }

                result.SetPixel(x, y, color);
            }
        }

        if (markers is not null)
        {
            for (var y = 0; y < height; y++)
            {
                for (var k = 0; k < markers.Left && k < width; k++)
                    result.SetPixel(k, y, markers.Color);

                for (var k = 0; k < markers.Right && k < width; k++)
                    result.SetPixel(width - 1 - k, y, markers.Color);
            }
        }

        return new RapportResult(
            result,
            tw,
            th,
            repeatX ? JoinRatio(unit, tw, th, drop, vertical: false, tw == fullWidth ? NaturalJoin(src, w, h, tx, ty, tw, th, unit, vertical: false) : 0) : double.NaN,
            repeatY ? JoinRatio(unit, tw, th, 0, vertical: true, th == fullHeight ? NaturalJoin(src, w, h, tx, ty, tw, th, unit, vertical: true) : 0) : double.NaN);
    }

    /// <summary>
    /// Seam visibility of a repeat: pixel mismatch across the joins divided by the mean mismatch
    /// between neighbouring columns (or rows) inside the rapport. 1.0 reads like the texture itself.
    /// </summary>
    public static double SeamRatio(
        DesignDocument tiled,
        int period,
        bool vertical,
        int skipLeft = 0,
        int skipRight = 0)
    {
        ArgumentNullException.ThrowIfNull(tiled);

        var size =
            vertical
                ? tiled.Height
                : tiled.Width;
        var across =
            vertical
                ? tiled.Width
                : tiled.Height;
        double seam = 0;
        var seams = 0;
        double inside = 0;
        var insides = 0;

        double Mismatch(
            int a)
        {
            var count = 0;
            var total = 0;

            for (var k = vertical ? skipLeft : 0;
                 k < across - (vertical ? skipRight : 0);
                 k++)
            {
                var p =
                    vertical
                        ? tiled.GetPixel(k, a)
                        : tiled.GetPixel(a, k);
                var q =
                    vertical
                        ? tiled.GetPixel(k, a + 1)
                        : tiled.GetPixel(a + 1, k);

                if (p != q)
                    count++;

                total++;
            }

            return count / (double)Math.Max(1, total);
        }

        for (var a = Math.Max(0, vertical ? 0 : skipLeft);
             a + 1 < size - (vertical ? 0 : skipRight);
             a++)
        {
            if ((a + 1) % period == 0)
            {
                seam += Mismatch(a);
                seams++;
            }
            else if (a % 7 == 0)
            {
                inside += Mismatch(a);
                insides++;
            }
        }

        if (seams == 0 ||
            insides == 0)
        {
            return 1;
        }

        return seam / seams / Math.Max(1e-6, inside / insides);
    }

    /// <summary>Join ratio above which a repeat seam reads as a line.</summary>
    public const double VisibleSeam = 1.5;

    private static double JoinRatio(
        byte[] unit,
        int tw,
        int th,
        int drop,
        bool vertical,
        double natural)
    {
        double Across(
            int a)
        {
            var count = 0;

            for (var y = 0; y < th; y++)
            {
                var right =
                    a + 1 < tw
                        ? unit[y * tw + a + 1]
                        : unit[Mod(y - drop, th) * tw];

                if (unit[y * tw + a] != right)
                    count++;
            }

            return count / (double)th;
        }

        double Along(
            int a)
        {
            var count = 0;

            for (var x = 0; x < tw; x++)
            {
                var below =
                    a + 1 < th
                        ? unit[(a + 1) * tw + x]
                        : unit[x];

                if (unit[a * tw + x] != below)
                    count++;
            }

            return count / (double)tw;
        }

        var size =
            vertical
                ? th
                : tw;

        if (size < 3)
            return double.NaN;

        double inside = 0;
        var samples = 0;

        for (var a = 0; a < size - 1; a += Math.Max(1, size / 64))
        {
            inside += vertical ? Along(a) : Across(a);
            samples++;
        }

        var join =
            vertical
                ? Along(size - 1)
                : Across(size - 1);

        // A join that is exactly the design's own continuation is judged against that
        // transition, not against the average: a streak edge that falls on the rapport border
        // is part of the design.
        return join / Math.Max(1e-6, Math.Max(inside / samples, natural));
    }

    /// <summary>
    /// When the source continues past the rapport end with the rapport's own start (an exact
    /// period), the mismatch of that transition in the source; otherwise 0.
    /// </summary>
    private static double NaturalJoin(
        byte[] src,
        int w,
        int h,
        int tx,
        int ty,
        int tw,
        int th,
        byte[] unit,
        bool vertical)
    {
        if (vertical)
        {
            if (ty + th >= h ||
                tx + tw > w)
            {
                return 0;
            }

            var same = 0;
            var change = 0;

            for (var x = 0; x < tw; x++)
            {
                var next =
                    src[(ty + th) * w + tx + x];

                if (next == unit[x])
                    same++;

                if (next != src[(ty + th - 1) * w + tx + x])
                    change++;
            }

            return same >= 0.95 * tw
                ? change / (double)tw
                : 0;
        }

        if (tx + tw >= w ||
            ty + th > h)
        {
            return 0;
        }

        var equal = 0;
        var changed = 0;

        for (var y = 0; y < th; y++)
        {
            var next =
                src[(ty + y) * w + tx + tw];

            if (next == unit[y * tw])
                equal++;

            if (next != src[(ty + y) * w + tx + tw - 1])
                changed++;
        }

        return equal >= 0.95 * th
            ? changed / (double)th
            : 0;
    }

    private static int Mod(
        int value,
        int size) =>
        (value % size + size) % size;

    /// <summary>
    /// Makes the rapport tile seamlessly across the width. Inside a band of columns each row
    /// switches, at the minimum-mismatch column, between the rapport and the source content that
    /// truly continues across the join: the columns just left of the rapport (band at the
    /// rapport's end), or the columns just right of it (band at its start). When the rapport is
    /// the whole design the two ends are overlapped instead and the period shrinks by the band.
    /// Returns the (possibly narrower) rapport.
    /// </summary>
    private static (byte[] Unit, int Width) JoinHorizontally(
        byte[] src,
        int w,
        int h,
        int tx,
        int ty,
        int tw,
        int th,
        int drop,
        byte[] unit,
        int band)
    {
        // With a drop, the next repeat across shows rapport row (row - drop) beside row `row`;
        // the source carries the same offset, so its neighbours are read with it.
        int SourceRow(
            int row) =>
            ty + Mod(row, th);


        if (band < 2)
            return (unit, tw);

        var error =
            new int[th * band];
        int[] cut;

        if (tx >= band)
        {
            // End band: switch to the content leading into the rapport start.
            // End band of row u is followed by row (u - drop) of the next repeat, which the
            // source leads into from the columns left of the rapport start.
            for (var y = 0; y < th; y++)
                for (var x = 0; x < band; x++)
                    error[y * band + x] = unit[y * tw + tw - band + x] != src[SourceRow(y - drop) * w + tx - band + x] ? 1 : 0;

            cut = MinimumPath(error, band, th);

            for (var y = 0; y < th; y++)
                for (var x = cut[y]; x < band; x++)
                    unit[y * tw + tw - band + x] = src[SourceRow(y - drop) * w + tx - band + x];

            return (unit, tw);
        }

        if (tx + tw + band <= w)
        {
            // Start band: begin with the content that follows the rapport end.
            // Start band of row v sits beside row (v + drop) of the previous repeat; the source
            // continues that row in the columns right of the rapport end.
            for (var y = 0; y < th; y++)
                for (var x = 0; x < band; x++)
                    error[y * band + x] = unit[y * tw + x] != src[SourceRow(y + drop) * w + tx + tw + x] ? 1 : 0;

            cut = MinimumPath(error, band, th);

            for (var y = 0; y < th; y++)
                for (var x = 0; x < cut[y]; x++)
                    unit[y * tw + x] = src[SourceRow(y + drop) * w + tx + tw + x];

            return (unit, tw);
        }

        // Whole design: overlap the last band columns (of the row beside it) onto the first ones.
        var narrow =
            tw - band;
        var result =
            new byte[narrow * th];

        for (var y = 0; y < th; y++)
            for (var x = 0; x < band; x++)
                error[y * band + x] = unit[Mod(y + drop, th) * tw + narrow + x] != unit[y * tw + x] ? 1 : 0;

        cut = MinimumPath(error, band, th);

        for (var y = 0; y < th; y++)
        {
            for (var x = 0; x < narrow; x++)
            {
                result[y * narrow + x] =
                    x < band && x < cut[y]
                        ? unit[Mod(y + drop, th) * tw + narrow + x]
                        : unit[y * tw + x];
            }
        }

        return (result, narrow);
    }

    /// <summary>The same join across the length (bands of rows).</summary>
    private static (byte[] Unit, int Height) JoinVertically(
        byte[] src,
        int w,
        int h,
        int tx,
        int ty,
        int tw,
        int th,
        byte[] unit,
        int band)
    {
        if (band < 2)
            return (unit, th);

        // Error grids are laid out per column of the rapport (path runs across the width).
        var error =
            new int[tw * band];
        int[] cut;

        if (ty >= band)
        {
            for (var x = 0; x < tw; x++)
                for (var y = 0; y < band; y++)
                    error[x * band + y] = unit[(th - band + y) * tw + x] != src[(ty - band + y) * w + tx + x] ? 1 : 0;

            cut = MinimumPath(error, band, tw);

            for (var x = 0; x < tw; x++)
                for (var y = cut[x]; y < band; y++)
                    unit[(th - band + y) * tw + x] = src[(ty - band + y) * w + tx + x];

            return (unit, th);
        }

        if (ty + th + band <= h)
        {
            for (var x = 0; x < tw; x++)
                for (var y = 0; y < band; y++)
                    error[x * band + y] = unit[y * tw + x] != src[(ty + th + y) * w + tx + x] ? 1 : 0;

            cut = MinimumPath(error, band, tw);

            for (var x = 0; x < tw; x++)
                for (var y = 0; y < cut[x]; y++)
                    unit[y * tw + x] = src[(ty + th + y) * w + tx + x];

            return (unit, th);
        }

        var narrow =
            th - band;
        var result =
            new byte[tw * narrow];

        for (var x = 0; x < tw; x++)
            for (var y = 0; y < band; y++)
                error[x * band + y] = unit[(narrow + y) * tw + x] != unit[y * tw + x] ? 1 : 0;

        cut = MinimumPath(error, band, tw);

        for (var y = 0; y < narrow; y++)
        {
            for (var x = 0; x < tw; x++)
            {
                result[y * tw + x] =
                    y < band && y < cut[x]
                        ? unit[(narrow + y) * tw + x]
                        : unit[y * tw + x];
            }
        }

        return (result, narrow);
    }

    /// <summary>Per line, the band column where the minimum-error 8-connected path crosses.</summary>
    private static int[] MinimumPath(
        int[] error,
        int band,
        int lines)
    {
        var cost =
            new double[lines * band];
        var from =
            new int[lines * band];

        double Bias(
            int x) =>
            0.01 *
            Math.Abs(
                x -
                (band - 1) / 2.0);

        for (var x = 0; x < band; x++)
            cost[x] = error[x] + Bias(x);

        for (var line = 1; line < lines; line++)
        {
            for (var x = 0; x < band; x++)
            {
                var best = x;

                for (var px = Math.Max(0, x - 1); px <= Math.Min(band - 1, x + 1); px++)
                {
                    if (cost[(line - 1) * band + px] < cost[(line - 1) * band + best])
                        best = px;
                }

                cost[line * band + x] =
                    cost[(line - 1) * band + best] +
                    error[line * band + x] +
                    Bias(x);
                from[line * band + x] = best;
            }
        }

        var cut =
            new int[lines];
        var end = 0;

        for (var x = 1; x < band; x++)
        {
            if (cost[(lines - 1) * band + x] < cost[(lines - 1) * band + end])
                end = x;
        }

        for (var line = lines - 1; line >= 0; line--)
        {
            cut[line] = end;
            end = from[line * band + end];
        }

        return cut;
    }
}
