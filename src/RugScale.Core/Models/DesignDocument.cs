namespace RugScale.Core.Models;

/// <summary>
/// The pixel-based design surface: a grid of palette indices plus the palette itself.
/// This is the framework-independent core the UI layer renders and edits through commands.
/// </summary>
public sealed class DesignDocument
{
    private readonly byte[] _pixels;

    public int Width { get; }
    public int Height { get; }
    public Palette Palette { get; }
    public HashSet<byte> StopColors { get; } = new();
    public HashSet<byte> ParkColors { get; } = new();

    public DesignDocument(int width, int height, Palette? palette = null)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Design dimensions must be positive.");

        Width = width;
        Height = height;
        Palette = palette ?? new Palette();
        _pixels = new byte[width * height];
    }

    public byte GetPixel(int x, int y)
    {
        EnsureInBounds(x, y);
        return _pixels[y * Width + x];
    }

    /// <summary>Sets a pixel directly. Prefer routing through a command for undo/redo support.</summary>
    public void SetPixel(int x, int y, byte paletteIndex)
    {
        EnsureInBounds(x, y);
        if (paletteIndex >= Palette.Count)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex), "Palette index does not exist.");
        _pixels[y * Width + x] = paletteIndex;
    }

    private void EnsureInBounds(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException($"Coordinate ({x},{y}) is outside the {Width}x{Height} design.");
    }
}
