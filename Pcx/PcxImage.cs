using SkiaSharp;

namespace MetalMintSolid.Pcx;

public class PcxImage
{
    public required PcxHeader Header { get; set; }
    public required byte[] ImageData { get; set; }

    /// <summary>
    /// 256-color palette read from / written to the trailing palette block (VGA
    /// PCX files). Null for EGA images which use the 16-color header palette.
    /// </summary>
    public SKColor[]? ExtendedPalette { get; set; }

    public SKBitmap AsBitmap()
    {
        if (Header.IsVga)
            return AsBitmapVga();

        return AsBitmapEga();
    }

    private SKBitmap AsBitmapEga()
    {
        var w = Header.WindowMax.X + 1;
        var h = Header.WindowMax.Y + 1;
        var indexedImage = new int[w * h];
        var lineSize = Header.BytesPerPlaneLine * Header.ColorPlanes;

        for (int y = 0; y < h; y++)
        {
            var plane1 = ImageData.AsSpan(y * lineSize + (0 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);
            var plane2 = ImageData.AsSpan(y * lineSize + (1 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);
            var plane3 = ImageData.AsSpan(y * lineSize + (2 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);
            var plane4 = ImageData.AsSpan(y * lineSize + (3 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);

            for (int x = 0; x < w; x++)
            {
                var b = x / 8;
                var bit = x % 8;
                var mask = 1 << (7 - bit);

                indexedImage[y * w + x] =
                    (((plane1[b] & mask) >> (7 - bit)) << 0) |
                    (((plane2[b] & mask) >> (7 - bit)) << 1) |
                    (((plane3[b] & mask) >> (7 - bit)) << 2) |
                    (((plane4[b] & mask) >> (7 - bit)) << 3);
            }
        }

        var decoded = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                decoded.SetPixel(x, y, Header.Palette[indexedImage[x + y * w]]);
            }
        }

        return decoded;
    }

    private SKBitmap AsBitmapVga()
    {
        var w = Header.WindowMax.X + 1;
        var h = Header.WindowMax.Y + 1;

        // For VGA, use the extended 256-color trailing palette if available,
        // otherwise fall back to the 16-color header palette
        var palette = ExtendedPalette ?? Header.Palette;

        var decoded = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Each scanline is BytesPerPlaneLine bytes wide (may include padding beyond image width)
                var index = ImageData[y * Header.BytesPerPlaneLine + x];

                if (index < palette.Length)
                    decoded.SetPixel(x, y, palette[index]);
                else
                    decoded.SetPixel(x, y, new SKColor(0, 0, 0)); // Fallback for out-of-range indices
            }
        }

        return decoded;
    }

    /// <summary>
    /// Encodes a bitmap into an MGS PCX/PCC image.
    /// EGA (16-color, 4bpp) is produced for images with up to 16 colors; VGA
    /// (256-color, 8bpp) for 17-256 colors. <paramref name="forceVga"/> overrides
    /// the auto-choice so an edited texture is written in the SAME format as the
    /// original it replaces (pass the original's <c>Header.IsVga</c>): a VGA slot
    /// must stay VGA even if the edit happens to use few colors, and vice-versa.
    /// </summary>
    public static PcxImage FromBitmap(SKBitmap bitmap, PaletteData? palette = null, bool? forceVga = null)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;

        // If a palette is supplied (e.g. the ORIGINAL texture's CLUT, for texture
        // swapping) it is used VERBATIM and IN ORDER -- never rebuilt or de-duped.
        // This is essential: MGS textures share CLUTs in VRAM, so the new texture
        // must reference the exact same palette ordering as the original or every
        // other texture sharing that CLUT renders with the wrong colors. When no
        // palette is supplied we gather distinct colors from the bitmap (first
        // appearance order), matching the original encoder for EGA byte-identity.
        List<SKColor> colors;
        if (palette != null)
        {
            colors = palette.Palette.ToList();
        }
        else
        {
            var acc = new List<SKColor>();
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.Alpha != 255) color = new SKColor(0, 0, 0);
                    acc.Add(color);
                }
            colors = acc.Distinct().ToList();
        }

        var vga = forceVga ?? (colors.Count > 16);

        if (!vga && colors.Count > 16)
            throw new NotSupportedException($"EGA palettes are limited to 16 colors (palette has {colors.Count}).");
        if (vga && colors.Count > 256)
            throw new NotSupportedException($"VGA palettes are limited to 256 colors (palette has {colors.Count}).");

        var resolve = MakeResolver(colors);
        return vga ? BuildVga(bitmap, w, h, colors, resolve) : BuildEga(bitmap, w, h, colors, resolve);
    }

    // Maps a pixel to a palette index: exact match first (first occurrence wins,
    // so duplicate CLUT entries render identically), nearest RGB as a fallback for
    // colors the edit introduced that aren't in the fixed palette. Non-opaque
    // pixels collapse to black, matching MGS's transparency convention.
    private static Func<SKColor, int> MakeResolver(List<SKColor> palette)
    {
        var dict = new Dictionary<SKColor, int>();
        for (int i = 0; i < palette.Count; i++)
            if (!dict.ContainsKey(palette[i])) dict[palette[i]] = i;

        return px =>
        {
            if (px.Alpha != 255) px = new SKColor(0, 0, 0);
            if (dict.TryGetValue(px, out var idx)) return idx;
            int best = 0; long bestD = long.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                long dr = px.Red - palette[i].Red, dg = px.Green - palette[i].Green, db = px.Blue - palette[i].Blue;
                long dd = dr * dr + dg * dg + db * db;
                if (dd < bestD) { bestD = dd; best = i; }
            }
            return best;
        };
    }

    private static PcxImage BuildEga(SKBitmap bitmap, int w, int h, List<SKColor> colors, Func<SKColor, int> resolve)
    {
        var header = new PcxHeader
        {
            Manufacturer = 0x0A,
            Version = 5,
            Encoding = 1,
            BitsPerPlane = 1,
            WindowMin = new Vector2UInt16 { X = 0, Y = 0 },
            WindowMax = new Vector2UInt16
            {
                X = Convert.ToUInt16(w - 1),
                Y = Convert.ToUInt16(h - 1)
            },
            VerticalDPI = 1600,
            HorizontalDPI = 1200,
            Palette = [.. colors],
            Reserved = 0,
            ColorPlanes = 4,
            BytesPerPlaneLine = Convert.ToUInt16(Math.Ceiling(w / 8.0)),
            PaletteInfo = 1,
            ScrSize = new Vector2UInt16 { X = 640, Y = 480 },
            MgsMagic = 12345,
            Padding = new byte[40]
        };

        var indexedImage = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                indexedImage[x, y] = (byte)resolve(bitmap.GetPixel(x, y));

        MemoryStream buffer = new();
        for (int y = 0; y < h; y++)
        {
            var plane1 = new byte[header.BytesPerPlaneLine];
            var plane2 = new byte[header.BytesPerPlaneLine];
            var plane3 = new byte[header.BytesPerPlaneLine];
            var plane4 = new byte[header.BytesPerPlaneLine];

            for (int x = 0; x < header.BytesPerPlaneLine; x++)
            {
                for (int i = 0; i < 8; i++)
                {
                    var xSrc = (x * 8) + i;
                    if (xSrc >= w) continue;

                    plane1[x] |= (byte)(((indexedImage[xSrc, y] & 0x01) >> 0) << (7 - i));
                    plane2[x] |= (byte)(((indexedImage[xSrc, y] & 0x02) >> 1) << (7 - i));
                    plane3[x] |= (byte)(((indexedImage[xSrc, y] & 0x04) >> 2) << (7 - i));
                    plane4[x] |= (byte)(((indexedImage[xSrc, y] & 0x08) >> 3) << (7 - i));
                }
            }

            buffer.Write(plane1);
            buffer.Write(plane2);
            buffer.Write(plane3);
            buffer.Write(plane4);
        }

        return new PcxImage
        {
            Header = header,
            ImageData = RleCompress(buffer.ToArray())
        };
    }

    private static PcxImage BuildVga(SKBitmap bitmap, int w, int h, List<SKColor> colors, Func<SKColor, int> resolve)
    {
        var stride = (ushort)(w + (w & 1));

        var header = new PcxHeader
        {
            Manufacturer = 0x0A,
            Version = 5,
            Encoding = 1,
            BitsPerPlane = 8,
            WindowMin = new Vector2UInt16 { X = 0, Y = 0 },
            WindowMax = new Vector2UInt16
            {
                X = Convert.ToUInt16(w - 1),
                Y = Convert.ToUInt16(h - 1)
            },
            VerticalDPI = 1600,
            HorizontalDPI = 1200,
            Palette = BuildHeaderPalette(colors),
            Reserved = 0,
            ColorPlanes = 1,
            BytesPerPlaneLine = stride,
            PaletteInfo = 1,
            ScrSize = new Vector2UInt16 { X = 640, Y = 480 },
            MgsMagic = 12345,
            NColors = (ushort)colors.Count,
            Padding = new byte[40]
        };

        var raw = new byte[stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                raw[y * stride + x] = (byte)resolve(bitmap.GetPixel(x, y));

        // Trailing CLUT block carries the palette IN ORDER. Always 256 entries on
        // disk; entries beyond the real palette are padded black.
        var ext = new SKColor[256];
        for (int i = 0; i < 256; i++) ext[i] = i < colors.Count ? colors[i] : new SKColor(0, 0, 0);

        return new PcxImage
        {
            Header = header,
            ImageData = RleCompress(raw),
            ExtendedPalette = ext
        };
    }

    private static SKColor[] BuildHeaderPalette(List<SKColor> colors)
    {
        var hp = new SKColor[16];
        for (int i = 0; i < 16; i++) hp[i] = i < colors.Count ? colors[i] : new SKColor(0, 0, 0);
        return hp;
    }

    // PCX run-length encoding. Matches the original encoder exactly: every byte
    // >= 0xC0 is escaped as a 1-count run (0xC1, value); all other bytes are
    // written literally. (Run-merging is intentionally not performed.)
    private static byte[] RleCompress(byte[] uncompressed)
    {
        MemoryStream bufferCompressed = new();
        var index = 0;
        while (uncompressed.Length != index)
        {
            var b = uncompressed[index];
            var repeat = 1;
            index++;
            if (b >= 192 || repeat > 1)
            {
                bufferCompressed.WriteByte((byte)(0xC0 | repeat));
                bufferCompressed.WriteByte(b);
            }
            else
            {
                bufferCompressed.WriteByte(b);
            }
        }
        return bufferCompressed.ToArray();
    }
}

public static class BinaryReaderPcxImageExtensions
{
    public static PcxImage ReadPcxImage(this BinaryReader reader)
    {
        var header = reader.ReadPcxHeader();
        var imageData = reader.ReadPcxImageData(header);

        SKColor[]? extendedPalette = null;

        // For VGA (8bpp) images, try to read the trailing 256-color palette
        if (header.IsVga)
        {
            extendedPalette = reader.TryReadTrailingPalette();

            if (extendedPalette == null)
            {
                // No trailing palette found — fall back to the 16-color header palette
                Console.WriteLine("VGA image has no trailing 256-color palette, using header palette");
            }
        }

        return new PcxImage
        {
            Header = header,
            ImageData = imageData,
            ExtendedPalette = extendedPalette
        };
    }

    private static byte[] ReadPcxImageData(this BinaryReader reader, PcxHeader header)
    {
        MemoryStream dataBuffer = new();
        var uncompressedLength = header.ColorPlanes * (header.WindowMax.Y + 1) * header.BytesPerPlaneLine;

        while (uncompressedLength > 0)
        {
            var b = reader.ReadByte();
            if (b >= 192)
            {
                // Repeat or >= 192
                var repeatCount = b & 0x3F;
                var repeatedByte = reader.ReadByte();

                for (var r = 0; r < repeatCount; r++)
                {
                    dataBuffer.WriteByte(repeatedByte);
                    uncompressedLength--;
                }
            }
            else
            {
                // No repeat and < 192
                dataBuffer.WriteByte(b);
                uncompressedLength--;
            }
        }

        return dataBuffer.ToArray();
    }

    /// <summary>
    /// Attempts to read a 256-color trailing palette from the remaining stream data.
    /// Standard VGA PCX format: 0x0C marker byte followed by 768 bytes (256 RGB triples).
    /// Returns null if no valid trailing palette is found.
    /// </summary>
    private static SKColor[]? TryReadTrailingPalette(this BinaryReader reader)
    {
        var remaining = reader.BaseStream.Length - reader.BaseStream.Position;

        // Standard trailing palette: 1 byte marker + 256 * 3 bytes = 769 bytes
        if (remaining < 769)
        {
            if (remaining > 0)
                Console.WriteLine($"Unexpected {remaining} bytes after image payload (too small for trailing palette)");
            return null;
        }

        var marker = reader.ReadByte();
        if (marker != 0x0C)
        {
            Console.WriteLine($"Expected trailing palette marker 0x0C but got 0x{marker:X2}, skipping trailing palette");
            return null;
        }

        var palette = new SKColor[256];
        for (int i = 0; i < 256; i++)
        {
            palette[i] = new SKColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        if (reader.BaseStream.Length != reader.BaseStream.Position)
            Console.WriteLine("Unexpected data after trailing palette");

        return palette;
    }
}

public static class BinaryWriterPcxImageExtensions
{
    public static void Write(this BinaryWriter writer, PcxImage image)
    {
        writer.Write(image.Header);
        writer.Write(image.ImageData);

        // VGA images carry a 256-color CLUT in a trailing block: 0x0C marker then
        // 256 RGB triples. Without it the texture's colors are lost on reload.
        if (image.Header.IsVga && image.ExtendedPalette != null)
        {
            writer.Write((byte)0x0C);
            for (int i = 0; i < 256; i++)
            {
                var c = i < image.ExtendedPalette.Length ? image.ExtendedPalette[i] : new SKColor(0, 0, 0);
                writer.Write(c.Red);
                writer.Write(c.Green);
                writer.Write(c.Blue);
            }

            // Original MGS VGA .pcc files pad the file to an even byte count after
            // the trailing palette; reproduce that so packing/alignment matches.
            if (writer.BaseStream.Position % 2 != 0) writer.Write((byte)0x00);
        }
    }
}
