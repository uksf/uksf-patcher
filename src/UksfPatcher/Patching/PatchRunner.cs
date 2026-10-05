namespace UksfPatcher.Patching;

public sealed class PatchRunner(string addons, string patches, string stateDir, TextWriter output)
{
    private readonly StateStore _store = new(stateDir);

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
            var state = _store.Load(manifest.Pbo);
            if (state == null || sha != state.PatchedSha256 || state.PatchSetSha256 != manifest.PatchSetHash)
            {
                output.WriteLine($"{manifest.Pbo}: NOT APPLIED (run apply)");
                problems++;
                continue;
            }

            var backup = _store.ReadBackup(state) ?? throw new PatchException($"{manifest.Pbo}: pristine backup is missing or corrupt");
            var rebuilt = StateStore.Sha256(PatchEngine.Patch(backup, manifest));
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
        var state = _store.Load(manifest.Pbo);
        if (state != null && currentSha == state.PatchedSha256 && state.PatchSetSha256 == manifest.PatchSetHash)
        {
            output.WriteLine($"{manifest.Pbo}: up to date");
            return;
        }

        var pristine = current;
        var reason = state == null ? "first patch" : "upstream PBO changed";
        if (state != null && currentSha == state.PatchedSha256)
        {
            pristine = _store.ReadBackup(state)
                ?? throw new PatchException($"{manifest.Pbo}: the PBO is patched but its pristine backup is missing or corrupt in {_store.Directory}");
            reason = "patch set changed";
        }
        else if (state != null && currentSha == state.PristineSha256)
        {
            reason = "PBO is pristine";
        }

        var patched = PatchEngine.Patch(pristine, manifest);
        var patchedSha = StateStore.Sha256(patched);
        if (dryRun)
        {
            output.WriteLine($"{manifest.Pbo}: would patch ({reason}) -> {patchedSha}");
            return;
        }

        _store.Save(new PatchState(manifest.Pbo, StateStore.Sha256(pristine), manifest.PatchSetHash, patchedSha), pristine);
        AtomicFile.Write(path, patched);
        output.WriteLine($"{manifest.Pbo}: patched ({reason}) -> {patchedSha}");
    }

    private void RestoreOrphan(PatchState state, bool dryRun)
    {
        var path = PboPath(state.Pbo);
        var currentSha = File.Exists(path) ? StateStore.Sha256(File.ReadAllBytes(path)) : null;
        if (currentSha != state.PatchedSha256)
        {
            output.WriteLine($"{state.Pbo}: manifest removed and the PBO is not ours any more; forgetting its state");
            if (!dryRun)
            {
                _store.Forget(state.Pbo);
            }

            return;
        }

        var backup = _store.ReadBackup(state)
            ?? throw new PatchException($"{state.Pbo}: manifest removed but the pristine backup is missing or corrupt");
        output.WriteLine($"{state.Pbo}: manifest removed; {(dryRun ? "would restore" : "restored")} pristine");
        if (!dryRun)
        {
            AtomicFile.Write(path, backup);
            _store.Forget(state.Pbo);
        }
    }

    private string PboPath(string pbo) => Path.Join(addons, pbo);

    private byte[] ReadPbo(string pbo)
    {
        var path = PboPath(pbo);
        return File.Exists(path) ? File.ReadAllBytes(path) : throw new PatchException($"{pbo} is not in {addons}");
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
