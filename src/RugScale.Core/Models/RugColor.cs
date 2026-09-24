namespace RugScale.Core.Models;

/// <summary>An opaque RGB color used in a design's indexed palette.</summary>
public readonly record struct RugColor(byte R, byte G, byte B)
{
    public static readonly RugColor Black = new(0, 0, 0);
    public static readonly RugColor White = new(255, 255, 255);
}
