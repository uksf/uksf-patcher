using System.Text.Json.Nodes;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Tests;

public sealed class PatchSafetyTests : IDisposable
{
    private const string ReplaceX = "\"find\": \"class Stage1\", \"replace\": \"class StageX\", \"count\": 1";
    private const string ReplaceY = "\"find\": \"class Stage1\", \"replace\": \"class StageY\", \"count\": 1";

    private readonly RunnerSandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void CrashBeforeReplacingAnInstalledPatchSetIsFinishedFromTheBackupOnRerun()
    {
        var installedA = InstallAThenCrashWhileInstallingB();

        var rerun = _box.Run("apply");

        Assert.True(rerun.Code == 0, rerun.Error);
        Assert.NotEqual(installedA, File.ReadAllBytes(_box.PboPath));
        Assert.Contains("class StageY", _box.RvmatText());
        Assert.DoesNotContain("class StageX", _box.RvmatText());
        Assert.Contains("up to date", _box.Run("apply").Output);
        Assert.Equal(0, _box.Run("verify").Code);
    }

    [Fact]
    public void CrashBeforeReplacingAnInstalledPatchSetThenRemovingTheManifestRestoresPristine()
    {
        InstallAThenCrashWhileInstallingB();
        File.Delete(_box.ManifestPath);

        var result = _box.Run("apply");

        Assert.True(result.Code == 0, result.Error);
        Assert.Contains("restored pristine", result.Output);
        Assert.Equal(_box.Pristine, File.ReadAllBytes(_box.PboPath));
    }

    [Fact]
    public void CrashDuringTheFirstApplyIsRetriedOnRerun()
    {
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        Assert.Throws<SimulatedCrash>(() => Crashing().Apply(dryRun: false));
        Assert.Equal(_box.Pristine, File.ReadAllBytes(_box.PboPath));

        var rerun = _box.Run("apply");

        Assert.True(rerun.Code == 0, rerun.Error);
        Assert.Contains("class StageX", _box.RvmatText());
        Assert.Equal(0, _box.Run("verify").Code);
    }

    [Fact]
    public void RemovedManifestWithAForeignPboKeepsThePristineBackup()
    {
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        _box.Run("apply");
        File.WriteAllBytes(_box.PboPath, WithOtherEntryChanged(_box.Pristine));
        File.Delete(_box.ManifestPath);

        var result = _box.Run("apply");

        Assert.Equal(0, result.Code);
        Assert.Equal(_box.Pristine, File.ReadAllBytes(new StateStore(_box.State).BackupPath(RunnerSandbox.PboName)));
    }

    [Fact]
    public void SourcePboWithABadTrailerIsRejected()
    {
        var damaged = (byte[])_box.Pristine.Clone();
        damaged[OtherEntry(damaged).DataOffset] ^= 0xff;
        File.WriteAllBytes(_box.PboPath, damaged);
        _box.WriteManifest(_box.RvmatSha(), ReplaceX, StateStore.Sha256(damaged));

        var result = _box.Run("apply");

        Assert.Equal(1, result.Code);
        Assert.Contains(RunnerSandbox.PboName, result.Error);
        Assert.Contains("SHA-1 trailer", result.Error);
        Assert.Equal(damaged, File.ReadAllBytes(_box.PboPath));
    }

    [Fact]
    public void BackupWithABadTrailerIsRejectedByVerify()
    {
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        _box.Run("apply");
        var store = new StateStore(_box.State);
        var damaged = File.ReadAllBytes(store.BackupPath(RunnerSandbox.PboName));
        damaged[OtherEntry(damaged).DataOffset] ^= 0xff;
        File.WriteAllBytes(store.BackupPath(RunnerSandbox.PboName), damaged);
        var state = JsonNode.Parse(File.ReadAllText(store.StatePath(RunnerSandbox.PboName)))!;
        state["PristineSha256"] = StateStore.Sha256(damaged);
        File.WriteAllText(store.StatePath(RunnerSandbox.PboName), state.ToJsonString());
        var manifest = JsonNode.Parse(File.ReadAllText(_box.ManifestPath))!;
        manifest["pristineSha256"] = StateStore.Sha256(damaged);
        File.WriteAllText(_box.ManifestPath, manifest.ToJsonString());

        var verify = _box.Run("verify");

        Assert.Equal(1, verify.Code);
        Assert.Contains("SHA-1 trailer", verify.Error);
        Assert.Contains(store.BackupPath(RunnerSandbox.PboName), verify.Error);
    }

    [Fact]
    public void UpstreamChangeOutsideThePinnedAssetsFailsApplyAndDryRun()
    {
        var upstream = WithOtherEntryChanged(_box.Pristine);
        File.WriteAllBytes(_box.PboPath, upstream);
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        var expected = StateStore.Sha256(_box.Pristine);
        var actual = StateStore.Sha256(upstream);

        foreach (var result in new[] { _box.Run("apply"), _box.Run("apply", "--dry-run"), _box.Run("verify") })
        {
            Assert.Equal(1, result.Code);
            Assert.Contains(RunnerSandbox.PboName, result.Error);
            Assert.Contains($"expected sha256 {expected}", result.Error);
            Assert.Contains($"actual {actual}", result.Error);
        }

        Assert.Equal(upstream, File.ReadAllBytes(_box.PboPath));
    }

    [Fact]
    public void BackupThatNoLongerMatchesTheManifestPinFailsVerify()
    {
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        _box.Run("apply");
        var wrong = new string('1', 64);
        var manifest = JsonNode.Parse(File.ReadAllText(_box.ManifestPath))!;
        manifest["pristineSha256"] = wrong;
        File.WriteAllText(_box.ManifestPath, manifest.ToJsonString());

        var verify = _box.Run("verify");

        Assert.Equal(1, verify.Code);
        Assert.Contains($"expected sha256 {wrong}", verify.Error);
        Assert.Contains($"actual {StateStore.Sha256(_box.Pristine)}", verify.Error);
    }

    [Fact]
    public void ManifestWithoutAPristinePinIsRejected()
    {
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        var manifest = JsonNode.Parse(File.ReadAllText(_box.ManifestPath))!.AsObject();
        manifest.Remove("pristineSha256");
        File.WriteAllText(_box.ManifestPath, manifest.ToJsonString());

        var result = _box.Run("apply");

        Assert.Equal(1, result.Code);
        Assert.Contains("pristineSha256", result.Error);
    }

    [Fact]
    public void PendingOutputIsCommittedOnlyWhenItIsInstalled()
    {
        var state = new PatchState("x.pbo", "p", "setA", "a") { PendingPatchSetSha256 = "setB", PendingPatchedSha256 = "b" };

        Assert.Equal(new PatchState("x.pbo", "p", "setB", "b"), state.Settled("b"));
        Assert.Equal(new PatchState("x.pbo", "p", "setA", "a"), state.Settled("a"));
        Assert.Equal(state, state.Settled("foreign"));
        Assert.True(state.Produced("a") && state.Produced("b") && !state.Produced("p"));
    }

    private byte[] InstallAThenCrashWhileInstallingB()
    {
        _box.WriteManifest(_box.RvmatSha(), ReplaceX);
        Assert.Equal(0, _box.Run("apply").Code);
        var installedA = File.ReadAllBytes(_box.PboPath);
        _box.WriteManifest(_box.RvmatSha(), ReplaceY);

        Assert.Throws<SimulatedCrash>(() => Crashing().Apply(dryRun: false));

        Assert.Equal(installedA, File.ReadAllBytes(_box.PboPath));
        return installedA;
    }

    private PatchRunner Crashing() => new(_box.Addons, _box.Patches, _box.State, TextWriter.Null)
    {
        BeforeReplace = _ => throw new SimulatedCrash()
    };

    private static PboEntry OtherEntry(byte[] pbo) =>
        RawPbo.Parse(pbo).Entries.First(e => e.DataSize > 0 && !e.Name.Equals(RunnerSandbox.Rvmat, StringComparison.OrdinalIgnoreCase));

    private static byte[] WithOtherEntryChanged(byte[] pboBytes)
    {
        var pbo = RawPbo.Parse(pboBytes);
        var entry = OtherEntry(pboBytes);
        return pbo.Rebuild(new Dictionary<int, byte[]> { [entry.Index] = [.. pbo.ReadData(entry), 0x20] });
    }

    private sealed class SimulatedCrash : Exception;
}
