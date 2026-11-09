using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class MoveRotationAxisOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "animation");
        var selectionName = OpJson.String(op, "selection");
        var values = OpJson.Floats(op, "offset", 3);
        var offset = new Vector3(values[0], values[1], values[2]);
        var animations = odol.Animations ?? throw new PatchException($"{context.AssetPath}: no animations");
        var matches = animations.AnimationClasses.Select((a, i) => (a, i))
            .Where(x => x.a.AnimName.Equals(name, StringComparison.OrdinalIgnoreCase) && x.a.AnimType <= 3).ToArray();
        if (matches.Length != 1) throw new PatchException($"{context.AssetPath}: expected one rotation animation '{name}'");
        var index = matches[0].i;
        var memory = FindLod(odol, "memory", context);
        var selection = OdolGraph.Selection(memory, selectionName, context.AssetPath);
        var vertices = selection.SelectedVertices.ToArray();
        if (vertices.Length != 2 || memory.NamedSelections.Any(s => s != selection && s.SelectedVertices.Any(vertices.Contains)))
            throw new PatchException($"{context.AssetPath}: axis selection '{selectionName}' must have two unshared points");
        var positions = memory.Vertices.ToArray();
        foreach (var v in vertices) positions[v] = new Vector3P(positions[v].Vector3 + offset);
        LibraryAccess.Set(memory, nameof(LOD.Vertices), new TrackedArray<Vector3P>(positions));
        foreach (var group in animations.AxisData)
        {
            if (index >= group.Length || group[index].Length != 2)
                throw new PatchException($"{context.AssetPath}: invalid axis data for '{name}'");
            group[index][0] = new Vector3P(group[index][0].Vector3 + offset);
        }
    }
}
