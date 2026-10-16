using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class TranslateSelectionTests : IDisposable
{
    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Fuselage() => Models.Load(Models.EawsPbo, Models.EawsFuselage);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static LOD Lod0(byte[] model) => OdolFile.Parse(model, "typhoon").Lods.Single(l => LodNames.Matches(l.Resolution, 0));

    [Theory]
    [InlineData("burner_fire_1_left", 12)]
    [InlineData("#46", 104)]
    public void MovesOnlyTheSelectionByTheOffset(string selection, int expectedMoved)
    {
        var before = Lod0(Fuselage());
        var after = Lod0(Apply(Fuselage(), $$"""{"op":"translate-selection","lod":"0","selection":"{{selection}}","offset":[0,0,-1.44]}"""));

        var moved = Enumerable.Range(0, before.Vertices.Count).Where(v => before.Vertices[v].Z != after.Vertices[v].Z).ToList();
        Assert.Equal(expectedMoved, moved.Count);
        Assert.All(moved, v => Assert.Equal(before.Vertices[v].Z - 1.44f, after.Vertices[v].Z, 0.0001f));
        Assert.All(moved, v => Assert.Equal((before.Vertices[v].X, before.Vertices[v].Y), (after.Vertices[v].X, after.Vertices[v].Y)));
    }

    [Fact]
    public void RefusesASelectionThatSharesVertices()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Fuselage(), """{"op":"translate-selection","lod":"0","selection":"zbytek","offset":[0,0,1]}"""));

        Assert.Contains("shared with other faces", e.Message);
    }

    [Fact]
    public void RefusesASectionThatDoesNotExist()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Fuselage(), """{"op":"translate-selection","lod":"0","selection":"#9999","offset":[0,0,1]}"""));

        Assert.Contains("no section 9999", e.Message);
    }
}
