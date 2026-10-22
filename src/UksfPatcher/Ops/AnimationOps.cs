using System.Runtime.CompilerServices;
using System.Text.Json;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class RebindVerticesOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var section = OpJson.Int(op, "section", -1);
        var box = OpJson.Floats(op, "box", 6);
        var expected = OpJson.Int(op, "vertices", -1);
        if (section < 0 || section >= lod.Sections.Length)
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no section {section}");
        }

        var names = odol.ModelInfo.Skeleton.SkeletonBoneNames.Select(b => b.BoneName).ToArray();
        var from = SubBone(lod, names, OpJson.String(op, "from"), add: false, context);
        var to = SubBone(lod, names, OpJson.String(op, "to"), add: true, context);

        var faces = lod.Polygons.Faces;
        var own = OdolGraph.FacesInSection(lod, lod.Sections[section]).ToHashSet();
        var verts = own.SelectMany(f => faces[f].VertexIndices).Distinct().Where(v => Inside(lod.Vertices[v], box)).ToArray();
        if (verts.Length != expected)
        {
            throw new PatchException($"{context.AssetPath}: the box holds {verts.Length} vertices of section {section}, the patch expects {expected}");
        }

        var shared = Enumerable.Range(0, faces.Length).Where(f => !own.Contains(f)).SelectMany(f => faces[f].VertexIndices).Intersect(verts).Count();
        if (shared > 0)
        {
            throw new PatchException($"{context.AssetPath}: {shared} of the vertices are shared with faces outside section {section}");
        }

        var refs = OdolGraph.BoneRefs(lod);
        foreach (var v in verts)
        {
            var data = LibraryAccess.Get<byte[]>(refs[v], "Data");
            if (OdolGraph.BoneCount(refs[v]) != 1 || data[0] != from)
            {
                throw new PatchException($"{context.AssetPath}: vertex {v} is not weighted to '{OpJson.String(op, "from")}' alone");
            }

            var r = OdolGraph.Clone(refs[v]);
            var copy = (byte[])data.Clone();
            copy[0] = (byte)to;
            LibraryAccess.Set(r, "Data", copy);
            refs[v] = r;
        }

        OdolGraph.SetBoneRefs(lod, refs);
        BindSelectionOp.FitBoneWindow(lod, lod.Sections[section], refs, context);
    }

    private static bool Inside(Vector3P p, float[] box) =>
        p.X >= box[0] && p.X <= box[1] && p.Y >= box[2] && p.Y <= box[3] && p.Z >= box[4] && p.Z <= box[5];

    private static int SubBone(LOD lod, string[] names, string bone, bool add, OpContext context)
    {
        var skel = Array.FindIndex(names, n => n.Equals(bone, StringComparison.OrdinalIgnoreCase));
        if (skel < 0)
        {
            throw new PatchException($"{context.AssetPath}: skeleton has no bone '{bone}'");
        }

        var sets = LibraryAccess.Get<Array>(lod, "SkeletonToSubSkeleton");
        var set = sets.GetValue(skel)!;
        var subs = LibraryAccess.Get<int[]>(set, "SubSkeletons");
        if (subs.Length > 0)
        {
            return subs[0];
        }

        if (!add)
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no bone '{bone}'");
        }

        var sub = lod.SubSkeletonsToSkeleton.Length;
        if (sub > byte.MaxValue)
        {
            throw new PatchException($"{context.AssetPath}: bone '{bone}' would get sub-skeleton index {sub}, which a vertex weight cannot hold");
        }

        LibraryAccess.Set(lod, nameof(LOD.SubSkeletonsToSkeleton), lod.SubSkeletonsToSkeleton.Append(skel).ToArray());
        LibraryAccess.Set(set, "SubSkeletons", new[] { sub });
        return sub;
    }
}

public sealed class AddTranslationOp : OdolOp
{
    private const uint Translation = 4;
    private static readonly string[] Addresses = ["clamp", "mirror", "loop"];

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "animation");
        var bone = OpJson.String(op, "bone");
        var axis = OpJson.Floats(op, "axis", 3);
        var address = Array.IndexOf(Addresses, (OpJson.OptionalString(op, "address") ?? "clamp").ToLowerInvariant());
        if (address < 0)
        {
            throw new PatchException($"{context.AssetPath}: address must be one of {string.Join(", ", Addresses)}");
        }

        var animations = odol.Animations ?? throw new PatchException($"{context.AssetPath}: model has no animations");
        if (animations.AnimationClasses.Any(a => a.AnimName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: model already has an animation '{name}'");
        }

        var skel = Array.FindIndex(odol.ModelInfo.Skeleton.SkeletonBoneNames, b => b.BoneName.Equals(bone, StringComparison.OrdinalIgnoreCase));
        if (skel < 0)
        {
            throw new PatchException($"{context.AssetPath}: skeleton has no bone '{bone}'");
        }

        var minValue = OpJson.Float(op, "minValue");
        var maxValue = OpJson.Float(op, "maxValue");
        var anim = (AnimationClass)RuntimeHelpers.GetUninitializedObject(typeof(AnimationClass));
        LibraryAccess.Set(anim, nameof(AnimationClass.AnimType), Translation);
        LibraryAccess.Set(anim, nameof(AnimationClass.AnimName), name);
        LibraryAccess.Set(anim, nameof(AnimationClass.AnimSource), OpJson.String(op, "animationSource"));
        LibraryAccess.Set(anim, nameof(AnimationClass.MinPhase), minValue);
        LibraryAccess.Set(anim, nameof(AnimationClass.MaxPhase), maxValue);
        LibraryAccess.Set(anim, nameof(AnimationClass.MinValue), minValue);
        LibraryAccess.Set(anim, nameof(AnimationClass.MaxValue), maxValue);
        LibraryAccess.Set(anim, nameof(AnimationClass.SourceAddress), (uint)address);
        LibraryAccess.Set(anim, nameof(AnimationClass.Offset0), OpJson.Float(op, "offset0"));
        LibraryAccess.Set(anim, nameof(AnimationClass.Offset1), OpJson.Float(op, "offset1"));

        var index = animations.AnimationClasses.Length;
        LibraryAccess.Set(animations, nameof(Animations.AnimationClasses), animations.AnimationClasses.Append(anim).ToArray());
        for (var j = 0; j < animations.Bones2Anims.Length; j++)
        {
            animations.Bones2Anims[j][skel] = animations.Bones2Anims[j][skel].Append((uint)index).ToArray();
            animations.Anims2Bones[j] = animations.Anims2Bones[j].Append(skel).ToArray();
            animations.AxisData[j] = animations.AxisData[j].Append([new Vector3P(0, 0, 0), new Vector3P(axis[0], axis[1], axis[2])]).ToArray();
        }
    }
}
