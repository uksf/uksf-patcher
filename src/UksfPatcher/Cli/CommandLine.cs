using System.Security.Cryptography;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Cli;

public static class CommandLine
{
    public const string Usage = """
        uksf-patcher apply  --addons <dir> --patches <dir> --state <dir> [--dry-run]
        uksf-patcher verify --addons <dir> --patches <dir> --state <dir>
        uksf-patcher inspect <pbo> [filter]
        uksf-patcher hash <pbo> [path-in-pbo]
        uksf-patcher p3d <pbo> <path-in-pbo> [lod]
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0)
            {
                error.WriteLine(Usage);
                return 2;
            }

            return args[0] switch
            {
                "apply" => RunPatcher(args, output, verifyOnly: false),
                "verify" => RunPatcher(args, output, verifyOnly: true),
                "inspect" when args.Length >= 2 => Inspect(args[1], args.Length > 2 ? args[2] : null, output),
                "hash" when args.Length == 2 => HashPbo(args[1], output),
                "hash" when args.Length == 3 => Hash(args[1], args[2], output),
                "p3d" when args.Length is 3 or 4 => P3dInfo.Run(args[1], args[2], args.Length == 4 ? args[3] : null, output),
                _ => Fail(error, Usage)
            };
        }
        catch (PatchException e)
        {
            error.WriteLine($"error: {e.Message}");
            return 1;
        }
        catch (PboFormatException e)
        {
            error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int RunPatcher(string[] args, TextWriter output, bool verifyOnly)
    {
        var options = ParseOptions(args.Skip(1).ToArray());
        if (!options.TryGetValue("addons", out var addons) || !options.TryGetValue("patches", out var patches)
            || !options.TryGetValue("state", out var state) || addons == null || patches == null || state == null)
        {
            throw new PatchException("--addons, --patches and --state are required");
        }

        if (IsInside(Path.GetFullPath(state), Path.GetFullPath(Path.Join(addons, ".."))))
        {
            throw new PatchException("--state must be outside the mod folder, or the pristine backups ship with the mod");
        }

        var runner = new PatchRunner(addons, patches, state, output);
        return verifyOnly ? runner.Verify() : runner.Apply(options.ContainsKey("dry-run"));
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string?>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                throw new PatchException($"unexpected argument '{args[i]}'");
            }

            var key = args[i][2..];
            if (key == "dry-run")
            {
                result[key] = null;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new PatchException($"--{key} needs a value");
            }

            result[key] = args[++i];
        }

        return result;
    }

    private static int Inspect(string path, string? filter, TextWriter output)
    {
        var pbo = RawPbo.Read(path);
        output.WriteLine($"header {pbo.HeaderLength} bytes, {pbo.Entries.Count} entries, trailer hash valid: {pbo.TrailerHashValid()}");
        foreach (var e in pbo.Entries.Where(e => filter == null || e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            output.WriteLine($"{e.Index,6} {e.Packing:X8} {e.OriginalSize,10} {e.DataSize,10} {e.Name}");
        }

        return 0;
    }

    private static int Hash(string path, string asset, TextWriter output)
    {
        var pbo = RawPbo.Read(path);
        var entry = pbo.Find(asset) ?? throw new PatchException($"{asset} is not in {path}");
        output.WriteLine(Convert.ToHexStringLower(SHA256.HashData(pbo.ReadData(entry))));
        return 0;
    }

    private static int HashPbo(string path, TextWriter output)
    {
        output.WriteLine(StateStore.Sha256(File.ReadAllBytes(path)));
        return 0;
    }

    private static bool IsInside(string path, string folder) =>
        (Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar)
            .StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 2;
    }
}
