using System.Text.Json;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed record OpContext(string ManifestDirectory, string AssetPath);

public interface IAssetOp
{
    byte[] Apply(byte[] asset, JsonElement op, OpContext context);
}

public static class OpRegistry
{
    public const int OutputRevisionForcingRebuild = 1;

    private static readonly Dictionary<string, IAssetOp> Ops = new()
    {
        ["replace-file"] = new ReplaceFileOp(),
        ["text-replace"] = new TextReplaceOp(),
        ["rtm-strip-bones"] = new RtmStripBonesOp(),
        ["set-bone-parent"] = new SetBoneParentOp(),
        ["remove-component"] = new RemoveComponentOp(),
        ["convex-hull-component"] = new ConvexHullComponentOp(),
        ["remove-degenerate-faces"] = new RemoveDegenerateFacesOp(),
    };

    public static IEnumerable<string> Names => Ops.Keys;

    public static byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "op");
        if (!Ops.TryGetValue(name, out var impl))
        {
            throw new PatchException($"{context.AssetPath}: unknown op '{name}' (known: {string.Join(", ", Ops.Keys)})");
        }

        return impl.Apply(asset, op, context);
    }

    public static IEnumerable<string> ReferencedFiles(JsonElement op) =>
        op.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? [s.GetString()!] : [];
}

public static class OpJson
{
    public static string String(JsonElement op, string name) =>
        op.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new PatchException($"op is missing string \"{name}\": {op}");

    public static int Int(JsonElement op, string name, int fallback) =>
        op.TryGetProperty(name, out var v) ? v.GetInt32() : fallback;

    public static IReadOnlyList<string> Strings(JsonElement op, string name) =>
        op.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString()!).ToList()
            : throw new PatchException($"op is missing string array \"{name}\": {op}");
}
