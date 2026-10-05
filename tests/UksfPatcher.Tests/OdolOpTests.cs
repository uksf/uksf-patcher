using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public class OdolOpTests(ITestOutputHelper log)
{
    public static TheoryData<string, string> OdolModels => new()
    {
        { Models.RhiccPbo, Models.Rhicc },
        { Models.PumaPbo, Models.PumaTroop },
        { Models.RncPbo, Models.RncApartment },
    };

    [Theory]
    [MemberData(nameof(OdolModels))]
    public void OdolParseAndSerialiseIsByteIdentical(string pbo, string asset)
    {
        var bytes = Models.Load(pbo, asset);
        var copy = OdolFile.Serialise(OdolFile.Parse(bytes, asset));

        log.WriteLine($"{asset}: {bytes.Length} bytes, original {StateStore.Sha256(bytes)}, re-serialised {StateStore.Sha256(copy)}");
        Assert.Equal(bytes, copy);
    }

    [Fact]
    public void SetBoneParentDetachesTheRhiccTurretBones()
    {
        var bytes = Models.Load(Models.RhiccPbo, Models.Rhicc);
        var before = OdolFile.Parse(bytes, "rhicc").ModelInfo.Skeleton.SkeletonBoneNames.ToDictionary(b => b.BoneName, b => b.ParentBoneName);
        var op = new SetBoneParentOp();

        var patched = op.Apply(bytes, TestData.Op("""{"op":"set-bone-parent","bone":"otocvez_2","from":"swing_2","to":""}"""), TestData.Context());
        patched = op.Apply(patched, TestData.Op("""{"op":"set-bone-parent","bone":"otocvez_3","from":"swing_3","to":""}"""), TestData.Context());
        var after = OdolFile.Parse(patched, "rhicc").ModelInfo.Skeleton.SkeletonBoneNames.ToDictionary(b => b.BoneName, b => b.ParentBoneName);

        log.WriteLine($"otocvez_2 parent '{before["otocvez_2"]}' -> '{after["otocvez_2"]}', otocvez_3 parent '{before["otocvez_3"]}' -> '{after["otocvez_3"]}', size {bytes.Length} -> {patched.Length}");
        Assert.Equal("", after["otocvez_2"]);
        Assert.Equal("", after["otocvez_3"]);
        Assert.Equal(before.Count, after.Count);
        Assert.All(before.Where(b => b.Key is not ("otocvez_2" or "otocvez_3")), b => Assert.Equal(b.Value, after[b.Key]));
        Assert.Equal(bytes.Length - "swing_2".Length - "swing_3".Length, patched.Length);
        Assert.DoesNotContain(after.Values, p => p.StartsWith("swing"));
    }

    [Theory]
    [InlineData("""{"op":"set-bone-parent","bone":"otocvez_2","from":"wrong","to":""}""", "has parent 'swing_2'")]
    [InlineData("""{"op":"set-bone-parent","bone":"nope","from":"swing_2","to":""}""", "no bone 'nope'")]
    [InlineData("""{"op":"set-bone-parent","bone":"otocvez_2","from":"swing_2","to":"swing_9"}""", "not a bone")]
    [InlineData("""{"op":"set-bone-parent","bone":"otocvez","from":"","to":"gunner"}""", "cycle")]
    public void SetBoneParentRejectsUnsafeEdits(string op, string message)
    {
        var bytes = Models.Load(Models.RhiccPbo, Models.Rhicc);

        var e = Assert.Throws<PatchException>(() => new SetBoneParentOp().Apply(bytes, TestData.Op(op), TestData.Context()));

        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void RemoveComponentDropsThePumaProxyTriangleComponents()
    {
        var bytes = Models.Load(Models.PumaPbo, Models.PumaTroop);
        var fire = string.Join(",", Enumerable.Range(135, 15).Select(i => $"\"Component{i}\""));

        var patched = new RemoveComponentOp().Apply(bytes, TestData.Op($$"""{"op":"remove-component","lod":"geometryFire","components":[{{fire}}]}"""), TestData.Context());
        patched = new RemoveComponentOp().Apply(patched, TestData.Op("""{"op":"remove-component","lod":"geometryView","components":["Component51"]}"""), TestData.Context());

        var before = OdolFile.Parse(bytes, "puma");
        var after = OdolFile.Parse(patched, "puma");
        foreach (var (lod, removed) in new[] { ("geometryFire", 15), ("geometryView", 1) })
        {
            var b = Lod(before, lod);
            var a = Lod(after, lod);
            log.WriteLine($"{lod}: components {Components(b)} -> {Components(a)}, selections {b.NamedSelections.Length} -> {a.NamedSelections.Length}, proxies {ProxyNames(b).Count} -> {ProxyNames(a).Count}");
            Assert.Equal(Components(b) - removed, Components(a));
            Assert.Equal(ProxyNames(b), ProxyNames(a));
            Assert.Equal(b.Polygons.Faces.Length, a.Polygons.Faces.Length);
            Assert.Equal(b.Vertices.Count, a.Vertices.Count);
        }

        Assert.All(before.Lods.Zip(after.Lods).Where(x => !LodNames.Matches(x.First.Resolution, 7e15f) && !LodNames.Matches(x.First.Resolution, 6e15f)),
            x => Assert.Equal(x.First.NamedSelections.Select(s => s.Name), x.Second.NamedSelections.Select(s => s.Name)));
    }

    [Fact]
    public void RemoveComponentRefusesARealComponentUnlessAsked()
    {
        var bytes = Models.Load(Models.PumaPbo, Models.PumaTroop);

        var e = Assert.Throws<PatchException>(() =>
            new RemoveComponentOp().Apply(bytes, TestData.Op("""{"op":"remove-component","lod":"geometryFire","components":["Component14"]}"""), TestData.Context()));

        Assert.Contains("not proxy faces", e.Message);
    }

    private static BIS.P3D.ODOL.LOD Lod(BIS.P3D.ODOL.ODOL odol, string name) =>
        odol.Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution(name)));

    private static int Components(BIS.P3D.ODOL.LOD lod) =>
        lod.NamedSelections.Count(s => s.Name.StartsWith("component", StringComparison.OrdinalIgnoreCase));

    private static List<string> ProxyNames(BIS.P3D.ODOL.LOD lod) =>
        LibraryAccess.Get<Array>(lod, "RawProxies").Cast<object>()
            .Select(p => lod.NamedSelections[LibraryAccess.Get<int>(p, "NamedSelectionIndex")].Name).ToList();
}
