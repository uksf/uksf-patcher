using System.Runtime.CompilerServices;
using System.Text.Json;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddHideOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "animation");
        var bone = OpJson.String(op, "bone");
        var animations = odol.Animations ?? throw new PatchException($"{context.AssetPath}: model has no animations");
        if (animations.AnimationClasses.Any(a => a.AnimName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new PatchException($"{context.AssetPath}: model already has an animation '{name}'");
        var skel = Array.FindIndex(odol.ModelInfo.Skeleton.SkeletonBoneNames, b => b.BoneName.Equals(bone, StringComparison.OrdinalIgnoreCase));
        if (skel < 0)
            throw new PatchException($"{context.AssetPath}: skeleton has no bone '{bone}'");

        var anim = (AnimationClass)RuntimeHelpers.GetUninitializedObject(typeof(AnimationClass));
        LibraryAccess.Set(anim, nameof(AnimationClass.AnimType), 9u);
        LibraryAccess.Set(anim, nameof(AnimationClass.AnimName), name);
        LibraryAccess.Set(anim, nameof(AnimationClass.AnimSource), OpJson.String(op, "animationSource"));
        LibraryAccess.Set(anim, nameof(AnimationClass.MinPhase), 0f);
        LibraryAccess.Set(anim, nameof(AnimationClass.MaxPhase), 1f);
        LibraryAccess.Set(anim, nameof(AnimationClass.MinValue), 0f);
        LibraryAccess.Set(anim, nameof(AnimationClass.MaxValue), 1f);
        LibraryAccess.Set(anim, nameof(AnimationClass.HideValue), 0.5f);
        LibraryAccess.Set(anim, nameof(AnimationClass.Unused55), -1f);
        var index = animations.AnimationClasses.Length;
        LibraryAccess.Set(animations, nameof(Animations.AnimationClasses), animations.AnimationClasses.Append(anim).ToArray());
        for (var j = 0; j < animations.Bones2Anims.Length; j++)
        {
            animations.Bones2Anims[j][skel] = animations.Bones2Anims[j][skel].Append((uint)index).ToArray();
            animations.Anims2Bones[j] = animations.Anims2Bones[j].Append(skel).ToArray();
            animations.AxisData[j] = animations.AxisData[j].Append([new Vector3P(0, 0, 0), new Vector3P(0, 0, 0)]).ToArray();
        }
    }
}
