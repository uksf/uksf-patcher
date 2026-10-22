using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class SetSectionTextureTests : IDisposable
{
    private const string Clear = "#(argb,8,8,3)color(0,0,0,0,ca)";
    private const string Mfd2 = "eaws_ef2000\\data\\cockpit\\mfd2_ca.paa";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Fuselage() => Models.Load(Models.EawsPbo, Models.EawsFuselage);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static LOD ViewPilot(byte[] model) => OdolFile.Parse(model, "typhoon").Lods.Single(l => LodNames.Name(l.Resolution) == "viewPilot");

    private static string[] SectionTextures(LOD lod) => lod.Sections.Select(s => s.TextureIndex < 0 ? "" : lod.Textures[s.TextureIndex]).ToArray();

    [Fact]
    public void RetexturesOnlyTheNamedSection()
    {
        var before = SectionTextures(ViewPilot(Fuselage()));
        var after = SectionTextures(ViewPilot(Apply(Fuselage(), $$"""{"op":"set-section-texture","lod":"viewPilot","section":47,"texture":"{{Clear.Replace("\\", "\\\\")}}"}""")));

        Assert.Equal(Mfd2, before[47], ignoreCase: true);
        Assert.Equal(Clear, after[47]);
        var changed = Enumerable.Range(0, before.Length).Where(s => before[s] != after[s]).ToList();
        Assert.Equal([47], changed);
        Assert.Contains(after, t => t.Equals(Mfd2, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RefusesASectionThatDoesNotExist()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Fuselage(), """{"op":"set-section-texture","lod":"viewPilot","section":9999,"texture":"x.paa"}"""));

        Assert.Contains("no section 9999", e.Message);
    }
}
