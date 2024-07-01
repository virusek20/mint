using SkiaSharp;

namespace MetalMintSolid.Pcx;

public class PaletteData
{
    public ushort Cx { get; set; }
    public ushort Cy { get; set; }

    public required List<SKColor> Palette { get; set; }
}
