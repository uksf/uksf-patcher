using UksfPatcher.Ops;
using UksfPatcher.P3d;

namespace UksfPatcher.Tests;

public sealed class L118AmmoTests
{
    private const string RemoveCasing =
        "{\"op\":\"remove-faces\",\"lod\":\"1\",\"axis\":[0,0,0,0,-1,0],\"wholeParts\":true,\"allBeyond\":-0.04,\"anyBeyond\":-0.04,\"faces\":124}";

    [Fact]
    public void RemovesTheWholeCasingAndKeepsTheOverlappingShell()
    {
        var round = Models.Load(Models.MortarsPbo, Models.RoundHe);

        var patched = OpRegistry.Apply(round, TestData.Op(RemoveCasing), TestData.Context(TestData.File_("")));

        var lod = OdolFile.Parse(patched, "round").Lods.Single(l => l.Resolution == 1);
        var used = lod.Polygons.Faces.SelectMany(f => f.VertexIndices).Select(v => lod.Vertices[v].Vector3.Y).ToList();
        Assert.Equal(540, lod.Polygons.Faces.Length);
        Assert.Equal(-0.043f, used.Min(), 0.001f);
        Assert.Equal(0.457f, used.Max(), 0.001f);
    }
}
