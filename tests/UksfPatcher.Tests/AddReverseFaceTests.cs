using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class AddReverseFaceTests
{
    private const string Operation = """{"op":"add-reverse-face","lod":"0","face":13491,"vertices":[17098,17065,17100]}""";

    [Fact]
    public void AddsOppositeWindingInTheSameSectionAndPreservesSelections()
    {
        var model = Models.Load(Models.EawsPbo, Models.EawsFuselage);
        var before = OdolFile.Parse(model, "typhoon").Lods[0];
        var output = OpRegistry.Apply(model, TestData.Op(Operation), TestData.Context());
        var after = OdolFile.Parse(output, "typhoon").Lods[0];

        Assert.Equal(before.Polygons.Faces.Length + 1, after.Polygons.Faces.Length);
        Assert.Equal(new[] { 17100, 17065, 17098 }, after.Polygons.Faces[13492].VertexIndices);
        Assert.Equal(before.Polygons.Faces[13492].VertexIndices, after.Polygons.Faces[13493].VertexIndices);
        Assert.Equal(before.Vertices.Count, after.Vertices.Count);
        var ranges = OdolFaces.SectionFaceRanges(after, "typhoon");
        Assert.True(ranges[50].First <= 13491 && ranges[50].End > 13492);
        foreach (var selection in before.NamedSelections)
        {
            var expected = selection.SelectedFaces.Select(f => f >= 13492 ? f + 1 : f).ToList();
            if (selection.SelectedFaces.Contains(13491)) expected.Add(13492);
            Assert.Equal(expected.Order(), after.NamedSelections.Single(s => s.Name == selection.Name).SelectedFaces.Order());
        }

        Assert.Throws<PatchException>(() => OpRegistry.Apply(output, TestData.Op(Operation), TestData.Context()));
    }

    [Fact]
    public void RejectsUnexpectedVertexIndices()
    {
        var model = Models.Load(Models.EawsPbo, Models.EawsFuselage);
        var op = TestData.Op("""{"op":"add-reverse-face","lod":"0","face":13491,"vertices":[17098,17065,0]}""");
        Assert.Throws<PatchException>(() => OpRegistry.Apply(model, op, TestData.Context()));
    }
}
