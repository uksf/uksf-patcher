using System.Text;
using System.Text.Json;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class ReplaceFileOp : IAssetOp
{
    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var source = OpJson.String(op, "source");
        var path = Path.Join(context.ManifestDirectory, source);
        if (!File.Exists(path))
        {
            throw new PatchException($"{context.AssetPath}: replace-file source '{source}' does not exist");
        }

        return File.ReadAllBytes(path);
    }
}

public sealed class TextReplaceOp : IAssetOp
{
    private static readonly byte[] RapifiedMagic = [0, (byte)'r', (byte)'a', (byte)'P'];

    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var find = OpJson.String(op, "find");
        var replace = OpJson.String(op, "replace");
        var expected = OpJson.Int(op, "count", 1);
        if (find.Length == 0)
        {
            throw new PatchException($"{context.AssetPath}: text-replace \"find\" is empty");
        }

        if (asset.AsSpan().StartsWith(RapifiedMagic))
        {
            throw new PatchException($"{context.AssetPath}: text-replace cannot edit a rapified (binarised) config; use replace-file");
        }

        var text = Encoding.Latin1.GetString(asset);
        var found = CountOccurrences(text, find);
        if (found != expected)
        {
            throw new PatchException($"{context.AssetPath}: text-replace expected {expected} match(es) of \"{find}\", found {found}");
        }

        return Encoding.Latin1.GetBytes(text.Replace(find, replace, StringComparison.Ordinal));
    }

    private static int CountOccurrences(string text, string find)
    {
        var count = 0;
        for (var i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + find.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
