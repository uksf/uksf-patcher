using BIS.P3D.MLOD;
using UksfPatcher.Ops;
using UksfPatcher.Patching;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public class MlodOpTests(ITestOutputHelper log)
{
    public static TheoryData<string> MissileModels =>
    [
        "v2_launchpad1", "v2", "shahab3", "iskander_9K720", "iskander_launchpad1", "minuteman_2"
    ];

    [Theory]
    [MemberData(nameof(MissileModels))]
    public void MlodLibraryRoundTripIsByteIdentical(string model)
    {
        var bytes = Models.Load(Models.MissilesPbo, $@"objects\{model}.p3d");
        var copy = new MLOD(new MemoryStream(bytes)).WriteToMemory().ToArray();

        log.WriteLine($"{model}: original {StateStore.Sha256(bytes)}, re-serialised {StateStore.Sha256(copy)}");
        Assert.Equal(bytes, copy);
    }

    public static TheoryData<string> ModelsTheRptNames =>
    [
        "v2_launchpad1", "v2", "shahab3", "iskander_9K720", "iskander_launchpad1"
    ];

    [Theory]
    [MemberData(nameof(ModelsTheRptNames))]
    public void RemoveDegenerateFacesLeavesOnlyRealFacesAndKeepsEveryOtherFace(string model)
    {
        var bytes = Models.Load(Models.MissilesPbo, $@"objects\{model}.p3d");

        var patched = new RemoveDegenerateFacesOp().Apply(bytes, TestData.Op("""{"op":"remove-degenerate-faces"}"""), TestData.Context());

        var before = new MLOD(new MemoryStream(bytes));
        var after = new MLOD(new MemoryStream(patched));
        Assert.Equal(before.Lods.Length, after.Lods.Length);
        var removed = 0;
        foreach (var (b, a) in before.Lods.Zip(after.Lods))
        {
            var kept = b.Faces.Where(f => RemoveDegenerateFacesOp.FaceArea(b, f) >= RemoveDegenerateFacesOp.DefaultMinArea).ToList();
            removed += b.Faces.Length - a.Faces.Length;
            Assert.Equal(kept.Count, a.Faces.Length);
            Assert.All(a.Faces, f => Assert.True(RemoveDegenerateFacesOp.FaceArea(a, f) >= RemoveDegenerateFacesOp.DefaultMinArea));
            Assert.Equal(kept.Select(Signature), a.Faces.Select(Signature));
            Assert.Equal(b.Points.Length, a.Points.Length);
            Assert.Equal(b.Taggs.Select(t => t.Name), a.Taggs.Select(t => t.Name));
            Assert.All(a.Taggs.OfType<NamedSelectionTagg>(), t => Assert.Equal(a.Faces.Length, t.Faces.Length));
            Assert.All(a.Taggs.OfType<UVSetTagg>(), t => Assert.Equal(a.Faces.Length, t.FaceUVs.Length));
            Assert.Equal(b.Taggs.OfType<NamedSelectionTagg>().Select(t => SelectedKept(t, b)), a.Taggs.OfType<NamedSelectionTagg>().Select(t => t.Faces.Count(x => x != 0)));
        }

        log.WriteLine($"{model}: removed {removed} degenerate faces, {bytes.Length} -> {patched.Length} bytes");
        Assert.True(removed > 0);
    }

    public static TheoryData<string, int, int[]> FacesTheRptNames => new()
    {
        { "v2", 635, [256, 569, 542] },
        { "v2_launchpad1", 5, [3, 1, 8] },
        { "shahab3", 167, [238, 192, 197] },
        { "shahab3", 0, [9, 245, 75] },
        { "iskander_launchpad1", 271, [218, 219, 256] },
        { "iskander_9K720", 72, [67, 330, 202] },
    };

    [Theory]
    [MemberData(nameof(FacesTheRptNames))]
    public void EveryFaceTheRptWarnsAboutIsRemoved(string model, int face, int[] points)
    {
        var bytes = Models.Load(Models.MissilesPbo, $@"objects\{model}.p3d");
        var lods = new MLOD(new MemoryStream(bytes)).Lods
            .Where(l => face < l.Faces.Length && points.All(l.Faces[face].RealVertices.Select(v => v.PointIndex).Contains))
            .ToList();

        Assert.NotEmpty(lods);
        Assert.All(lods, l => Assert.True(RemoveDegenerateFacesOp.FaceArea(l, l.Faces[face]) < RemoveDegenerateFacesOp.DefaultMinArea));
        log.WriteLine($"{model} face {face} [{string.Join(",", points)}]: zero area in {lods.Count} LOD(s), so the op removes it");
    }

    [Fact]
    public void ModelWithoutDegenerateFacesIsAnError()
    {
        var bytes = Models.Load(Models.MissilesPbo, @"objects\minuteman_2.p3d");

        var e = Assert.Throws<PatchException>(() => new RemoveDegenerateFacesOp().Apply(bytes, TestData.Op("""{"op":"remove-degenerate-faces"}"""), TestData.Context()));

        Assert.Contains("no degenerate faces", e.Message);
    }

    [Fact]
    public void RemoveDegenerateFacesChecksTheExpectedCount()
    {
        var bytes = Models.Load(Models.MissilesPbo, @"objects\v2_launchpad1.p3d");

        var e = Assert.Throws<PatchException>(() =>
            new RemoveDegenerateFacesOp().Apply(bytes, TestData.Op("""{"op":"remove-degenerate-faces","expectedRemoved":1}"""), TestData.Context()));

        Assert.Contains("expected to remove 1", e.Message);
    }

    [Fact]
    public void RemoveDegenerateFacesRefusesBinarisedModels()
    {
        var odol = Models.Load(Models.PumaPbo, Models.PumaTroop);

        Assert.Throws<PatchException>(() => new RemoveDegenerateFacesOp().Apply(odol, TestData.Op("""{"op":"remove-degenerate-faces"}"""), TestData.Context()));
    }

    private static int SelectedKept(NamedSelectionTagg tagg, P3DM_LOD lod) =>
        tagg.Faces.Where((x, i) => x != 0 && RemoveDegenerateFacesOp.FaceArea(lod, lod.Faces[i]) >= RemoveDegenerateFacesOp.DefaultMinArea).Count();

    private static string Signature(Face f) =>
        $"{f.Texture}|{f.Material}|{f.Flags}|{string.Join(",", f.RealVertices.Select(v => $"{v.PointIndex}/{v.NormalIndex}/{v.U}/{v.V}"))}";
}
