using UksfPatcher.Patching;
using UksfPatcher.Pbo;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public class PboRoundTripTests(ITestOutputHelper log)
{
    public static TheoryData<string> Specimens =>
    [
        "Ballistic_missiles.pbo", "RHICC.pbo", "rksla3-pumahc.pbo", "rnc_apartments.pbo", "sps_ai_axmc_ace.pbo"
    ];

    [Theory]
    [MemberData(nameof(Specimens))]
    public void RebuildWithoutEditsIsByteIdentical(string name)
    {
        var original = File.ReadAllBytes(TestData.Specimen(name));
        var pbo = RawPbo.Parse(original);
        var rebuilt = pbo.Rebuild(new Dictionary<int, byte[]>());

        log.WriteLine($"{name}: {pbo.Entries.Count} entries, original {StateStore.Sha256(original)}, rebuilt {StateStore.Sha256(rebuilt)}");
        Assert.True(pbo.TrailerHashValid());
        Assert.Equal(original, rebuilt);
    }

    [Theory]
    [MemberData(nameof(Specimens))]
    public void ReplacingOneEntryLeavesEveryOtherEntryByteIdentical(string name)
    {
        var original = File.ReadAllBytes(TestData.Specimen(name));
        var pbo = RawPbo.Parse(original);
        var target = pbo.Entries.First(e => !e.IsCompressed && e.DataSize > 0);
        var replacement = TestData.Ascii("replaced " + name);

        var rebuilt = RawPbo.Parse(pbo.Rebuild(new Dictionary<int, byte[]> { [target.Index] = replacement }));

        Assert.True(rebuilt.TrailerHashValid());
        Assert.Equal(pbo.Entries.Count, rebuilt.Entries.Count);
        foreach (var (before, after) in pbo.Entries.Zip(rebuilt.Entries))
        {
            Assert.Equal(before.NameBytes, after.NameBytes);
            if (before.Index == target.Index)
            {
                Assert.Equal(replacement, rebuilt.ReadData(after));
                continue;
            }

            Assert.Equal(before.Packing, after.Packing);
            Assert.Equal(before.Timestamp, after.Timestamp);
            Assert.True(pbo.RawData(before).SequenceEqual(rebuilt.RawData(after)));
        }
    }

    [Fact]
    public void ObfuscatedNamesArePreservedAsBytes()
    {
        var original = File.ReadAllBytes(TestData.Specimen("sps_ai_axmc_ace.pbo"));
        var pbo = RawPbo.Parse(original);
        var mangled = pbo.Entries.Where(e => e.NameBytes.Any(b => b >= 0x80 || b == '*' || b == '?' || b < 0x20)).ToList();

        Assert.True(mangled.Count >= 10, $"expected an obfuscated specimen, found {mangled.Count} mangled names");
        var stored = mangled.First(e => !e.IsCompressed && e.DataSize > 0);
        var rebuilt = pbo.Rebuild(new Dictionary<int, byte[]> { [stored.Index] = pbo.RawData(stored).ToArray() });
        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void FindStripsThePboPrefix()
    {
        var pbo = RawPbo.Read(TestData.Specimen("RHICC.pbo"));

        Assert.Equal(@"rhicc\addons", pbo.Prefix.ToLowerInvariant());
        Assert.Same(pbo.Find("RHICC.p3d"), pbo.Find(@"rhicc\addons\rhicc.p3d"));
    }
}
