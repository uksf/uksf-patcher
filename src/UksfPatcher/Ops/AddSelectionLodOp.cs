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
        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Faces), kept.Select(i => faces[i]).ToArray());
        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Unused1), (uint)kept.Sum(i => OdolFaces.Weight(faces[i])));
        var section = lod.Sections[0];
        LibraryAccess.Set(section, nameof(Section.FaceLowerIndex), 0);
        LibraryAccess.Set(section, nameof(Section.FaceUpperIndex), (int)kept.Sum(i => OdolFaces.Weight(faces[i])));
        LibraryAccess.Set(lod, nameof(LOD.Sections), new[] { section });
        OdolGraph.SetRawProxies(lod, []);
        lod.NamedSelections = selections.Select(s =>
        {
            var own = s.SelectedVertices.ToHashSet();
            var listed = kept.Where(i => faces[i].VertexIndices.All(own.Contains)).Select(i => remap[i]).ToArray();
            return MemoryLod.NewSelection(s.Name, listed, s.SelectedVertices.ToArray());
        }).ToArray();
    }
}
