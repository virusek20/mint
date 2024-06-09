using MetalMintSolid.Extensions;
using MetalMintSolid.Kmd.Builder;
using SharpGLTF.Geometry;
using SharpGLTF.Schema2;
using System.Numerics;

namespace MetalMintSolid.Kmd;

public static class KmdImporter
{
    public const long MAX_OBJ_VERTS = 126;

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
                BonePosition = new((int)Math.Round(bonePos.X), (int)Math.Round(bonePos.Y), (int)Math.Round(bonePos.Z)),
                BoundingBoxEnd = new(int.MinValue, int.MinValue, int.MinValue),
                BoundingBoxStart = new(int.MaxValue, int.MaxValue, int.MaxValue),
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

    private static KmdObject FromGltfObj(KmdModel newModel, int bone, List<(IVertexBuilder A, IVertexBuilder B, IVertexBuilder C, Material Material)> triangles)
    {
        var obj = newModel.Objects[bone];
        var objPos = newModel.GetObjectPosition(obj);

        var builder = new KmdObjectBuilder(obj, objPos);

        var inputTriangles = triangles.Select(t => new Triangle(t.A, t.B, t.C, t.Material)).ToList();
        var (quads, tris) = Util.QuadRebuilder.FindQuads(inputTriangles);

        quads.RemoveWhere(builder.TryAddQuad);
        tris.RemoveWhere(builder.TryAddTriangle);

        // We went over the limit, split into new bone
        while (quads.Count > 0 || tris.Count > 0)
        {
            Console.WriteLine($"Bone '{bone}', has {quads.Count} remaining quads and {triangles.Count} remaining triangles, splitting into multiple objects");

            newModel.Header.ObjectCount++;
            newModel.Header.BoneCount++;

            var newBone = builder.NewBone(bone);
            newModel.Objects.Add(newBone);

            var subBuilder = new KmdObjectBuilder(newBone, newModel.GetObjectPosition(newBone));
            quads.RemoveWhere(subBuilder.TryAddQuad);
            tris.RemoveWhere(subBuilder.TryAddTriangle);

            subBuilder.Build();
        }

        return builder.Build();
    }
}