namespace MetalMintSolid;

public readonly record struct Vector2Int8(sbyte X, sbyte Y);
public readonly record struct Vector2UInt8(byte X, byte Y);
public readonly record struct Vector2UInt16(ushort X, ushort Y);
public readonly record struct Vector3Int32(int X, int Y, int Z);
public readonly record struct Vector4Int16(short X, short Y, short Z, short W);
public readonly record struct Vector4UInt8(byte X, byte Y, byte Z, byte W);


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