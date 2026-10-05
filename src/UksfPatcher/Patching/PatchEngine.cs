using UksfPatcher.Ops;
using UksfPatcher.Pbo;

namespace UksfPatcher.Patching;

public static class PatchEngine
{
    public static byte[] Patch(byte[] pristinePbo, Manifest manifest)
    {
        RawPbo pbo;
        try
        {
            pbo = RawPbo.Parse(pristinePbo);
        }
        catch (PboFormatException e)
        {
            throw new PatchException($"{manifest.Pbo}: cannot parse: {e.Message}");
        }

        var replacements = new Dictionary<int, byte[]>();
        foreach (var asset in manifest.Assets)
        {
            var entry = pbo.Find(asset.Path) ?? throw new PatchException($"{manifest.Pbo}: asset '{asset.Path}' is not in the PBO");
            if (replacements.ContainsKey(entry.Index))
            {
                throw new PatchException($"{manifest.Pbo}: asset '{asset.Path}' is listed twice");
            }

            var data = pbo.ReadData(entry);
            var actual = StateStore.Sha256(data);
            if (actual != asset.Sha256)
            {
                throw new PatchException(
                    $"{manifest.Pbo}: asset '{asset.Path}' does not match its pin: expected sha256 {asset.Sha256}, actual {actual}. Upstream changed the asset; review and re-pin the patch.");
            }

            var context = new OpContext(manifest.Directory, $"{manifest.Pbo}:{asset.Path}");
            foreach (var op in asset.Ops)
            {
                data = OpRegistry.Apply(data, op, context);
            }

            replacements[entry.Index] = data;
        }

        return pbo.Rebuild(replacements);
    }
}
