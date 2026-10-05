using UksfPatcher.Ops;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public class FileOpTests
{
    [Fact]
    public void TextReplaceReplacesTheExpectedNumberOfMatches()
    {
        var result = new TextReplaceOp().Apply(TestData.Ascii("a=1; b=1;"), TestData.Op("""{"op":"text-replace","find":"=1","replace":"=2","count":2}"""), TestData.Context());

        Assert.Equal("a=2; b=2;", System.Text.Encoding.Latin1.GetString(result));
    }

    [Fact]
    public void TextReplaceFailsWhenTheMatchCountDiffers()
    {
        var e = Assert.Throws<PatchException>(() =>
            new TextReplaceOp().Apply(TestData.Ascii("a=1; b=1;"), TestData.Op("""{"op":"text-replace","find":"=1","replace":"=2"}"""), TestData.Context()));

        Assert.Contains("expected 1 match(es)", e.Message);
        Assert.Contains("found 2", e.Message);
    }

    [Fact]
    public void TextReplaceRefusesRapifiedConfigs()
    {
        byte[] rapified = [0, (byte)'r', (byte)'a', (byte)'P', 1, 2];

        Assert.Throws<PatchException>(() =>
            new TextReplaceOp().Apply(rapified, TestData.Op("""{"op":"text-replace","find":"x","replace":"y"}"""), TestData.Context()));
    }

    [Fact]
    public void ReplaceFileReadsTheSourceNextToTheManifest()
    {
        var dir = TestData.TempDir();
        File.WriteAllBytes(Path.Join(dir, "new.bin"), [9, 8, 7]);

        var result = new ReplaceFileOp().Apply([1], TestData.Op("""{"op":"replace-file","source":"new.bin"}"""), TestData.Context(dir));

        Assert.Equal([9, 8, 7], result);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void UnknownOpsFail()
    {
        var e = Assert.Throws<PatchException>(() => OpRegistry.Apply([1], TestData.Op("""{"op":"nope"}"""), TestData.Context()));

        Assert.Contains("unknown op 'nope'", e.Message);
    }

    [Fact]
    public void RtmStripBonesRemovesTheBonesFromTheTableAndEveryFrame()
    {
        var rtm = File.ReadAllBytes(TestData.File_("AH64_pilot.rtm"));
        var before = RtmStripBonesOp.Parse(rtm, TestData.Context());
        var strip = before.Bones.Take(2).ToArray();

        var result = new RtmStripBonesOp().Apply(rtm, TestData.Op($$"""{"op":"rtm-strip-bones","bones":["{{strip[0]}}","{{strip[1]}}"]}"""), TestData.Context());
        var after = RtmStripBonesOp.Parse(result, TestData.Context());

        Assert.Equal(before.Bones.Skip(2), after.Bones);
        Assert.Equal(before.Frames, after.Frames);
        Assert.Equal(rtm.Length - before.Frames * 2 * 80 - 2 * 32, result.Length);
        var bisRtm = new BIS.RTM.RTM(new MemoryStream(result));
        Assert.Equal(after.Bones.Count, bisRtm.BoneNames.Length);
    }

    [Fact]
    public void RtmStripBonesFailsForAMissingBone()
    {
        var rtm = File.ReadAllBytes(TestData.File_("AH64_pilot.rtm"));

        var e = Assert.Throws<PatchException>(() =>
            new RtmStripBonesOp().Apply(rtm, TestData.Op("""{"op":"rtm-strip-bones","bones":["no_such_bone"]}"""), TestData.Context()));

        Assert.Contains("no_such_bone", e.Message);
    }
}
