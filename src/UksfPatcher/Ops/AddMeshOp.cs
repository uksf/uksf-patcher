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

            var fits = lod.UvSets.Select(s => CylinderUv.Fit(lod, s.GetUV(), bandFaces, frame, context.AssetPath)).ToArray();
            Append(lod, mesh, fits, frame, OpJson.Float(op, "attachAlong"), ring, bandFaces, context);
            OdolBounds.Grow(odol, lod);
        }
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

    private static void Append(LOD lod, SourceMesh mesh, CylinderUv[] fits, AxialFrame frame, float fold, List<int> ring, List<int> bandFaces, OpContext context)
    {
        Vector3 Place(int point)
        {
            var p = mesh.Points[point];
            if (!mesh.Weld[point])
            {
                return p;
            }

            var nearest = ring.Select(v => lod.Vertices[v].Vector3).MinBy(r => Vector3.Distance(r, p));
            return Vector3.Distance(nearest, p) < 0.006f
                ? nearest
                : throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no barrel ring vertex within 6 mm of weld point {p}");
        }


        var n0 = lod.Vertices.Count;
        var positions = lod.Vertices.ToList();
        var normals = lod.NormalsCompressed.ToList();
        var st = lod.STCoordsCompressed.ToList();
        var clip = lod.Clip.ToList();
        var bones = OdolGraph.BoneRefs(lod);
        var uvs = lod.UvSets.Select(s => s.GetUV().ToList()).ToArray();
        var keyed = new Dictionary<(int, int, int, int), int>();
        var newFaces = new List<Polygon>();
        foreach (var face in mesh.Faces)
        {
            var pts = face.Select(c => Place(c.Point)).ToArray();
            var unwrapped = fits.Select(fit => fit.Unwrap(pts)).ToArray();
            var idx = new int[face.Length];
            for (var k = 0; k < face.Length; k++)
            {
                var p = pts[k];
                var n = face[k].Normal;
                var along = frame.Along(p);
                var folded = along > fold;
                var uv = fits.Select((fit, s) => fit.Map(unwrapped[s][k], folded ? 2 * fold - along : along)).ToArray();
                var key = (face[k].Point, (int)UvData.Pack([-n.X, -n.Y, -n.Z], 0), (int)Math.Round(uv[0].X * 4096), (int)Math.Round(uv[0].Y * 4096));
                if (!keyed.TryGetValue(key, out var v))
                {
                    v = keyed[key] = positions.Count;
                    var (s, t) = fits[0].Tangents(p, n, folded ? -1 : 1);
                    positions.Add(new Vector3P(p));
                    normals.Add(UvData.Pack([-n.X, -n.Y, -n.Z], 0));
                    st.Add(Tuple.Create(UvData.Pack([s.X, s.Y, s.Z], 0), UvData.Pack([t.X, t.Y, t.Z], 0)));
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
            UvData.Write(lod.UvSets[set], uvs[set].ToArray(), context);
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

public sealed record SourceCorner(int Point, Vector3 Normal);

public sealed class SourceMesh
{
    public required Vector3[] Points { get; init; }

    public required SourceCorner[][] Faces { get; init; }

    public required bool[] Weld { get; init; }

    public static SourceMesh Load(JsonElement op, OpContext context)
    {
        var path = Path.Join(context.ManifestDirectory, OpJson.String(op, "source"));
        if (!File.Exists(path))
        {
            throw new PatchException($"{context.AssetPath}: add-mesh source '{path}' does not exist");
        }

        var lod = new MLOD(path).Lods.Single();
        var s = OpJson.Floats(op, "sourceAxis", 6);
        var target = AxialFrame.From(op, context);
        var scale = OpJson.Float(op, "scale");
        Vector3 sourceOrigin = new(s[0], s[1], s[2]), sourceFwd = Vector3.Normalize(new(s[3], s[4], s[5]));
        var sourceUp = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, sourceFwd) * sourceFwd);
        var targetUp = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, target.Direction) * target.Direction);
        var sourceBasis = Rows(Vector3.Cross(sourceUp, sourceFwd), sourceUp, sourceFwd);
        var targetBasis = Rows(Vector3.Cross(targetUp, target.Direction), targetUp, target.Direction);
        var attach = target.Origin + target.Direction * OpJson.Float(op, "attachAlong");
        Vector3 Rotate(Vector3 v) => Vector3.TransformNormal(Vector3.TransformNormal(v, Matrix4x4.Transpose(sourceBasis)), targetBasis);
        bool OnLoop(Vector3 p, JsonElement l) =>
            Math.Abs(Vector3.Dot(p - sourceOrigin, sourceFwd) - (l[0].GetSingle() - Vector3.Dot(sourceOrigin, sourceFwd))) < 5e-4f
            && Math.Abs((p - sourceOrigin - Vector3.Dot(p - sourceOrigin, sourceFwd) * sourceFwd).Length() - l[1].GetSingle()) < 5e-4f;
        var loopRadii = op.TryGetProperty("loopRadii", out var lr) ? lr.EnumerateArray().ToArray() : [];
        var weld = op.GetProperty("weldLoop");
        var points = lod.Points.Select(p =>
        {
            var rel = p.Vector3 - sourceOrigin;
            var axial = Vector3.Dot(rel, sourceFwd) * sourceFwd;
            var radial = (rel - axial) * scale;
            foreach (var l in loopRadii.Where(l => OnLoop(p.Vector3, l)))
            {
                radial = Vector3.Normalize(radial) * l[2].GetSingle();
            }

            return attach + Rotate(axial * scale + radial);
        }).ToArray();
        var welded = lod.Points.Select(p => OnLoop(p.Vector3, weld)).ToArray();
        if (welded.Count(w => w) == 0)
        {
            throw new PatchException($"{context.AssetPath}: add-mesh weldLoop matches no source point");
        }

        var faces = lod.Faces.Select(f => f.RealVertices.Select(v => new SourceCorner(v.PointIndex, Rotate(lod.Normals[v.NormalIndex].Vector3))).ToArray()).ToList();
        if (faces.Any(f => f.Length != 3 && f.Length != 4))
        {
            throw new PatchException($"{context.AssetPath}: add-mesh source has a face that is not a triangle or quad");
        }

        var tris = faces.SelectMany(f => f.Length == 3 ? [f] : new[] { new[] { f[0], f[1], f[2] }, new[] { f[0], f[2], f[3] } }).ToArray();
        Vector3 Centroid(SourceCorner[] f) => (points[f[0].Point] + points[f[1].Point] + points[f[2].Point]) / 3;
        var rim = tris.Max(f => target.Radius(Centroid(f))) * 0.9f;
        var outward = tris.Where(f => target.Radius(Centroid(f)) > rim).Sum(f => Math.Sign(Vector3.Dot(
            Vector3.Cross(points[f[1].Point] - points[f[0].Point], points[f[2].Point] - points[f[0].Point]),
            Centroid(f) - target.Origin - target.Along(Centroid(f)) * target.Direction)));
        var oriented = tris.Select(f => outward >= 0 ? f : [f[0], f[2], f[1]]).Select(f =>
        {
            var geo = Vector3.Normalize(Vector3.Cross(points[f[1].Point] - points[f[0].Point], points[f[2].Point] - points[f[0].Point]));
            return f.Select(c => new SourceCorner(c.Point, Vector3.Dot(c.Normal, geo) >= 0 ? Vector3.Normalize(c.Normal) : -Vector3.Normalize(c.Normal))).ToArray();
        }).ToArray();
        return new SourceMesh { Points = points, Faces = oriented, Weld = welded };
    }

    private static Matrix4x4 Rows(Vector3 a, Vector3 b, Vector3 c) => new(a.X, a.Y, a.Z, 0, b.X, b.Y, b.Z, 0, c.X, c.Y, c.Z, 0, 0, 0, 0, 1);
}
