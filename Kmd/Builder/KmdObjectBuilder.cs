using MetalMintSolid.Extensions;
using SharpGLTF.Geometry;
using SharpGLTF.Schema2;
using System.Numerics;

namespace MetalMintSolid.Kmd.Builder;

public record Triangle(IVertexBuilder A, IVertexBuilder B, IVertexBuilder C, Material Material)
{
    public bool SharesEdge(Triangle triangle)
    {
        if (Material != triangle.Material) return false;

        HashSet<Vector3> verticies =
        [
            A.GetGeometry().GetPosition(),
            B.GetGeometry().GetPosition(),
            C.GetGeometry().GetPosition(),
            triangle.A.GetGeometry().GetPosition(),
            triangle.B.GetGeometry().GetPosition(),
            triangle.C.GetGeometry().GetPosition(),
        ];

        HashSet<IVertexBuilder> verticies2 =
        [
            A,
            B,
            C,
            triangle.A,
            triangle.B,
            triangle.C,
        ];

        if (verticies.Count != verticies2.Count)
        {
            return false;
        }

        // todo. check uv continuity
        if (verticies.Count == 3)
        {
            Console.WriteLine("Identical triangles detected, try merging vertices by distance");
            return false;
        }

        return verticies.Count <= 4;
    }

    public Vector3 Normal()
    {
        var a = B.GetGeometry().GetPosition() - A.GetGeometry().GetPosition();
        var b = C.GetGeometry().GetPosition() - A.GetGeometry().GetPosition();

        return Vector3.Normalize(Vector3.Cross(a, b));
    }

    public Quad MakeQuad(Triangle b)
    {
        if (Material != b.Material) throw new NotSupportedException("Cannot have a quad with two materials");

        List<IVertexBuilder> vertices = [A, B, C];
        var positions = vertices.Select(v => v.GetGeometry().GetPosition());

        if (!positions.Contains(b.A.GetGeometry().GetPosition())) vertices.Add(b.A);
        if (!positions.Contains(b.B.GetGeometry().GetPosition())) vertices.Add(b.B);
        if (!positions.Contains(b.C.GetGeometry().GetPosition())) vertices.Add(b.C);

        if (vertices.Count != 4) throw new NotSupportedException("Resulting shape is not a quad");

        var pos = vertices.Select(v => v.GetGeometry().GetPosition());
        var centroid = pos.Aggregate((acc, v) => v + acc) * 0.25f;
        var normal = Normal();
        Vector3 other = new(0, 1, 0);
        if (Math.Abs(Math.Abs(Vector3.Dot(other, normal)) - 1.0) < 0.0001) other = new(0, 0, -1);

        var x_axis = Vector3.Normalize(Vector3.Cross(other, normal));
        var y_axis = Vector3.Normalize(Vector3.Cross(normal, x_axis));

        List<double> angles = [];
        foreach (var v in vertices)
        {
            var vector = v.GetGeometry().GetPosition() - centroid;
            var x_pos = Vector3.Dot(vector, x_axis);
            var y_pos = Vector3.Dot(vector, y_axis);

            var angle = Math.Atan2(y_pos, x_pos);
            angles.Add(angle);
        }

        var ordered = angles.Zip(vertices).OrderBy(v => v.First).Select(a => a.Second).ToList();

        return new(ordered[0], ordered[1], ordered[2], ordered[3], Material);
    }
}

public record Quad(IVertexBuilder A, IVertexBuilder B, IVertexBuilder C, IVertexBuilder D, Material Material);

public class KmdObjectBuilder(KmdObject obj, Vector3Int32 objectPosition)
{
    private readonly List<Vector3> _vertices = [];
    private readonly List<Vector3> _normalVertices = [];

    /// <summary>
    /// Tries to add a triangle to the current object,
    /// checking whether the total vertex count won't go over <see cref="KmdImporter.MAX_OBJ_VERTS"/>
    /// </summary>
    /// <returns>Whether the triangle has been added to this object</returns>
    public bool TryAddTriangle(Triangle triangle)
    {
        return TryAddQuad(new(triangle.A, triangle.B, triangle.B, triangle.C, triangle.Material));
    }

    /// <summary>
    /// Tries to add a quad to the current object,
    /// checking whether the total vertex count won't go over <see cref="KmdImporter.MAX_OBJ_VERTS"/>
    /// </summary>
    /// <returns>Whether the quad has been added to this object</returns>
    public bool TryAddQuad(Quad quad)
    {
        var a = quad.D;
        var b = quad.C;
        var c = quad.B;
        var d = quad.A;

        var ap = a.GetGeometry().GetPosition();
        var bp = b.GetGeometry().GetPosition();
        var cp = c.GetGeometry().GetPosition();
        var dp = d.GetGeometry().GetPosition();

        HashSet<Vector3> uniqueVerts = [ ap, bp, cp, dp ];
        var neededVerticies = 4 - uniqueVerts.Count(_vertices.Contains);
        if (_vertices.Count + neededVerticies > KmdImporter.MAX_OBJ_VERTS) return false;

        // Invalid triangles
        // if (IsDegenerate(tri.A, tri.B, tri.C)) continue;

        // Vertex position
        var ai = _vertices.IndexOfOrAdd(ap);
        var bi = _vertices.IndexOfOrAdd(bp);
        var ci = _vertices.IndexOfOrAdd(cp);
        var di = _vertices.IndexOfOrAdd(dp);

        // Duplicates
        // if (obj.VertexOrderTable.Any(v => v.X == ai && v.Y == bi && v.Z == ci && v.W == di)) continue;

        obj.VertexOrderTable.Add(new Vector4UInt8
        {
            X = (byte)ai,
            Y = (byte)bi,
            Z = (byte)ci,
            W = (byte)di
        });

        // Vertex normal
        a.GetGeometry().TryGetNormal(out var na);
        b.GetGeometry().TryGetNormal(out var nb);
        c.GetGeometry().TryGetNormal(out var nc);
        d.GetGeometry().TryGetNormal(out var nd);

        var nai = _normalVertices.IndexOfOrAdd(na);
        var nbi = _normalVertices.IndexOfOrAdd(nb);
        var nci = _normalVertices.IndexOfOrAdd(nc);
        var ndi = _normalVertices.IndexOfOrAdd(nd);

        // TODO: One of the original objects has the top bit set like this for some reason, need to investigate that
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

        // Textures
        var aTex = a.GetMaterial().GetTexCoord(0);
        var bTex = b.GetMaterial().GetTexCoord(0);
        var cTex = c.GetMaterial().GetTexCoord(0);
        var dTex = d.GetMaterial().GetTexCoord(0);

        obj.UvTable.Add(new Vector2UInt8 { X = (byte)Math.Round(Math.Clamp(aTex.X, 0, 1) * 256.0), Y = (byte)Math.Round(Math.Clamp(aTex.Y, 0, 1) * 256.0) });
        obj.UvTable.Add(new Vector2UInt8 { X = (byte)Math.Round(Math.Clamp(bTex.X, 0, 1) * 256.0), Y = (byte)Math.Round(Math.Clamp(bTex.Y, 0, 1) * 256.0) });
        obj.UvTable.Add(new Vector2UInt8 { X = (byte)Math.Round(Math.Clamp(cTex.X, 0, 1) * 256.0), Y = (byte)Math.Round(Math.Clamp(cTex.Y, 0, 1) * 256.0) });
        obj.UvTable.Add(new Vector2UInt8 { X = (byte)Math.Round(Math.Clamp(dTex.X, 0, 1) * 256.0), Y = (byte)Math.Round(Math.Clamp(dTex.Y, 0, 1) * 256.0) });


        const bool DEBUG_TEXTURE = false;
        if (DEBUG_TEXTURE) obj.PCXHashedFileNames.Add(60655);
        else
        {
            ushort materialNum = 47252;

            var materialHash = quad.Material.Name;
            if (materialHash.Contains("replace"))
                materialNum = ushort.Parse(materialHash[..5]);

            obj.PCXHashedFileNames.Add(materialNum);
        }

        return true;
    }

    /// <summary>
    /// Calculates all info (coordinates, face counts, bounds) from supplied primitives
    /// </summary>
    public KmdObject Build()
    {
        obj.VertexCount = (uint)_vertices.Count;
        obj.NormalVertexCount = (uint)_normalVertices.Count;
        obj.FaceCount = (uint)obj.PCXHashedFileNames.Count;

        var objectSpaceVerticies = _vertices.Select(vertex => new Vector4Int16
        {
            X = (short)Math.Round(vertex.X - objectPosition.X),
            Y = (short)Math.Round(vertex.Y - objectPosition.Y),
            Z = (short)Math.Round(vertex.Z - objectPosition.Z),
            W = -1, // TODO: Parenting, should be the index of vertex in parent object which this vertex should be parented to?
        }).ToList();

        // Bounding boxes are in model space (not object space)
        RecalculateBounds(objectSpaceVerticies);

        foreach (var vertex in objectSpaceVerticies) obj.VertexCoordsTable.Add(vertex);
        foreach (var normalVertex in _normalVertices)
        {
            obj.NormalVertexCoordsTable.Add(new Vector4Int16
            {
                X = (short)Math.Round(normalVertex.X * -4096.0),
                Y = (short)Math.Round(normalVertex.Y * -4096.0),
                Z = (short)Math.Round(normalVertex.Z * -4096.0),
                W = -1, // TODO: What even is this
            });
        }

        return obj;
    }

    public KmdObject NewBone(int parentBoneId)
    {
        var copy = obj.CreateDeepCopy();

        copy.VertexCoordsTable.Clear();
        copy.VertexOrderTable.Clear();

        copy.NormalVertexCoordsTable.Clear();
        copy.NormalVertexOrderTable.Clear();

        copy.UvTable.Clear();
        copy.PCXHashedFileNames.Clear();

        copy.ParentBoneId = parentBoneId;
        copy.BonePosition = new(0, 0, 0);

        return copy;
    }

    private void RecalculateBounds(List<Vector4Int16> verticies)
    {
        foreach (var pos in verticies)
        {
            if (obj.BoundingBoxStart.X > pos.X) obj.BoundingBoxStart = obj.BoundingBoxStart with { X = pos.X };
            if (obj.BoundingBoxStart.Y > pos.Y) obj.BoundingBoxStart = obj.BoundingBoxStart with { Y = pos.Y };
            if (obj.BoundingBoxStart.Z > pos.Z) obj.BoundingBoxStart = obj.BoundingBoxStart with { Z = pos.Z };

            if (obj.BoundingBoxEnd.X < pos.X) obj.BoundingBoxEnd = obj.BoundingBoxEnd with { X = pos.X };
            if (obj.BoundingBoxEnd.Y < pos.Y) obj.BoundingBoxEnd = obj.BoundingBoxEnd with { Y = pos.Y };
            if (obj.BoundingBoxEnd.Z < pos.Z) obj.BoundingBoxEnd = obj.BoundingBoxEnd with { Z = pos.Z };
        }
    }
}
