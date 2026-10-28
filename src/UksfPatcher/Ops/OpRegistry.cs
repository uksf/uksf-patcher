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
        ["set-skeleton-name"] = new SetSkeletonNameOp(),
        ["strip-animations"] = new StripAnimationsOp(),
        ["remove-component"] = new RemoveComponentOp(),
        ["convex-hull-component"] = new ConvexHullComponentOp(),
        ["remove-degenerate-faces"] = new RemoveDegenerateFacesOp(),
        ["set-animation-source"] = new SetAnimationSourceOp(),
        ["bind-selection"] = new BindSelectionOp(),
        ["translate-selection"] = new TranslateSelectionOp(),
        ["rebind-vertices"] = new RebindVerticesOp(),
        ["add-translation"] = new AddTranslationOp(),
        ["remap-proxies"] = new RemapProxiesOp(),
        ["add-memory-proxies"] = new AddMemoryProxiesOp(),
        ["restore-lod-proxies"] = new RestoreLodProxiesOp(),
        ["add-memory-point"] = new AddMemoryPointOp(),
        ["set-memory-point"] = new SetMemoryPointOp(),
        ["split-memory-point"] = new SplitMemoryPointOp(),
        ["cut-seams"] = new CutSeamsOp(),
        ["uv-split"] = new UvSplitOp(),
        ["move-sections"] = new MoveSectionsOp(),
        ["retexture"] = new RetextureOp(),
        ["set-section-texture"] = new SetSectionTextureOp(),
        ["add-physx"] = new AddPhysxOp(),
        ["stretch-along-axis"] = new StretchAlongAxisOp(),
        ["remove-faces"] = new RemoveFacesOp(),
        ["add-mesh"] = new AddMeshOp(),
        ["rebuild-shadow-volume"] = new RebuildShadowVolumeOp(),
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

    public static string? OptionalString(JsonElement op, string name) =>
        op.TryGetProperty(name, out _) ? String(op, name) : null;

    public static float Float(JsonElement op, string name) =>
        op.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && float.IsFinite(v.GetSingle())
            ? v.GetSingle()
            : throw new PatchException($"op is missing a finite number \"{name}\": {op}");

    public static float[] Floats(JsonElement op, string name, int count) =>
        op.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == count
            && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number && float.IsFinite(x.GetSingle()))
            ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray()
            : throw new PatchException($"op needs \"{name}\" as an array of {count} finite numbers: {op}");

    public static JsonElement SourceJson(JsonElement op, OpContext context)
    {
        var source = String(op, "source");
        var path = Path.Join(context.ManifestDirectory, source);
        if (!File.Exists(path))
        {
            throw new PatchException($"{context.AssetPath}: source '{source}' does not exist");
        }

        return JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.Clone();
    }

    public static IReadOnlyList<string> Strings(JsonElement op, string name) =>
        op.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString()!).ToList()
            : throw new PatchException($"op is missing string array \"{name}\": {op}");
}
