using System.Numerics;
using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class L118BarrelTests
{
    private const string Axis = "\"axis\":[0,0.885,1.636,0,0.0487,-0.9988]";
    private const string RemoveBrake = "{\"op\":\"remove-faces\",\"lod\":\"1\"," + Axis + ",\"bones\":[\"barrel\"],\"allBeyond\":3.86,\"anyBeyond\":3.86,\"faces\":464}";
    private const string Stretch = "{\"op\":\"stretch-along-axis\",\"lods\":[\"1\"]," + Axis + ",\"bones\":[\"barrel\",\"m119_zasleh\"],\"cut\":3.5,\"length\":0.70,\"vertices\":500}";
    private const string StretchBore = "{\"op\":\"stretch-along-axis\",\"lods\":[\"1\"]," + Axis + ",\"bones\":[\"barrel\"],\"cut\":4.5,\"maxRadius\":0.0545,\"length\":-0.0257,\"vertices\":98}";
    private const string StretchMemory = "{\"op\":\"stretch-along-axis\",\"lods\":[\"memory\"]," + Axis + ",\"cut\":3.5,\"maxRadius\":0.3,\"length\":0.74,\"vertices\":4}";
    private const string AddBrake = "{\"op\":\"add-mesh\",\"lods\":[\"1\"],\"source\":\"L118_muzzle.p3d\",\"sourceAxis\":[0,0,3.75021,0,0,1]," + Axis +
        ",\"attachAlong\":4.5742,\"scale\":1.06,\"weldLoops\":[[3.75021,0.0558],[3.89092,0.04773]],\"loopRadii\":[[3.89092,0.04773,0.0530]],\"bone\":\"barrel\"," +
        "\"uvFrom\":{\"alongMin\":3.1111,\"alongMax\":4.5742,\"radius\":0.0591,\"tolerance\":0.0015}}";

    private static readonly AxialFrame Frame = new(new Vector3(0, 0.885f, 1.636f), Vector3.Normalize(new Vector3(0, 0.0487f, -0.9988f)));

    private static byte[] Apply(byte[] model, params string[] ops) =>
        ops.Aggregate(model, (m, op) => OpRegistry.Apply(m, TestData.Op(op), TestData.Context(TestData.File_(""))));

    private static byte[] M119() => Models.Load(Models.M119Pbo, Models.M119);

    private static LOD Lod(ODOL odol, string name) => odol.Lods.Single(l => LodNames.Name(l.Resolution) == name);

    [Fact]
    public void ReplacesTheBrakeOnALongerBarrel()
    {
        var lod = Lod(OdolFile.Parse(Apply(M119(), RemoveBrake, Stretch, StretchBore, AddBrake), "m119"), "1");
        var pos = lod.Vertices.Select(v => v.Vector3).ToArray();
        bool OnSeam(int v) => Math.Abs(Frame.Along(pos[v]) - 4.5742f) < 0.002f && Math.Abs(Frame.Radius(pos[v]) - 0.0591f) < 0.002f;
        string Key(int v) => $"{pos[v].X:F4},{pos[v].Y:F4},{pos[v].Z:F4}";
        var seamEdges = lod.Polygons.Faces
            .SelectMany(f => f.VertexIndices.Select((v, k) => (A: v, B: f.VertexIndices[(k + 1) % f.VertexIndices.Length])))
            .Where(e => OnSeam(e.A) && OnSeam(e.B))
            .GroupBy(e => string.CompareOrdinal(Key(e.A), Key(e.B)) < 0 ? Key(e.A) + Key(e.B) : Key(e.B) + Key(e.A))
            .Select(g => g.Count()).ToList();

        Assert.Equal(46639 - 464 + 1028, lod.Polygons.Faces.Length);
        Assert.Equal(16, seamEdges.Count);
        Assert.All(seamEdges, uses => Assert.Equal(2, uses));
        Assert.Equal(5.059f, pos.Max(p => Frame.Radius(p) < 0.2f ? Frame.Along(p) : 0), 0.01f);
    }

    [Fact]
    public void MovesTheMuzzleMemoryPointWithTheBrake()
    {
        float Muzzle(byte[] model)
        {
            var mem = Lod(OdolFile.Parse(model, "m119"), "memory");
            return Frame.Along(mem.Vertices[OdolGraph.Selection(mem, "usti_hlavne", "m119").SelectedVertices[0]]);
        }

        Assert.Equal(Muzzle(M119()) + 0.74f, Muzzle(Apply(M119(), StretchMemory)), 0.001f);
    }

    [Fact]
    public void RefusesARemovalThatMatchesTheWrongFaceCount()
    {
        var e = Assert.Throws<PatchException>(() => Apply(M119(), RemoveBrake.Replace("\"faces\":464", "\"faces\":463")));

        Assert.Contains("matches 464 faces", e.Message);
    }

    [Fact]
    public void RefusesABrakeThatDoesNotMeetTheBarrel()
    {
        var e = Assert.Throws<PatchException>(() => Apply(M119(), RemoveBrake, Stretch, StretchBore, AddBrake.Replace("\"attachAlong\":4.5742", "\"attachAlong\":4.60")));

        Assert.Contains("within 6 mm", e.Message);
    }
}
