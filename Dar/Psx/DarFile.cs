using MetalMintSolid.Util;

namespace MetalMintSolid.Dar.Psx;

public class DarFile
{
    public required ushort Hash { get; set; }
    public required byte Extension { get; set; }
    public string ExtensionName => ExtensionNames.Extensions[Extension];
    public byte Padding { get; set; }
    public required byte[] Data { get; set; }

    public override string ToString()
    {
        return $"{Hash}.{ExtensionName} ({Data.Length} B)";
    }
}

public static class BinaryReaderDarFileExtensions
{
    public static DarFile ReadDarFile(this BinaryReader reader)
    {
        var hash = reader.ReadUInt16();
        var extension = reader.ReadByte();
        var padding = reader.ReadByte();
        var length = reader.ReadInt32();

        return new DarFile
        {
            Hash = hash,
            Extension = extension,
            Padding = padding,
            Data = reader.ReadBytes(length)
        };
    }
}

public static class BinaryWriterDarFileExtensions
{
    public static void Write(this BinaryWriter writer, DarFile file)
    {
        writer.Write(file.Hash);
        writer.Write(file.Extension);
        writer.Write(file.Padding);
        writer.Write(file.Data.Length);
        writer.Write(file.Data);
    }
}