using System.Text.Json;
using UksfPatcher.Cli;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Tests;

public sealed class PatchRunnerTests : IDisposable
{
    private const string PboName = "Ballistic_missiles.pbo";
    private const string Rvmat = @"textures\iskander_9K720\iskander_9K720_HD.rvmat";

    private readonly string _root = TestData.TempDir();
    private readonly string _addons;
    private readonly string _patches;
    private readonly string _state;
    private readonly byte[] _pristine;

    public PatchRunnerTests()
    {
        _addons = Directory.CreateDirectory(Path.Join(_root, "mod", "addons")).FullName;
        _patches = Directory.CreateDirectory(Path.Join(_root, "patches")).FullName;
        _state = Path.Join(_root, "state");
        File.Copy(TestData.Specimen(PboName), PboPath);
        _pristine = File.ReadAllBytes(PboPath);
    }

    private string PboPath => Path.Join(_addons, PboName);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ApplyPatchesOnceAndSkipsTheSecondRun()
    {
        WriteManifest(RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class Stage1 \", \"count\": 1");

        var first = Run("apply");
        var patched = File.ReadAllBytes(PboPath);
        var stamp = File.GetLastWriteTimeUtc(PboPath);
        var second = Run("apply");

        Assert.Contains("patched (first patch)", first.Output);
        Assert.Contains("up to date", second.Output);
        Assert.Equal(0, second.Code);
        Assert.Equal(patched, File.ReadAllBytes(PboPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(PboPath));
        Assert.Contains("class Stage1 ", RvmatText());
        Assert.DoesNotContain("class Stage1  ", RvmatText());
        Assert.Equal(0, Run("verify").Code);
    }

    [Fact]
    public void ChangedPatchSetIsAppliedToThePristineBackupNotOnTopOfTheOldPatch()
    {
        WriteManifest(RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");
        Run("apply");
        WriteManifest(RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageY\", \"count\": 1");

        var result = Run("apply");

        Assert.Contains("patch set changed", result.Output);
        Assert.Contains("class StageY", RvmatText());
        Assert.DoesNotContain("class StageX", RvmatText());
    }

    [Fact]
    public void PinMismatchFailsLoudlyAndWritesNothing()
    {
        var wrong = new string('0', 64);
        WriteManifest(wrong, "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");

        var result = Run("apply");

        Assert.Equal(1, result.Code);
        Assert.Contains(PboName, result.Error);
        Assert.Contains(Rvmat, result.Error);
        Assert.Contains($"expected sha256 {wrong}", result.Error);
        Assert.Contains($"actual {RvmatSha()}", result.Error);
        Assert.Equal(_pristine, File.ReadAllBytes(PboPath));
        Assert.False(Directory.Exists(_state));
        Assert.Empty(Directory.GetFiles(_addons, "*.tmp"));
    }

    [Fact]
    public void DryRunAndVerifyChangeNothing()
    {
        WriteManifest(RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");

        var dry = Run("apply", "--dry-run");
        var verify = Run("verify");

        Assert.Contains("would patch", dry.Output);
        Assert.Equal(1, verify.Code);
        Assert.Contains("NOT APPLIED", verify.Output);
        Assert.Equal(_pristine, File.ReadAllBytes(PboPath));
    }

    [Fact]
    public void RemovingTheManifestRestoresThePristinePbo()
    {
        WriteManifest(RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");
        Run("apply");
        File.Delete(Path.Join(_patches, PboName + ".json"));

        var result = Run("apply");

        Assert.Contains("restored pristine", result.Output);
        Assert.Equal(_pristine, File.ReadAllBytes(PboPath));
        Assert.Empty(Directory.GetFiles(_state));
    }

    [Fact]
    public void StateInsideTheModFolderIsRejected()
    {
        WriteManifest(RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");
        var output = new StringWriter();
        var error = new StringWriter();

        var code = CommandLine.Run(["apply", "--addons", _addons, "--patches", _patches, "--state", Path.Join(_addons, "..", "state")], output, error);

        Assert.Equal(1, code);
        Assert.Contains("--state must be outside", error.ToString());
    }

    private (int Code, string Output, string Error) Run(params string[] extra)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string[] args = [extra[0], "--addons", _addons, "--patches", _patches, "--state", _state, .. extra.Skip(1)];
        var code = CommandLine.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string RvmatSha()
    {
        var pbo = RawPbo.Parse(_pristine);
        return StateStore.Sha256(pbo.ReadData(pbo.Find(Rvmat)!));
    }

    private string RvmatText()
    {
        var pbo = RawPbo.Read(PboPath);
        return System.Text.Encoding.Latin1.GetString(pbo.ReadData(pbo.Find(Rvmat)!));
    }

    private void WriteManifest(string sha, string opFields)
    {
        var json = $$"""
            {
              "format": 1,
              "pbo": "{{PboName}}",
              "assets": [
                { "path": {{JsonSerializer.Serialize(Rvmat)}}, "sha256": "{{sha}}", "ops": [ { "op": "text-replace", {{opFields}} } ] }
              ]
            }
            """;
        File.WriteAllText(Path.Join(_patches, PboName + ".json"), json);
    }
}
