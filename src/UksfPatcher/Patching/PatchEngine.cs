using UksfPatcher.Ops;
using UksfPatcher.Pbo;

namespace UksfPatcher.Patching;

public static class PatchEngine
{
    public static byte[] Patch(byte[] pristinePbo, Manifest manifest)
    {
        var pbo = Pbo(manifest.Pbo, () => RawPbo.Parse(pristinePbo));
        pbo.RequireValidTrailer(manifest.Pbo);

        var replacements = new Dictionary<int, byte[]>();
        var additions = new List<PboAddition>();
        var removals = new HashSet<int>();
        foreach (var asset in manifest.Assets)
        {
            var where = $"{manifest.Pbo}:{asset.Path}";
            var sourcePath = asset.CopyOf ?? asset.Path;
            var entry = Pbo(where, () => pbo.Find(sourcePath)) ?? throw new PatchException($"{manifest.Pbo}: asset '{sourcePath}' is not in the PBO");
            if (asset.CopyOf != null)
            {
                var name = pbo.EntryName(asset.Path);
                if (InvalidEntryName(name) is { } why)
                {
                    throw new PatchException($"{where}: copy target is not a valid PBO entry name: {why}");
                }

                if (Pbo(where, () => pbo.Find(asset.Path)) != null || additions.Any(a => RawPbo.Normalise(a.Name) == RawPbo.Normalise(name)))
                {
                    throw new PatchException($"{manifest.Pbo}: copy target '{asset.Path}' already exists");
                }
            }
            else if (replacements.ContainsKey(entry.Index) || removals.Contains(entry.Index))
            {
                throw new PatchException($"{manifest.Pbo}: asset '{asset.Path}' is listed twice");
            }

            var data = AssetFormat.Guard(where, () => pbo.ReadData(entry));
            var actual = StateStore.Sha256(data);
            if (actual != asset.Sha256)
            {
                throw new PatchException(
                    $"{manifest.Pbo}: asset '{sourcePath}' does not match its pin: expected sha256 {asset.Sha256}, actual {actual}. Upstream changed the asset; review and re-pin the patch.");
            }

            if (asset.Remove)
            {
                removals.Add(entry.Index);
                continue;
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

        var output = Pbo(manifest.Pbo, () => pbo.Rebuild(replacements, additions, removals));
        if (additions.Count > 0 || removals.Count > 0)
        {
            RequireEntries(manifest.Pbo, pbo, output, additions, removals);
        }

        return output;
    }

    private static void RequireEntries(string label, RawPbo before, byte[] output, IReadOnlyList<PboAddition> additions, IReadOnlySet<int> removals)
    {
        var after = Pbo($"{label} after adding or removing entries", () => RawPbo.Parse(output));
        var expected = before.Entries.Count + additions.Count - removals.Count;
        if (after.Entries.Count != expected)
        {
            throw new PatchException($"{label}: the rebuilt PBO has {after.Entries.Count} entries, expected {expected}");
        }

        foreach (var index in removals)
        {
            if (Pbo(label, () => after.Find(before.Entries[index].Name)) != null)
            {
                throw new PatchException($"{label}: the rebuilt PBO still contains the removed entry '{before.Entries[index].Name}'");
            }
        }

        foreach (var e in before.Entries.Where(e => !removals.Contains(e.Index)))
        {
            if (Pbo(label, () => after.Find(e.Name)) == null)
            {
                throw new PatchException($"{label}: the rebuilt PBO lost the entry '{e.Name}'");
            }
        }

        foreach (var addition in additions)
        {
            var entry = Pbo(label, () => after.Find(addition.Name));
            if (entry == null || !after.RawData(entry).SequenceEqual(addition.Data))
            {
                throw new PatchException($"{label}: the rebuilt PBO does not contain the added entry '{addition.Name}'");
            }
        }
    }

    private static string? InvalidEntryName(string name)
    {
        if (name.Length == 0)
        {
            return "the name is empty";
        }

        if (name.Any(c => c < 32 || c > 255))
        {
            return "the name has a control character or a character outside Latin-1";
        }

        return name.Split('\\').Any(part => part.Length == 0) ? "the name has an empty path segment" : null;
    }

    private static T Pbo<T>(string label, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (PboFormatException e)
        {
            throw new PatchException($"{label}: cannot parse: {e.Message}");
        }
    }
}
