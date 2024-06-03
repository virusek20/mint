using System.Text;

namespace MetalMintSolid.Dar;

public class DarFile
{
    public required string Name { get; set; }
    public required byte[] Data { get; set; }

    public override string ToString()
    {
        return $"{Name} ({Data.Length} B)";
    }
}

public static class BinaryReaderDarFileExtensions
{
    public static DarFile ReadDarFile(this BinaryReader reader)
    {
        // ASCIIZ Name
        var nameBuilder = new StringBuilder();
        while (true)
        {
            char c = reader.ReadChar();
            if (c == '\0') break;

            nameBuilder.Append(c);
        }

        // Padding to 4 byte boundary
        int padding = 4 - (int)(reader.BaseStream.Position % 4);
        if (padding != 4) reader.ReadBytes(padding);

        // Length prefixed content with null termination
        var fileLength = Convert.ToInt32(reader.ReadUInt32());
        var content = reader.ReadBytes(fileLength);
        if (reader.ReadByte() != 0) throw new InvalidDataException("Expected null terminator at end of file");

        return new DarFile
        {
            Name = nameBuilder.ToString(),
            Data = content
        };
    }
}

public static class BinaryWriterDarFileExtensions
{
    public static void Write(this BinaryWriter writer, DarFile file)
    {
        // ASCIIZ Name
        var nameBytes = Encoding.ASCII.GetBytes(file.Name);
        writer.Write(nameBytes);
        writer.Write((byte)0);

        // Padding to 4 byte boundary
        int padding = 4 - (int)(writer.BaseStream.Position % 4);
        if (padding != 4)
        {
            for (int i = 0; i < padding; i++) writer.Write((byte)0);
        }

        // Length prefixed content with null termination
        writer.Write(Convert.ToUInt32(file.Data.Length));
        writer.Write(file.Data);
        writer.Write((byte)0);
    }
}