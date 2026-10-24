using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public sealed record AxialFrame(Vector3 Origin, Vector3 Direction)
{
    public static AxialFrame From(JsonElement op, OpContext context)
    {
        var a = OpJson.Floats(op, "axis", 6);
        var direction = new Vector3(a[3], a[4], a[5]);
        if (direction.Length() < 1e-6f)
        {
            throw new PatchException($"{context.AssetPath}: axis direction is zero");
        }

        return new AxialFrame(new Vector3(a[0], a[1], a[2]), Vector3.Normalize(direction));
    }

    public float Along(Vector3 p) => Vector3.Dot(p - Origin, Direction);

    public float Along(Vector3P p) => Along(p.Vector3);

    public float Radius(Vector3 p)
    {
        var rel = p - Origin;
        return (rel - Along(p) * Direction).Length();
    }
}

public static class BoneWeights
{
    public static Func<int, bool> BoundOnlyTo(ODOL odol, LOD lod, IReadOnlyCollection<string> bones, OpContext context)
    {
        var names = odol.ModelInfo.Skeleton.SkeletonBoneNames.Select(b => b.BoneName).ToArray();
        foreach (var bone in bones)
        {
            if (!names.Contains(bone, StringComparer.OrdinalIgnoreCase))
            {
                throw new PatchException($"{context.AssetPath}: skeleton has no bone '{bone}'");
            }
        }

        var wanted = lod.SubSkeletonsToSkeleton.Select(s => bones.Contains(names[s], StringComparer.OrdinalIgnoreCase)).ToArray();
        var refs = OdolGraph.BoneRefs(lod);
        if (refs.Count != lod.Vertices.Count)
        {
            return _ => false;
        }

        return v =>
        {
            var count = OdolGraph.BoneCount(refs[v]);
            var data = LibraryAccess.Get<byte[]>(refs[v], "Data");
            return count > 0 && Enumerable.Range(0, count).All(k => wanted[data[2 * k]]);
        };
    }
}

public static class OdolBounds
{
    public static void Grow(ODOL odol, LOD lod)
    {
        if (lod.Vertices.Count == 0)
        {
            return;
        }

        var points = lod.Vertices.Select(v => v.Vector3).ToArray();
        var min = Vector3.Min(lod.BMin.Vector3, points.Aggregate(Vector3.Min));
        var max = Vector3.Max(lod.BMax.Vector3, points.Aggregate(Vector3.Max));
        var radius = Math.Max(lod.BRadius, points.Max(p => (p - lod.BCenter.Vector3).Length()));
        LibraryAccess.Set(lod, nameof(LOD.BMin), new Vector3P(min));
        LibraryAccess.Set(lod, nameof(LOD.BMax), new Vector3P(max));
        LibraryAccess.Set(lod, nameof(LOD.BRadius), radius);

        var info = odol.ModelInfo;
        LibraryAccess.Set(info, nameof(ModelInfo.BoundingSphere), Math.Max(info.BoundingSphere, points.Max(p => p.Length())));
        if (lod.Resolution < LodNames.Resolution("geometry"))
        {
            LibraryAccess.Set(info, nameof(ModelInfo.BboxMinVisual), new Vector3P(Vector3.Min(info.BboxMinVisual.Vector3, min)));
            LibraryAccess.Set(info, nameof(ModelInfo.BboxMaxVisual), new Vector3P(Vector3.Max(info.BboxMaxVisual.Vector3, max)));
        }

        LibraryAccess.Set(info, nameof(ModelInfo.BboxMin), new Vector3P(Vector3.Min(info.BboxMin.Vector3, min)));
        LibraryAccess.Set(info, nameof(ModelInfo.BboxMax), new Vector3P(Vector3.Max(info.BboxMax.Vector3, max)));
    }
}
