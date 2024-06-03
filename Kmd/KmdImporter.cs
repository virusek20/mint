using MetalMintSolid.Extensions;
using SharpGLTF.Geometry;
using SharpGLTF.Schema2;
using System.Linq;
using System.Numerics;

namespace MetalMintSolid.Kmd;

public static class KmdImporter
{
    public static KmdModel FromGltf(ModelRoot root, KmdModel original)
    {
        var scene = root.DefaultScene;
        if (scene == null) throw new NotSupportedException("No default scene specified");
        if (scene.VisualChildren.Count() != 1) throw new NotSupportedException("Too many root objects!");

        var armature = scene.VisualChildren.First();
        var meshes = armature.VisualChildren.Where(c => c.Mesh != null).ToList();
        if (meshes.Count != 1) throw new NotSupportedException("Too many meshes!");

        var meshNode = meshes.Single();

        var skin = meshNode.Skin;
        if (skin.JointsCount != original.Header.BoneCount) throw new NotSupportedException("Bone count does not match!");
        var jointDict = new Dictionary<int, int>();
        for (int i = 0; i < skin.JointsCount; i++)
        {
            var jointName = skin.GetJoint(i).Joint.Name;
            var jointNumber = jointName.Substring(5);

            jointDict.Add(i, int.Parse(jointNumber));
        }

        var mesh = meshNode.Mesh;
        var rootBone = armature.VisualChildren.FirstOrDefault(c => c.Name == "bone_0");
        if (rootBone == null) throw new NotSupportedException("🅱️oneless model");

        static int CountBones(Node node)
        {
            int count = 1;
            foreach (var item in node.VisualChildren)
            {
                count += CountBones(item);
            }

            return count;
        }

        var boneCount = CountBones(rootBone);
        var originalBoneCount = original.Objects.Count;
        if (boneCount != originalBoneCount) throw new NotSupportedException("Bone count mismatch, rig is probably invalid!");

        var newModel = new KmdModel
        {
            Header = new KmdHeader
            {
                BoneCount = original.Header.BoneCount,
                ObjectCount = original.Header.ObjectCount,
                BoundingBoxEnd = original.Header.BoundingBoxEnd,
                BoundingBoxStart = original.Header.BoundingBoxStart
                /*
                BoundingBoxEnd = new() { X = int.MinValue, Y = int.MinValue, Z = int.MinValue },
                BoundingBoxStart = new() { X = int.MaxValue, Y = int.MaxValue, Z = int.MaxValue }
                */
            },
            Objects = []
        };

        for (int i = 0; i < boneCount; i++)
        {
            var o = original.Objects[i];
            var index = jointDict[i];
            var bonePos = skin.GetJoint(i).Joint.LocalTransform.Translation;

            newModel.Objects.Add(new KmdObject
            {
                BitFlags = o.BitFlags,
                BonePosition = new() { X = (int)bonePos.X, Y = (int)bonePos.Y, Z = (int)bonePos.Z }, // TODO: Can we move this without breaking the animations?
                BoundingBoxEnd = new() { X = int.MinValue, Y = int.MinValue, Z = int.MinValue },
                BoundingBoxStart = new() { X = int.MaxValue, Y = int.MaxValue, Z = int.MaxValue },
                Padding = o.Padding,
                ParentBoneId = o.ParentBoneId,
                Unknown = o.Unknown,
                VertexCoordsTable = [],
                VertexOrderTable = [],
                NormalVertexCoordsTable = [],
                NormalVertexOrderTable = [],
                UvTable = [],
                PCXHashedFileNames = []
            });
        }

        var gltfBones = mesh.Primitives
            .SelectMany(p => p.EvaluateTriangles())
            .GroupBy(t => (int)t.A.GetSkinning().JointsLow.X);

        foreach (var bone in gltfBones) FromGltfObj(newModel, bone.Key, [.. bone]);

        uint rootOffset = 32 + 88 * (uint)newModel.Objects.Count;

        foreach (var obj in newModel.Objects)
        {
            obj.VertexCoordOffset = rootOffset;
            rootOffset += obj.VertexCount * 8;

            obj.NormalVertexCoordOffset = rootOffset;
            rootOffset += obj.NormalVertexCount * 8;

            obj.VertexOrderOffset = rootOffset;
            rootOffset += (uint)obj.VertexOrderTable.Count * 4;

            obj.NormalVertexOrderOffset = rootOffset;
            rootOffset += (uint)obj.NormalVertexOrderTable.Count * 4;

            obj.UVOffset = rootOffset;
            rootOffset += obj.FaceCount * 4 * 2;

            obj.TextureNameOffset = rootOffset;
            rootOffset += (uint)obj.PCXHashedFileNames.Count * 2;
        }

        return newModel;
    }

    private static KmdObject FromGltfObj(KmdModel newModel, int bone, IEnumerable<(IVertexBuilder A, IVertexBuilder B, IVertexBuilder C, Material Material)> triangles)
    {
        var obj = newModel.Objects[bone];

        var vertMap = new List<Vector3>();
        var normVertMap = new List<Vector3>();

        foreach (var tri in triangles)
        {
            var a = tri.C;
            var b = tri.B;
            var c = tri.B;
            var d = tri.A;

            a.GetGeometry().TryGetNormal(out var na);
            b.GetGeometry().TryGetNormal(out var nb);
            c.GetGeometry().TryGetNormal(out var nc);
            d.GetGeometry().TryGetNormal(out var nd);

            var nai = normVertMap.IndexOfOrAdd(na);
            var nbi = normVertMap.IndexOfOrAdd(nb);
            var nci = normVertMap.IndexOfOrAdd(nc);
            var ndi = normVertMap.IndexOfOrAdd(nd);

            /*
            obj.NormalVertexOrderTable.Add(new Vector4UInt8
            {
                X = (byte)(nai | 0x80),
                Y = (byte)(nbi | 0x80),
                Z = (byte)(nci | 0x80),
                W = (byte)(ndi | 0x80)
            });
            */

            obj.NormalVertexOrderTable.Add(new Vector4UInt8
            {
                X = (byte)nai,
                Y = (byte)nbi,
                Z = (byte)nci,
                W = (byte)ndi
            });

            var ai = vertMap.IndexOfOrAdd(a.GetGeometry().GetPosition());
            var bi = vertMap.IndexOfOrAdd(b.GetGeometry().GetPosition());
            var ci = vertMap.IndexOfOrAdd(c.GetGeometry().GetPosition());
            var di = vertMap.IndexOfOrAdd(d.GetGeometry().GetPosition());

            obj.VertexOrderTable.Add(new Vector4UInt8
            {
                X = (byte)ai,
                Y = (byte)bi,
                Z = (byte)ci,
                W = (byte)di
            });

            var aTex = a.GetMaterial().GetTexCoord(0);
            var bTex = b.GetMaterial().GetTexCoord(0);
            var cTex = c.GetMaterial().GetTexCoord(0);
            var dTex = d.GetMaterial().GetTexCoord(0);

            obj.UvTable.Add(new Vector2UInt8 { X = (byte)(aTex.X * 256.0), Y = (byte)(aTex.Y * 256.0) });
            obj.UvTable.Add(new Vector2UInt8 { X = (byte)(bTex.X * 256.0), Y = (byte)(bTex.Y * 256.0) });
            obj.UvTable.Add(new Vector2UInt8 { X = (byte)(cTex.X * 256.0), Y = (byte)(cTex.Y * 256.0) });
            obj.UvTable.Add(new Vector2UInt8 { X = (byte)(dTex.X * 256.0), Y = (byte)(dTex.Y * 256.0) });

            var materialHash = tri.Material.Name;
            var materialNum = (ushort)47255;//ushort.Parse(materialHash);
            obj.PCXHashedFileNames.Add(materialNum);
        }

        var objPos = newModel.GetObjectPosition(obj);
        foreach (var vert in vertMap)
        {
            var pos = new Vector4Int16
            {
                X = (short)Math.Round(vert.X - objPos.X),
                Y = (short)Math.Round(vert.Y - objPos.Y),
                Z = (short)Math.Round(vert.Z - objPos.Z),
                W = -1, // TODO: Parenting
            };

            obj.VertexCoordsTable.Add(pos);

            // Object bounds
            if (obj.BoundingBoxStart.X > pos.X) obj.BoundingBoxStart.X = pos.X;
            if (obj.BoundingBoxStart.Y > pos.Y) obj.BoundingBoxStart.Y = pos.Y;
            if (obj.BoundingBoxStart.Z > pos.Z) obj.BoundingBoxStart.Z = pos.Z;

            if (obj.BoundingBoxEnd.X < pos.X) obj.BoundingBoxEnd.X = pos.X;
            if (obj.BoundingBoxEnd.Y < pos.Y) obj.BoundingBoxEnd.Y = pos.Y;
            if (obj.BoundingBoxEnd.Z < pos.Z) obj.BoundingBoxEnd.Z = pos.Z;

            // Model bounds
            /*
            if (vert.X < newModel.Header.BoundingBoxStart.X) newModel.Header.BoundingBoxStart.X = (int)vert.X;
            if (vert.Y < newModel.Header.BoundingBoxStart.Y) newModel.Header.BoundingBoxStart.Y = (int)vert.Y;
            if (vert.Z < newModel.Header.BoundingBoxStart.Z) newModel.Header.BoundingBoxStart.Z = (int)vert.Z;

            if (vert.X > newModel.Header.BoundingBoxEnd.X) newModel.Header.BoundingBoxEnd.X = (int)vert.X;
            if (vert.Y > newModel.Header.BoundingBoxEnd.Y) newModel.Header.BoundingBoxEnd.Y = (int)vert.Y;
            if (vert.Z > newModel.Header.BoundingBoxEnd.Z) newModel.Header.BoundingBoxEnd.Z = (int)vert.Z;
            */
        }

        foreach (var vert in normVertMap)
        {
            obj.NormalVertexCoordsTable.Add(new Vector4Int16
            {
                X = (short)Math.Round(vert.X * -4096.0),
                Y = (short)Math.Round(vert.Y * -4096.0),
                Z = (short)Math.Round(vert.Z * -4096.0),
                W = -1, // TODO: What even is this
            });
        }


        const long maxVert = 126;
        if (vertMap.Count >= maxVert)
        {
            Console.WriteLine($"Bone '{bone}', has {vertMap.Count} verticies, splitting into multiple objects");

            newModel.Header.ObjectCount++;
            newModel.Header.BoneCount++;

            var copy = obj.CreateDeepCopy();
            copy.ParentBoneId = bone;
            copy.BonePosition.X = 0;
            copy.BonePosition.Y = 0;
            copy.BonePosition.Z = 0;

            var skipFace = obj.VertexOrderTable.First(pos => pos.X >= maxVert || pos.Y >= maxVert || pos.Z >= maxVert || pos.W >= maxVert);
            var newTriangles = triangles.Skip(obj.VertexOrderTable.IndexOf(skipFace));

            newModel.Objects.Add(copy);
            FromGltfObj(newModel, newModel.Objects.Count - 1, newTriangles);
        }

        obj.VertexCount = (uint)Math.Min(obj.VertexCount, maxVert);
        obj.VertexCoordsTable = obj.VertexCoordsTable.Take((int)obj.VertexCount).ToList();
        obj.VertexOrderTable = obj.VertexOrderTable
            .Where(pos => pos.X < maxVert && pos.Y < maxVert && pos.Z < maxVert && pos.W < maxVert)
            .ToList();

        obj.FaceCount = (uint)obj.VertexOrderTable.Count;
        obj.NormalVertexCount = (uint)normVertMap.Count;

        return obj;
    }

    private static void Split()
    {

    }
}
