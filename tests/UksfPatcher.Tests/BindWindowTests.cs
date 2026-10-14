using UksfPatcher.Ops;
using UksfPatcher.P3d;

namespace UksfPatcher.Tests;

public sealed class BindWindowTests
{
    [Fact]
    public void BoxedBindLeavesAnUntouchedUnweightedSectionAlone()
    {
        var lod = TyphoonModel.Lod0();
        var (name, hit, miss) = lod.NamedSelections.Where(n => n.IsSectional)
            .SelectMany(n => n.Sections.SelectMany(a => n.Sections.Where(b => b != a).Select(b => (n.Name, a, b, all: n.Sections))))
            .Where(x => TyphoonModel.Isolated(lod, x.a) && TyphoonModel.Unweighted(lod, x.a) && TyphoonModel.Unweighted(lod, x.b))
            .Select(x => (x.Name, x.a, x.b))
            .First(x => OnlyBoxes(lod, lod.NamedSelections.Single(n => n.Name == x.Name).Sections, x.a));
        var bounds = Box(lod, hit);
        var op = $$"""{"op":"bind-selection","lod":"0","selection":"{{name}}","bone":"vrtule 0","box":[{{string.Join(',', bounds)}}]}""";

        var after = OdolFile.Parse(OpRegistry.Apply(Models.Load(Models.EawsPbo, Models.EawsFuselage), TestData.Op(op), TestData.Context()), "typhoon").Lods[0];

        Assert.False(TyphoonModel.Unweighted(after, hit));
        Assert.True(TyphoonModel.Unweighted(after, miss));
        Assert.Equal(lod.Sections[miss].BonesCount, after.Sections[miss].BonesCount);
    }

    private static bool OnlyBoxes(BIS.P3D.ODOL.LOD lod, IEnumerable<int> sections, int target)
    {
        var box = Box(lod, target);
        return sections.Where(s => s != target).SelectMany(s => OdolGraph.FacesInSection(lod, lod.Sections[s]))
            .All(f => !lod.Polygons.Faces[f].VertexIndices.All(v => Inside(lod, v, box)));
    }

    private static float[] Box(BIS.P3D.ODOL.LOD lod, int s)
    {
        var p = TyphoonModel.SectionVertices(lod, s).Select(v => lod.Vertices[v]).ToList();
        return [p.Min(v => v.X), p.Max(v => v.X), p.Min(v => v.Y), p.Max(v => v.Y), p.Min(v => v.Z), p.Max(v => v.Z)];
    }

    private static bool Inside(BIS.P3D.ODOL.LOD lod, int v, float[] b) =>
        lod.Vertices[v].X >= b[0] && lod.Vertices[v].X <= b[1] && lod.Vertices[v].Y >= b[2] && lod.Vertices[v].Y <= b[3] && lod.Vertices[v].Z >= b[4] && lod.Vertices[v].Z <= b[5];
}
