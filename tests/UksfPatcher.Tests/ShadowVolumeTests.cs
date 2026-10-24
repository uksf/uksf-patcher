using System.Collections;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class ShadowVolumeTests
{
    private const string Rebuild = "{\"op\":\"rebuild-shadow-volume\",\"lod\":\"10000\",\"from\":\"11000\"}";

    private static byte[] Apply(byte[] model, string op) => OpRegistry.Apply(model, TestData.Op(op), TestData.Context(TestData.File_("")));

    private static List<string> Corners(byte[] model)
    {
        var lod = OdolFile.Parse(model, "m119").Lods.Single(l => l.Resolution == 10000);
        var bones = OdolGraph.BoneRefs(lod);
        var neighbours = LibraryAccess.Get<IEnumerable>(lod, "NeighborBoneRef").Cast<object>().ToList();
        string Bone(object r) => $"{OdolGraph.BoneCount(r)}:{Convert.ToHexString(LibraryAccess.Get<byte[]>(r, "Data"))}";
        string Corner(int v)
        {
            var n = neighbours[v];
            int a = LibraryAccess.Get<ushort>(n, "PosA"), b = LibraryAccess.Get<ushort>(n, "PosB");
            return $"{lod.Vertices[v].Vector3} {((Vector3P)lod.NormalsCompressed[v]).Vector3} {lod.Clip[v]} {Bone(bones[v])} " +
                $"{lod.Vertices[a].Vector3} {Bone(LibraryAccess.Get<object>(n, "RtwA"))} {lod.Vertices[b].Vector3} {Bone(LibraryAccess.Get<object>(n, "RtwB"))}";
        }

        return OdolFaces.SectionFaceRanges(lod, "m119")
            .SelectMany(r => lod.Polygons.Faces[r.First..r.End].Select(f => $"{r.First} " + string.Join(" | ", f.VertexIndices.Select(Corner))).Order())
            .ToList();
    }

    [Fact]
    public void RebuildsTheShippedShadowVolumeCornerForCorner()
    {
        var m119 = Models.Load(Models.M119Pbo, Models.M119);

        Assert.Equal(Corners(m119), Corners(Apply(m119, Rebuild)));
    }

    [Fact]
    public void RebuildsTheShadowVolumeOfAModelWithoutBones()
    {
        var round = Models.Load(Models.MortarsPbo, Models.RoundHe);
        static List<string> Faces(byte[] model)
        {
            var lod = OdolFile.Parse(model, "round").Lods.Single(l => l.Resolution == 10002);
            Assert.Empty(LibraryAccess.Get<IEnumerable>(lod, "NeighborBoneRef").Cast<object>());
            return lod.Polygons.Faces.Select(f => string.Join(" | ", f.VertexIndices.Select(v => $"{lod.Vertices[v].Vector3} {lod.Clip[v]}"))).Order().ToList();
        }

        var rebuilt = Apply(round, Rebuild.Replace("10000", "10002").Replace("11000", "11002"));

        Assert.Equal(2656, Faces(rebuilt).Count);
        Assert.Equal(Faces(round), Faces(rebuilt));
    }

    [Fact]
    public void RefusesATexturedSourceLod()
    {
        var m119 = Models.Load(Models.M119Pbo, Models.M119);

        var e = Assert.Throws<PatchException>(() => Apply(m119, Rebuild.Replace("\"11000\"", "\"1\"")));

        Assert.Contains("a LOD has tangents", e.Message);
    }
}
