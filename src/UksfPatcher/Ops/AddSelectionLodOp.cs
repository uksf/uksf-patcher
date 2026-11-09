using System.Numerics;
using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddSelectionLodOp : OdolOp
{
    private static readonly Dictionary<string, string> IndexFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roadway"] = "Roadway",
        ["hitpoints"] = "Hitpoints",
    };

    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lodName = OpJson.String(op, "lod");
        if (!IndexFields.TryGetValue(lodName, out var indexField))
        {
            throw new PatchException($"{context.AssetPath}: add-selection-lod supports roadway or hitpoints, not '{lodName}'");
        }

        var from = FindLod(odol, OpJson.String(op, "fromLod"), context);
        var wanted = OpJson.Strings(op, "selections");
        var expected = OpJson.Int(op, "faces");
        var selections = wanted.Select(name => from.NamedSelections.SingleOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PatchException($"{context.AssetPath}: {OpJson.String(op, "fromLod")} has no selection '{name}'")).ToArray();
        var verts = selections.SelectMany(s => s.SelectedVertices).ToHashSet();
        var faces = from.Polygons.Faces;
        var kept = Enumerable.Range(0, faces.Length).Where(i =>
        {
            var v = faces[i].VertexIndices;
            return v.Length > 0 && v.All(verts.Contains);
        }).ToArray();
        if (kept.Length != expected)
        {
            throw new PatchException($"{context.AssetPath}: selections {string.Join(", ", wanted)} have {kept.Length} faces, the patch expects {expected}");
        }

        if (from.Sections.Length == 0)
        {
            throw new PatchException($"{context.AssetPath}: {OpJson.String(op, "fromLod")} has no sections");
        }

        var lod = LodInsert.InsertClone(odol, from, LodNames.Resolution(lodName), indexField, context.AssetPath);
        var remap = kept.Select((face, i) => (face, i)).ToDictionary(p => p.face, p => p.i);
        var newFaces = kept.Select(i => faces[i]).ToList();
        var named = selections.Select(s =>
        {
            var own = s.SelectedVertices.ToHashSet();
            var listed = kept.Where(i => faces[i].VertexIndices.All(own.Contains)).Select(i => remap[i]).ToArray();
            return MemoryLod.NewSelection(s.Name, listed, s.SelectedVertices.ToArray());
        }).ToList();
        AddQuads(lod, op, newFaces, named, context);
        var bytes = newFaces.Sum(OdolFaces.Weight);
        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Faces), newFaces.ToArray());
        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Unused1), (uint)bytes);
        var section = lod.Sections[0];
        LibraryAccess.Set(section, nameof(Section.FaceLowerIndex), 0);
        LibraryAccess.Set(section, nameof(Section.FaceUpperIndex), bytes);
        LibraryAccess.Set(lod, nameof(LOD.Sections), new[] { section });
        OdolGraph.SetRawProxies(lod, []);
        lod.NamedSelections = named.ToArray();
    }

    private static void AddQuads(LOD lod, JsonElement op, List<Polygon> faces, List<NamedSelection> named, OpContext context)
    {
        if (!op.TryGetProperty("quads", out var quads))
        {
            return;
        }

        var refs = OdolGraph.BoneRefs(lod);
        if (refs.Count != lod.Vertices.Count)
        {
            refs = Enumerable.Range(0, lod.Vertices.Count).Select(_ => OdolGraph.EmptyBoneRef()).ToList();
        }

        foreach (var quad in quads.EnumerateArray())
        {
            var name = OpJson.String(quad, "name");
            var points = quad.GetProperty("points").EnumerateArray().Select(p => p.EnumerateArray().Select(v => v.GetSingle()).ToArray()).ToArray();
            if (points.Length != 4 || points.Any(p => p.Length != 3))
            {
                throw new PatchException($"{context.AssetPath}: quad '{name}' needs four x, y, z points");
            }

            var added = points.Select(p => MemoryLod.AppendVertex(lod, Point(p), lod.Clip[0], lod.NormalsCompressed[0], OdolGraph.EmptyBoneRef(), refs)).ToArray();
            named.Add(MemoryLod.NewSelection(name, [faces.Count, faces.Count + 1], added));
            faces.Add(OdolFaces.NewFace(added));
            faces.Add(OdolFaces.NewFace(added.Reverse().ToArray()));
        }

        OdolGraph.SetBoneRefs(lod, refs);
    }

    private static Vector3 Point(float[] p) => new(p[0], p[1], p[2]);
}
