using MetalMintSolid.Kmd.Builder;
using SharpGLTF.Geometry;
using SharpGLTF.Schema2;

namespace MetalMintSolid.Kmd;

public static class KmdImporter
{
    public const long MAX_OBJ_VERTS = 255;

    public static KmdModel FromGltf(ModelRoot root, KmdModel original)
    {
        var scene = root.DefaultScene;
        if (scene == null) throw new NotSupportedException("No default scene specified");

        static IEnumerable<Node> EnumerateNodes(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in EnumerateNodes(node.VisualChildren)) yield return child;
            }
        }

        var allNodes = EnumerateNodes(scene.VisualChildren).ToList();
        var skinnedMeshNode = allNodes.FirstOrDefault(n => n.Mesh != null && n.Skin != null);
        if (skinnedMeshNode == null)
        {
            return FromGltfRigid(scene, original, allNodes);
        }

        var armature = scene.VisualChildren.FirstOrDefault();
        if (armature == null) throw new NotSupportedException("No root object found");

        var meshNode = skinnedMeshNode;

        var skin = meshNode.Skin;
        if (skin.JointsCount != original.Header.BoneCount) throw new NotSupportedException("Bone count does not match!");

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
            var joint = skin.GetJoint(i).Joint;
            var bonePos = joint.LocalTransform.Translation;

            newModel.Objects.Add(new KmdObject
            {
                Name = joint.Name,
                BitFlags = o.BitFlags,
                BonePosition = new((int)Math.Round(bonePos.X), (int)Math.Round(bonePos.Y), (int)Math.Round(bonePos.Z)),
                BoundingBoxEnd = new(int.MinValue, int.MinValue, int.MinValue),
                BoundingBoxStart = new(int.MaxValue, int.MaxValue, int.MaxValue),
                Padding = o.Padding,
                ParentBoneId = o.ParentBoneId,
                Extend = o.Extend,
                NonPairingVertexIndicies = [],
                VertexCoordsTable = [],
                VertexOrderTable = [],
                NormalVertexCoordsTable = [],
                NormalVertexOrderTable = [],
                UvTable = [],
                PCXHashedFileNames = []
            });
        }

        var gltfBoneTris = mesh.Primitives
            .SelectMany(p => p.EvaluateTriangles())
            .GroupBy(t => (int)t.A.GetSkinning().JointsLow.X);

        foreach (var bone in gltfBoneTris) FromGltfObj(newModel, bone.Key, [.. bone]);

        uint rootOffset = 32 + 88 * (uint)newModel.Objects.Count;

        Dictionary<string, string> boneNamesToParents = new Dictionary<string, string>()
        {
            { "bone_1", "bone_0" },
            { "bone_4", "bone_1" },
            { "bone_7", "bone_4" },
            { "bone_8", "bone_7" },
            { "bone_5", "bone_1" },
            { "bone_9", "bone_5" },
            { "bone_6", "bone_1" },
            { "bone_10", "bone_6" },
            { "bone_11", "bone_10" },
            { "bone_2", "bone_0" },
            { "bone_12", "bone_2" },
            { "bone_13", "bone_12" },
            { "bone_3", "bone_0" },
            { "bone_14", "bone_3" },
            { "bone_15", "bone_14" }
        };

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

            if (boneNamesToParents.ContainsKey(obj.Name))
            {
                var parentBoneName = boneNamesToParents[obj.Name];
                var parentBone = newModel.Objects.Find(o => o.Name == parentBoneName);
                if (parentBone == null) throw new NotSupportedException("Missing parent bone");
                obj.SetupParentBoneVertexPairs(parentBone, newModel);
            }
        }

        // Bake the pairing so the written KMD has coincident seams (no joint gaps when viewed
        // statically); the engine's runtime node-merging then becomes a no-op rather than the
        // only thing holding the joints together.
        newModel.BakeVertexPairs();

        return newModel;
    }


    private static KmdModel FromGltfRigid(Scene scene, KmdModel original, List<Node> allNodes)
    {
        var meshNodes = allNodes.Where(n => n.Mesh != null).ToList();
        if (meshNodes.Count == 0) throw new NotSupportedException("No meshes found in rigid model");

        var totalObjectCount = Math.Max(meshNodes.Count, original.Objects.Count);

        if (meshNodes.Count != original.Objects.Count)
        {
            Console.WriteLine($"Note: GLTF has {meshNodes.Count} mesh nodes, original KMD has {original.Objects.Count} objects.");
            if (meshNodes.Count > original.Objects.Count)
                Console.WriteLine($"  {meshNodes.Count - original.Objects.Count} extra mesh(es) will be added as new KMD objects with default metadata.");
            else
                Console.WriteLine($"  {original.Objects.Count - meshNodes.Count} original object(s) have no matching GLTF mesh and will be kept empty.");
        }

        var newModel = new KmdModel
        {
            Header = new KmdHeader
            {
                BoneCount = (uint)totalObjectCount,
                ObjectCount = (uint)totalObjectCount,
                BoundingBoxEnd = original.Header.BoundingBoxEnd,
                BoundingBoxStart = original.Header.BoundingBoxStart
            },
            Objects = []
        };

        // Create objects for all original indices first (preserving metadata)
        for (int i = 0; i < original.Objects.Count; i++)
        {
            var o = original.Objects[i];
            newModel.Objects.Add(new KmdObject
            {
                Name = o.Name,
                BitFlags = o.BitFlags,
                BonePosition = o.BonePosition,
                BoundingBoxEnd = new(int.MinValue, int.MinValue, int.MinValue),
                BoundingBoxStart = new(int.MaxValue, int.MaxValue, int.MaxValue),
                Padding = o.Padding,
                ParentBoneId = o.ParentBoneId,
                Extend = o.Extend,
                NonPairingVertexIndicies = [],
                VertexCoordsTable = [],
                VertexOrderTable = [],
                NormalVertexCoordsTable = [],
                NormalVertexOrderTable = [],
                UvTable = [],
                PCXHashedFileNames = []
            });
        }

        // Create new objects for any extra GLTF meshes beyond the original count
        for (int i = original.Objects.Count; i < totalObjectCount; i++)
        {
            var lastOriginal = original.Objects[^1];
            newModel.Objects.Add(new KmdObject
            {
                Name = $"object_{i}",
                BitFlags = lastOriginal.BitFlags,
                BonePosition = new(0, 0, 0),
                BoundingBoxEnd = new(int.MinValue, int.MinValue, int.MinValue),
                BoundingBoxStart = new(int.MaxValue, int.MaxValue, int.MaxValue),
                Padding = lastOriginal.Padding,
                ParentBoneId = 0,
                Extend = lastOriginal.Extend,
                NonPairingVertexIndicies = [],
                VertexCoordsTable = [],
                VertexOrderTable = [],
                NormalVertexCoordsTable = [],
                NormalVertexOrderTable = [],
                UvTable = [],
                PCXHashedFileNames = []
            });
        }

        static int ParseObjectIndex(Node node)
        {
            string?[] names = [node.Name, node.Mesh?.Name];
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var parts = name.Split('_');
                var last = parts.LastOrDefault();
                if (int.TryParse(last, out var index)) return index;
            }

            return -1;
        }

        var indexedNodes = meshNodes
            .Select((node, fallbackIndex) => new { node, index = ParseObjectIndex(node), fallbackIndex })
            .OrderBy(x => x.index == -1 ? int.MaxValue : x.index)
            .ThenBy(x => x.fallbackIndex)
            .ToList();

        for (int i = 0; i < indexedNodes.Count; i++)
        {
            var entry = indexedNodes[i];
            var objectIndex = entry.index == -1 ? i : entry.index;
            if (objectIndex < 0 || objectIndex >= newModel.Objects.Count)
            {
                throw new NotSupportedException($"Rigid mesh node '{entry.node.Name}' maps to invalid object index {objectIndex}");
            }

            var triangles = entry.node.Mesh!.Primitives
                .SelectMany(p => p.EvaluateTriangles())
                .Select(t => (t.A, t.B, t.C, p: (Material)t.Material))
                .Select(t => (t.A, t.B, t.C, t.p))
                .ToList();

            FromGltfObj(newModel, objectIndex, triangles
                .Select(t => (t.A, t.B, t.C, t.p))
                .ToList());
        }

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
            Console.WriteLine($"Bone '{bone}', has {quads.Count} remaining quads and {triangles.Count} remaining triangles, splitting into multiple objects, this will probably break animations");

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
