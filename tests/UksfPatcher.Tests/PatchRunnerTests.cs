using UksfPatcher.Cli;

namespace UksfPatcher.Tests;

public sealed class PatchRunnerTests : IDisposable
{
    private const string PboName = RunnerSandbox.PboName;
    private const string Rvmat = RunnerSandbox.Rvmat;

    private readonly RunnerSandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void ApplyPatchesOnceAndSkipsTheSecondRun()
    {
        _box.WriteManifest(_box.RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class Stage1 \", \"count\": 1");

        var first = _box.Run("apply");
        var patched = File.ReadAllBytes(_box.PboPath);
        var stamp = File.GetLastWriteTimeUtc(_box.PboPath);
        var second = _box.Run("apply");

        Assert.Contains("patched (first patch)", first.Output);
        Assert.Contains("up to date", second.Output);
        Assert.Equal(0, second.Code);
        Assert.Equal(patched, File.ReadAllBytes(_box.PboPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_box.PboPath));
        Assert.Contains("class Stage1 ", _box.RvmatText());
        Assert.DoesNotContain("class Stage1  ", _box.RvmatText());
        Assert.Equal(0, _box.Run("verify").Code);
    }

    [Fact]
    public void ChangedPatchSetIsAppliedToThePristineBackupNotOnTopOfTheOldPatch()
    {
        _box.WriteManifest(_box.RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");
        _box.Run("apply");
        _box.WriteManifest(_box.RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageY\", \"count\": 1");

        var result = _box.Run("apply");

        Assert.Contains("patch set changed", result.Output);
        Assert.Contains("class StageY", _box.RvmatText());
        Assert.DoesNotContain("class StageX", _box.RvmatText());
    }

    [Fact]
    public void PinMismatchFailsLoudlyAndWritesNothing()
    {
        var wrong = new string('0', 64);
        _box.WriteManifest(wrong, "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");

        var result = _box.Run("apply");

        Assert.Equal(1, result.Code);
        Assert.Contains(PboName, result.Error);
        Assert.Contains(Rvmat, result.Error);
        Assert.Contains($"expected sha256 {wrong}", result.Error);
        Assert.Contains($"actual {_box.RvmatSha()}", result.Error);
        Assert.Equal(_box.Pristine, File.ReadAllBytes(_box.PboPath));
        Assert.False(Directory.Exists(_box.State));
        Assert.Empty(Directory.GetFiles(_box.Addons, "*.tmp"));
    }

    [Fact]
    public void DryRunAndVerifyChangeNothing()
    {
        _box.WriteManifest(_box.RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");

        var dry = _box.Run("apply", "--dry-run");
        var verify = _box.Run("verify");

        Assert.Contains("would patch", dry.Output);
        Assert.Equal(1, verify.Code);
        Assert.Contains("NOT APPLIED", verify.Output);
        Assert.Equal(_box.Pristine, File.ReadAllBytes(_box.PboPath));
    }

    [Fact]
    public void RemovingTheManifestRestoresThePristinePbo()
    {
        _box.WriteManifest(_box.RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");
        _box.Run("apply");
        File.Delete(_box.ManifestPath);

        var result = _box.Run("apply");

        Assert.Contains("restored pristine", result.Output);
        Assert.Equal(_box.Pristine, File.ReadAllBytes(_box.PboPath));
        Assert.Empty(Directory.GetFiles(_box.State));
    }

    [Fact]
    public void StateInsideTheModFolderIsRejected()
    {
        _box.WriteManifest(_box.RvmatSha(), "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1");
        var output = new StringWriter();
        var error = new StringWriter();

        var code = CommandLine.Run(["apply", "--addons", _box.Addons, "--patches", _box.Patches, "--state", Path.Join(_box.Addons, "..", "state")], output, error);

        Assert.Equal(1, code);
        Assert.Contains("--state must be outside", error.ToString());
    }
}
