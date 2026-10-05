using BIS.P3D.MLOD;
using UksfPatcher.Cli;
using UksfPatcher.P3d;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public sealed class ExampleManifestTests(ITestOutputHelper log) : IDisposable
{
    private readonly string _root = TestData.TempDir();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ExampleManifestsPatchOnlyTheirAssetsAndTheModelsStillParse()
    {
        var patches = Path.Join(TestData.Root, "examples", "patches");
        var addons = Directory.CreateDirectory(Path.Join(_root, "mod", "addons")).FullName;
        var manifests = Manifest.LoadAll(patches);
        foreach (var m in manifests)
        {
            File.Copy(TestData.Specimen(m.Pbo), Path.Join(addons, m.Pbo));
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var code = CommandLine.Run(["apply", "--addons", addons, "--patches", patches, "--state", Path.Join(_root, "state")], output, error);
        log.WriteLine(output.ToString());
        Assert.True(code == 0, error.ToString());

        foreach (var m in manifests)
        {
            var before = RawPbo.Read(TestData.Specimen(m.Pbo));
            var after = RawPbo.Read(Path.Join(addons, m.Pbo));
            var patched = m.Assets.Select(a => before.Find(a.Path)!.Index).ToHashSet();
            Assert.True(after.TrailerHashValid());
            foreach (var (b, a) in before.Entries.Zip(after.Entries))
            {
                Assert.Equal(b.NameBytes, a.NameBytes);
                if (!patched.Contains(b.Index))
                {
                    Assert.True(before.RawData(b).SequenceEqual(after.RawData(a)), $"{m.Pbo}:{b.Name} changed");
                    continue;
                }

                var data = after.ReadData(a);
                Assert.NotEqual(before.ReadData(b), data);
                if (data.AsSpan().StartsWith("ODOL"u8))
                {
                    OdolFile.Parse(data, b.Name);
                }
                else
                {
                    Assert.NotEmpty(new MLOD(new MemoryStream(data)).Lods);
                }
            }

            log.WriteLine($"{m.Pbo}: {patched.Count} asset(s) patched, {before.Entries.Count - patched.Count} entries byte-identical");
        }

        Assert.Equal(0, CommandLine.Run(["verify", "--addons", addons, "--patches", patches, "--state", Path.Join(_root, "state")], output, error));
    }
}
