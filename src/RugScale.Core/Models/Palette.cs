namespace RugScale.Core.Models;

/// <summary>
/// Indexed color palette for a design. A design pixel stores a palette index (byte),
/// not a raw color, matching the indexed-color model used by carpet/weave CAD tools.
/// </summary>
public sealed class Palette
{
    public const int MaximumColorCount = 256;
    private readonly List<RugColor> _colors;

    public Palette(IEnumerable<RugColor>? initialColors = null)
    {
        _colors = initialColors?.Take(MaximumColorCount).ToList() ?? new List<RugColor> { RugColor.White, RugColor.Black };
        // An indexed RugScale document always has addressable slots 0..255.  File formats such as
        // BMP may carry a shorter colour table, but padding it here keeps palette UI, shortcuts
        // and colour-transfer operations from treating missing indexes as non-existent colours.
        while (_colors.Count < MaximumColorCount)
            _colors.Add(RugColor.Black);
    }

    /// <summary>
    /// The 256-entry palette a brand new design starts with, so every index 0-255 is a real,
    /// distinct color the user can draw with right away instead of the design shipping with only
    /// a handful. Index 0 is white and index 1 is black (0 doubles as the default erase/background
    /// color — see DesignSurfaceViewModel.BackgroundPaletteIndex), then the standard 6x6x6 RGB
    /// cube fills 2-215 with an even spread of hues, and a fine grayscale ramp fills the rest.
    /// The ramp's steps are 255/41ths, which never land on the cube's own grays (multiples of 51),
    /// so all 256 entries stay distinct.
    /// </summary>
    public static Palette CreateDefault256()
    {
        var colors = new List<RugColor>(DefaultColorCount) { RugColor.White, RugColor.Black };

        for (var r = 0; r < 6; r++)
        {
            for (var g = 0; g < 6; g++)
            {
                for (var b = 0; b < 6; b++)
                {
                    var color = new RugColor((byte)(r * 51), (byte)(g * 51), (byte)(b * 51));
                    if (color != RugColor.Black && color != RugColor.White)
                        colors.Add(color);
                }
            }
        }

        for (var i = 0; colors.Count < DefaultColorCount; i++)
        {
            var level = (byte)((i + 1) * 255 / 41);
            colors.Add(new RugColor(level, level, level));
        }

        return new Palette(colors);
    }

    private const int DefaultColorCount = MaximumColorCount;

    public int Count => _colors.Count;

    public RugColor this[int index] => _colors[index];

    public int Add(RugColor color)
    {
        if (_colors.Count >= MaximumColorCount)
            throw new InvalidOperationException($"An indexed palette cannot exceed {MaximumColorCount} colors.");
        _colors.Add(color);
        return _colors.Count - 1;
    }

    public void Set(int index, RugColor color) => _colors[index] = color;

    public IReadOnlyList<RugColor> Colors => _colors;
}
