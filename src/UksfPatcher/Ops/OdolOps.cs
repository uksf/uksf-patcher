using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public abstract class OdolOp : IAssetOp
{
    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        OdolFile.RequireRoundTrip(asset, context.AssetPath);
        var odol = OdolFile.Parse(asset, context.AssetPath);
        Edit(odol, op, context);
        var output = OdolFile.Serialise(odol);
        var reparsed = OdolFile.Parse(output, context.AssetPath);
        if (RewritesFaces)
        {
            OdolFaces.RequirePartitions(reparsed, context.AssetPath);
        }

        return output;
    }

    protected virtual bool RewritesFaces => false;

    protected abstract void Edit(ODOL odol, JsonElement op, OpContext context);

    protected static LOD FindLod(ODOL odol, string name, OpContext context)
    {
        var resolution = LodNames.Resolution(name);
        return odol.Lods.SingleOrDefault(l => LodNames.Matches(l.Resolution, resolution))
            ?? throw new PatchException($"{context.AssetPath}: no {name} LOD");
    }
}

public sealed class SetBoneParentOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var boneName = OpJson.String(op, "bone");
        var from = OpJson.String(op, "from");
        var to = OpJson.String(op, "to");
        var bones = odol.ModelInfo.Skeleton.SkeletonBoneNames ?? [];
        var bone = bones.SingleOrDefault(b => Same(b.BoneName, boneName))
            ?? throw new PatchException($"{context.AssetPath}: skeleton has no bone '{boneName}'");
        if (!Same(bone.ParentBoneName, from))
        {
            throw new PatchException($"{context.AssetPath}: bone '{boneName}' has parent '{bone.ParentBoneName}', the patch expects '{from}'");
        }

        if (to.Length > 0 && !bones.Any(b => Same(b.BoneName, to)))
        {
            throw new PatchException($"{context.AssetPath}: new parent '{to}' is not a bone of the skeleton");
        }

        LibraryAccess.Set(bone, nameof(SkeletonBoneName.ParentBoneName), to);
        RequireAcyclic(bones, context);
    }

    private static void RequireAcyclic(SkeletonBoneName[] bones, OpContext context)
    {
        var parents = bones.ToDictionary(b => b.BoneName.ToLowerInvariant(), b => b.ParentBoneName.ToLowerInvariant());
        foreach (var start in parents.Keys)
        {
            var seen = new HashSet<string>();
            for (var b = start; b.Length > 0 && parents.ContainsKey(b); b = parents[b])
            {
                if (!seen.Add(b))
                {
                    throw new PatchException($"{context.AssetPath}: the new parent makes a cycle through '{b}'");
                }
            }
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class SetSkeletonNameOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var from = OpJson.String(op, "from");
        var to = OpJson.String(op, "to");
        if (to.Length == 0)
        {
            throw new PatchException($"{context.AssetPath}: set-skeleton-name \"to\" is empty");
        }

        var skeleton = odol.ModelInfo.Skeleton;
        if (!string.Equals(skeleton.SkeletonName, from, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchException($"{context.AssetPath}: skeleton is '{skeleton.SkeletonName}', the patch expects '{from}'");
        }

        LibraryAccess.Set(skeleton, nameof(Skeleton.SkeletonName), to);
    }
}

public sealed class RemoveComponentOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var names = OpJson.Strings(op, "components").Select(n => n.ToLowerInvariant()).ToHashSet();
        var requireProxyFaces = !op.TryGetProperty("requireProxyFaces", out var r) || r.GetBoolean();
        var selections = lod.NamedSelections;
        var proxyFaces = selections.Where(s => s.Name.StartsWith("proxy:", StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.SelectedFaces).ToHashSet();
        foreach (var name in names)
        {
            var selection = selections.SingleOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no selection '{name}'");
            if (!name.StartsWith("component"))
            {
                throw new PatchException($"{context.AssetPath}: '{name}' is not a component selection");
            }

            if (requireProxyFaces && !selection.SelectedFaces.All(proxyFaces.Contains))
            {
                throw new PatchException($"{context.AssetPath}: '{name}' has faces that are not proxy faces; set requireProxyFaces false to remove a real component");
            }
        }

        var proxies = LibraryAccess.Get<Array>(lod, "RawProxies").Cast<object>().ToList();
        var proxyNames = proxies.Select(p => selections[LibraryAccess.Get<int>(p, "NamedSelectionIndex")].Name).ToList();
        var kept = selections.Where(s => !names.Contains(s.Name.ToLowerInvariant())).ToArray();
        var newIndex = kept.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);
        foreach (var proxy in proxies)
        {
            var old = selections[LibraryAccess.Get<int>(proxy, "NamedSelectionIndex")];
            if (!newIndex.TryGetValue(old, out var index))
            {
                throw new PatchException($"{context.AssetPath}: '{old.Name}' is a proxy's selection and cannot be removed");
            }

            LibraryAccess.Set(proxy, "NamedSelectionIndex", index);
        }

        lod.NamedSelections = kept;
        var after = proxies.Select(p => kept[LibraryAccess.Get<int>(p, "NamedSelectionIndex")].Name).ToList();
        if (!after.SequenceEqual(proxyNames))
        {
            throw new PatchException($"{context.AssetPath}: proxy selections changed while removing components");
        }
    }
}
