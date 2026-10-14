using UksfPatcher.Ops;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class TyphoonOpRejectTests : IDisposable
{
    private const string UvSplit = """{"op":"uv-split","lod":"0","source":"plan.json","selection":"zbytek","newSelection":"formation_on","texture":"u\\x\\b.paa"}""";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Reject(string op, string? plan = null)
    {
        if (plan != null)
        {
            File.WriteAllText(Path.Join(_dir, "plan.json"), plan);
        }

        var model = Models.Load(Models.EawsPbo, Models.EawsFuselage);
        return Assert.Throws<PatchException>(() => OpRegistry.Apply(model, TestData.Op(op), TestData.Context(_dir))).Message;
    }

    [Fact]
    public void NonFiniteNumbersAreRefused()
    {
        Assert.Contains("finite", Reject("""{"op":"add-memory-point","name":"x","position":[1e100,0,0]}"""));
        Assert.Contains("mass", Reject("""{"op":"add-physx","mass":1e-40}"""));
    }

    [Fact]
    public void DuplicateProxyRulesAreRefused()
    {
        var message = Reject("""{"op":"remap-proxies","rules":[{"model":"\\a3\\weapons_f\\ammo\\bomb_01_f","id":1,"toModel":"\\a","toId":1},{"model":"\\a3\\weapons_f\\ammo\\bomb_01_f","id":1,"toModel":"\\b","toId":1}]}""");

        Assert.Contains("2 rules", message);
    }

    [Theory]
    [InlineData("""{"islands":[]}""", "non-empty")]
    [InlineData("""{"islands":[{"sheet":"C","box":[0,0],"pos":[0,0],"verts":[0]}]}""", "sheet")]
    [InlineData("""{"islands":[{"sheet":"B","uv":[[0,0],[1,1]],"verts":[0]}]}""", "\"uv\"")]
    [InlineData("""{"islands":[{"sheet":"B","uv":[[0,0,0]],"verts":[0]}]}""", "\"uv\"")]
    [InlineData("""{"islands":[{"sheet":"B","s":0,"box":[0,0],"pos":[0,0],"verts":[0]}]}""", "positive")]
    [InlineData("""{"islands":[{"sheet":"B","box":[0,0],"pos":[0,0],"verts":[99999999]}]}""", "inside the LOD")]
    public void MalformedUvPlansAreRefused(string plan, string expected)
    {
        Assert.Contains(expected, Reject(UvSplit, plan));
    }

    [Fact]
    public void UvSplitRefusesAnExistingSelectionName()
    {
        var message = Reject(UvSplit.Replace("formation_on", "camo1"), """{"islands":[{"sheet":"B","box":[0,0],"pos":[0,0],"verts":[0]}]}""");

        Assert.Contains("already has selection 'camo1'", message);
    }

    [Fact]
    public void UvSplitRefusesSectionsOnAnotherTexture()
    {
        var model = TyphoonModel.Lod0();
        var foreign = model.Sections.Select((s, i) => (s, i)).First(x => x.s.TextureIndex >= 0 && model.Textures[x.s.TextureIndex].EndsWith("chute.paa")).i;
        var vertex = TyphoonModel.FirstVertex(model, foreign);

        var message = Reject(UvSplit, $$"""{"islands":[{"sheet":"B","box":[0,0],"pos":[0,0],"verts":[{{vertex}}]}]}""");

        Assert.Contains($"section {foreign}", message);
    }

    [Fact]
    public void UvSplitRefusesAPlanThatLeavesSheetBEmpty()
    {
        var model = TyphoonModel.Lod0();
        var section = model.NamedSelections.Single(n => n.Name == "zbytek").Sections.First();
        var verts = TyphoonModel.SectionVertices(model, section);

        var message = Reject(UvSplit, $$"""{"islands":[{"sheet":"A","box":[0,0],"pos":[0,0],"verts":[{{string.Join(',', verts)}}]}]}""");

        Assert.Contains("no faces on sheet B", message);
    }
}
