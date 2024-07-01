using System.Text;

namespace MetalMintSolid.Dir;

public class DirFile
{
    public required string Name { get; set; }
    public required int Offset { get; set; }

    /// <summary>
    /// Gets the entry name without any control characters
    /// </summary>
    public string SanitizedName => new(Name.Where(c => !char.IsControl(c)).ToArray());

    public override string ToString()
    {
        return $"{SanitizedName} @ 0x{Offset:X}";
    }
}

public static class BinaryReaderDirFileExtensions
{
    public static DirFile ReadDirFile(this BinaryReader reader)
    {
        var name = reader.ReadChars(8);
        var offset = reader.ReadInt32() * 2048;

        return new DirFile
        {
            Name = new(name),
            Offset = offset
        };
    }
}

public static class BinaryWriterDirFileExtensions
{
    public static void Write(this BinaryWriter writer, DirFile file)
    {
        var nameBytes = Encoding.ASCII.GetBytes(file.Name);
        if (nameBytes.Length > 8) throw new NotSupportedException("Max DIR archive file name length can be 8 ASCII characters");

        if (file.Offset % 2048 != 0) throw new NotSupportedException("DIR entry offsets have to be 2048 byte aligned");

        Array.Resize(ref nameBytes, 8); // Pad in case we have less
        writer.Write(nameBytes);
        writer.Write(file.Offset / 2048);
    }
}