using System.Security.Cryptography;
using System.Text.Json;

namespace UksfPatcher.Patching;

public sealed record PatchState(string Pbo, string PristineSha256, string? PatchSetSha256, string? PatchedSha256)
{
    public string? PendingPatchSetSha256 { get; init; }
    public string? PendingPatchedSha256 { get; init; }

    public bool Produced(string sha) => sha == PatchedSha256 || sha == PendingPatchedSha256;

    public PatchState Settled(string installedSha)
    {
        if (PendingPatchedSha256 != null && installedSha == PendingPatchedSha256)
        {
            return this with { PatchSetSha256 = PendingPatchSetSha256, PatchedSha256 = PendingPatchedSha256, PendingPatchSetSha256 = null, PendingPatchedSha256 = null };
        }

        return PendingPatchedSha256 != null && installedSha == PatchedSha256
            ? this with { PendingPatchSetSha256 = null, PendingPatchedSha256 = null }
            : this;
    }
}

public sealed class StateStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Directory { get; } = directory;

    public string StatePath(string pbo) => Path.Join(Directory, pbo + ".state.json");

    public string BackupPath(string pbo) => Path.Join(Directory, pbo + ".pristine");

    public PatchState? Load(string pbo)
    {
        var path = StatePath(pbo);
        return File.Exists(path) ? JsonSerializer.Deserialize<PatchState>(File.ReadAllBytes(path)) : null;
    }

    public IEnumerable<PatchState> LoadAll() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.GetFiles(Directory, "*.state.json").Select(f => JsonSerializer.Deserialize<PatchState>(File.ReadAllBytes(f))!)
            : [];

    public void Save(PatchState state, byte[] pristine)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var backup = BackupPath(state.Pbo);
        if (!File.Exists(backup) || Sha256(File.ReadAllBytes(backup)) != state.PristineSha256)
        {
            AtomicFile.Write(backup, pristine);
        }

        SaveState(state);
    }

    public void SaveState(PatchState state)
    {
        System.IO.Directory.CreateDirectory(Directory);
        AtomicFile.Write(StatePath(state.Pbo), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
    }

    public byte[]? ReadBackup(PatchState state)
    {
        var path = BackupPath(state.Pbo);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return Sha256(bytes) == state.PristineSha256 ? bytes : null;
    }

    public void Forget(string pbo)
    {
        ForgetState(pbo);
        File.Delete(BackupPath(pbo));
    }

    public void ForgetState(string pbo) => File.Delete(StatePath(pbo));

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

public static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        var temp = path + ".uksf-patcher.tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
