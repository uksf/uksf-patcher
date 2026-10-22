using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class FlameStretchTests : IDisposable
{
    private const string RebindLeft = """{"op":"rebind-vertices","lod":"0","section":46,"from":"afterburner_left","to":"afterburner_left_end","box":[0,2,-1,1,8,9.5],"vertices":18}""";
    private const string StretchLeft = """{"op":"add-translation","animation":"afterburner_left_stretch","bone":"afterburner_left_end","animationSource":"afterburner_stretch","minValue":0,"maxValue":1,"offset0":1.6,"offset1":4.2,"axis":[0,0,1]}""";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Fuselage() => Models.Load(Models.EawsPbo, Models.EawsFuselage);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static ODOL Parse(byte[] model) => OdolFile.Parse(model, "typhoon");

    private static int Skel(ODOL odol, string bone) => Array.FindIndex(odol.ModelInfo.Skeleton.SkeletonBoneNames, b => b.BoneName == bone);

    private static List<int> BonesOf(LOD lod, int section)
    {
        var refs = OdolGraph.BoneRefs(lod);
        return OdolGraph.FacesInSection(lod, lod.Sections[section]).SelectMany(f => lod.Polygons.Faces[f].VertexIndices).Distinct()
            .Select(v => (int)LibraryAccess.Get<byte[]>(refs[v], "Data")[0]).ToList();
    }

    [Fact]
    public void RebindsTheRearFlameRingToTheEndBone()
    {
        var odol = Parse(Apply(Fuselage(), RebindLeft));
        var lod = odol.Lods[0];
        var end = Array.IndexOf(lod.SubSkeletonsToSkeleton, Skel(odol, "afterburner_left_end"));

        Assert.Equal(127, end);
        var bones = BonesOf(lod, 46);
        Assert.Equal(18, bones.Count(b => b == end));
        Assert.Equal(34, bones.Count(b => b == 106));
        Assert.Equal(52, bones.Count(b => b == 105));
        var sec = lod.Sections[46];
        Assert.True(sec.MinBoneIndex <= 105 && end < sec.MinBoneIndex + sec.BonesCount && sec.BonesCount <= Ops.SectionBones.MaxPerSection);
    }

    [Fact]
    public void AddsAThrottleTranslationToEveryLod()
    {
        var odol = Parse(Apply(Fuselage(), StretchLeft));
        var anims = odol.Animations;
        var k = Array.FindIndex(anims.AnimationClasses, a => a.AnimName == "afterburner_left_stretch");
        var a = anims.AnimationClasses[k];
        var bone = Skel(odol, "afterburner_left_end");

        Assert.Equal(209, k);
        Assert.Equal((4u, "afterburner_stretch", 0f, 1f, 1.6f, 4.2f), (a.AnimType, a.AnimSource, a.MinValue, a.MaxValue, a.Offset0, a.Offset1));
        for (var j = 0; j < odol.Lods.Length; j++)
        {
            Assert.Equal(bone, anims.Anims2Bones[j][k]);
            Assert.Equal([(uint)k], anims.Bones2Anims[j][bone]);
            Assert.Equal((0f, 0f, 1f), (anims.AxisData[j][k][1].X, anims.AxisData[j][k][1].Y, anims.AxisData[j][k][1].Z));
        }
    }

    [Fact]
    public void RefusesABoxThatHoldsTheWrongVertexCount()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Fuselage(), RebindLeft.Replace("\"vertices\":18", "\"vertices\":19")));

        Assert.Contains("holds 18 vertices", e.Message);
    }

    [Fact]
    public void RefusesADuplicateAnimation()
    {
        var once = Apply(Fuselage(), StretchLeft);
        var e = Assert.Throws<PatchException>(() => Apply(once, StretchLeft));

        Assert.Contains("already has an animation", e.Message);
    }
}
