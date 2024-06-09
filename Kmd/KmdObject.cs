namespace MetalMintSolid.Kmd;

public class KmdObject
{
    public uint BitFlags { get; set; }
    public uint FaceCount { get; set; }

    public required Vector3Int32 BoundingBoxStart { get; set; }
    public required Vector3Int32 BoundingBoxEnd { get; set; }

    public required Vector3Int32 BonePosition { get; set; }
    public int ParentBoneId { get; set; }

    public uint Unknown { get; set; }

    public uint VertexCount { get; set; }
    public uint VertexCoordOffset { get; set; }
    public uint VertexOrderOffset { get; set; }

    public uint NormalVertexCount { get; set; }
    public uint NormalVertexCoordOffset { get; set; }
    public uint NormalVertexOrderOffset { get; set; }

    public uint UVOffset { get; set; }
    public uint TextureNameOffset { get; set; }
    public uint Padding { get; set; }

    public required List<Vector4Int16> VertexCoordsTable { get; set; }
    public required List<Vector4Int16> NormalVertexCoordsTable { get; set; }

    public required List<Vector4UInt8> VertexOrderTable { get; set; }
    public required List<Vector4UInt8> NormalVertexOrderTable { get; set; }

    public required List<Vector2UInt8> UvTable { get; set; }

    public required List<ushort> PCXHashedFileNames { get; set; }
}

public static class BinaryReaderKmdObjectExtensions
{
    public static KmdObject ReadKmdObject(this BinaryReader reader)
    {
        var kmd = new KmdObject
        {
            BitFlags = reader.ReadUInt32(),
            FaceCount = reader.ReadUInt32(),
            BoundingBoxStart = reader.ReadVector3Int32(),
            BoundingBoxEnd = reader.ReadVector3Int32(),
            BonePosition = reader.ReadVector3Int32(),
            ParentBoneId = reader.ReadInt32(),
            Unknown = reader.ReadUInt32(),
            VertexCount = reader.ReadUInt32(),
            VertexCoordOffset = reader.ReadUInt32(),
            VertexOrderOffset = reader.ReadUInt32(),
            NormalVertexCount = reader.ReadUInt32(),
            NormalVertexCoordOffset = reader.ReadUInt32(),
            NormalVertexOrderOffset = reader.ReadUInt32(),
            UVOffset = reader.ReadUInt32(),
            TextureNameOffset = reader.ReadUInt32(),
            Padding = reader.ReadUInt32(),
            VertexCoordsTable = [],
            NormalVertexCoordsTable = [],
            VertexOrderTable = [],
            NormalVertexOrderTable = [],
            UvTable = [],
            PCXHashedFileNames = []
        };

        reader.BaseStream.Position = kmd.VertexCoordOffset;
        for (int i = 0; i < kmd.VertexCount; i++)
        {
            kmd.VertexCoordsTable.Add(reader.ReadVector4Int16());
        }

        reader.BaseStream.Position = kmd.VertexOrderOffset;
        for (int i = 0; i < kmd.FaceCount; i++)
        {
            kmd.VertexOrderTable.Add(reader.ReadVector4UInt8());
        }


        // These might be per face * vertex (so 4 times as many)
        reader.BaseStream.Position = kmd.UVOffset;
        for (int i = 0; i < kmd.FaceCount * 4; i++)
        {
            // Divide by 256.0 
            kmd.UvTable.Add(reader.ReadVector2UInt8());
        }

        reader.BaseStream.Position = kmd.NormalVertexCoordOffset;
        for (int i = 0; i < kmd.NormalVertexCount; i++)
        {
            // Divide by 4096.0
            kmd.NormalVertexCoordsTable.Add(reader.ReadVector4Int16());
        }

        reader.BaseStream.Position = kmd.NormalVertexOrderOffset;
        for (int i = 0; i < kmd.FaceCount; i++)
        {
            kmd.NormalVertexOrderTable.Add(reader.ReadVector4UInt8());
        }

        reader.BaseStream.Position = kmd.TextureNameOffset;
        for (int i = 0; i < kmd.FaceCount; i++)
        {
            kmd.PCXHashedFileNames.Add(reader.ReadUInt16());
        }

        return kmd;
    }
}

public static class BinaryWriterKmdObjectExtensions
{
    public static void Write(this BinaryWriter writer, KmdObject kmd)
    {
        writer.Write(kmd.BitFlags);
        writer.Write(kmd.FaceCount);
        writer.Write(kmd.BoundingBoxStart);
        writer.Write(kmd.BoundingBoxEnd);
        writer.Write(kmd.BonePosition);
        writer.Write(kmd.ParentBoneId);
        writer.Write(kmd.Unknown);
        writer.Write(kmd.VertexCount);
        writer.Write(kmd.VertexCoordOffset);
        writer.Write(kmd.VertexOrderOffset);
        writer.Write(kmd.NormalVertexCount);
        writer.Write(kmd.NormalVertexCoordOffset);
        writer.Write(kmd.NormalVertexOrderOffset);
        writer.Write(kmd.UVOffset);
        writer.Write(kmd.TextureNameOffset);
        writer.Write(kmd.Padding);

        writer.BaseStream.Position = kmd.VertexCoordOffset;
        foreach (var vertex in kmd.VertexCoordsTable) writer.Write(vertex);

        writer.BaseStream.Position = kmd.VertexOrderOffset;
        foreach (var order in kmd.VertexOrderTable) writer.Write(order);

        writer.BaseStream.Position = kmd.UVOffset;
        foreach (var uv in kmd.UvTable) writer.Write(uv);

        writer.BaseStream.Position = kmd.NormalVertexCoordOffset;
        foreach (var vertex in kmd.NormalVertexCoordsTable) writer.Write(vertex);

        writer.BaseStream.Position = kmd.NormalVertexOrderOffset;
        foreach (var order in kmd.NormalVertexOrderTable) writer.Write(order);

        writer.BaseStream.Position = kmd.TextureNameOffset;
        foreach (var texture in kmd.PCXHashedFileNames) writer.Write(texture);
    }
}