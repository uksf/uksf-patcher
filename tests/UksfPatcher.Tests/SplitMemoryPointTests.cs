using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class SplitMemoryPointTests : IDisposable
{
    private const string Split = """{"op":"split-memory-point","name":"muzzle_3","position":[0.860060871,0.0414807796,-2.22018695],"boneFrom":"chamber_3"}""";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static LOD Memory(byte[] model) => OdolFile.Parse(model, "rhicc").Lods.Single(l => LodNames.Matches(l.Resolution, 1e15f));

    private static int Point(LOD mem, string name) => mem.NamedSelections.Single(n => n.Name == name).SelectedVertices.Single();

    [Fact]
    public void SplitsTheRhiccLeftMuzzleOffTheHiddenLightVertexAndBindsItLikeTheChamber()
    {
        var model = Models.Load(Models.RhiccPbo, Models.Rhicc);
        var before = Memory(model);
        var after = Memory(Apply(model, Split));

        var muzzle = Point(after, "muzzle_3");
        var old = Point(before, "muzzle_3");
        var chamber = Point(after, "chamber_3");
        Assert.Equal(before.Vertices.Count, muzzle);
        Assert.Equal((0.860060871f, 0.0414807796f, -2.22018695f), (after.Vertices[muzzle].X, after.Vertices[muzzle].Y, after.Vertices[muzzle].Z));
        Assert.Contains(old, after.NamedSelections.Single(n => n.Name == "axis_minigunl_barrels").SelectedVertices);
        Assert.Equal(before.Vertices[old].X, after.Vertices[old].X);
        var refs = OdolGraph.BoneRefs(after);
        Assert.Equal(OdolGraph.BoneCount(refs[chamber]), OdolGraph.BoneCount(refs[muzzle]));
        Assert.Equal(after.Clip[chamber], after.Clip[muzzle]);
    }

    [Fact]
    public void RefusesAPointThatIsNotShared()
    {
        var once = Apply(Models.Load(Models.RhiccPbo, Models.Rhicc), Split);

        var e = Assert.Throws<PatchException>(() => Apply(once, Split));

        Assert.Contains("use set-memory-point", e.Message);
    }
}
