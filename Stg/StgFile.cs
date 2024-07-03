namespace MetalMintSolid.Stg;

// TODO: This whole class isn't really used ATM
public class StgFile
{
    public required StgHeader Header { get; set; }
    public required List<StgConfig> Configs { get; set; }
}

public static class BinaryReaderStgFileExtensions
{
    public static StgFile ReadStage(this BinaryReader reader)
    {
        return new StgFile
        {
            Header = reader.ReadStgHeader(),
            Configs = reader.ReadStgConfigList(),
        };
    }
}

public static class BinaryWriterStgFileExtensions
{
    public static void Write(this BinaryWriter writer, StgFile file)
    {
        writer.Write(file.Header);
        foreach (var config in file.Configs) writer.Write(config);

        // TODO: Write data
    }
}