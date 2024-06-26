using System.Drawing;

namespace MetalMintSolid.Pcx;

public class PcxImage
{
    public required PcxHeader Header { get; set; }
    public required byte[] ImageData { get; set; }

    public Bitmap AsBitmap()
    {
        var indexedImage = new int[(Header.WindowMax.X + 1) * (Header.WindowMax.Y + 1)];
        var lineSize = Header.BytesPerPlaneLine * Header.ColorPlanes;

        for (int y = 0; y < Header.WindowMax.Y + 1; y++)
        {
            var plane1 = ImageData.AsSpan(y * lineSize + (0 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);
            var plane2 = ImageData.AsSpan(y * lineSize + (1 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);
            var plane3 = ImageData.AsSpan(y * lineSize + (2 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);
            var plane4 = ImageData.AsSpan(y * lineSize + (3 * Header.BytesPerPlaneLine), Header.BytesPerPlaneLine);

            for (int x = 0; x < Header.WindowMax.X + 1; x++)
            {
                var b = x / 8;
                var bit = x % 8;
                var mask = 1 << (7 - bit);

                indexedImage[y * (Header.WindowMax.X + 1) + x] =
                    (((plane1[b] & mask) >> (7 - bit)) << 0) |
                    (((plane2[b] & mask) >> (7 - bit)) << 1) |
                    (((plane3[b] & mask) >> (7 - bit)) << 2) |
                    (((plane4[b] & mask) >> (7 - bit)) << 3);
            }
        }

        var decoded = new Bitmap(Header.WindowMax.X + 1, Header.WindowMax.Y + 1);
        for (int y = 0; y < Header.WindowMax.Y + 1; y++)
        {
            for (int x = 0; x < Header.WindowMax.X + 1; x++)
            {
                decoded.SetPixel(x, y, Header.Palette[indexedImage[x + y * (Header.WindowMax.X + 1)]]);
            }
        }

        return decoded;
    }

    public static PcxImage FromBitmap(Bitmap bitmap, PaletteData? palette = null)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;

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
            Palette = new Color[16],
            Reserved = 0,
            ColorPlanes = 4,
            BytesPerPlaneLine = Convert.ToUInt16(Math.Ceiling(w / 8.0)),
            PaletteInfo = 1,
            ScrSize = new Vector2UInt16 { X = 640, Y = 480 },
            MgsMagic = 12345,
            Padding = new byte[40]
        };

        if (palette == null)
        {
            palette = new()
            {
                Palette = []
            };

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.A != 255) color = Color.FromArgb(0, 0, 0);

                    palette.Palette.Add(color);
                }
            }
        }

        palette.Palette = palette.Palette.Distinct().ToList();
        if (palette.Palette.Count > 16) throw new NotSupportedException("EGA palettes are limited to 16 colors");
        header.Palette = [.. palette.Palette];

        var indexedImage = new byte[w, h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                // TODO: Slow
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A != 255) pixel = Color.FromArgb(0, 0, 0);

                var colorIndex = Array.IndexOf(header.Palette, pixel);
                //if (colorIndex == -1) Console.WriteLine($"Cannot find color '{pixel}'");

                indexedImage[x, y] = (byte)colorIndex;
            }
        }

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

        MemoryStream bufferCompressed = new();

        var uncompressed = buffer.ToArray();
        var index = 0;

        while (uncompressed.Length != index)
        {
            var b = uncompressed[index];
            var repeat = 1;

            // TODO: This compression is optional really
            /*
            while ((index % 9) != 8 && index + repeat < uncompressed.Length && uncompressed[index + repeat] == b)
            {
                repeat++;
                index++;
            }
            */

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

        return new PcxImage
        {
            Header = header,
            ImageData = bufferCompressed.ToArray()
        };
    }
}

public static class BinaryReaderPcxImageExtensions
{
    public static PcxImage ReadPcxImage(this BinaryReader reader)
    {
        var header = reader.ReadPcxHeader();

        return new PcxImage
        {
            Header = header,
            ImageData = reader.ReadPcxImageData(header)
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

        if (reader.BaseStream.Length != reader.BaseStream.Position) Console.WriteLine("Unexpected data after image payload");
        return dataBuffer.ToArray();
    }
}

public static class BinaryWriterPcxImageExtensions
{
    public static void Write(this BinaryWriter writer, PcxImage image)
    {
        writer.Write(image.Header);
        writer.Write(image.ImageData);
    }
}