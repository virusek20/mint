namespace MetalMintSolid;

public class Vector2Int8
{
    public sbyte X { get; set; }
    public sbyte Y { get; set; }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}

public class Vector2UInt8
{
    public byte X { get; set; }
    public byte Y { get; set; }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}

public class Vector2UInt16
{
    public ushort X { get; set; }
    public ushort Y { get; set; }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}

public class Vector3Int32
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z})";
    }
}

public class Vector4Int16
{
    public short X { get; set; }
    public short Y { get; set; }
    public short Z { get; set; }
    public short W { get; set; }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z}, {W})";
    }
}

public class Vector4UInt8
{
    public byte X { get; set; }
    public byte Y { get; set; }
    public byte Z { get; set; }
    public byte W { get; set; }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z}, {W})";
    }
}

public static class BinaryReaderVectorExtensions
{
    public static Vector2UInt16 ReadVector2UInt16(this BinaryReader reader)
    {
        return new Vector2UInt16
        {
            X = reader.ReadUInt16(),
            Y = reader.ReadUInt16(),
        };
    }

    public static Vector2Int8 ReadVector2Int8(this BinaryReader reader)
    {
        return new Vector2Int8
        {
            X = reader.ReadSByte(),
            Y = reader.ReadSByte(),
        };
    }

    public static Vector2UInt8 ReadVector2UInt8(this BinaryReader reader)
    {
        return new Vector2UInt8
        {
            X = reader.ReadByte(),
            Y = reader.ReadByte(),
        };
    }

    public static Vector3Int32 ReadVector3Int32(this BinaryReader reader)
    {
        return new Vector3Int32
        {
            X = reader.ReadInt32(),
            Y = reader.ReadInt32(),
            Z = reader.ReadInt32()
        };
    }

    public static Vector4Int16 ReadVector4Int16(this BinaryReader reader)
    {
        return new Vector4Int16
        {
            X = reader.ReadInt16(),
            Y = reader.ReadInt16(),
            Z = reader.ReadInt16(),
            W = reader.ReadInt16()
        };
    }

    public static Vector4UInt8 ReadVector4UInt8(this BinaryReader reader)
    {
        return new Vector4UInt8
        {
            X = reader.ReadByte(),
            Y = reader.ReadByte(),
            Z = reader.ReadByte(),
            W = reader.ReadByte()
        };
    }
}

public static class BinaryWriterVectorExtensions
{
    public static void Write(this BinaryWriter writer, Vector2UInt16 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
    }

    public static void Write(this BinaryWriter writer, Vector2Int8 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
    }
    public static void Write(this BinaryWriter writer, Vector2UInt8 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
    }

    public static void Write(this BinaryWriter writer, Vector4UInt8 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
        writer.Write(vector.W);
    }

    public static void Write(this BinaryWriter writer, Vector3Int32 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }

    public static void Write(this BinaryWriter writer, Vector4Int16 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
        writer.Write(vector.W);
    }
}