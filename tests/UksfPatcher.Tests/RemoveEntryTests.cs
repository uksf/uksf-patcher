using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Tests;

public sealed class RemoveEntryTests : IDisposable
{
    private const string CtabPbo = "ctab_core.pbo";
    private const string Compiled = @"functions\fnc_updateInterface.sqfc";
    private const string Source = @"functions\fnc_updateInterface.sqf";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RemovesTheCompiledScriptAndKeepsEveryOtherEntryByteIdentical()
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(CtabPbo));
        var before = RawPbo.Parse(pristine);

        var after = RawPbo.Parse(PatchEngine.Patch(pristine, Manifest(pristine, $$"""{ "path": "{{Escape(Compiled)}}", "sha256": "{{RunnerSandbox.EntrySha(pristine, Compiled)}}", "remove": true }""")));

        Assert.True(after.TrailerHashValid());
        Assert.Equal(before.Prefix, after.Prefix);
        Assert.Equal(before.Entries.Count - 1, after.Entries.Count);
        Assert.Null(after.Find(Compiled));
        var kept = before.Entries.Where(e => e.Name != Compiled).ToList();
        foreach (var (b, a) in kept.Zip(after.Entries))
        {
            Assert.Equal(b.NameBytes, a.NameBytes);
            Assert.Equal((b.Packing, b.OriginalSize, b.Timestamp), (a.Packing, a.OriginalSize, a.Timestamp));
            Assert.True(before.RawData(b).SequenceEqual(after.RawData(a)));
        }
    }

    [Fact]
    public void RemovesOneEntryAndEditsAnotherInTheSameManifest()
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(CtabPbo));

        var after = RawPbo.Parse(PatchEngine.Patch(pristine, Manifest(pristine,
            $$"""{ "path": "{{Escape(Compiled)}}", "sha256": "{{RunnerSandbox.EntrySha(pristine, Compiled)}}", "remove": true }""",
            $$"""{ "path": "{{Escape(Source)}}", "sha256": "{{RunnerSandbox.EntrySha(pristine, Source)}}", "ops": [ { "op": "text-replace", "find": "disableSerialization;", "replace": "disableSerialization; // patched" } ] }""")));

        Assert.Null(after.Find(Compiled));
        Assert.Contains("disableSerialization; // patched", System.Text.Encoding.Latin1.GetString(after.ReadData(after.Find(Source)!)));
    }

    [Fact]
    public void RefusesARemovedEntryWhoseBytesChanged()
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(CtabPbo));

        var e = Assert.Throws<PatchException>(() => PatchEngine.Patch(pristine, Manifest(pristine, $$"""{ "path": "{{Escape(Compiled)}}", "sha256": "{{new string('0', 64)}}", "remove": true }""")));

        Assert.Contains("does not match its pin", e.Message);
    }

    [Fact]
    public void RefusesARemovedEntryWithOps()
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(CtabPbo));

        var e = Assert.Throws<PatchException>(() => Manifest(pristine, $$"""{ "path": "{{Escape(Compiled)}}", "sha256": "{{RunnerSandbox.EntrySha(pristine, Compiled)}}", "remove": true, "ops": [] }"""));

        Assert.Contains("takes no", e.Message);
    }

    private static string Escape(string path) => path.Replace(@"\", @"\\");

    private Manifest Manifest(byte[] pristine, params string[] assets)
    {
        var file = Path.Join(_dir, CtabPbo + ".json");
        File.WriteAllText(file, $$"""
            { "format": 1, "pbo": "{{CtabPbo}}", "pristineSha256": "{{StateStore.Sha256(pristine)}}", "assets": [ {{string.Join(", ", assets)}} ] }
            """);
        return Patching.Manifest.Load(file);
    }
}
