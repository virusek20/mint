namespace MetalMintSolid.Stg;

public class StgHeader
{
    // Unknown, even in RE project
    public byte Field0 { get; set; }
    public byte Field1 { get; set; }

    /// <summary>
    /// Gets stage file size in sectors (2048 bytes)
    /// </summary>
    public short Size { get; set; }
}

public static class BinaryReaderStgHeaderExtensions
{
    public static StgHeader ReadStgHeader(this BinaryReader reader)
    {
        return new StgHeader
        {
            Field0 = reader.ReadByte(),
            Field1 = reader.ReadByte(),
            Size = reader.ReadInt16(),
        };
    }
}

public static class BinaryWriterStgHeaderExtensions
{
    public static void Write(this BinaryWriter writer, StgHeader header)
    {
        writer.Write(header.Field0);
        writer.Write(header.Field1);
        writer.Write(header.Size);
    }
}