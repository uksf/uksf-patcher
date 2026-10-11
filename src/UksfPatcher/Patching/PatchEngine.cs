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

        pbo.RequireValidTrailer(manifest.Pbo);

        var replacements = new Dictionary<int, byte[]>();
        var additions = new List<PboAddition>();
        foreach (var asset in manifest.Assets)
        {
            var sourcePath = asset.CopyOf ?? asset.Path;
            var entry = pbo.Find(sourcePath) ?? throw new PatchException($"{manifest.Pbo}: asset '{sourcePath}' is not in the PBO");
            if (asset.CopyOf != null)
            {
                var name = pbo.EntryName(asset.Path);
                if (pbo.Find(asset.Path) != null || additions.Any(a => RawPbo.Normalise(a.Name) == RawPbo.Normalise(name)))
                {
                    throw new PatchException($"{manifest.Pbo}: copy target '{asset.Path}' already exists");
                }
            }
            else if (replacements.ContainsKey(entry.Index))
            {
                throw new PatchException($"{manifest.Pbo}: asset '{asset.Path}' is listed twice");
            }

            var where = $"{manifest.Pbo}:{asset.Path}";
            var data = AssetFormat.Guard(where, () => pbo.ReadData(entry));
            var actual = StateStore.Sha256(data);
            if (actual != asset.Sha256)
            {
                throw new PatchException(
                    $"{manifest.Pbo}: asset '{sourcePath}' does not match its pin: expected sha256 {asset.Sha256}, actual {actual}. Upstream changed the asset; review and re-pin the patch.");
            }

            var context = new OpContext(manifest.Directory, where);
            foreach (var op in asset.Ops)
            {
                var input = data;
                data = AssetFormat.Guard(where, () => OpRegistry.Apply(input, op, context));
            }

            if (asset.CopyOf != null)
            {
                additions.Add(new PboAddition(pbo.EntryName(asset.Path), entry.Timestamp, data));
            }
            else
            {
                replacements[entry.Index] = data;
            }
        }

        return pbo.Rebuild(replacements, additions);
    }
}
