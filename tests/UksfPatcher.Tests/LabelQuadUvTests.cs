using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class LabelQuadUvTests : IDisposable
{
    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private byte[] Apply(string json) => OpRegistry.Apply(Models.Load(Models.EawsPbo, Models.EawsFuselage), TestData.Op(json), TestData.Context(_dir));

    [Fact]
    public void CustomUvIsWrittenOntoTheNewQuad()
    {
        var after = OdolFile.Parse(Apply("""
            {"op":"add-label-quads","lods":["0"],"texture":"#(argb,8,8,1)color(0,0,0,0)","templateSection":0,
             "labels":[{"selection":"name","quads":[{"normal":[-1,0,0],"uv":[[0,0],[1,0],[1,0.222222],[0,0.222222]],
             "corners":[[-0.478,0.83,-3.95],[-0.478,0.83,-4.35],[-0.478,0.78,-4.35],[-0.478,0.78,-3.95]]}]}]}
            """), "typhoon");
        var lod = after.Lods.Single(l => LodNames.Matches(l.Resolution, 0));
        var uv = lod.UvSets[0].GetUV();
        var n = uv.Length;

        Assert.Equal(0f, uv[n - 4].X, 3);
        Assert.Equal(0f, uv[n - 4].Y, 3);
        Assert.Equal(1f, uv[n - 3].X, 3);
        Assert.Equal(0f, uv[n - 3].Y, 3);
        Assert.Equal(1f, uv[n - 2].X, 3);
        Assert.Equal(0.222f, uv[n - 2].Y, 3);
        Assert.Equal(0f, uv[n - 1].X, 3);
        Assert.Equal(0.222f, uv[n - 1].Y, 3);
    }

    [Fact]
    public void OmittedUvFillsTheTexture()
    {
        var after = OdolFile.Parse(Apply("""
            {"op":"add-label-quads","lods":["0"],"texture":"#(argb,8,8,1)color(0,0,0,0)","templateSection":0,
             "labels":[{"selection":"name","quads":[{"normal":[-1,0,0],
             "corners":[[-0.478,0.83,-3.95],[-0.478,0.83,-4.35],[-0.478,0.78,-4.35],[-0.478,0.78,-3.95]]}]}]}
            """), "typhoon");
        var uv = after.Lods.Single(l => LodNames.Matches(l.Resolution, 0)).UvSets[0].GetUV();

        Assert.Equal(1f, uv[^2].X, 3);
        Assert.Equal(1f, uv[^2].Y, 3);
        Assert.Equal(0f, uv[^1].X, 3);
        Assert.Equal(1f, uv[^1].Y, 3);
    }

    [Fact]
    public void AShortUvListIsRefused()
    {
        var error = Assert.Throws<PatchException>(() => Apply("""
            {"op":"add-label-quads","lods":["0"],"texture":"#(argb,8,8,1)color(0,0,0,0)","templateSection":0,
             "labels":[{"selection":"name","quads":[{"normal":[-1,0,0],"uv":[[0,0],[1,0]],
             "corners":[[-0.478,0.83,-3.95],[-0.478,0.83,-4.35],[-0.478,0.78,-4.35],[-0.478,0.78,-3.95]]}]}]}
            """));

        Assert.Contains("4 finite", error.Message);
    }
}
