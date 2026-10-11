using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public sealed class KitModelTests(ITestOutputHelper log) : IDisposable
{
    private const string KitPath = @"z\ewdj\addons\ew_modular\data\ewdj_generator_kit.p3d";
    private const string StripSwitch = """{"op":"strip-animations","sources":["switch_source"]}""";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void AddedEntryIsReadableAndEveryOtherEntryIsUnchanged()
    {
        var original = File.ReadAllBytes(TestData.Specimen(Models.EwdjPbo));
        var pbo = RawPbo.Parse(original);
        var data = TestData.Ascii("added entry");

        var rebuilt = RawPbo.Parse(pbo.Rebuild(new Dictionary<int, byte[]>(), [new PboAddition(pbo.EntryName(KitPath), 1234, data)]));

        Assert.True(rebuilt.TrailerHashValid());
        Assert.Equal(pbo.Prefix, rebuilt.Prefix);
        Assert.Equal(pbo.Entries.Count + 1, rebuilt.Entries.Count);
        foreach (var (before, after) in pbo.Entries.Zip(rebuilt.Entries))
        {
            Assert.Equal(before.NameBytes, after.NameBytes);
            Assert.True(pbo.RawData(before).SequenceEqual(rebuilt.RawData(after)));
        }

        var added = rebuilt.Find(KitPath)!;
        Assert.Equal(@"data\ewdj_generator_kit.p3d", added.Name);
        Assert.Equal(1234u, added.Timestamp);
        Assert.Equal(data, rebuilt.ReadData(added));
    }

    [Fact]
    public void StripAnimationsOnAV74ModelMatchesTheLibrary()
    {
        var bytes = Models.Load(Models.RhiccPbo, Models.Rhicc);
        var before = OdolFile.Parse(bytes, "rhicc");
        var sources = before.Animations.AnimationClasses.Select(a => a.AnimSource).Distinct().ToList();
        var op = $$"""{"op":"strip-animations","sources":{{System.Text.Json.JsonSerializer.Serialize(sources)}}}""";

        var output = new StripAnimationsOp().Apply(bytes, TestData.Op(op), TestData.Context());
        var after = OdolFile.Parse(output, "rhicc");

        log.WriteLine($"rhicc: {before.Animations.AnimationClasses.Length} animations on [{string.Join(", ", sources)}], {bytes.Length} -> {output.Length} bytes");
        Assert.Null(after.Animations);
        Assert.Equal(before.Lods.Select(l => (l.Resolution, l.NamedSelections.Length)), after.Lods.Select(l => (l.Resolution, l.NamedSelections.Length)));
        Assert.Equal(before.ModelInfo.Skeleton.SkeletonName, after.ModelInfo.Skeleton.SkeletonName);
        OdolFile.RequireRoundTrip(output, "rhicc");
    }

    [Fact]
    public void StripAnimationsRemovesTheV75SwitchBlockOnce()
    {
        var bytes = Models.Load(Models.EwdjPbo, Models.EwdjGenerator);

        var output = new StripAnimationsOp().Apply(bytes, TestData.Op(StripSwitch), TestData.Context());
        var again = Assert.Throws<PatchException>(() => new StripAnimationsOp().Apply(output, TestData.Op(StripSwitch), TestData.Context()));

        uint[] addresses = [647660, 195421, 18700, 8224, 4613, 3897, 3453, 2737, 696, 5342, 1399969, 647660, 195421, 18700, 5342, 4613, 3897, 3453, 2737, 8224];
        Assert.Equal(bytes.Length - 427, output.Length);
        Assert.Equal(bytes.AsSpan(0, 505).ToArray(), output.AsSpan(0, 505).ToArray());
        Assert.Equal(0, output[505]);
        Assert.Equal(addresses, Enumerable.Range(0, 20).Select(i => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(506 + 4 * i))));
        Assert.Equal(bytes.AsSpan(933 + 80).ToArray(), output.AsSpan(506 + 80).ToArray());
        Assert.Contains("has no animations", again.Message);
    }

    [Fact]
    public void StripAnimationsIgnoresAnAnimationBlockOutsideTheAnimationSection()
    {
        var bytes = Models.Load(Models.EwdjPbo, Models.EwdjGenerator);
        var stripped = new StripAnimationsOp().Apply(bytes, TestData.Op(StripSwitch), TestData.Context());
        var planted = stripped.Concat(bytes.AsSpan(505, 933 + 80 - 505).ToArray()).ToArray();

        var e = Assert.Throws<PatchException>(() => new StripAnimationsOp().Apply(planted, TestData.Op(StripSwitch), TestData.Context()));

        Assert.Contains("has no animations", e.Message);
    }

    [Theory]
    [InlineData(653, 2, -1, 16)]
    [InlineData(657, 1, 0x40000000, 4)]
    public void StripAnimationsRefusesAnInvalidBoneTableCount(int offset, int original, int count, int payload)
    {
        var model = Models.Load(Models.EwdjPbo, Models.EwdjGenerator);
        Assert.Equal(original, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(model.AsSpan(offset)));
        var bytes = model[..(offset + 4)].Concat(model[(offset + 4 + payload)..]).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), count);

        var e = Assert.Throws<PatchException>(() => new StripAnimationsOp().Apply(bytes, TestData.Op(StripSwitch), TestData.Context()));

        Assert.Contains("the animation block at offset 505 is malformed", e.Message);
    }

    [Fact]
    public void StripAnimationsMatchesSourcesWithoutCase()
    {
        var bytes = Models.Load(Models.EwdjPbo, Models.EwdjGenerator);

        var output = new StripAnimationsOp().Apply(bytes, TestData.Op("""{"op":"strip-animations","sources":["Switch_SOURCE"]}"""), TestData.Context());

        Assert.Equal(bytes.Length - 427, output.Length);
    }

    [Fact]
    public void StripAnimationsRefusesADifferentSourceSet()
    {
        var bytes = Models.Load(Models.EwdjPbo, Models.EwdjGenerator);

        var e = Assert.Throws<PatchException>(() => new StripAnimationsOp().Apply(bytes, TestData.Op("""{"op":"strip-animations","sources":["switch_source","door"]}"""), TestData.Context()));

        Assert.Contains("sources are [switch_source], not [door, switch_source]", e.Message);
    }

    [Fact]
    public void CopyOfAddsAPatchedCopyAndLeavesTheSourceUntouched()
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(Models.EwdjPbo));

        var patched = RawPbo.Parse(PatchEngine.Patch(pristine, Manifest(KitPath)));
        var source = RawPbo.Parse(pristine);

        Assert.True(patched.TrailerHashValid());
        Assert.Equal(source.ReadData(source.Find(Models.EwdjGenerator)!), patched.ReadData(patched.Find(Models.EwdjGenerator)!));
        Assert.Equal(new StripAnimationsOp().Apply(source.ReadData(source.Find(Models.EwdjGenerator)!), TestData.Op(StripSwitch), TestData.Context()), patched.ReadData(patched.Find(KitPath)!));
    }

    [Fact]
    public void CopyOfRefusesToOverwriteAnExistingEntry()
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(Models.EwdjPbo));

        var e = Assert.Throws<PatchException>(() => PatchEngine.Patch(pristine, Manifest(@"z\ewdj\addons\ew_modular\data\ewdj_generator_ghost.p3d")));

        Assert.Contains("already exists", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("data\\kit\u0000.p3d")]
    [InlineData("data\\kit\u0141.p3d")]
    [InlineData(@"data\\kit.p3d")]
    public void CopyOfRefusesATargetThatIsNotAValidEntryName(string target)
    {
        var pristine = File.ReadAllBytes(TestData.Specimen(Models.EwdjPbo));

        var e = Assert.Throws<PatchException>(() => PatchEngine.Patch(pristine, Manifest(target)));

        Assert.Contains("copy target is not a valid PBO entry name", e.Message);
    }

    [Fact]
    public void AnAmbiguousCopyTargetNamesThePboAndTheAsset()
    {
        var original = RawPbo.Parse(File.ReadAllBytes(TestData.Specimen(Models.EwdjPbo)));
        var name = original.EntryName(KitPath);
        var twice = original.Rebuild(new Dictionary<int, byte[]>(), [new PboAddition(name, 1, [1]), new PboAddition(name, 1, [2])]);

        var e = Assert.Throws<PatchException>(() => PatchEngine.Patch(twice, Manifest(KitPath, twice)));

        Assert.Contains($"{Models.EwdjPbo}:{KitPath}: cannot parse: 2 entries are named", e.Message);
    }

    private Manifest Manifest(string target, byte[]? pristine = null)
    {
        pristine ??= File.ReadAllBytes(TestData.Specimen(Models.EwdjPbo));
        var sha = RunnerSandbox.EntrySha(pristine, Models.EwdjGenerator);
        var file = Path.Join(_dir, Models.EwdjPbo + ".json");
        File.WriteAllText(file, $$"""
            {
              "format": 1,
              "pbo": "{{Models.EwdjPbo}}",
              "pristineSha256": "{{StateStore.Sha256(pristine)}}",
              "assets": [
                { "path": {{System.Text.Json.JsonSerializer.Serialize(target)}}, "copyOf": {{System.Text.Json.JsonSerializer.Serialize(Models.EwdjGenerator)}}, "sha256": "{{sha}}", "ops": [ {{StripSwitch}} ] }
              ]
            }
            """);
        return Patching.Manifest.Load(file);
    }
}
