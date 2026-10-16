using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class TranslateSelectionOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var selection = OpJson.String(op, "selection");
        var offset = OpJson.Floats(op, "offset", 3);
        int[] sections;
        if (selection.StartsWith('#'))
        {
            var index = int.Parse(selection[1..]);
            if (index < 0 || index >= lod.Sections.Length)
            {
                throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no section {index}");
            }

            sections = [index];
        }
        else
        {
            var ns = OdolGraph.Selection(lod, selection, context.AssetPath);
            if (!ns.IsSectional || ns.Sections.Count == 0)
            {
                throw new PatchException($"{context.AssetPath}: '{selection}' is not a sectional selection");
            }

            sections = ns.Sections.ToArray();
        }

        var faces = lod.Polygons.Faces;
        var own = sections.SelectMany(s => OdolGraph.FacesInSection(lod, lod.Sections[s])).ToHashSet();
        if (own.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: '{selection}' has no faces");
        }

        var verts = own.SelectMany(f => faces[f].VertexIndices).ToHashSet();
        var shared = Enumerable.Range(0, faces.Length).Where(f => !own.Contains(f)).SelectMany(f => faces[f].VertexIndices).Count(verts.Contains);
        if (shared > 0)
        {
            throw new PatchException($"{context.AssetPath}: {shared} vertices of '{selection}' are shared with other faces");
        }

        var moved = lod.Vertices.Select((p, i) => verts.Contains(i) ? new Vector3P(p.X + offset[0], p.Y + offset[1], p.Z + offset[2]) : p);
        LibraryAccess.Set(lod, nameof(LOD.Vertices), new TrackedArray<Vector3P>(moved));
    }
}
