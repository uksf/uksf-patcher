using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class AddHideTests
{
    [Fact]
    public void AddsHideAnimationToToeBoneAcrossLods()
    {
        var model = Models.Load("a400m.pbo", "A400M_F.p3d");
        var op = TestData.Op("""{"op":"add-hide","animation":"toe_hide","bone":"ramp_bottom2","animationSource":"toe_hide"}""");
        var after = OdolFile.Parse(OpRegistry.Apply(model, op, TestData.Context()), "a400");
        var index = Array.FindIndex(after.Animations.AnimationClasses, a => a.AnimName == "toe_hide");
        Assert.True(index >= 0);
        var animation = after.Animations.AnimationClasses[index];
        Assert.Equal((9u, "toe_hide", 0.5f, -1f), (animation.AnimType, animation.AnimSource, animation.HideValue, animation.Unused55));
        var bone = Array.FindIndex(after.ModelInfo.Skeleton.SkeletonBoneNames, b => b.BoneName.Equals("ramp_bottom2", StringComparison.OrdinalIgnoreCase));
        Assert.True(bone >= 0);
        foreach (var map in after.Animations.Bones2Anims) Assert.Contains((uint)index, map[bone]);
        Assert.Throws<PatchException>(() => OpRegistry.Apply(OpRegistry.Apply(model, op, TestData.Context()), op, TestData.Context()));
    }
}
