namespace UksfPatcher.Patching;

public sealed class PatchRunner(string addons, string patches, string stateDir, TextWriter output)
{
    private readonly StateStore _store = new(stateDir);

    public Action<string>? BeforeReplace { get; init; }

    public int Apply(bool dryRun)
    {
        var manifests = Manifest.LoadAll(patches);
        foreach (var manifest in manifests)
        {
            ApplyOne(manifest, dryRun);
        }

        foreach (var orphan in _store.LoadAll().Where(s => manifests.All(m => !Same(m.Pbo, s.Pbo))).ToList())
        {
            RestoreOrphan(orphan, dryRun);
        }

        return 0;
    }

    public int Verify()
    {
        var manifests = Manifest.LoadAll(patches);
        var problems = 0;
        foreach (var manifest in manifests)
        {
            var current = ReadPbo(manifest.Pbo);
            var sha = StateStore.Sha256(current);
            var state = _store.Load(manifest.Pbo)?.Settled(sha);
            var pristine = ResolvePristine(manifest, current, sha, state).Bytes;
            if (state == null || sha != state.PatchedSha256 || state.PatchSetSha256 != manifest.PatchSetHash)
            {
                output.WriteLine($"{manifest.Pbo}: NOT APPLIED (run apply)");
                problems++;
                continue;
            }

            var rebuilt = StateStore.Sha256(PatchEngine.Patch(pristine, manifest));
            if (rebuilt != sha)
            {
                output.WriteLine($"{manifest.Pbo}: MISMATCH: re-applying the patch gives {rebuilt}, the PBO is {sha}");
                problems++;
                continue;
            }

            output.WriteLine($"{manifest.Pbo}: ok ({sha})");
        }

        foreach (var orphan in _store.LoadAll().Where(s => manifests.All(m => !Same(m.Pbo, s.Pbo))))
        {
            output.WriteLine($"{orphan.Pbo}: patched but no manifest (run apply to restore pristine)");
            problems++;
        }

        return problems == 0 ? 0 : 1;
    }

    private void ApplyOne(Manifest manifest, bool dryRun)
    {
        var path = PboPath(manifest.Pbo);
        var current = ReadPbo(manifest.Pbo);
        var currentSha = StateStore.Sha256(current);
        var stored = _store.Load(manifest.Pbo);
        var state = stored?.Settled(currentSha);
        if (state != null && currentSha == state.PatchedSha256 && state.PatchSetSha256 == manifest.PatchSetHash)
        {
            if (!dryRun && state != stored)
            {
                _store.SaveState(state);
            }

            output.WriteLine($"{manifest.Pbo}: up to date");
            return;
        }

        var (pristine, reason) = ResolvePristine(manifest, current, currentSha, state);
        var patched = PatchEngine.Patch(pristine, manifest);
        var patchedSha = StateStore.Sha256(patched);
        if (dryRun)
        {
            output.WriteLine($"{manifest.Pbo}: would patch ({reason}) -> {patchedSha}");
            return;
        }

        var installedIsOurs = state != null && currentSha == state.PatchedSha256;
        var pending = new PatchState(manifest.Pbo, StateStore.Sha256(pristine),
            installedIsOurs ? state!.PatchSetSha256 : null, installedIsOurs ? state!.PatchedSha256 : null)
        {
            PendingPatchSetSha256 = manifest.PatchSetHash,
            PendingPatchedSha256 = patchedSha
        };
        _store.Save(pending, pristine);
        BeforeReplace?.Invoke(path);
        AtomicFile.Write(path, patched);
        _store.SaveState(pending.Settled(patchedSha));
        output.WriteLine($"{manifest.Pbo}: patched ({reason}) -> {patchedSha}");
    }

    private (byte[] Bytes, string Reason) ResolvePristine(Manifest manifest, byte[] current, string currentSha, PatchState? state)
    {
        byte[] pristine;
        string reason;
        string source;
        if (state != null && currentSha != state.PristineSha256 && state.Produced(currentSha))
        {
            pristine = ReadBackup(state);
            reason = "patch set changed";
            source = $"the backup {_store.BackupPath(state.Pbo)}";
        }
        else
        {
            pristine = current;
            reason = state == null ? "first patch" : currentSha == state.PristineSha256 ? "PBO is pristine" : "upstream PBO changed";
            source = PboPath(manifest.Pbo);
        }

        var actual = StateStore.Sha256(pristine);
        if (actual != manifest.PristineSha256)
        {
            throw new PatchException(
                $"{manifest.Pbo}: the pristine PBO ({source}) does not match its pin: expected sha256 {manifest.PristineSha256}, actual {actual}. Upstream changed the PBO; review and re-pin the patch.");
        }

        return (pristine, reason);
    }

    private void RestoreOrphan(PatchState state, bool dryRun)
    {
        var path = PboPath(state.Pbo);
        var currentSha = File.Exists(path) ? StateStore.Sha256(File.ReadAllBytes(path)) : null;
        if (currentSha == state.PristineSha256)
        {
            output.WriteLine($"{state.Pbo}: manifest removed and the PBO is pristine; forgetting its state");
            if (!dryRun)
            {
                _store.Forget(state.Pbo);
            }

            return;
        }

        if (currentSha == null || !state.Produced(currentSha))
        {
            output.WriteLine($"{state.Pbo}: manifest removed and the PBO is not ours any more; forgetting its state, keeping the pristine backup {_store.BackupPath(state.Pbo)}");
            if (!dryRun)
            {
                _store.ForgetState(state.Pbo);
            }

            return;
        }

        var backup = ReadBackup(state);
        output.WriteLine($"{state.Pbo}: manifest removed; {(dryRun ? "would restore" : "restored")} pristine");
        if (!dryRun)
        {
            AtomicFile.Write(path, backup);
            _store.Forget(state.Pbo);
        }
    }

    private byte[] ReadBackup(PatchState state)
    {
        var backupPath = _store.BackupPath(state.Pbo);
        var backup = _store.ReadBackup(state)
            ?? throw new PatchException($"{state.Pbo}: the PBO is patched but its pristine backup {backupPath} is missing or corrupt");
        Pbo.RawPbo.RequireValidTrailer(backup, backupPath);
        return backup;
    }

    private string PboPath(string pbo) => Path.Join(addons, pbo);

    private byte[] ReadPbo(string pbo)
    {
        var path = PboPath(pbo);
        return File.Exists(path) ? File.ReadAllBytes(path) : throw new PatchException($"{pbo} is not in {addons}");
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
