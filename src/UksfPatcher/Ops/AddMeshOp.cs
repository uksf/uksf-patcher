using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddMeshOp : OdolOp
{
    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var mesh = SourceMesh.Load(op, context);
        var frame = AxialFrame.From(op, context);
        var band = op.GetProperty("uvFrom");
        foreach (var name in OpJson.Strings(op, "lods"))
        {
            var lod = FindLod(odol, name, context);
            var bound = BoneWeights.BoundOnlyTo(odol, lod, [OpJson.String(op, "bone")], context);
            var bandFaces = BandFaces(lod, frame, bound, band);
            if (bandFaces.Count == 0)
            {
                throw new PatchException($"{context.AssetPath}: LOD {name} has no barrel faces in the uvFrom band");
            }

            var ring = bandFaces.SelectMany(f => lod.Polygons.Faces[f].VertexIndices).Distinct()
                .Where(v => Math.Abs(frame.Along(lod.Vertices[v]) - OpJson.Float(band, "alongMax")) < OpJson.Float(band, "tolerance")).ToList();
            if (ring.Count == 0)
            {
                throw new PatchException($"{context.AssetPath}: LOD {name} has no barrel ring at the attach point");
            }

            var fits = Fits(odol, op, lod, bandFaces, frame, context);
            var weldTargets = lod.Polygons.Faces.SelectMany(f => f.VertexIndices).Distinct().Where(bound).ToList();
            Append(lod, mesh, fits, frame, OpJson.Float(op, "attachAlong"), ring, weldTargets, bandFaces, context);
            OdolBounds.Grow(odol, lod);
        }
    }

    private CylinderUv?[] Fits(ODOL odol, JsonElement op, LOD lod, List<int> bandFaces, AxialFrame frame, OpContext context)
    {
        if (!op.GetProperty("uvFrom").TryGetProperty("fit", out var fit))
        {
            return lod.UvSets.Select(s => s.DefaultFill ? null : CylinderUv.Fit(lod, s.GetUV(), bandFaces, frame, context.AssetPath)).ToArray();
        }

        var from = FindLod(odol, OpJson.String(fit, "lod"), context);
        var fromFaces = BandFaces(from, frame, BoneWeights.BoundOnlyTo(odol, from, [OpJson.String(op, "bone")], context), fit);
        return lod.UvSets.Select((s, i) => s.DefaultFill || i >= from.UvSets.Length || from.UvSets[i].DefaultFill
            ? null
            : CylinderUv.Fit(from, from.UvSets[i].GetUV(), fromFaces, frame, context.AssetPath)).ToArray();
    }

    private static List<int> BandFaces(LOD lod, AxialFrame frame, Func<int, bool> bound, JsonElement band)
    {
        float min = OpJson.Float(band, "alongMin"), max = OpJson.Float(band, "alongMax");
        float radius = OpJson.Float(band, "radius"), tol = OpJson.Float(band, "tolerance");
        bool In(int v)
        {
            var p = lod.Vertices[v].Vector3;
            var along = frame.Along(p);
            return bound(v) && along > min - tol && along < max + tol && Math.Abs(frame.Radius(p) - radius) < tol;
        }

        return Enumerable.Range(0, lod.Polygons.Faces.Length).Where(f => lod.Polygons.Faces[f].VertexIndices.All(In)).ToList();
    }

    private static void Append(LOD lod, SourceMesh mesh, CylinderUv?[] fits, AxialFrame frame, float fold, List<int> ring, List<int> weldTargets, List<int> bandFaces, OpContext context)
    {
        Vector3 Place(int point)
        {
            var p = mesh.Points[point];
            if (!mesh.Weld[point])
            {
                return p;
            }

            var nearest = weldTargets.Select(v => lod.Vertices[v].Vector3).MinBy(r => Vector3.Distance(r, p));
            return Vector3.Distance(nearest, p) < 0.006f
                ? nearest
                : throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no barrel vertex within 6 mm of weld point {p}");
        }

        var n0 = lod.Vertices.Count;
        var positions = lod.Vertices.ToList();
        var normals = lod.NormalsCompressed.ToList();
        var st = lod.STCoordsCompressed.ToList();
        var tangentFit = Array.FindIndex(fits, f => f != null);
        var hasTangents = st.Count == n0;
        var clip = lod.Clip.ToList();
        var bones = OdolGraph.BoneRefs(lod);
        var uvs = lod.UvSets.Select(s => s.GetUV().ToList()).ToArray();
        var keyed = new Dictionary<(int, int, int, int), int>();
        var newFaces = new List<Polygon>();
        foreach (var face in mesh.Faces)
        {
            var pts = face.Select(c => Place(c.Point)).ToArray();
            var projected = fits.Select(fit => fit == null ? null : FaceProjection.Project(fit, frame, pts, fold)).ToArray();
            var idx = new int[face.Length];
            for (var k = 0; k < face.Length; k++)
            {
                var p = pts[k];
                var n = face[k].Normal;
                var uv = projected.Select(set => set?[k].Uv ?? Vector2.Zero).ToArray();
                var key = (face[k].Point, (int)UvData.Pack([-n.X, -n.Y, -n.Z], 0), (int)Math.Round(uv[0].X * 4096), (int)Math.Round(uv[0].Y * 4096));
                if (!keyed.TryGetValue(key, out var v))
                {
                    v = keyed[key] = positions.Count;
                    positions.Add(new Vector3P(p));
                    normals.Add(UvData.Pack([-n.X, -n.Y, -n.Z], 0));
                    if (hasTangents && tangentFit < 0)
                    {
                        st.Add(st[ring[0]]);
                    }
                    else if (hasTangents)
                    {
                        var corner = projected[tangentFit]![k];
                        var (s, t) = fits[tangentFit]!.Tangents(corner.DPdUnwrapped, corner.DPdAlong, n);
                        st.Add(Tuple.Create(UvData.Pack([s.X, s.Y, s.Z], 0), UvData.Pack([t.X, t.Y, t.Z], 0)));
                    }
                    clip.Add(lod.Clip[ring[0]]);
                    bones.Add(bones[ring[0]]);
                    for (var set = 0; set < uvs.Length; set++)
                    {
                        uvs[set].Add(uv[set]);
                    }
                }

                idx[k] = v;
            }

            newFaces.Add(OdolFaces.NewFace(idx));
        }

        var added = positions.Count - n0;
        LibraryAccess.Set(lod, nameof(LOD.Vertices), new TrackedArray<Vector3P>(positions));
        LibraryAccess.Set(lod, nameof(LOD.NormalsCompressed), new TrackedArray<Vector3PCompressed>(normals));
        LibraryAccess.Set(lod, nameof(LOD.STCoordsCompressed), new TrackedArray<Tuple<Vector3PCompressed, Vector3PCompressed>>(st));
        LibraryAccess.Set(lod, nameof(LOD.Clip), new TrackedArray<int>(clip));
        OdolGraph.SetBoneRefs(lod, bones);
        for (var set = 0; set < uvs.Length; set++)
        {
            if (fits[set] != null)
            {
                UvData.Write(lod.UvSets[set], uvs[set].ToArray(), context);
            }

            LibraryAccess.Set(lod.UvSets[set], nameof(UVSet.NVertices), (uint)positions.Count);
        }

        LibraryAccess.Set(lod, nameof(LOD.VertexCount), (uint)positions.Count);
        InsertFaces(lod, newFaces, bandFaces, ring, Enumerable.Range(n0, added).ToArray(), context);
    }

    private static void InsertFaces(LOD lod, List<Polygon> newFaces, List<int> bandFaces, List<int> ring, int[] newVertices, OpContext context)
    {
        var ranges = OdolFaces.SectionFaceRanges(lod, context.AssetPath);
        var section = Enumerable.Range(0, ranges.Length).MaxBy(s => bandFaces.Count(f => f >= ranges[s].First && f < ranges[s].End));
        var at = ranges[section].End;
        var faces = lod.Polygons.Faces.Take(at).Concat(newFaces).Concat(lod.Polygons.Faces.Skip(at)).ToArray();
        var newRanges = ranges.Select((r, s) => s < section ? r : s == section ? (r.First, r.End + newFaces.Count) : (r.First + newFaces.Count, r.End + newFaces.Count)).ToArray();
        var inserted = Enumerable.Range(at, newFaces.Count).ToArray();
        foreach (var selection in lod.NamedSelections)
        {
            var selFaces = selection.SelectedFaces.Select(f => f >= at ? f + newFaces.Count : f).ToList();
            if (selFaces.Count > 0 && bandFaces.All(selection.SelectedFaces.Contains))
            {
                selFaces.AddRange(inserted);
            }

            if (selFaces.Count > 0)
            {
                OdolFaces.SetSelectedFaces(selection, selFaces);
            }

            if (selection.SelectedVertices.Count > 0 && ring.All(selection.SelectedVertices.Contains))
            {
                var weights = selection.SelectedVerticesWeights.ToList();
                var weighted = weights.Count == selection.SelectedVertices.Count;
                var full = weighted ? weights[selection.SelectedVertices.ToList().IndexOf(ring[0])] : (byte)0;
                LibraryAccess.Set(selection, nameof(NamedSelection.SelectedVertices), new TrackedArray<int>(selection.SelectedVertices.Concat(newVertices)));
                if (weighted)
                {
                    weights.AddRange(newVertices.Select(_ => full));
                    LibraryAccess.Set(selection, nameof(NamedSelection.SelectedVerticesWeights), new TrackedArray<byte>(weights));
                    LibraryAccess.Set(selection, nameof(NamedSelection.ExpectedSize), weights.Count);
                }
            }
        }

        OdolFaces.Replace(lod, faces, newRanges);
    }
}
