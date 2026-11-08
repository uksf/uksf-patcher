using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;

namespace UksfPatcher.Tests;

public sealed class A400LodOpsTests
{
    [Fact]
    public void CopiesNamedFacesIntoANewRoadwayLod()
    {
        var model = Models.Load(Models.EawsPbo, Models.EawsFuselage);
        var source = OdolFile.Parse(model, "typhoon").Lods.Single(l => LodNames.Matches(l.Resolution, 1e13f));
        var selection = source.NamedSelections.First(s => s.SelectedVertices.Count > 3
            && source.Polygons.Faces.Count(f => f.VertexIndices.All(s.SelectedVertices.Contains)) > 0);
        var faces = source.Polygons.Faces.Count(f => f.VertexIndices.All(selection.SelectedVertices.ToHashSet().Contains));
        var json = $$"""{"op":"add-selection-lod","lod":"roadway","fromLod":"geometryFire","selections":["{{selection.Name}}"],"faces":{{faces}}}""";

        var after = OdolFile.Parse(OpRegistry.Apply(model, TestData.Op(json), TestData.Context()), "typhoon");
        var roadway = after.Lods.Single(l => LodNames.Matches(l.Resolution, 3e15f));

        Assert.Equal(faces, roadway.Polygons.Faces.Length);
        Assert.Equal((byte)Array.IndexOf(after.Lods, roadway), LibraryAccess.Get<byte>(after.ModelInfo, "Roadway"));
        Assert.Contains(roadway.NamedSelections, s => s.Name == selection.Name && s.SelectedFaces.Count == faces);
    }

    [Fact]
    public void AddsANamedPointSelectionToTheHitPointsLod()
    {
        var model = Models.Load(Models.EawsPbo, Models.EawsFuselage);
        var json = """{"op":"add-points","lod":"hitpoints","name":"HitTest","points":2,"positions":[[1,2,3],[4,5,6]]}""";

        var after = OdolFile.Parse(OpRegistry.Apply(model, TestData.Op(json), TestData.Context()), "typhoon");
        var hitpoints = after.Lods.Single(l => LodNames.Matches(l.Resolution, 5e15f));
        var selection = hitpoints.NamedSelections.Single(s => s.Name == "HitTest");

        Assert.Equal(2, selection.SelectedVertices.Count);
        Assert.Equal((1f, 2f, 3f), (hitpoints.Vertices[selection.SelectedVertices[0]].X, hitpoints.Vertices[selection.SelectedVertices[0]].Y, hitpoints.Vertices[selection.SelectedVertices[0]].Z));
        Assert.Equal((4f, 5f, 6f), (hitpoints.Vertices[selection.SelectedVertices[1]].X, hitpoints.Vertices[selection.SelectedVertices[1]].Y, hitpoints.Vertices[selection.SelectedVertices[1]].Z));
    }

    [Fact]
    public void IsolatesASharedMemorySelectionOntoANewVertex()
    {
        var model = Models.Load(Models.RhiccPbo, Models.Rhicc);
        var before = OdolFile.Parse(model, "rhicc").Lods.Single(l => LodNames.Matches(l.Resolution, 1e15f));
        var shared = before.NamedSelections.First(s => s.SelectedVertices.Count == 1 && s.SelectedFaces.Count == 0
            && before.NamedSelections.Any(o => o != s && o.SelectedVertices.Contains(s.SelectedVertices[0])));
        var old = shared.SelectedVertices[0];
        var json = $$"""{"op":"isolate-memory-selection","name":"{{shared.Name}}","position":[1.25,2.5,3.75]}""";

        var after = OdolFile.Parse(OpRegistry.Apply(model, TestData.Op(json), TestData.Context()), "rhicc")
            .Lods.Single(l => LodNames.Matches(l.Resolution, 1e15f));
        var point = after.NamedSelections.Single(s => s.Name == shared.Name).SelectedVertices.Single();

        Assert.Equal(before.Vertices.Count, point);
        Assert.Equal((1.25f, 2.5f, 3.75f), (after.Vertices[point].X, after.Vertices[point].Y, after.Vertices[point].Z));
        Assert.Equal(before.Vertices[old].X, after.Vertices[old].X);
        Assert.Contains(after.NamedSelections, s => s.Name != shared.Name && s.SelectedVertices.Contains(old));
    }

    [Fact]
    public void BuildsTheA400RampRoadwayAndSplitsItsSharedGetInPoints()
    {
        var path = "C:/Users/Tim/.agent-scratch/inbox-1108/a400-extract/A400M_F.p3d";
        if (!File.Exists(path))
        {
            return;
        }

        var model = File.ReadAllBytes(path);
        var parsed = OdolFile.Parse(model, "A400M_F.p3d");
        var geometry = parsed.Lods.Single(l => LodNames.Matches(l.Resolution, 1e13f));
        var names = new[] { "ramp_bottom", "ramp_bottom2" };
        var verts = names.SelectMany(n => geometry.NamedSelections.Single(s => s.Name == n).SelectedVertices).ToHashSet();
        var faces = geometry.Polygons.Faces.Count(f => f.VertexIndices.Length > 0 && f.VertexIndices.All(verts.Contains));
        Assert.Equal(32, faces);

        var roadway = $$"""{"op":"add-selection-lod","lod":"roadway","fromLod":"geometry","selections":["ramp_bottom","ramp_bottom2"],"faces":{{faces}}}""";
        var split = """{"op":"isolate-memory-selection","name":"pos driver","position":[2.15,1.05,-7.85],"bone":"ramp_bottom"}""";
        var points = """{"op":"add-points","lod":"hitpoints","name":"HitHull","points":1,"positions":[[0,2.5,0]]}""";
        var after = model;
        foreach (var json in new[] { roadway, split, points })
        {
            after = OpRegistry.Apply(after, TestData.Op(json), TestData.Context());
        }

        var odol = OdolFile.Parse(after, "A400M_F.p3d");
        var road = odol.Lods.Single(l => LodNames.Matches(l.Resolution, 3e15f));
        var memory = odol.Lods.Single(l => LodNames.Matches(l.Resolution, 1e15f));
        var driver = memory.NamedSelections.Single(s => s.Name == "pos driver").SelectedVertices.Single();
        Assert.Equal(faces, road.Polygons.Faces.Length);
        Assert.Equal(2.15f, memory.Vertices[driver].X);
        Assert.Equal(1, OdolGraph.BoneCount(OdolGraph.BoneRefs(memory)[driver]));
        Assert.Contains(odol.Lods.Single(l => LodNames.Matches(l.Resolution, 5e15f)).NamedSelections, s => s.Name == "HitHull");
    }
}
