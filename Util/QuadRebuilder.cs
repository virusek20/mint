using MetalMintSolid.Kmd.Builder;
using System.Numerics;

namespace MetalMintSolid.Util;

public static class QuadRebuilder
{
    public static (HashSet<Quad> quads, HashSet<Triangle> triangles) FindQuads(List<Triangle> triangles)
    {
        var quads = new HashSet<Quad>();

        // Please stop reading here for your own sanity
        warcrime:
        foreach (var currentTriangle in triangles)
        {
            foreach (var checkTriangle in triangles)
            {
                if (currentTriangle != checkTriangle &&
                    currentTriangle.SharesEdge(checkTriangle) &&
                    Vector3.Dot(currentTriangle.Normal(), checkTriangle.Normal()) > 0.99)
                {
                    quads.Add(currentTriangle.MakeQuad(checkTriangle));
                    triangles.Remove(currentTriangle);
                    triangles.Remove(checkTriangle);
                    goto warcrime;
                }
            }
        }

        return (quads, triangles.ToHashSet());
    }
}
