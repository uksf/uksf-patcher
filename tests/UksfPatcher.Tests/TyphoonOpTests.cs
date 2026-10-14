using BIS.P3D.ODOL;
using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;
using Xunit.Abstractions;

namespace UksfPatcher.Tests;

public sealed class TyphoonOpTests(ITestOutputHelper log) : IDisposable
{
    private readonly string _dir = TestData.TempDir();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Fuselage() => Models.Load(Models.EawsPbo, Models.EawsFuselage);

    private byte[] Apply(byte[] model, string json) => OpRegistry.Apply(model, TestData.Op(json), TestData.Context(_dir));

    private static ODOL Parse(byte[] model) => OdolFile.Parse(model, "typhoon");

    private static LOD Lod(ODOL odol, float resolution) => odol.Lods.Single(l => LodNames.Matches(l.Resolution, resolution));

    [Fact]
    public void SetAnimationSourceMovesTheHookOffTheGear()
    {
        var after = Parse(Apply(Fuselage(), """{"op":"set-animation-source","animation":"vrtule_0","to":"rotor","address":"loop"}"""));
        after = Parse(Apply(OdolFile.Serialise(after), """{"op":"set-animation-source","animation":"towhook","to":"hook"}"""));

        var hook = after.Animations.AnimationClasses.Single(a => a.AnimName == "towhook");
        var fan = after.Animations.AnimationClasses.Single(a => a.AnimName == "vrtule_0");
        log.WriteLine($"towhook {hook.AnimSource}/{hook.SourceAddress}, vrtule_0 {fan.AnimSource}/{fan.SourceAddress}");
        Assert.Equal(("hook", 0u), (hook.AnimSource, hook.SourceAddress));
        Assert.Equal(("rotor", 2u), (fan.AnimSource, fan.SourceAddress));
        Assert.Throws<PatchException>(() => Apply(Fuselage(), """{"op":"set-animation-source","animation":"nope","to":"hook"}"""));
        Assert.Throws<PatchException>(() => Apply(Fuselage(), """{"op":"set-animation-source","animation":"towhook","to":"hook","address":"wrap"}"""));
    }

    [Fact]
    public void BindSelectionWeightsOnlyTheBoxedTurbineDisc()
    {
        var before = Lod(Parse(Fuselage()), 0);
        var after = Lod(Parse(Apply(Fuselage(), """{"op":"bind-selection","lod":"0","selection":"#8","bone":"vrtule 0","box":[0,1.2,-1,1,4,4.4]}""")), 0);

        var changed = Enumerable.Range(0, before.Vertices.Count)
            .Where(v => OdolGraph.BoneCount(OdolGraph.BoneRefs(before)[v]) != OdolGraph.BoneCount(OdolGraph.BoneRefs(after)[v])).ToList();
        log.WriteLine($"{changed.Count} vertices bound, x {changed.Min(v => after.Vertices[v].X):F3}..{changed.Max(v => after.Vertices[v].X):F3}");
        Assert.NotEmpty(changed);
        Assert.All(changed, v => Assert.InRange(after.Vertices[v].X, 0f, 1.2f));
        Assert.All(changed, v => Assert.Equal(1, OdolGraph.BoneCount(OdolGraph.BoneRefs(after)[v])));
    }

    [Fact]
    public void BindSelectionRefusesVerticesThatAlreadyHaveWeights()
    {
        var once = Apply(Fuselage(), """{"op":"bind-selection","lod":"0","selection":"#8","bone":"vrtule 0","box":[0,1.2,-1,1,4,4.4]}""");

        var e = Assert.Throws<PatchException>(() => Apply(once, """{"op":"bind-selection","lod":"0","selection":"#8","bone":"vrtule 1","box":[0,1.2,-1,1,4,4.4]}"""));

        Assert.Contains("already has bone weights", e.Message);
    }

    [Fact]
    public void RemapAndMemoryProxiesGiveTheFirstPylonAMemoryProxy()
    {
        var remapped = Apply(Fuselage(), """{"op":"remap-proxies","rules":[{"model":"\\a3\\weapons_f\\ammo\\bomb_01_f","id":1,"toModel":"\\a3\\weapons_f\\dynamicloadout\\pylonpod_3x_missile_agm_02_f","toId":1}]}""");
        var after = Parse(Apply(remapped, """{"op":"add-memory-proxies","filter":"pylonpod"}"""));

        var mem = Lod(after, 1e15f);
        var proxies = OdolGraph.RawProxies(mem).Cast<object>().ToList();
        var name = mem.NamedSelections[LibraryAccess.Get<int>(proxies.Single(), "NamedSelectionIndex")].Name;
        log.WriteLine($"memory LOD: {proxies.Count} proxy '{name}', {mem.Polygons.Faces.Length} face, {mem.Vertices.Count} vertices");
        Assert.Equal(@"proxy:\a3\weapons_f\dynamicloadout\pylonpod_3x_missile_agm_02_f.001", name);
        Assert.Single(mem.Polygons.Faces);
        Assert.Equal(Lod(Parse(Fuselage()), 1e15f).Vertices.Count + 3, mem.Vertices.Count);
        Assert.Throws<PatchException>(() => Apply(OdolFile.Serialise(after), """{"op":"add-memory-proxies","filter":"pylonpod"}"""));
        Assert.Contains("no proxy matches", Assert.Throws<PatchException>(() => Apply(Fuselage(),
            """{"op":"remap-proxies","rules":[{"model":"\\a3\\nothing","id":1,"toModel":"\\a3\\x","toId":1}]}""")).Message);
    }

    [Fact]
    public void AddMemoryPointPlacesANamedVertexOnce()
    {
        var after = Apply(Fuselage(), """{"op":"add-memory-point","name":"Wheel_1_center","position":[-0.005,-1.649,-2.486]}""");

        var mem = Lod(Parse(after), 1e15f);
        var point = mem.NamedSelections.Single(n => n.Name == "Wheel_1_center");
        var v = mem.Vertices[point.SelectedVertices.Single()];
        log.WriteLine($"Wheel_1_center at {v.X} {v.Y} {v.Z}");
        Assert.Equal((-0.005f, -1.649f, -2.486f), (v.X, v.Y, v.Z));
        Assert.Throws<PatchException>(() => Apply(after, """{"op":"add-memory-point","name":"wheel_1_center","position":[0,0,0]}"""));
    }

    [Fact]
    public void RetextureAndMoveSectionsRetargetOnlyTheNamedTexture()
    {
        var retextured = Apply(Fuselage(), """{"op":"retexture","from":"eaws_ef2000\\data\\sides.paa","to":"u\\x\\sides_co.paa"}""");
        var moved = Parse(Apply(retextured, """{"op":"move-sections","lod":"viewPilot","texture":"eaws_ef2000\\data\\top.paa","newSelection":"camo_pilot","from":["camo1"]}"""));

        var pilot = Lod(moved, 1100);
        var camoPilot = pilot.NamedSelections.Single(n => n.Name == "camo_pilot").Sections.ToList();
        log.WriteLine($"camo_pilot sections [{string.Join(',', camoPilot)}]");
        Assert.All(moved.Lods, l => Assert.DoesNotContain(@"eaws_ef2000\data\sides.paa", l.Textures));
        Assert.Contains(@"u\x\sides_co.paa", moved.Lods[0].Textures);
        Assert.NotEmpty(camoPilot);
        Assert.Empty(pilot.NamedSelections.Single(n => n.Name == "camo1").Sections.Intersect(camoPilot));
        Assert.Throws<PatchException>(() => Apply(Fuselage(), """{"op":"retexture","from":"none.paa","to":"x.paa"}"""));
    }

    [Fact]
    public void CutSeamsDuplicatesAVertexAndRepointsItsFace()
    {
        var lod = Lod(Parse(Fuselage()), 0);
        var face = lod.Polygons.Faces[0].VertexIndices;
        var n0 = lod.Vertices.Count;
        var plan = $$$"""{"copies":[{{{face[0]}}}],"faces":[{"verts":[{{{string.Join(',', face)}}}],"map":{"{{{face[0]}}}":{{{n0}}}}}]}""";
        File.WriteAllText(Path.Join(_dir, "cuts.json"), plan);

        var after = Lod(Parse(Apply(Fuselage(), """{"op":"cut-seams","lod":"0","source":"cuts.json"}""")), 0);

        Assert.Equal(n0 + 1, after.Vertices.Count);
        Assert.Equal(n0, after.Polygons.Faces[0].VertexIndices[0]);
        Assert.Equal(lod.Vertices[face[0]].X, after.Vertices[n0].X);
        Assert.Equal(lod.UvSets[0].GetUV()[face[0]].X, after.UvSets[0].GetUV()[n0].X, 3);
    }

    [Fact]
    public void UvSplitMovesASectionOntoTheNewSheet()
    {
        var lod = Lod(Parse(Fuselage()), 0);
        var section = lod.NamedSelections.Single(n => n.Name == "zbytek").Sections
            .First(s => SectionIsIsolated(lod, s));
        var verts = OdolGraph.FacesInSection(lod, lod.Sections[section]).SelectMany(f => lod.Polygons.Faces[f].VertexIndices).Distinct().ToList();
        File.WriteAllText(Path.Join(_dir, "plan.json"), $$"""{"islands":[{"sheet":"B","box":[0,0],"pos":[0,0],"s":0.5,"verts":[{{string.Join(',', verts)}}]}]}""");

        var after = Lod(Parse(Apply(Fuselage(), """{"op":"uv-split","lod":"0","source":"plan.json","selection":"zbytek","newSelection":"formation_on","texture":"u\\x\\b.paa"}""")), 0);

        var moved = after.NamedSelections.Single(n => n.Name == "formation_on").Sections.Single();
        log.WriteLine($"section {section} ({verts.Count} vertices) -> formation_on section {moved}");
        Assert.Equal(@"u\x\b.paa", after.Textures[after.Sections[moved].TextureIndex]);
        Assert.DoesNotContain(moved, after.NamedSelections.Single(n => n.Name == "zbytek").Sections);
        Assert.Equal(lod.Sections.Length, after.Sections.Length);
        OdolFaces.RequirePartitions(Parse(OdolFile.Serialise(Parse(Apply(Fuselage(), """{"op":"uv-split","lod":"0","source":"plan.json","selection":"zbytek","newSelection":"formation_on","texture":"u\\x\\b.paa"}""")))), "typhoon");
    }

    [Fact]
    public void AddPhysxClonesTheGeometryLodAndSetsTheMass()
    {
        var before = Parse(Fuselage());
        var after = Parse(Apply(Fuselage(), """{"op":"add-physx","mass":11000}"""));

        var physx = after.Lods.Single(l => l.Resolution == 4e13f);
        log.WriteLine($"LODs {before.Lods.Length} -> {after.Lods.Length}, mass {before.ModelInfo.Mass} -> {after.ModelInfo.Mass}");
        Assert.Equal(before.Lods.Length + 1, after.Lods.Length);
        Assert.Equal(Lod(before, 1e13f).Vertices.Count, physx.Vertices.Count);
        Assert.Equal(11000f, after.ModelInfo.Mass);
        Assert.Throws<PatchException>(() => Apply(OdolFile.Serialise(after), """{"op":"add-physx","mass":11000}"""));
    }

    private static bool SectionIsIsolated(LOD lod, int s)
    {
        var own = OdolGraph.FacesInSection(lod, lod.Sections[s]).ToHashSet();
        var verts = own.SelectMany(f => lod.Polygons.Faces[f].VertexIndices).ToHashSet();
        return own.Count > 0 && !Enumerable.Range(0, lod.Polygons.Faces.Length).Where(f => !own.Contains(f))
            .Any(f => lod.Polygons.Faces[f].VertexIndices.Any(verts.Contains));
    }
}
