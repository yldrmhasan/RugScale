using RugScale.Core.Models;

namespace RugScale.Core.IO;

public sealed record IndexedBmpImage(
    DesignDocument Document,
    int XPixelsPerMeter,
    int YPixelsPerMeter);

/// <summary>
/// Minimal lossless codec for uncompressed 8-bit indexed BMP files used by RugScale training,
/// audits and host integration. Palette indexes are preserved exactly; no RGB resampling occurs.
/// </summary>
public static class IndexedBmpCodec
{
    public static IndexedBmpImage Read(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream =
            File.OpenRead(path);
        using var reader =
            new BinaryReader(stream);

        if (reader.ReadUInt16() != 0x4D42)
        {
            throw new InvalidDataException(
                "Not a BMP file.");
        }

        _ = reader.ReadUInt32();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        var pixelOffset =
            reader.ReadUInt32();
        var dibSize =
            reader.ReadUInt32();

        if (dibSize < 40)
        {
            throw new InvalidDataException(
                "Only BITMAPINFOHEADER-or-newer BMP is supported.");
        }

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
                    (int)dibSize -
                    40));
        }

        var paletteEntryBytes =
            checked(
                (int)pixelOffset -
                (14 +
                 (int)dibSize));
        var availableEntries =
            Math.Max(
                0,
                paletteEntryBytes /
                4);
        var requestedEntries =
            colorsUsed == 0
                ? 256
                : Math.Min(
                    256,
                    checked(
                        (int)colorsUsed));
        var paletteEntries =
            Math.Min(
                requestedEntries,
                availableEntries);

        var colors =
            Enumerable.Repeat(
                    RugColor.Black,
                    256)
                .ToArray();

        for (var index = 0;
             index < paletteEntries;
             index++)
        {
            var b =
                reader.ReadByte();
            var g =
                reader.ReadByte();
            var r =
                reader.ReadByte();
            _ = reader.ReadByte();

            colors[index] =
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
            stream.ReadExactly(row);

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

        return new IndexedBmpImage(
            document,
            xppm,
            yppm);
    }

    public static void Write(
        string path,
        DesignDocument document,
        int xPixelsPerMeter = 0,
        int yPixelsPerMeter = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var directory =
            Path.GetDirectoryName(
                Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

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
        writer.Write(
            xPixelsPerMeter);
        writer.Write(
            yPixelsPerMeter);
        writer.Write(
            (uint)256);
        writer.Write(
            (uint)256);

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
                 document.Height -
                 1;
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
