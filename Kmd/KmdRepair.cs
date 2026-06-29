namespace MetalMintSolid.Kmd;

/// <summary>
/// Retrofit repairs for KMDs that were authored with the ORIGINAL MetalMintSolid (pre weld/concave fix).
/// Those files have three classes of defect this restores:
///   1. Unstitched "flap" cracks (missing mesh nodes). The old exporter could leave a small patch of
///      the surface open - a boundary loop of edges that no second face closes - most visibly on the
///      rear pelvis (the "missing mesh node on the butt"). The faces around it exist, so the model looks
///      solid in a double-sided viewer, but Noesis / the PSX engine cull backfaces and the open patch
///      reads as a hole. Fix: find each small open boundary loop, pick the bone that owns it, and fan-fill
///      it with triangles whose winding makes the VISIBLE side face outward (texture + UVs copied from the
///      neighbouring faces on that bone). See <see cref="SealBoundaryCracks"/>.
///   2. Concave quads. The old encoder merged two triangles into a quad even when the result was concave.
///      The PSX renderer splits every quad along one fixed diagonal; on a concave quad that diagonal can
///      fall OUTSIDE the shape, leaving a visible hole (hands, knees, ...). Fix: detect each concave quad
///      and split it back into two triangles along the interior diagonal (the one from the reflex vertex).
///      Each triangle is stored as a degenerate quad with the duplicate vertex in the LAST TWO slots
///      (a,b,c,c) so it renders identically under either PSX diagonal convention.
///   3. Unpaired seam vertices. The old pairing test (dist &lt; 1) missed seam verts that landed 1-2u off
///      their parent after int16 rounding, leaving joints unwelded. Fix: re-derive pairings from
///      ParentBoneId with a tolerance, then bake (same passes the GLTF importer now runs).
/// </summary>
public static class KmdRepair
{
    /// <summary>
    /// Full retrofit, in the order seal -> split -> (re-pair + bake) -> re-stripe offsets.
    /// Returns (boundaryTrisAdded, concaveQuadsSplit, vertsNewlyPaired, vertsBaked).
    /// </summary>
    public static (int sealed_, int split, int paired, int baked) Repair(
        this KmdModel model,
        float pairingTolerance = 2.0f,
        bool sealCracks = true,
        bool splitConcave = true,
        bool weldSeams = true,
        int sealMaxLoopVerts = 5,
        bool sealAnywhere = false,
        Action<string>? log = null)
    {
        int sealed_ = sealCracks ? model.SealBoundaryCracks(pairingTolerance, sealMaxLoopVerts, sealAnywhere, log: log) : 0;

        int split = splitConcave ? model.SplitConcaveQuads() : 0;

        int paired = 0, baked = 0;
        if (weldSeams)
        {
            paired = model.RepairParentVertexPairs(pairingTolerance);
            baked = model.BakeVertexPairs();
        }

        // Face/normal arrays changed (sealing appends, splitting rewrites) so re-stripe every object's
        // section offsets exactly the way KmdImporter does after a fresh build.
        model.RecalculateOffsets();
        return (sealed_, split, paired, baked);
    }

    /// <summary>
    /// Closes small open boundary loops ("missing mesh nodes") by fan-filling them with triangles.
    ///
    /// An edge that only one face uses is a boundary edge; a connected ring of boundary edges is an open
    /// loop. A correctly built character surface has none on its body (only deliberate openings like the
    /// neck collar). The original tool occasionally left a tiny open patch - classically the rear pelvis -
    /// that backface culling turns into a visible hole. This finds those loops and seals them.
    ///
    /// Safety: by default only loops with &lt;= <paramref name="maxLoopVerts"/> vertices that sit on the
    /// lower/central body (centroid Y below the waist and near the X centreline) are sealed, so legitimate
    /// openings (neck, sleeve cuffs, large design holes) are never touched. Pass
    /// <paramref name="sealAnywhere"/> = true to fill every small loop regardless of where it is (advanced;
    /// review the log first). Each sealed loop is reported via <paramref name="log"/>.
    ///
    /// Vertices are welded across bones within <paramref name="tolerance"/> only to DETECT the loop; the
    /// fill triangles reference the owning bone's existing vertices, so no geometry is moved and no new
    /// vertices are added (only faces, plus one outward normal per loop).
    /// </summary>
    /// <returns>The number of triangles added.</returns>
    public static int SealBoundaryCracks(this KmdModel model, float tolerance = 2.0f, int maxLoopVerts = 5,
        bool sealAnywhere = false, int regionMaxY = -40, int regionMaxAbsX = 130, Action<string>? log = null)
    {
        log ??= _ => { };
        int objCount = model.Objects.Count;
        if (objCount == 0) return 0;

        // ---- gather every vertex in world space, tagged with (bone, local index) ----
        var goff = new int[objCount];
        int acc = 0;
        for (int i = 0; i < objCount; i++) { goff[i] = acc; acc += model.Objects[i].VertexCoordsTable.Count; }
        int n = acc;
        if (n == 0) return 0;

        var px = new double[n]; var py = new double[n]; var pz = new double[n];
        var tagBone = new int[n]; var tagLocal = new int[n];
        for (int i = 0; i < objCount; i++)
        {
            var o = model.Objects[i];
            var wp = model.GetObjectPosition(o);
            for (int li = 0; li < o.VertexCoordsTable.Count; li++)
            {
                int gi = goff[i] + li;
                var v = o.VertexCoordsTable[li];
                px[gi] = v.X + wp.X; py[gi] = v.Y + wp.Y; pz[gi] = v.Z + wp.Z;
                tagBone[gi] = i; tagLocal[gi] = li;
            }
        }

        // ---- union-find weld by distance (spatial hash keeps it linear) ----
        var uf = new int[n];
        for (int i = 0; i < n; i++) uf[i] = i;
        int Find(int a) { while (uf[a] != a) { uf[a] = uf[uf[a]]; a = uf[a]; } return a; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) uf[ra] = rb; }

        double cell = Math.Max(tolerance, 1.0);
        (int, int, int) Key(int i) => ((int)Math.Floor(px[i] / cell), (int)Math.Floor(py[i] / cell), (int)Math.Floor(pz[i] / cell));
        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int i = 0; i < n; i++) { var k = Key(i); if (!grid.TryGetValue(k, out var l)) { l = []; grid[k] = l; } l.Add(i); }
        double t2 = (double)tolerance * tolerance;
        for (int i = 0; i < n; i++)
        {
            var (cx, cy, cz) = Key(i);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var l)) continue;
                        foreach (int j in l)
                        {
                            if (j <= i) continue;
                            double dd = (px[j] - px[i]) * (px[j] - px[i]) + (py[j] - py[i]) * (py[j] - py[i]) + (pz[j] - pz[i]) * (pz[j] - pz[i]);
                            if (dd <= t2) Union(i, j);
                        }
                    }
        }

        var rep = new Dictionary<int, int>();
        var W = new int[n];
        for (int k = 0; k < n; k++) { int r = Find(k); if (!rep.TryGetValue(r, out var id)) { id = rep.Count; rep[r] = id; } W[k] = id; }
        int wcount = rep.Count;

        var cenX = new double[wcount]; var cenY = new double[wcount]; var cenZ = new double[wcount]; var cenN = new int[wcount];
        for (int k = 0; k < n; k++) { int w = W[k]; cenX[w] += px[k]; cenY[w] += py[k]; cenZ[w] += pz[k]; cenN[w]++; }
        for (int w = 0; w < wcount; w++) if (cenN[w] > 0) { cenX[w] /= cenN[w]; cenY[w] /= cenN[w]; cenZ[w] /= cenN[w]; }

        int Gid(int bone, int local) => W[goff[bone] + local];

        // ---- triangulate all faces on welded ids; record which welded tris touch each edge ----
        var edgeFaces = new Dictionary<(int, int), List<int[]>>();
        void AddEdge(int a, int b, int[] tri) { var e = a < b ? (a, b) : (b, a); if (!edgeFaces.TryGetValue(e, out var l)) { l = []; edgeFaces[e] = l; } l.Add(tri); }
        void AddTri(int[] t) { AddEdge(t[0], t[1], t); AddEdge(t[1], t[2], t); AddEdge(t[2], t[0], t); }
        for (int oi = 0; oi < objCount; oi++)
        {
            var o = model.Objects[oi];
            foreach (var vo in o.VertexOrderTable)
            {
                int[] w4 = [Gid(oi, vo.X), Gid(oi, vo.Y), Gid(oi, vo.Z), Gid(oi, vo.W)];
                var u = new List<int>(4);
                foreach (var x in w4) if (!u.Contains(x)) u.Add(x);
                if (u.Count == 3) AddTri([u[0], u[1], u[2]]);
                else if (u.Count == 4) { AddTri([w4[0], w4[1], w4[2]]); AddTri([w4[0], w4[2], w4[3]]); }
            }
        }

        var bnd = edgeFaces.Where(kv => kv.Value.Count == 1).ToList();
        if (bnd.Count == 0) return 0;

        var adj = new Dictionary<int, HashSet<int>>();
        void Link(int a, int b)
        {
            if (!adj.TryGetValue(a, out var s)) { s = []; adj[a] = s; }
            s.Add(b);
        }
        foreach (var kv in bnd)
        {
            var (a, b) = kv.Key;
            Link(a, b);
            Link(b, a);
        }

        var seen = new HashSet<int>();
        var comps = new List<List<int>>();
        foreach (var s in adj.Keys)
        {
            if (seen.Contains(s)) continue;
            var stack = new Stack<int>(); stack.Push(s);
            var comp = new List<int>();
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                if (!seen.Add(u)) continue;
                comp.Add(u);
                foreach (var nb in adj[u]) if (!seen.Contains(nb)) stack.Push(nb);
            }
            comps.Add(comp);
        }

        int sealedTris = 0;
        foreach (var comp in comps)
        {
            var compSet = new HashSet<int>(comp);
            double cX = 0, cY = 0, cZ = 0;
            foreach (var w in comp) { cX += cenX[w]; cY += cenY[w]; cZ += cenZ[w]; }
            cX /= comp.Count; cY /= comp.Count; cZ /= comp.Count;

            bool inRegion = sealAnywhere || (cY < regionMaxY && Math.Abs(cX) < regionMaxAbsX);
            if (!(comp.Count <= maxLoopVerts && inRegion)) continue;

            // owner bone owns the most loop verts; every loop vert must exist on it (else it is a real seam)
            var owners = new Dictionary<int, int>();
            var widLocal = new Dictionary<int, Dictionary<int, int>>();
            for (int k = 0; k < n; k++)
            {
                int w = W[k];
                if (!compSet.Contains(w)) continue;
                int bi = tagBone[k];
                owners[bi] = owners.GetValueOrDefault(bi) + 1;
                if (!widLocal.TryGetValue(w, out var m)) { m = []; widLocal[w] = m; }
                m[bi] = tagLocal[k];
            }
            int owner = owners.OrderByDescending(kv => kv.Value).First().Key;
            if (comp.Any(w => !widLocal[w].ContainsKey(owner)))
            {
                log($"  [seal] loop @({cX:0},{cY:0},{cZ:0}) skipped (spans multiple bones - looks like a real seam)");
                continue;
            }
            var locs = comp.ToDictionary(w => w, w => widLocal[w][owner]);
            var ob = model.Objects[owner];

            // outward VISIBLE direction = average of -(geometric normal) of the faces bordering the loop
            double onx = 0, ony = 0, onz = 0;
            foreach (var kv in bnd)
            {
                var (ea, eb) = kv.Key;
                if (!compSet.Contains(ea) && !compSet.Contains(eb)) continue;
                var tri = kv.Value[0];
                double ax = cenX[tri[0]], ay = cenY[tri[0]], az = cenZ[tri[0]];
                double ux = cenX[tri[1]] - ax, uy = cenY[tri[1]] - ay, uz = cenZ[tri[1]] - az;
                double vx = cenX[tri[2]] - ax, vy = cenY[tri[2]] - ay, vz = cenZ[tri[2]] - az;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double m = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (m < 1e-9) continue;
                onx -= nx / m; ony -= ny / m; onz -= nz / m;
            }
            double onm = Math.Sqrt(onx * onx + ony * ony + onz * onz);
            if (onm < 1e-9) { onx = 0; ony = 0; onz = 1; onm = 1; }
            onx /= onm; ony /= onm; onz /= onm;

            // order the loop by walking its boundary adjacency
            var order = new List<int> { comp[0] };
            int prev = -1, cur = comp[0];
            while (true)
            {
                int next = -1;
                foreach (var x in adj[cur]) if (compSet.Contains(x) && x != prev && !order.Contains(x)) { next = x; break; }
                if (next < 0) break;
                order.Add(next); prev = cur; cur = next;
            }
            if (order.Count < 3) order = [.. comp];
            if (order.Count < 3) continue;

            // texture + per-vertex UV borrowed from the owner faces around the loop
            var uvFor = new Dictionary<int, Vector2UInt8>();
            for (int fi = 0; fi < ob.VertexOrderTable.Count; fi++)
            {
                var vo = ob.VertexOrderTable[fi];
                byte[] cs = [vo.X, vo.Y, vo.Z, vo.W];
                for (int c = 0; c < 4; c++)
                {
                    int li = cs[c];
                    if (!uvFor.ContainsKey(li)) { int uvi = fi * 4 + c; if (uvi < ob.UvTable.Count) uvFor[li] = ob.UvTable[uvi]; }
                }
            }
            ushort tex = ob.PCXHashedFileNames.Count > 0 ? ob.PCXHashedFileNames[0] : (ushort)0;
            var localSet = new HashSet<int>(locs.Values);
            for (int fi = 0; fi < ob.VertexOrderTable.Count; fi++)
            {
                var vo = ob.VertexOrderTable[fi];
                if (localSet.Contains(vo.X) || localSet.Contains(vo.Y) || localSet.Contains(vo.Z) || localSet.Contains(vo.W))
                { if (fi < ob.PCXHashedFileNames.Count) tex = ob.PCXHashedFileNames[fi]; break; }
            }

            // append one outward normal for the whole patch (stored = -visible * 4096, matching the exporter)
            ob.NormalVertexCoordsTable.Add(new Vector4Int16
            {
                X = (short)Math.Round(-onx * 4096),
                Y = (short)Math.Round(-ony * 4096),
                Z = (short)Math.Round(-onz * 4096),
                W = 0
            });
            byte ni = (byte)((ob.NormalVertexCoordsTable.Count - 1) & 0x7F);

            int a0 = order[0];
            for (int t = 1; t < order.Count - 1; t++)
            {
                int l1 = locs[a0], l2 = locs[order[t]], l3 = locs[order[t + 1]];
                var P1 = ob.VertexCoordsTable[l1]; var P2 = ob.VertexCoordsTable[l2]; var P3 = ob.VertexCoordsTable[l3];
                double ux = P2.X - P1.X, uy = P2.Y - P1.Y, uz = P2.Z - P1.Z;
                double vx = P3.X - P1.X, vy = P3.Y - P1.Y, vz = P3.Z - P1.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double m = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (m > 1e-9)
                {
                    // visible side = -geometric; if it faces inward, flip the triangle so it faces out
                    double vis = (-nx / m) * onx + (-ny / m) * ony + (-nz / m) * onz;
                    if (vis < 0) (l2, l3) = (l3, l2);
                }
                ob.VertexOrderTable.Add(new Vector4UInt8 { X = (byte)l1, Y = (byte)l2, Z = (byte)l3, W = (byte)l3 });
                ob.NormalVertexOrderTable.Add(new Vector4UInt8 { X = ni, Y = ni, Z = ni, W = ni });
                ob.UvTable.Add(uvFor.GetValueOrDefault(l1, new Vector2UInt8(127, 127)));
                ob.UvTable.Add(uvFor.GetValueOrDefault(l2, new Vector2UInt8(127, 127)));
                ob.UvTable.Add(uvFor.GetValueOrDefault(l3, new Vector2UInt8(127, 127)));
                ob.UvTable.Add(uvFor.GetValueOrDefault(l3, new Vector2UInt8(127, 127)));
                ob.PCXHashedFileNames.Add(tex);
                sealedTris++;
            }
            ob.FaceCount = (uint)ob.VertexOrderTable.Count;
            log($"  [seal] closed loop @({cX:0},{cY:0},{cZ:0}) in bone {owner} (+{order.Count - 2} tri, tex={tex})");
        }
        return sealedTris;
    }

    /// <summary>
    /// Re-derives parent-bone vertex pairings for every child object using its ParentBoneId
    /// (the GLTF importer does the same thing, but keyed off bone names). Returns vertices whose
    /// pairing index changed.
    /// </summary>
    public static int RepairParentVertexPairs(this KmdModel model, float tolerance = 2.0f)
    {
        int changed = 0;
        foreach (var obj in model.Objects)
        {
            if (obj.ParentBoneId < 0 || obj.ParentBoneId >= model.Objects.Count) continue;
            var parent = model.Objects[obj.ParentBoneId];

            var before = obj.VertexCoordsTable.Select(v => v.W).ToArray();
            obj.SetupParentBoneVertexPairs(parent, model, tolerance);
            for (int i = 0; i < before.Length; i++)
                if (before[i] != obj.VertexCoordsTable[i].W) changed++;
        }
        return changed;
    }

    /// <summary>
    /// Splits every concave quad in the model into two triangles along its interior diagonal.
    /// Triangles already stored as degenerate quads (a duplicated index) are left untouched, and
    /// convex quads are left untouched (either of their diagonals is safe). Returns the number of
    /// concave quads that were split.
    /// </summary>
    public static int SplitConcaveQuads(this KmdModel model)
    {
        int totalSplit = 0;
        foreach (var obj in model.Objects)
        {
            var newOrder = new List<Vector4UInt8>(obj.VertexOrderTable.Count);
            var newNormalOrder = new List<Vector4UInt8>(obj.NormalVertexOrderTable.Count);
            var newUv = new List<Vector2UInt8>(obj.UvTable.Count);
            var newTex = new List<ushort>(obj.PCXHashedFileNames.Count);

            for (int fi = 0; fi < obj.VertexOrderTable.Count; fi++)
            {
                var vo = obj.VertexOrderTable[fi];
                var no = fi < obj.NormalVertexOrderTable.Count ? obj.NormalVertexOrderTable[fi] : default;
                ushort tex = fi < obj.PCXHashedFileNames.Count ? obj.PCXHashedFileNames[fi] : (ushort)0;
                var uv = new Vector2UInt8[4];
                for (int c = 0; c < 4; c++)
                {
                    int idx = fi * 4 + c;
                    uv[c] = idx < obj.UvTable.Count ? obj.UvTable[idx] : default;
                }

                byte[] v = [vo.X, vo.Y, vo.Z, vo.W];
                byte[] nn = [no.X, no.Y, no.Z, no.W];

                bool isQuad = v[0] != v[1] && v[0] != v[2] && v[0] != v[3]
                              && v[1] != v[2] && v[1] != v[3] && v[2] != v[3];

                int reflex = -1;
                if (isQuad)
                {
                    var p = new (double x, double y, double z)[4];
                    for (int c = 0; c < 4; c++)
                    {
                        var vc = obj.VertexCoordsTable[v[c]];
                        p[c] = (vc.X, vc.Y, vc.Z);
                    }
                    reflex = ReflexCorner(p);
                }

                if (reflex < 0)
                {
                    newOrder.Add(vo);
                    if (fi < obj.NormalVertexOrderTable.Count) newNormalOrder.Add(no);
                    newUv.AddRange(uv);
                    newTex.Add(tex);
                    continue;
                }

                int r = reflex;
                int[][] tris =
                [
                    [r, (r + 1) & 3, (r + 2) & 3],
                    [r, (r + 2) & 3, (r + 3) & 3]
                ];
                foreach (var t in tris)
                {
                    int a = t[0], b = t[1], c = t[2];
                    newOrder.Add(new Vector4UInt8 { X = v[a], Y = v[b], Z = v[c], W = v[c] });
                    newNormalOrder.Add(new Vector4UInt8 { X = nn[a], Y = nn[b], Z = nn[c], W = nn[c] });
                    newUv.Add(uv[a]); newUv.Add(uv[b]); newUv.Add(uv[c]); newUv.Add(uv[c]);
                    newTex.Add(tex);
                }
                totalSplit++;
            }

            obj.VertexOrderTable = newOrder;
            obj.NormalVertexOrderTable = newNormalOrder;
            obj.UvTable = newUv;
            obj.PCXHashedFileNames = newTex;
            obj.FaceCount = (uint)newOrder.Count;
        }
        return totalSplit;
    }

    /// <summary>
    /// Re-stripes every object's data-section offsets contiguously, identical to the layout pass in
    /// KmdImporter. Must be called after any change that resizes the face/normal arrays.
    /// </summary>
    public static void RecalculateOffsets(this KmdModel model)
    {
        uint root = 32 + 88 * (uint)model.Objects.Count;
        foreach (var obj in model.Objects)
        {
            obj.VertexCount = (uint)obj.VertexCoordsTable.Count;
            obj.NormalVertexCount = (uint)obj.NormalVertexCoordsTable.Count;
            obj.FaceCount = (uint)obj.VertexOrderTable.Count;

            obj.VertexCoordOffset = root; root += obj.VertexCount * 8;
            obj.NormalVertexCoordOffset = root; root += obj.NormalVertexCount * 8;
            obj.VertexOrderOffset = root; root += (uint)obj.VertexOrderTable.Count * 4;
            obj.NormalVertexOrderOffset = root; root += (uint)obj.NormalVertexOrderTable.Count * 4;
            obj.UVOffset = root; root += obj.FaceCount * 4 * 2;
            obj.TextureNameOffset = root; root += (uint)obj.PCXHashedFileNames.Count * 2;
        }
    }

    /// <summary>
    /// Returns the index (0-3) of the reflex (concave) corner of a 4-gon given in winding order,
    /// or -1 if the quad is convex. Uses a Newell-plane normal and the per-vertex turn direction;
    /// the reflex corner is the single corner whose turn opposes the other three.
    /// </summary>
    private static int ReflexCorner((double x, double y, double z)[] p)
    {
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = p[i]; var b = p[(i + 1) & 3];
            nx += (a.y - b.y) * (a.z + b.z);
            ny += (a.z - b.z) * (a.x + b.x);
            nz += (a.x - b.x) * (a.y + b.y);
        }

        Span<double> turn = stackalloc double[4];
        for (int k = 0; k < 4; k++)
        {
            var prev = p[(k + 3) & 3]; var cur = p[k]; var next = p[(k + 1) & 3];
            double e1x = cur.x - prev.x, e1y = cur.y - prev.y, e1z = cur.z - prev.z;
            double e2x = next.x - cur.x, e2y = next.y - cur.y, e2z = next.z - cur.z;
            double cx = e1y * e2z - e1z * e2y;
            double cy = e1z * e2x - e1x * e2z;
            double cz = e1x * e2y - e1y * e2x;
            turn[k] = cx * nx + cy * ny + cz * nz;
        }

        int pos = 0, neg = 0;
        for (int k = 0; k < 4; k++)
        {
            if (turn[k] > 1e-6) pos++;
            else if (turn[k] < -1e-6) neg++;
        }
        if (pos == 0 || neg == 0) return -1;

        int reflex = 0;
        if (neg <= pos)
        {
            double min = double.MaxValue;
            for (int k = 0; k < 4; k++) if (turn[k] < min) { min = turn[k]; reflex = k; }
        }
        else
        {
            double max = double.MinValue;
            for (int k = 0; k < 4; k++) if (turn[k] > max) { max = turn[k]; reflex = k; }
        }
        return reflex;
    }
}
