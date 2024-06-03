namespace MetalMintSolid.Kmd;

public class KmdHeader
{
    public uint BoneCount { get; set; }
    public uint ObjectCount { get; set; }

    public required Vector3Int32 BoundingBoxStart { get; set; }
    public required Vector3Int32 BoundingBoxEnd { get; set; }
}

public static class BinaryReaderKmdHeaderExtensions
{
    public static KmdHeader ReadKmdHeader(this BinaryReader reader)
    {
        return new KmdHeader
        {
            BoneCount = reader.ReadUInt32(),
            ObjectCount = reader.ReadUInt32(),
            BoundingBoxStart = reader.ReadVector3Int32(),
            BoundingBoxEnd = reader.ReadVector3Int32()
        };
    }
}

public static class BinaryWriterKmdHeaderExtensions
{
    public static void Write(this BinaryWriter writer, KmdHeader header)
    {
        writer.Write(header.BoneCount);
        writer.Write(header.ObjectCount);
        writer.Write(header.BoundingBoxStart);
        writer.Write(header.BoundingBoxEnd);
    }
}