using System.Reflection;
using System.Text.Json;
using BIS.Core.Math;
using BIS.Core.Streams;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddPhysxOp : OdolOp
{
    private const float GeometryRes = 1e13f, PhysxRes = 4e13f;
    private const byte NoLod = 255;
    public const string PhysxIndexField = "Geometry";
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] SpecialLodIndices =
    [
        "GeometrySimple", "GeometryPhys", "Memory", "Geometry", "GeometryFire", "GeometryView",
        "GeometryViewPilot", "GeometryViewGunner", "GeometryViewCargo", "LandContact", "Roadway", "Paths", "Hitpoints",
    ];

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var mass = OpJson.Float(op, "mass");
        if (!float.IsFinite(mass) || mass <= 0 || !float.IsFinite(1 / mass))
        {
            throw new PatchException($"{context.AssetPath}: add-physx mass must be a positive number");
        }

        if (odol.Lods.Any(l => l.Resolution == PhysxRes))
        {
            throw new PatchException($"{context.AssetPath}: model already has a PhysX LOD");
        }

        var g = Array.FindIndex(odol.Lods, l => l.Resolution == GeometryRes);
        if (g < 0)
        {
            throw new PatchException($"{context.AssetPath}: model has no geometry LOD");
        }

        var at = g + 1;
        var mi = odol.ModelInfo;
        var moved = SpecialLodIndices.Append("MinShadow").Select(name => LibraryAccess.Get<byte>(mi, name)).Where(v => v != NoLod && v >= at);
        if (at >= NoLod || moved.Any(v => v + 1 >= NoLod))
        {
            throw new PatchException($"{context.AssetPath}: add-physx would push a special LOD index to {NoLod}, which means no LOD");
        }

        LibraryAccess.Set(odol, nameof(ODOL.Lods), Insert(odol.Lods, at, CloneLod(odol, odol.Lods[g], PhysxRes)));
        foreach (var name in SpecialLodIndices.Append("MinShadow"))
        {
            var v = LibraryAccess.Get<byte>(mi, name);
            if (v != NoLod && v >= at)
            {
                LibraryAccess.Set(mi, name, (byte)(v + 1));
            }
        }

        LibraryAccess.Set(mi, PhysxIndexField, (byte)at);

        foreach (var name in new[] { "PreferredShadowVolumeLod", "PreferredShadowBufferLod", "PreferredShadowBufferLodVis" })
        {
            var a = LibraryAccess.Get<int[]?>(mi, name);
            if (a == null)
            {
                continue;
            }

            var shifted = a.Select(x => x >= at ? x + 1 : x).ToList();
            shifted.Insert(at, -1);
            LibraryAccess.Set(mi, name, shifted.ToArray());
        }

        var an = odol.Animations;
        if (an != null)
        {
            LibraryAccess.Set(an, nameof(Animations.Bones2Anims), Insert(an.Bones2Anims, at, an.Bones2Anims[g]));
            LibraryAccess.Set(an, nameof(Animations.Anims2Bones), Insert(an.Anims2Bones, at, (int[])an.Anims2Bones[g].Clone()));
            LibraryAccess.Set(an, nameof(Animations.AxisData), Insert(an.AxisData, at, (Vector3P[][])an.AxisData[g].Clone()));
        }

        var old = mi.Mass;
        LibraryAccess.Set(mi, "Mass", mass);
        LibraryAccess.Set(mi, "InvMass", 1 / mass);
        var inv = LibraryAccess.Get<Vector3P[]?>(mi, "InvInertia");
        if (inv != null && old > 0)
        {
            var scaled = inv.Select(v => new Vector3P(v.X * old / mass, v.Y * old / mass, v.Z * old / mass)).ToArray();
            if (scaled.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
            {
                throw new PatchException($"{context.AssetPath}: add-physx mass {mass} gives a non-finite inverse inertia");
            }

            LibraryAccess.Set(mi, "InvInertia", scaled);
        }
    }

    private static LOD CloneLod(ODOL odol, LOD src, float resolution)
    {
        var info = typeof(LOD).GetProperty("LoadableLodInfo", InstanceAny)!.GetValue(src)!;
        using var m = new MemoryStream();
        var w = new BinaryWriterEx(m) { UseLZOCompression = true, UseCompressionFlag = true };
        typeof(LOD).GetMethod("Write", InstanceAny)!.Invoke(src, [w, odol.Version]);
        w.Flush();
        m.Position = 0;
        var r = new BinaryReaderEx(m) { UseLZOCompression = true, UseCompressionFlag = true };
        var ctor = typeof(LOD).GetConstructors(InstanceAny).Single(c => c.GetParameters().Length == 4);
        LOD clone = null!;
        OdolFile.WithLibraryTraceSilenced(() => clone = (LOD)ctor.Invoke([r, resolution, info, (int)odol.Version]));
        return clone;
    }

    private static T[] Insert<T>(T[] a, int at, T item) => a.Take(at).Append(item).Concat(a.Skip(at)).ToArray();
}
