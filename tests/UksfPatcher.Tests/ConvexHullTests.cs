using System.Numerics;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public class ConvexHullTests(ITestOutputHelper log)
{
    private static readonly string[] GeometryLods = ["geometry", "geometryFire", "geometryView", "geometryPhys"];

    [Fact]
    public void HullOfACubeWithAnInteriorPointIsTwelveOutwardTriangles()
    {
        var points = new List<Vector3>();
        for (var i = 0; i < 8; i++) points.Add(new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1));
        points.Add(new Vector3(0.5f, 0.5f, 0.5f));
        var ids = Enumerable.Range(100, points.Count).ToList();

        var hull = ConvexHull.Build(ids, points);
        var byId = ids.Zip(points).ToDictionary(x => x.First, x => x.Second);

        Assert.Equal(12, hull.Count);
        Assert.DoesNotContain(hull, t => t.A == 108 || t.B == 108 || t.C == 108);
        Assert.All(hull, t => Assert.True(ConvexHull.MaxOutsideDistance(t, byId, points) <= 1e-6));
        AssertClosed(hull.Select(t => new[] { t.A, t.B, t.C }));
    }

    [Fact]
    public void FlatPointSetsAreRejected()
    {
        var points = new List<Vector3> { new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0) };

        Assert.Throws<ArgumentException>(() => ConvexHull.Build([0, 1, 2, 3], points));
    }

    [Fact]
    public void ProxyTriangleComponentsCannotBeHulled()
    {
        var bytes = Models.Load(Models.PumaPbo, Models.PumaTroop);

        var e = Assert.Throws<PatchException>(() => new ConvexHullComponentOp().Apply(bytes,
            TestData.Op("""{"op":"convex-hull-component","lods":["geometryFire"],"component":"Component135"}"""), TestData.Context()));

        Assert.Contains("no volume", e.Message);
    }

    [Fact]
    public void HullReplacesTheRncComponentWithAClosedConvexSolid()
    {
        var bytes = Models.Load(Models.RncPbo, Models.RncApartment);
        var lods = string.Join(",", GeometryLods.Select(l => $"\"{l}\""));

        var patched = new ConvexHullComponentOp().Apply(bytes,
            TestData.Op($$"""{"op":"convex-hull-component","lods":[{{lods}}],"component":"Component113"}"""), TestData.Context());

        var before = OdolFile.Parse(bytes, "rnc");
        var after = OdolFile.Parse(patched, "rnc");
        foreach (var name in GeometryLods)
        {
            var b = before.Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution(name)));
            var a = after.Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution(name)));
            var oldComponent = b.NamedSelections.Single(s => s.Name == "component113");
            var component = a.NamedSelections.Single(s => s.Name == "component113");
            var faces = component.SelectedFaces.Select(i => a.Polygons.Faces[i].VertexIndices).ToList();
            var points = component.SelectedVertices.Select(v => ConvexHullComponentOp.Position(a, v)).ToList();
            var outward = ConvexHullComponentOp.FacesPointOutward(a, component);
            var worst = faces.Max(f =>
            {
                var t = outward ? new HullTriangle(f[0], f[1], f[2]) : new HullTriangle(f[0], f[2], f[1]);
                return ConvexHull.MaxOutsideDistance(t, f.Distinct().ToDictionary(v => v, v => ConvexHullComponentOp.Position(a, v)), points);
            });

            log.WriteLine($"{name}: component113 faces {oldComponent.SelectedFaces.Count} -> {faces.Count} hull triangles, {points.Count} points, " +
                $"worst point distance outside any hull face plane {worst:E2} m (<= 0 is convex), faces {b.Polygons.Faces.Length} -> {a.Polygons.Faces.Length}, winding {(outward ? "outward" : "inward")}");
            Assert.True(worst <= ConvexHull.Tolerance(points));
            AssertClosed(faces);
            Assert.Equal(b.Polygons.Faces.Length - oldComponent.SelectedFaces.Count + faces.Count, a.Polygons.Faces.Length);
            Assert.Equal(b.NamedSelections.Select(s => s.Name), a.NamedSelections.Select(s => s.Name));
            Assert.Equal(b.Vertices.Count, a.Vertices.Count);
            OdolFaces.SectionFaceRanges(a, name);
            foreach (var (sb, sa) in b.NamedSelections.Zip(a.NamedSelections).Where(x => x.First.Name != "component113"))
            {
                Assert.Equal(Faces(b, sb), Faces(a, sa));
            }
        }
    }

    private static List<string> Faces(BIS.P3D.ODOL.LOD lod, BIS.P3D.ODOL.NamedSelection s) =>
        s.SelectedFaces.Select(i => string.Join(",", lod.Polygons.Faces[i].VertexIndices)).Order().ToList();

    private static void AssertClosed(IEnumerable<int[]> triangles)
    {
        var edges = new Dictionary<(int, int), int>();
        foreach (var t in triangles)
        {
            for (var i = 0; i < 3; i++)
            {
                var a = t[i];
                var b = t[(i + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }

        Assert.All(edges, e => Assert.Equal(2, e.Value));
    }
}
