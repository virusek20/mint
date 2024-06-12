using System.Drawing;

namespace MetalMintSolid.Pcx;

public class PcxHeader
{
    /// <summary>
    /// Always 0x0A
    /// </summary>
    public byte Manufacturer { get; set; } = 0x0A;

    /// <summary>
    /// PC Paintbrush version. Acts as file format version.<br /><br />
    /// 0 = v2.5<br />
    /// 2 = v2.8 with palette<br />
    /// 3 = v2.8 without palette<br />
    /// 4 = Paintbrush for Windows<br />
    /// 5 = v3.0 or higher
    /// </summary>
    public byte Version { get; set; }

    /// <summary>
    /// Should be 0x01<br /><br />
    /// 0 = uncompressed image(not officially allowed, but some software supports it)<br />
    /// 1 = PCX run length encoding<br />
    /// </summary>
    public byte Encoding { get; set; }

    /// <summary>
    /// Number of bits per pixel in each entry of the colour planes (1, 2, 4, 8, 24) 
    /// </summary>
    public byte BitsPerPlane { get; set; }

    public required Vector2UInt16 WindowMin { get; set; }
    public required Vector2UInt16 WindowMax { get; set; }

    /// <summary>
    /// This is supposed to specify the image's vertical resolution in DPI (dots per inch), but it is rarely reliable. It often contains the image dimensions, or nothing at all.
    /// </summary>
    public ushort VerticalDPI { get; set; }

    /// <summary>
    /// This is supposed to specify the image's horizontal resolution in DPI (dots per inch), but it is rarely reliable. It often contains the image dimensions, or nothing at all.
    /// </summary>
    public ushort HorizontalDPI { get; set; }

    /// <summary>
    /// Palette for 16 colors or less, in three-byte RGB entries. Padded with 0x00 to 48 bytes in total length.
    /// </summary>
    public required Color[] Palette { get; set; } = new Color[16];

    /// <summary>
    /// Should be set to 0, but can sometimes contain junk. 
    /// </summary>
    public byte Reserved { get; set; }

    /// <summary>
    /// Number of colour planes. Multiply by BitsPerPlane to get the actual colour depth.
    /// </summary>
    public byte ColorPlanes { get; set; }

    /// <summary>
    /// Number of bytes to read for a single plane's scanline, i.e. at least ImageWidth ÷ 8 bits per byte × BitsPerPlane. Must be an even number.<br />
    /// Do <b>not</b> calculate from Xmax-Xmin. Normally a multiple of the machine's native word length (2 or 4).
    /// </summary>
    public ushort BytesPerPlaneLine { get; set; }

    /// <summary>
    /// How to interpret palette:<br /><br />
    /// 1 = Color / BW<br />
    /// 2 = Grayscale (ignored in PC Paintbrush IV and later)
    /// </summary>
    public ushort PaletteInfo { get; set; }

    /// <summary>
    /// Only supported by PC Paintbrush IV or higher; deals with scrolling. Best to just ignore it.
    /// </summary>
    public required Vector2UInt16 ScrSize { get; set; }

    public ushort MgsMagic { get; set; } = 12345;

    /// <summary>
    /// Unkwnown
    /// </summary>
    // TODO: Can be reverse engineered from the MGS decompilation repo
    public ushort Flags { get; set; } = 8;

    /// <summary>
    /// X position of image data in VRAM
    /// </summary>
    public ushort Px { get; set; } = 0x0380;

    /// <summary>
    /// Y position of image data in VRAM
    /// </summary>
    public ushort Py { get; set; } = 0x0040;

    /// <summary>
    /// X position of CLUT table in VRAM
    /// </summary>
    public ushort Cx { get; set; } = 0x0310;

    /// <summary>
    /// Y position of CLUT table in VRAM
    /// </summary>
    public ushort Cy { get; set; } = 0x00E2;

    /// <summary>
    /// Number of used colors, this will determine how many pixels will be copied to VRAM at <see cref="Cx"/>, <see cref="Cy"/>
    /// </summary>
    public ushort NColors { get; set; } = 16;

    /// <summary>
    /// Filler to bring header up to 128 bytes total. Can contain junk.
    /// </summary>
    public required byte[] Padding { get; set; } = new byte[40];
}

public static class BinaryReaderPcxHeaderExtensions
{
    public static PcxHeader ReadPcxHeader(this BinaryReader reader)
    {
        var header = new PcxHeader
        {
            Manufacturer = reader.ReadByte(),
            Version = reader.ReadByte(),
            Encoding = reader.ReadByte(),
            BitsPerPlane = reader.ReadByte(),
            WindowMin = reader.ReadVector2UInt16(),
            WindowMax = reader.ReadVector2UInt16(),
            VerticalDPI = reader.ReadUInt16(),
            HorizontalDPI = reader.ReadUInt16(),
            Palette = reader.ReadPcxPalette(),
            Reserved = reader.ReadByte(),
            ColorPlanes = reader.ReadByte(),
            BytesPerPlaneLine = reader.ReadUInt16(),
            PaletteInfo = reader.ReadUInt16(),
            ScrSize = reader.ReadVector2UInt16(),
            MgsMagic = reader.ReadUInt16(),
            Flags = reader.ReadUInt16(),
            Px = reader.ReadUInt16(),
            Py = reader.ReadUInt16(),
            Cx = reader.ReadUInt16(),
            Cy = reader.ReadUInt16(),
            NColors = reader.ReadUInt16(),
            Padding = reader.ReadBytes(40)
        };

        if (header.Manufacturer != 0x0A) throw new InvalidDataException($"Manufacturer is '{header.Manufacturer}', expected '10'");
        if (header.PaletteInfo != 1) throw new NotImplementedException("Grayscale images are not implemented");
        if (header.Encoding != 1) throw new NotImplementedException("Uncompressed images are not implemented");

        if (header.BitsPerPlane != 1 || header.ColorPlanes != 4) throw new NotImplementedException("This image is probably not EGA");
        if (header.MgsMagic != 12345) throw new InvalidDataException($"MGSMagic is '{header.MgsMagic}', expected '12345'");
        //if (header.NColors != 16) throw new InvalidDataException($"NColors is '{header.NColors}', expected '16'");
        if (header.NColors != 16) Console.WriteLine($"NColors is '{header.NColors}', expected '16'");

        return header;
    }

    private static Color[] ReadPcxPalette(this BinaryReader reader)
    {
        var palette = new Color[16];

        for (int i = 0; i < 16; i++)
        {
            palette[i] = Color.FromArgb(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        return palette;
    }
}

public static class BinaryWriterPcxHeaderExtensions
{
    public static void Write(this BinaryWriter writer, PcxHeader header)
    {
        writer.Write(header.Manufacturer);
        writer.Write(header.Version);
        writer.Write(header.Encoding);
        writer.Write(header.BitsPerPlane);
        writer.Write(header.WindowMin);
        writer.Write(header.WindowMax);
        writer.Write(header.VerticalDPI);
        writer.Write(header.HorizontalDPI);
        writer.Write(header.Palette);
        writer.Write(header.Reserved);
        writer.Write(header.ColorPlanes);
        writer.Write(header.BytesPerPlaneLine);
        writer.Write(header.PaletteInfo);
        writer.Write(header.ScrSize);
        writer.Write(header.MgsMagic);
        writer.Write(header.Flags);
        writer.Write(header.Px);
        writer.Write(header.Py);
        writer.Write(header.Cx);
        writer.Write(header.Cy);
        writer.Write(header.NColors);
        writer.Write(header.Padding);
    }

    private static void Write(this BinaryWriter writer, Color[] palettte)
    {
        if (palettte.Length > 16) throw new NotSupportedException("Palette size cannot be larger than 16 for EGA");

        for (int i = 0; i < 16; i++)
        {
            var color = palettte[i % palettte.Length];
            writer.Write(color.R);
            writer.Write(color.G);
            writer.Write(color.B);
        }
    }
}