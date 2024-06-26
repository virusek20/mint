namespace MetalMintSolid.Dir;

public class DirArchive
{
    public required List<DirFile> Files { get; set; } = [];
}

public static class BinaryReaderDarArchiveExtensions
{
    public static DirArchive ReadDirArchive(this BinaryReader reader)
    {
        var fileCount = reader.ReadInt32() / 12; // 8 bytes name, 4 bytes offest
        List<DirFile> files = [];

        for (int i = 0; i < fileCount; i++)
        {
            reader.BaseStream.Position = 4 + (12 * i);
            files.Add(reader.ReadDirFile());
        }

        return new DirArchive
        {
            Files = files
        };
    }
}

public static class BinaryWriterDarArchiveExtensions
{
    public static void Write(this BinaryWriter writer, DirArchive archive)
    {
        writer.Write(Convert.ToInt32(archive.Files.Count * 12));
        foreach (DirFile file in archive.Files) writer.Write(file);
    }
}