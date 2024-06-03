namespace MetalMintSolid.Dar;

public class DarArchive
{
    public required List<DarFile> Files { get; set; } = [];
}

public static class BinaryReaderDarArchiveExtensions
{
    public static DarArchive ReadDarArchive(this BinaryReader reader)
    {
        var fileCount = reader.ReadUInt32();
        List<DarFile> files = [];

        for (int i = 0; i < fileCount; i++)
        {
            files.Add(reader.ReadDarFile());
        }

        return new DarArchive
        {
            Files = files
        };
    }
}

public static class BinaryWriterDarArchiveExtensions
{
    public static void Write(this BinaryWriter writer, DarArchive archive)
    {
        writer.Write(Convert.ToUInt32(archive.Files.Count));
        foreach (DarFile file in archive.Files) writer.Write(file);
    }
}