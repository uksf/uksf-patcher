using System.Numerics;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class MoveRotationAxisTests
{
    [Fact]
    public void MovesEveryStoredAxisAndBothMemoryPointsWithoutChangingDirection()
    {
        var model = Models.Load("a400m.pbo", "A400M_F.p3d");
        var before = OdolFile.Parse(model, "a400");
        var operation = TestData.Op("""{"op":"move-rotation-axis","animation":"ramp_bottom2","selection":"ramp_bottom2_axis","offset":[0,0.08,-0.176]}""");
        var after = OdolFile.Parse(OpRegistry.Apply(model, operation, TestData.Context()), "a400");
        var index = Array.FindIndex(before.Animations.AnimationClasses, a => a.AnimName == "ramp_bottom2");
        var delta = new Vector3(0, 0.08f, -0.176f);
        Assert.True(index >= 0);
        Assert.Equal(before.Animations.AxisData.Length, after.Animations.AxisData.Length);
        for (var i = 0; i < before.Animations.AxisData.Length; i++)
        {
            Assert.Equal(before.Animations.AxisData[i][index][0].Vector3 + delta, after.Animations.AxisData[i][index][0].Vector3);
            Assert.Equal(before.Animations.AxisData[i][index][1].Vector3, after.Animations.AxisData[i][index][1].Vector3);
        }
        var oldMemory = before.Lods.Single(l => LodNames.Matches(l.Resolution, 1e15f));
        var newMemory = after.Lods.Single(l => LodNames.Matches(l.Resolution, 1e15f));
        var points = oldMemory.NamedSelections.Single(s => s.Name == "ramp_bottom2_axis").SelectedVertices;
        Assert.Equal(2, points.Count);
        foreach (var v in points) Assert.Equal(oldMemory.Vertices[v].Vector3 + delta, newMemory.Vertices[v].Vector3);
        Assert.Throws<PatchException>(() => OpRegistry.Apply(model, TestData.Op("""{"op":"move-rotation-axis","animation":"missing","selection":"ramp_bottom2_axis","offset":[0,0,0]}"""), TestData.Context()));
    }
}
