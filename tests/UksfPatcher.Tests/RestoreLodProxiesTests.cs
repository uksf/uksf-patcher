using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class RestoreLodProxiesTests : IDisposable
{
    private const string Op = """{"op":"restore-lod-proxies","from":"1","lods":["2","3","4","5"],"filter":"cup_tigr_"}""";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Tigr() => Models.Load(Models.TigrPbo, Models.Tigr);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static LOD Lod(byte[] model, string name) =>
        OdolFile.Parse(model, "tigr").Lods.Single(l => LodNames.Name(l.Resolution) == name);

    private static List<(string Key, int Bone, string Position, string Texture)> Wheels(LOD lod) =>
        OdolGraph.RawProxies(lod).Cast<object>()
            .Where(p => LibraryAccess.Get<string>(p, "ProxyModel").Contains("cup_tigr_"))
            .Select(p =>
            {
                var section = lod.Sections[LibraryAccess.Get<int>(p, "SectionIndex")];
                var t = LibraryAccess.Get<BIS.Core.Math.Matrix4P>(p, "Transformation").Matrix.Translation;
                return (Key: $"{Path.GetFileName(LibraryAccess.Get<string>(p, "ProxyModel"))}.{LibraryAccess.Get<int>(p, "SequenceID")}",
                    Bone: LibraryAccess.Get<int>(p, "BoneIndex"),
                    Position: $"{t.X:0.00},{t.Y:0.00},{t.Z:0.00}",
                    Texture: lod.Textures[section.TextureIndex]);
            })
            .OrderBy(w => w.Key).ToList();

    [Fact]
    public void EveryLodGetsAllFourWheelsAndRimsOnTheirLodOneBones()
    {
        var before = Tigr();
        var after = Apply(before, Op);

        Assert.Equal([8, 4, 2, 0, 0], new[] { "1", "2", "3", "4", "5" }.Select(n => Wheels(Lod(before, n)).Count));
        var lodOne = Wheels(Lod(after, "1")).Select(w => (w.Key, w.Bone, w.Position)).ToList();
        Assert.Equal(8, lodOne.Count);
        Assert.Contains(lodOne, w => w is { Key: "cup_tigr_wheel.1", Bone: 6, Position: "0.97,-1.44,-2.07" });
        foreach (var name in new[] { "2", "3", "4", "5" })
        {
            var wheels = Wheels(Lod(after, name));
            Assert.Equal(lodOne, wheels.Select(w => (w.Key, w.Bone, w.Position)));
            Assert.All(wheels, w => Assert.EndsWith("wheel_co.paa", w.Texture));
        }
    }

    [Fact]
    public void LeavesOtherProxiesAndLodOneUntouched()
    {
        var before = Tigr();
        var after = Apply(before, Op);

        Assert.Equal(Wheels(Lod(before, "1")), Wheels(Lod(after, "1")));
        foreach (var name in new[] { "1", "2", "3", "4", "5" })
        {
            Assert.Equal(OdolGraph.RawProxies(Lod(before, name)).Length + 8 - Wheels(Lod(before, name)).Count,
                OdolGraph.RawProxies(Lod(after, name)).Length);
        }
    }

    [Fact]
    public void RefusesALodThatAlreadyHasEveryProxy()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Tigr(), """{"op":"restore-lod-proxies","from":"1","lods":["1"],"filter":"cup_tigr_"}"""));

        Assert.Contains("already has every matching proxy", e.Message);
    }
}
