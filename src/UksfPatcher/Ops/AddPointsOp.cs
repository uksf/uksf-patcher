using System.Numerics;
using System.Text.Json;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddPointsOp : OdolOp
{
    private static readonly Dictionary<string, string> Creatable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hitpoints"] = "Hitpoints",
    };

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lodName = OpJson.String(op, "lod");
        var name = OpJson.String(op, "name");
        var positions = Positions(op, context);
        var expected = OpJson.Int(op, "points");
        if (positions.Count != expected)
        {
            throw new PatchException($"{context.AssetPath}: '{name}' has {positions.Count} points, the patch expects {expected}");
        }

        var lod = FindOrCreate(odol, lodName, context);
        if (lod.Polygons.Faces.Length > 0)
        {
            throw new PatchException($"{context.AssetPath}: add-points refuses {lodName}, which has faces");
        }

        if (lod.NamedSelections.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: {lodName} already has selection '{name}'");
        }

        var refs = BoneRefs(lod);
        var donor = lod.Vertices.Count > 0 ? 0 : -1;
        var added = new List<int>();
        foreach (var p in positions)
        {
            var boneRef = OdolGraph.EmptyBoneRef();
            var clip = donor < 0 ? 63 : lod.Clip[donor];
            var normal = donor < 0 ? new Vector3PCompressed(0) : lod.NormalsCompressed[donor];
            added.Add(MemoryLod.AppendVertex(lod, new Vector3(p[0], p[1], p[2]), clip, normal, boneRef, refs));
        }

        var bone = op.TryGetProperty("bone", out var boneEl) && boneEl.ValueKind == JsonValueKind.String ? boneEl.GetString() : null;
        if (!string.IsNullOrEmpty(bone))
        {
            Bind(odol, lod, refs, added, bone, context);
        }

        OdolGraph.SetBoneRefs(lod, refs);
        lod.NamedSelections = [.. lod.NamedSelections, MemoryLod.NewSelection(name, [], added.ToArray())];
    }

    private static LOD FindOrCreate(ODOL odol, string lodName, OpContext context)
    {
        var resolution = LodNames.Resolution(lodName);
        var existing = odol.Lods.SingleOrDefault(l => LodNames.Matches(l.Resolution, resolution));
        if (existing != null)
        {
            return existing;
        }

        if (!Creatable.TryGetValue(lodName, out var indexField))
        {
            throw new PatchException($"{context.AssetPath}: no {lodName} LOD");
        }

        var memory = odol.Lods.SingleOrDefault(l => LodNames.Matches(l.Resolution, LodNames.Resolution("memory")))
            ?? throw new PatchException($"{context.AssetPath}: model has no memory LOD to clone as {lodName}");
        var lod = LodInsert.InsertClone(odol, memory, resolution, indexField, context.AssetPath);
        lod.NamedSelections = [];
        return lod;
    }

    private static void Bind(ODOL odol, LOD lod, List<object> refs, List<int> vertices, string bone, OpContext context)
    {
        var names = odol.ModelInfo.Skeleton.SkeletonBoneNames.Select(b => b.BoneName).ToArray();
        var sub = Array.FindIndex(lod.SubSkeletonsToSkeleton, b => names[b].Equals(bone, StringComparison.OrdinalIgnoreCase));
        if (sub < 0)
        {
            throw new PatchException($"{context.AssetPath}: this LOD's skeleton has no bone '{bone}'");
        }

        foreach (var v in vertices)
        {
            var data = new byte[8];
            data[0] = (byte)sub;
            data[1] = 255;
            LibraryAccess.Set(refs[v], "Count", 1);
            LibraryAccess.Set(refs[v], "Data", data);
        }
    }

    private static List<object> BoneRefs(LOD lod)
    {
        var refs = OdolGraph.BoneRefs(lod);
        if (refs.Count == lod.Vertices.Count)
        {
            return refs;
        }

        if (refs.Count != 0)
        {
            throw new PatchException("add-points: bone-ref count does not match the vertex count");
        }

        return Enumerable.Range(0, lod.Vertices.Count).Select(_ => OdolGraph.EmptyBoneRef()).ToList();
    }

    private static List<float[]> Positions(JsonElement op, OpContext context)
    {
        if (!op.TryGetProperty("positions", out var raw) || raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() == 0)
        {
            throw new PatchException($"{context.AssetPath}: add-points needs a non-empty positions array");
        }

        var points = new List<float[]>();
        foreach (var item in raw.EnumerateArray())
        {
            var p = item.EnumerateArray().Select(v => v.GetSingle()).ToArray();
            if (p.Length != 3)
            {
                throw new PatchException($"{context.AssetPath}: each position needs x, y, z");
            }

            points.Add(p);
        }

        return points;
    }
}
