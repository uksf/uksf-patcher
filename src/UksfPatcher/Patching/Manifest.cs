using System.Security.Cryptography;
using System.Text.Json;

namespace UksfPatcher.Patching;

public sealed class PatchException(string message) : Exception(message);

public sealed record AssetPatch(string Path, string Sha256, IReadOnlyList<JsonElement> Ops);

public sealed class Manifest
{
    public const int FormatVersion = 1;

    private Manifest(string file, string pbo, string pristineSha256, IReadOnlyList<AssetPatch> assets, string patchSetHash)
    {
        File = file;
        Pbo = pbo;
        PristineSha256 = pristineSha256;
        Assets = assets;
        PatchSetHash = patchSetHash;
    }

    public string File { get; }
    public string Directory => System.IO.Path.GetDirectoryName(File)!;
    public string Pbo { get; }
    public string PristineSha256 { get; }
    public IReadOnlyList<AssetPatch> Assets { get; }
    public string PatchSetHash { get; }

    public static IReadOnlyList<Manifest> LoadAll(string patchesDir)
    {
        if (!System.IO.Directory.Exists(patchesDir))
        {
            throw new PatchException($"patches directory '{patchesDir}' does not exist");
        }

        var manifests = System.IO.Directory.GetFiles(patchesDir, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(Load)
            .ToList();
        var duplicate = manifests.GroupBy(m => m.Pbo, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new PatchException($"more than one manifest targets {duplicate.Key}");
        }

        return manifests;
    }

    public static Manifest Load(string file)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            throw new PatchException($"{file}: invalid JSON: {e.Message}");
        }

        var root = doc.RootElement;
        var version = root.TryGetProperty("format", out var f) ? f.GetInt32() : 0;
        if (version != FormatVersion)
        {
            throw new PatchException($"{file}: \"format\" must be {FormatVersion}");
        }

        var pbo = RequiredString(root, "pbo", file);
        var pristineSha = RequiredString(root, "pristineSha256", file).ToLowerInvariant();
        if (pristineSha.Length != 64 || !pristineSha.All(Uri.IsHexDigit))
        {
            throw new PatchException($"{file}: \"pristineSha256\" must be the 64-digit sha256 of the pristine PBO (uksf-patcher hash <pbo>)");
        }
        var assets = new List<AssetPatch>();
        foreach (var a in Required(root, "assets", file).EnumerateArray())
        {
            var path = RequiredString(a, "path", file);
            var sha = RequiredString(a, "sha256", file).ToLowerInvariant();
            var ops = Required(a, "ops", file).EnumerateArray().Select(o => o.Clone()).ToList();
            if (ops.Count == 0)
            {
                throw new PatchException($"{file}: {path} has no ops");
            }

            assets.Add(new AssetPatch(path, sha, ops));
        }

        if (assets.Count == 0)
        {
            throw new PatchException($"{file}: no assets");
        }

        var manifest = new Manifest(file, pbo, pristineSha, assets, "");
        return new Manifest(file, pbo, pristineSha, assets, manifest.ComputePatchSetHash());
    }

    private string ComputePatchSetHash()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(Ops.OpRegistry.OutputRevisionForcingRebuild));
        hash.AppendData(System.IO.File.ReadAllBytes(File));
        foreach (var source in Assets.SelectMany(a => a.Ops).SelectMany(Ops.OpRegistry.ReferencedFiles).Distinct())
        {
            var path = System.IO.Path.Join(Directory, source);
            if (!System.IO.File.Exists(path))
            {
                throw new PatchException($"{File}: referenced file '{source}' does not exist");
            }

            hash.AppendData(System.IO.File.ReadAllBytes(path));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static JsonElement Required(JsonElement e, string name, string file) =>
        e.TryGetProperty(name, out var v) ? v : throw new PatchException($"{file}: missing \"{name}\"");

    private static string RequiredString(JsonElement e, string name, string file)
    {
        var v = Required(e, name, file);
        return v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new PatchException($"{file}: \"{name}\" must be a string");
    }
}
