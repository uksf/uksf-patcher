using System.Collections;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class CutSeamsOp : OdolOp
{
    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var plan = OpJson.SourceJson(op, context);
        var copies = plan.GetProperty("copies").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var n0 = lod.Vertices.Count;
        if (copies.Any(c => c < 0 || c >= n0))
        {
            throw new PatchException($"{context.AssetPath}: cut-seams copies a vertex outside the LOD's {n0}");
        }

        if (LibraryAccess.Get<Array?>(lod, "Frames")?.Length > 0)
        {
            throw new PatchException($"{context.AssetPath}: cut-seams does not support LODs with keyframes");
        }

        T[] Grow<T>(IEnumerable<T> a)
        {
            var l = a.ToList();
            foreach (var c in copies)
            {
                l.Add(l[c]);
            }

            return l.ToArray();
        }

        LibraryAccess.Set(lod, nameof(LOD.Vertices), new TrackedArray<Vector3P>(Grow(lod.Vertices)));
        LibraryAccess.Set(lod, nameof(LOD.NormalsCompressed), new TrackedArray<Vector3PCompressed>(Grow(lod.NormalsCompressed)));
        LibraryAccess.Set(lod, nameof(LOD.STCoordsCompressed), new TrackedArray<Tuple<Vector3PCompressed, Vector3PCompressed>>(Grow(lod.STCoordsCompressed)));
        if (lod.Clip.Count == n0)
        {
            LibraryAccess.Set(lod, nameof(LOD.Clip), new TrackedArray<int>(Grow(lod.Clip)));
        }

        if (lod.VertexToPoint.Count == n0)
        {
            LibraryAccess.Set(lod, nameof(LOD.VertexToPoint), new TrackedArray<int>(Grow(lod.VertexToPoint)));
        }

        var bones = OdolGraph.BoneRefs(lod);
        if (bones.Count == n0)
        {
            OdolGraph.SetBoneRefs(lod, Grow(bones));
        }

        GrowNeighbourBones(lod, n0, copies, context);
        foreach (var set in lod.UvSets)
        {
            UvData.Write(set, Grow(set.GetUV()), context);
            LibraryAccess.Set(set, nameof(UVSet.NVertices), (uint)(n0 + copies.Length));
        }

        LibraryAccess.Set(lod, nameof(LOD.VertexCount), (uint)(n0 + copies.Length));
        JoinSelections(lod, n0, copies);
        RepointFaces(lod, plan, n0, copies, context);
    }

    private static void GrowNeighbourBones(LOD lod, int n0, int[] copies, OpContext context)
    {
        var neigh = LibraryAccess.Get<IEnumerable?>(lod, "NeighborBoneRef")?.Cast<object>().ToList();
        if (neigh == null || neigh.Count == 0)
        {
            return;
        }

        if (neigh.Count != n0)
        {
            throw new PatchException($"{context.AssetPath}: NeighborBoneRef has {neigh.Count} entries for {n0} vertices");
        }

        foreach (var c in copies)
        {
            neigh.Add(neigh[c]);
        }

        var type = neigh[0].GetType();
        var array = Array.CreateInstance(type, neigh.Count);
        for (var i = 0; i < neigh.Count; i++)
        {
            array.SetValue(neigh[i], i);
        }

        LibraryAccess.Set(lod, "NeighborBoneRef", Activator.CreateInstance(typeof(TrackedArray<>).MakeGenericType(type), array));
    }

    private static void JoinSelections(LOD lod, int n0, int[] copies)
    {
        var srcCopies = new Dictionary<int, List<int>>();
        for (var i = 0; i < copies.Length; i++)
        {
            (srcCopies.TryGetValue(copies[i], out var l) ? l : srcCopies[copies[i]] = []).Add(n0 + i);
        }

        foreach (var sel in lod.NamedSelections)
        {
            var verts = sel.SelectedVertices.ToList();
            if (verts.Count == 0)
            {
                continue;
            }

            var weights = sel.SelectedVerticesWeights.ToList();
            var weighted = weights.Count == verts.Count;
            var added = 0;
            for (int k = 0, m = verts.Count; k < m; k++)
            {
                if (!srcCopies.TryGetValue(verts[k], out var cs))
                {
                    continue;
                }

                foreach (var c in cs)
                {
                    verts.Add(c);
                    if (weighted)
                    {
                        weights.Add(weights[k]);
                    }

                    added++;
                }
            }

            if (added == 0)
            {
                continue;
            }

            LibraryAccess.Set(sel, nameof(NamedSelection.SelectedVertices), new TrackedArray<int>(verts));
            if (weighted)
            {
                LibraryAccess.Set(sel, nameof(NamedSelection.SelectedVerticesWeights), new TrackedArray<byte>(weights));
                LibraryAccess.Set(sel, nameof(NamedSelection.ExpectedSize), weights.Count);
            }
        }
    }

    private static void RepointFaces(LOD lod, JsonElement plan, int n0, int[] copies, OpContext context)
    {
        var faces = lod.Polygons.Faces;
        var byVerts = new Dictionary<string, int>();
        for (var f = 0; f < faces.Length; f++)
        {
            byVerts[string.Join(',', faces[f].VertexIndices)] = f;
        }

        foreach (var e in plan.GetProperty("faces").EnumerateArray())
        {
            var key = string.Join(',', e.GetProperty("verts").EnumerateArray().Select(x => x.GetInt32()));
            if (!byVerts.TryGetValue(key, out var f))
            {
                throw new PatchException($"{context.AssetPath}: cut-seams finds no face with vertices {key}");
            }

            var idx = faces[f].VertexIndices;
            foreach (var p in e.GetProperty("map").EnumerateObject())
            {
                int from = int.Parse(p.Name), to = p.Value.GetInt32(), k = Array.IndexOf(idx, from);
                if (k < 0)
                {
                    throw new PatchException($"{context.AssetPath}: cut-seams face {key} has no vertex {from}");
                }

                if (to < n0 || to - n0 >= copies.Length || copies[to - n0] != from)
                {
                    throw new PatchException($"{context.AssetPath}: cut-seams vertex {to} is not a copy of {from}");
                }

                idx[k] = to;
            }
        }
    }
}
