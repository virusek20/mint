using MetalMintSolid.Extensions;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System.Numerics;

namespace MetalMintSolid.Kmd;

public static class KmdExporter
{
    /// <summary>
    /// Converts a KMD model to a GLTF root object and optionally loads converted textures if <paramref name="texturePath"/> is not <see langword="null"/> or empty
    /// </summary>
    /// <param name="model">Converted model</param>
    /// <param name="texturePath">Path to textures or null</param>
    /// <returns></returns>
    public static ModelRoot ToGltf(this KmdModel model, string texturePath = "")
    {
        var hashFiles = model.Objects
            .SelectMany(o => o.PCXHashedFileNames)
            .ToHashSet();

        Dictionary<ushort, MaterialBuilder> materials;

        if (string.IsNullOrEmpty(texturePath))
        {
            materials = hashFiles
                .Select(p => (p, new MaterialBuilder(p.ToString()).WithUnlitShader()))
                .ToDictionary();
        }
        else
        {
            materials = Directory.GetFiles(texturePath)
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Distinct()
                .ToDictionary(StringExtensions.GV_StrCode_80016CCC)
                .Where(p => hashFiles.Contains(p.Key))
                .Select(p => (p.Key, new MaterialBuilder(p.Key.ToString())
                        .WithChannelImage("BaseColor", $"{texturePath}/{p.Value}.png")
                        .WithUnlitShader())
                ).ToDictionary();
        }

        // TODO: Why is this OrderBy, did I want a GroupBy here?
        var objParents = model.Objects.OrderBy(o => o.ParentBoneId).ToList();
        var nodes = new Dictionary<KmdObject, NodeBuilder>();
        var sceneBuilder = new SceneBuilder("default");

        for (int i = 0; i < objParents.Count; i++)
        {
            KmdObject obj = objParents[i];

            if (obj.ParentBoneId == -1)
            {
                var builder = new NodeBuilder($"bone_{i}");
                sceneBuilder.AddNode(builder);
                nodes[obj] = builder;
            }
            else
            {
                var parent = model.Objects[obj.ParentBoneId];
                var parentNode = nodes[parent];
                var builder = parentNode.CreateNode($"bone_{i}").WithLocalTranslation(new Vector3(obj.BonePosition.X, obj.BonePosition.Y, obj.BonePosition.Z));
                nodes[obj] = builder;
            }
        }

        var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>();
        for (int i = 0; i < model.Objects.Count; i++)
        {
            KmdObject obj = objParents[i];
            var offset = model.GetObjectPosition(obj);
            var vectorOffset = new Vector3(offset.X, offset.Y, offset.Z);

            obj.ToGltf(materials, mesh, i, vectorOffset);
        }


        if (false && model.Objects.Any(o => o.ParentBoneId != -1))
        {
            // Probably boned
            var links = nodes.Values.ToArray();
            sceneBuilder.AddSkinnedMesh(mesh, nodes[model.Objects[0]].WorldMatrix, links);
        }
        else
        {
            // Probably boneless
            sceneBuilder.AddRigidMesh(mesh, nodes[model.Objects[0]].WorldMatrix);
        }

        return sceneBuilder.ToGltf2();
    }

    public static void ToGltf(this KmdObject model, Dictionary<ushort, MaterialBuilder> materials, MeshBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4> mesh, int boneId, Vector3 offset)
    {
        var offsetMat = Matrix4x4.CreateTranslation(offset);

        int x = 0;
        for (int i = 0; i < model.FaceCount; i++)
        {
            var material = materials[model.PCXHashedFileNames[i]];
            var prim = mesh.UsePrimitive(material);

            var faceOrder = model.VertexOrderTable[i];
            var v1 = model.VertexCoordsTable[faceOrder.X];
            var v2 = model.VertexCoordsTable[faceOrder.Y];
            var v3 = model.VertexCoordsTable[faceOrder.Z];
            var v4 = model.VertexCoordsTable[faceOrder.W];

            var normalFaceOrder = model.NormalVertexOrderTable[i];
            var n1 = model.NormalVertexCoordsTable[normalFaceOrder.X & 0x7F];
            var n2 = model.NormalVertexCoordsTable[normalFaceOrder.Y & 0x7F];
            var n3 = model.NormalVertexCoordsTable[normalFaceOrder.Z & 0x7F];
            var n4 = model.NormalVertexCoordsTable[normalFaceOrder.W & 0x7F];

            var pos1 = new VertexPositionNormal(v1.X, v1.Y, v1.Z, -n1.X / 4096f, -n1.Y / 4096f, -n1.Z / 4096f);
            var pos2 = new VertexPositionNormal(v2.X, v2.Y, v2.Z, -n2.X / 4096f, -n2.Y / 4096f, -n2.Z / 4096f);
            var pos3 = new VertexPositionNormal(v3.X, v3.Y, v3.Z, -n3.X / 4096f, -n3.Y / 4096f, -n3.Z / 4096f);
            var pos4 = new VertexPositionNormal(v4.X, v4.Y, v4.Z, -n4.X / 4096f, -n4.Y / 4096f, -n4.Z / 4096f);

            pos1.ApplyTransform(offsetMat);
            pos2.ApplyTransform(offsetMat);
            pos3.ApplyTransform(offsetMat);
            pos4.ApplyTransform(offsetMat);

            var uv1 = new VertexTexture1(new Vector2(model.UvTable[x].X / 256f, model.UvTable[x++].Y / 256f));
            var uv2 = new VertexTexture1(new Vector2(model.UvTable[x].X / 256f, model.UvTable[x++].Y / 256f));
            var uv3 = new VertexTexture1(new Vector2(model.UvTable[x].X / 256f, model.UvTable[x++].Y / 256f));
            var uv4 = new VertexTexture1(new Vector2(model.UvTable[x].X / 256f, model.UvTable[x++].Y / 256f));

            var skin1 = new VertexJoints4((boneId, 1));
            var skin2 = new VertexJoints4((boneId, 1));
            var skin3 = new VertexJoints4((boneId, 1));
            var skin4 = new VertexJoints4((boneId, 1));

            prim.AddQuadrangle((pos4, uv4, skin4), (pos3, uv3, skin3), (pos2, uv2, skin2), (pos1, uv1, skin1));
        }
    }
}
