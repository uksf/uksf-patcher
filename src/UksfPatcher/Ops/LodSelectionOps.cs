using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class CopySelectionPointsOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var target = FindLod(odol, OpJson.String(op, "lod"), context);
        var name = OpJson.String(op, "name");
        var fromLod = FindLod(odol, OpJson.String(op, "fromLod"), context);
        var fromName = OpJson.String(op, "from");
        var shrink = OpJson.Float(op, "shrink");
        var expected = OpJson.Int(op, "points", -1);
        if (shrink is < 0 or > 1)
        {
            throw new PatchException($"{context.AssetPath}: shrink must be between 0 and 1");
        }
        if (target.Polygons.Faces.Length != 0)
        {
            throw new PatchException($"{context.AssetPath}: target LOD has faces; copy-selection-points only adds loose points");
        }
        if (target.Vertices.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: target LOD has no vertex to copy point flags from");
        }
        if (target.NamedSelections.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: target LOD already has selection '{name}'");
        }

        var source = fromLod.NamedSelections.SingleOrDefault(n => n.Name.Equals(fromName, StringComparison.OrdinalIgnoreCase))
            ?? throw new PatchException($"{context.AssetPath}: source LOD has no selection '{fromName}'");
        var positions = source.SelectedVertices.Distinct().Select(v => ToVector(fromLod.Vertices[v])).ToList();
        if (positions.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: source selection '{fromName}' has no vertices");
        }

        var centre = positions.Aggregate(Vector3.Zero, (a, b) => a + b) / positions.Count;
        var points = positions.Select(p => centre + (p - centre) * shrink).Prepend(centre).ToList();
        if (expected >= 0 && points.Count != expected)
        {
            throw new PatchException($"{context.AssetPath}: copy-selection-points '{name}' made {points.Count} points, manifest expects {expected}");
        }

        var donor = 0;
        var boneRefs = OdolGraph.BoneRefs(target);
        var added = points.Select(p => MemoryLod.AppendVertex(target, p, target.Clip[donor], target.NormalsCompressed[donor], OdolGraph.Clone(boneRefs[donor]), boneRefs)).ToArray();
        OdolGraph.SetBoneRefs(target, boneRefs);
        target.NamedSelections = target.NamedSelections.Append(MemoryLod.NewSelection(name, [], added)).ToArray();
    }

    private static Vector3 ToVector(Vector3P p) => new(p.X, p.Y, p.Z);
}

public sealed class SplitSelectionOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var name = OpJson.String(op, "selection");
        var into = OpJson.String(op, "into");
        var axis = OpJson.Int(op, "axis", -1);
        var keepBelow = OpJson.Float(op, "keepBelow");
        if (axis is < 0 or > 2)
        {
            throw new PatchException($"{context.AssetPath}: split-selection axis must be 0, 1 or 2");
        }
        if (lod.NamedSelections.Any(n => n.Name.Equals(into, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: LOD already has selection '{into}'");
        }

        var selection = lod.NamedSelections.SingleOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PatchException($"{context.AssetPath}: LOD has no selection '{name}'");
        if (selection.SelectedFaces.Any())
        {
            throw new PatchException($"{context.AssetPath}: selection '{name}' has faces; split-selection only splits loose points");
        }
        if (selection.Sections.Any())
        {
            throw new PatchException($"{context.AssetPath}: selection '{name}' has sections; split-selection only splits loose points");
        }
        if (selection.SelectedVerticesWeights.Any())
        {
            throw new PatchException($"{context.AssetPath}: selection '{name}' has vertex weights; split-selection only splits unweighted points");
        }

        var vertices = selection.SelectedVertices.ToArray();
        var below = vertices.Where(v => Coordinate(lod.Vertices[v], axis) < keepBelow).ToArray();
        var above = vertices.Where(v => Coordinate(lod.Vertices[v], axis) >= keepBelow).ToArray();
        if (below.Length == 0 || above.Length == 0)
        {
            throw new PatchException($"{context.AssetPath}: split of '{name}' at {keepBelow} on axis {axis} leaves one side empty");
        }

        LibraryAccess.Set(selection, nameof(NamedSelection.SelectedVertices), new TrackedArray<int>(below));
        lod.NamedSelections = lod.NamedSelections.Append(MemoryLod.NewSelection(into, [], above)).ToArray();
    }

    private static float Coordinate(Vector3P p, int axis) => axis switch { 0 => p.X, 1 => p.Y, _ => p.Z };
}
