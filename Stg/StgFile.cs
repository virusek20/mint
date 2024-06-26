namespace MetalMintSolid.Stg;

public class StgFile
{
    public required StgHeader Header { get; set; }
    public required List<StgConfig> Configs { get; set; }
}

public static class BinaryReaderStgFileExtensions
{
    public static StgFile ReadStage(this BinaryReader reader)
    {
        return null;
    }
}

public static class BinaryWriterStgFileExtensions
{
    public static void Write(this BinaryWriter writer, StgFile file)
    {
    }
}