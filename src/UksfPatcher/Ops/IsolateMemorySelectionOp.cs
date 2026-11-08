using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class IsolateMemorySelectionOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "name");
        var position = OpJson.Floats(op, "position", 3);
        var memory = FindLod(odol, "memory", context);
        var selection = memory.NamedSelections.SingleOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PatchException($"{context.AssetPath}: memory has no selection '{name}'");
        if (selection.SelectedVertices.Count == 0 || selection.SelectedFaces.Any() || selection.IsSectional)
        {
            throw new PatchException($"{context.AssetPath}: '{name}' is not a point selection");
        }

        var refs = OdolGraph.BoneRefs(memory);
        if (refs.Count != memory.Vertices.Count)
        {
            throw new PatchException($"{context.AssetPath}: memory bone-ref count does not match the vertex count");
        }

        var donor = selection.SelectedVertices[0];
        var boneRef = OdolGraph.EmptyBoneRef();
        var added = MemoryLod.AppendVertex(memory, new Vector3(position[0], position[1], position[2]), memory.Clip[donor], memory.NormalsCompressed[donor], boneRef, refs);
        var bone = op.TryGetProperty("bone", out var boneEl) && boneEl.ValueKind == JsonValueKind.String ? boneEl.GetString() : null;
        if (!string.IsNullOrEmpty(bone))
        {
            var sub = SubBone(memory, bone, context, odol);
            var data = new byte[8];
            data[0] = (byte)sub;
            data[1] = 255;
            LibraryAccess.Set(boneRef, "Count", 1);
            LibraryAccess.Set(boneRef, "Data", data);
        }

        OdolGraph.SetBoneRefs(memory, refs);
        LibraryAccess.Set(selection, nameof(NamedSelection.SelectedVertices), new TrackedArray<int>([added]));
        LibraryAccess.Set(selection, nameof(NamedSelection.SelectedFaces), new TrackedArray<int>([]));
    }

    private static int SubBone(LOD lod, string bone, OpContext context, ODOL odol)
    {
        var names = odol.ModelInfo.Skeleton.SkeletonBoneNames.Select(b => b.BoneName).ToArray();
        var skel = Array.FindIndex(names, n => n.Equals(bone, StringComparison.OrdinalIgnoreCase));
        if (skel < 0)
        {
            throw new PatchException($"{context.AssetPath}: skeleton has no bone '{bone}'");
        }

        var existing = Array.FindIndex(lod.SubSkeletonsToSkeleton, b => b == skel);
        if (existing >= 0)
        {
            return existing;
        }

        var sub = lod.SubSkeletonsToSkeleton.Length;
        if (sub > byte.MaxValue)
        {
            throw new PatchException($"{context.AssetPath}: bone '{bone}' would get sub-skeleton index {sub}, which a vertex weight cannot hold");
        }

        LibraryAccess.Set(lod, nameof(LOD.SubSkeletonsToSkeleton), lod.SubSkeletonsToSkeleton.Append(skel).ToArray());
        var sets = LibraryAccess.Get<Array>(lod, "SkeletonToSubSkeleton");
        var set = sets.GetValue(skel)!;
        LibraryAccess.Set(set, "SubSkeletons", new[] { sub });
        return sub;
    }
}
