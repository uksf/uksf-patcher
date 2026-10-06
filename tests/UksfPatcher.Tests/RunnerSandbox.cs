using System.Text.Json;
using UksfPatcher.Cli;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Tests;

public sealed class RunnerSandbox : IDisposable
{
    public const string PboName = "Ballistic_missiles.pbo";
    public const string Rvmat = @"textures\iskander_9K720\iskander_9K720_HD.rvmat";

    private readonly string _root = TestData.TempDir();

    public RunnerSandbox()
    {
        Addons = Directory.CreateDirectory(Path.Join(_root, "mod", "addons")).FullName;
        Patches = Directory.CreateDirectory(Path.Join(_root, "patches")).FullName;
        State = Path.Join(_root, "state");
        File.Copy(TestData.Specimen(PboName), PboPath);
        Pristine = File.ReadAllBytes(PboPath);
    }

    public string Addons { get; }
    public string Patches { get; }
    public string State { get; }
    public byte[] Pristine { get; }
    public string PboPath => Path.Join(Addons, PboName);
    public string ManifestPath => Path.Join(Patches, PboName + ".json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    public (int Code, string Output, string Error) Run(params string[] extra)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string[] args = [extra[0], "--addons", Addons, "--patches", Patches, "--state", State, .. extra.Skip(1)];
        var code = CommandLine.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    public PatchRunner Runner(TextWriter output) => new(Addons, Patches, State, output);

    public string RvmatSha() => EntrySha(Pristine, Rvmat);

    public static string EntrySha(byte[] pboBytes, string entry)
    {
        var pbo = RawPbo.Parse(pboBytes);
        return StateStore.Sha256(pbo.ReadData(pbo.Find(entry)!));
    }

    public string RvmatText()
    {
        var pbo = RawPbo.Read(PboPath);
        return System.Text.Encoding.Latin1.GetString(pbo.ReadData(pbo.Find(Rvmat)!));
    }

    public void WriteManifest(string sha, string opFields, string? pristineSha = null) =>
        WriteManifest(Rvmat, sha, $$"""{ "op": "text-replace", {{opFields}} }""", pristineSha);

    public void WriteManifest(string path, string sha, string op, string? pristineSha = null)
    {
        var json = $$"""
            {
              "format": 1,
              "pbo": "{{PboName}}",
              "pristineSha256": "{{pristineSha ?? StateStore.Sha256(Pristine)}}",
              "assets": [
                { "path": {{JsonSerializer.Serialize(path)}}, "sha256": "{{sha}}", "ops": [ {{op}} ] }
              ]
            }
            """;
        File.WriteAllText(ManifestPath, json);
    }
}
