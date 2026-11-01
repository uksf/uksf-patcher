using BIS.Core;
using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class LodSelectionTests : IDisposable
{
    private const string Split = """{"op":"split-selection","lod":"hitpoints","selection":"motor","into":"motor_2","axis":0,"keepBelow":0}""";
    private const string Copy = """{"op":"copy-selection-points","lod":"hitpoints","name":"hit_rudder","fromLod":"geometryFire","from":"rudder","shrink":0.6,"points":9}""";

    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static byte[] Typhoon() => Models.Load(Models.EawsPbo, Models.EawsFuselage);

    private static LOD HitPoints(byte[] model) => OdolFile.Parse(model, "typhoon").Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution("hitpoints")));

    private static int[] Points(LOD lod, string name) => lod.NamedSelections.Single(n => n.Name == name).SelectedVertices.ToArray();

    [Fact]
    public void SplitsTheTyphoonEngineIntoLeftAndRightHalves()
    {
        var after = HitPoints(Apply(Typhoon(), Split));

        var left = Points(after, "motor");
        var right = Points(after, "motor_2");
        Assert.Equal((14, 13), (left.Length, right.Length));
        Assert.All(left, v => Assert.True(after.Vertices[v].X < 0));
        Assert.All(right, v => Assert.True(after.Vertices[v].X >= 0));
    }

    [Fact]
    public void CopiesTheRudderFireGeometryIntoTheHitPointsLodAroundItsCentre()
    {
        var model = Typhoon();
        var before = HitPoints(model);
        var after = HitPoints(Apply(model, Copy));

        var added = Points(after, "hit_rudder");
        Assert.Equal(9, added.Length);
        Assert.Equal(before.Vertices.Count + 9, after.Vertices.Count);
        Assert.Empty(after.Polygons.Faces);
        var fire = OdolFile.Parse(model, "typhoon").Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution("geometryFire")));
        var src = Points(fire, "rudder").Distinct().Select(v => fire.Vertices[v]).ToArray();
        var cz = src.Average(p => p.Z);
        Assert.Equal(cz, after.Vertices[added[0]].Z, 3);
        var spread = src.Max(p => p.Y) - src.Min(p => p.Y);
        var copied = added.Skip(1).Max(v => after.Vertices[v].Y) - added.Skip(1).Min(v => after.Vertices[v].Y);
        Assert.Equal(spread * 0.6f, copied, 3);
    }

    [Fact]
    public void RefusesASplitThatLeavesOneSideEmpty()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Typhoon(), Split.Replace("\"keepBelow\":0", "\"keepBelow\":100")));

        Assert.Contains("leaves one side empty", e.Message);
    }

    private static byte[] WithMotor(Action<NamedSelection> change)
    {
        var odol = OdolFile.Parse(Typhoon(), "typhoon");
        var lod = odol.Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution("hitpoints")));
        change(lod.NamedSelections.Single(n => n.Name == "motor"));
        return OdolFile.Serialise(odol);
    }

    [Fact]
    public void RefusesAWeightedSelection()
    {
        var model = WithMotor(s =>
        {
            var weights = Enumerable.Range(0, s.SelectedVertices.Count()).Select(i => (byte)(10 + i)).ToArray();
            LibraryAccess.Set(s, nameof(NamedSelection.SelectedVerticesWeights), new TrackedArray<byte>(weights));
            LibraryAccess.Set(s, nameof(NamedSelection.ExpectedSize), weights.Length);
        });

        var e = Assert.Throws<PatchException>(() => Apply(model, Split));

        Assert.Contains("has vertex weights", e.Message);
    }

    [Fact]
    public void RefusesASelectionWithSections()
    {
        var model = WithMotor(s => LibraryAccess.Set(s, nameof(NamedSelection.Sections), new TrackedArray<int>([0])));

        var e = Assert.Throws<PatchException>(() => Apply(model, Split));

        Assert.Contains("has sections", e.Message);
    }

    [Fact]
    public void RefusesToCopyOverAnExistingSelection()
    {
        var e = Assert.Throws<PatchException>(() => Apply(Typhoon(), Copy.Replace("hit_rudder", "motor")));

        Assert.Contains("already has selection", e.Message);
    }
}
