using System.Text;
using System.Text.Json;
using UksfPatcher.Ops;

namespace UksfPatcher.Tests;

public static class TestData
{
    public static string Root { get; } = FindRoot();

    public static string Specimen(string name)
    {
        var path = Path.Join(Root, "testdata", "specimens", name);
        return File.Exists(path) ? path : throw new FileNotFoundException($"specimen missing; run scripts/fetch-specimens.sh", path);
    }

    public static string File_(string name) => Path.Join(Root, "testdata", name);

    public static string TempDir()
    {
        var dir = Path.Join(Path.GetTempPath(), "uksf-patcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static JsonElement Op(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static OpContext Context(string dir = ".") => new(dir, "test-asset");

    public static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "UksfPatcher.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("repository root not found");
    }
}
