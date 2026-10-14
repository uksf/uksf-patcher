using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class SetAnimationSourceOp : OdolOp
{
    private static readonly string[] Addresses = ["clamp", "mirror", "loop"];

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "animation");
        var source = OpJson.String(op, "to");
        var address = OpJson.OptionalString(op, "address");
        var index = address == null ? -1 : Array.IndexOf(Addresses, address.ToLowerInvariant());
        if (address != null && index < 0)
        {
            throw new PatchException($"{context.AssetPath}: address must be one of {string.Join(", ", Addresses)}");
        }

        var hits = (odol.Animations?.AnimationClasses ?? []).Where(a => a.AnimName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: model has no animation '{name}'");
        }

        foreach (var a in hits)
        {
            LibraryAccess.Set(a, nameof(AnimationClass.AnimSource), source);
            if (index >= 0)
            {
                LibraryAccess.Set(a, nameof(AnimationClass.SourceAddress), (uint)index);
            }
        }
    }
}

public sealed class BindSelectionOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var selection = OpJson.String(op, "selection");
        var bone = OpJson.String(op, "bone");
        var box = op.TryGetProperty("box", out _) ? OpJson.Floats(op, "box", 6) : null;
        var names = odol.ModelInfo.Skeleton.SkeletonBoneNames.Select(b => b.BoneName).ToArray();
        var sub = Array.FindIndex(lod.SubSkeletonsToSkeleton, b => names[b].Equals(bone, StringComparison.OrdinalIgnoreCase));
        if (sub < 0)
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no bone '{bone}'");
        }

        int[] sections;
        if (selection.StartsWith('#'))
        {
            sections = [int.Parse(selection[1..])];
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

        bool Inside(int v) => box == null || (lod.Vertices[v].X >= box[0] && lod.Vertices[v].X <= box[1] && lod.Vertices[v].Y >= box[2]
            && lod.Vertices[v].Y <= box[3] && lod.Vertices[v].Z >= box[4] && lod.Vertices[v].Z <= box[5]);
        var faces = lod.Polygons.Faces;
        var own = sections.SelectMany(s => OdolGraph.FacesInSection(lod, lod.Sections[s])).Where(f => faces[f].VertexIndices.All(Inside)).ToHashSet();
        if (own.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: no faces of '{selection}' match");
        }

        var verts = own.SelectMany(f => faces[f].VertexIndices).Distinct().ToArray();
        var shared = Enumerable.Range(0, faces.Length).Where(f => !own.Contains(f)).SelectMany(f => faces[f].VertexIndices).Intersect(verts).Count();
        if (shared > 0)
        {
            throw new PatchException($"{context.AssetPath}: {shared} vertices of '{selection}' are shared with other faces");
        }

        var refs = OdolGraph.BoneRefs(lod);
        if (verts.Any(v => OdolGraph.BoneCount(refs[v]) != 0))
        {
            throw new PatchException($"{context.AssetPath}: '{selection}' already has bone weights");
        }

        var template = refs.First(r => OdolGraph.BoneCount(r) == 1);
        foreach (var v in verts)
        {
            var r = OdolGraph.Clone(template);
            var data = (byte[])LibraryAccess.Get<byte[]>(template, "Data").Clone();
            data[0] = (byte)sub;
            LibraryAccess.Set(r, "Data", data);
            refs[v] = r;
        }

        OdolGraph.SetBoneRefs(lod, refs);
        foreach (var sec in sections.Select(s => lod.Sections[s]))
        {
            if (sub < sec.MinBoneIndex || sub >= sec.MinBoneIndex + sec.BonesCount)
            {
                LibraryAccess.Set(sec, nameof(Section.MinBoneIndex), Math.Max(0, sub - sec.BonesCount + 1));
            }
        }
    }
}

public sealed class RemapProxiesOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        if (!op.TryGetProperty("rules", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            throw new PatchException($"{context.AssetPath}: remap-proxies needs a non-empty \"rules\" array");
        }

        var rules = list.EnumerateArray().Select(r => (From: ModelPath(OpJson.String(r, "model")), FromId: OpJson.Int(r, "id", -1),
            To: ModelPath(OpJson.String(r, "toModel")), ToId: OpJson.Int(r, "toId", -1))).ToList();
        var used = new HashSet<(string, int)>();
        foreach (var lod in odol.Lods)
        {
            foreach (var p in OdolGraph.RawProxies(lod))
            {
                var model = LibraryAccess.Get<string>(p, "ProxyModel").ToLowerInvariant();
                var id = LibraryAccess.Get<int>(p, "SequenceID");
                var rule = rules.FirstOrDefault(r => r.From == model && r.FromId == id);
                if (rule.From == null)
                {
                    continue;
                }

                LibraryAccess.Set(p, "ProxyModel", rule.To);
                LibraryAccess.Set(p, "SequenceID", rule.ToId);
                var ns = lod.NamedSelections[LibraryAccess.Get<int>(p, "NamedSelectionIndex")];
                LibraryAccess.Set(ns, nameof(NamedSelection.Name), $"proxy:{rule.To}.{rule.ToId:000}");
                used.Add((rule.From, rule.FromId));
            }
        }

        var unused = rules.Where(r => !used.Contains((r.From, r.FromId))).Select(r => $"{r.From}|{r.FromId}").ToList();
        if (unused.Count > 0)
        {
            throw new PatchException($"{context.AssetPath}: no proxy matches {string.Join(", ", unused)}");
        }
    }

    private static string ModelPath(string p) => p.Replace('/', '\\').ToLowerInvariant();
}

public sealed class RetextureOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var from = OpJson.String(op, "from");
        var to = OpJson.String(op, "to");
        var changed = 0;
        foreach (var lod in odol.Lods)
        {
            var textures = lod.Textures.Select(t => t.Equals(from, StringComparison.OrdinalIgnoreCase) ? to : t).ToArray();
            changed += textures.Where((t, i) => t != lod.Textures[i]).Count();
            LibraryAccess.Set(lod, nameof(LOD.Textures), textures);
        }

        if (changed == 0)
        {
            throw new PatchException($"{context.AssetPath}: no LOD uses texture '{from}'");
        }
    }
}
