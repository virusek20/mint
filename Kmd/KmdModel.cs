namespace MetalMintSolid.Kmd;

public class KmdModel
{
    required public KmdHeader Header { get; set; }
    required public List<KmdObject> Objects { get; set; } = [];

    public Vector3Int32 GetObjectPosition(KmdObject obj)
    {
        if (obj.ParentBoneId == -1) return obj.BonePosition;
        else
        {
            var rootBonePos = GetObjectPosition(Objects[obj.ParentBoneId]);
            return new Vector3Int32
            {
                X = obj.BonePosition.X + rootBonePos.X,
                Y = obj.BonePosition.Y + rootBonePos.Y,
                Z = obj.BonePosition.Z + rootBonePos.Z,
            };
        }
    }
}

public static class BinaryReaderKmdModelExtensions
{
    public static KmdModel ReadKmdModel(this BinaryReader reader)
    {
        var header = reader.ReadKmdHeader();
        List<KmdObject> objects = [];

        for (int i = 0; i < header.ObjectCount; i++)
        {
            reader.BaseStream.Position = 32 + 88 * i;
            objects.Add(reader.ReadKmdObject());
        }


        return new KmdModel
        {
            Header = header,
            Objects = objects
        };
    }
}

public static class BinaryWriterKmdModelExtensions
{
    public static void Write(this BinaryWriter writer, KmdModel model)
    {
        writer.Write(model.Header);

        for (int i = 0; i < model.Objects.Count; i++)
        {
            writer.BaseStream.Position = 32 + 88 * i;
            writer.Write(model.Objects[i]);
        }
    }
}