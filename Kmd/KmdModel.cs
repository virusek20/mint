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

    /// <summary>
    /// Bakes the engine's runtime vertex pairing ("node merging") into the static geometry.
    /// For every vertex whose pairing index (Vector4Int16.W) points at a parent vertex,
    /// the vertex is moved to sit exactly on that parent vertex. MGS authors seam vertices
    /// 1-2 units off their parent and relies on the engine to snap them together each frame;
    /// a static viewer (Noesis) or a fresh extract therefore shows a thin crack at every
    /// paired joint (ankle, knee, hip, ...). Baking closes those cracks while leaving
    /// unpaired vertices (W == -1: fingertips, head crown, etc.) untouched.
    /// Returns the number of vertices that were moved.
    /// </summary>
    public int BakeVertexPairs()
    {
        // Process shallow bones first so a child snaps onto its parent's *already-baked*
        // vertices (a paired vertex follows the parent's final geometry).
        int Depth(KmdObject o)
        {
            int d = 0;
            var cur = o;
            while (cur.ParentBoneId >= 0 && cur.ParentBoneId < Objects.Count)
            {
                cur = Objects[cur.ParentBoneId];
                if (++d > Objects.Count) break; // guard against malformed/cyclic parents
            }
            return d;
        }

        int moved = 0;
        foreach (var obj in Objects.OrderBy(Depth))
        {
            if (obj.ParentBoneId < 0 || obj.ParentBoneId >= Objects.Count) continue;
            var parent = Objects[obj.ParentBoneId];

            var selfPos = GetObjectPosition(obj);
            var parentPos = GetObjectPosition(parent);

            for (int i = 0; i < obj.VertexCoordsTable.Count; i++)
            {
                int w = obj.VertexCoordsTable[i].W;
                if (w < 0 || w >= parent.VertexCoordsTable.Count) continue; // unpaired (W == -1) or invalid index

                var p = parent.VertexCoordsTable[w];
                var baked = obj.VertexCoordsTable[i] with
                {
                    // parent vertex world position expressed in this object's local space (W index preserved)
                    X = (short)(p.X + parentPos.X - selfPos.X),
                    Y = (short)(p.Y + parentPos.Y - selfPos.Y),
                    Z = (short)(p.Z + parentPos.Z - selfPos.Z)
                };
                if (baked != obj.VertexCoordsTable[i]) moved++;
                obj.VertexCoordsTable[i] = baked;
            }
        }
        return moved;
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