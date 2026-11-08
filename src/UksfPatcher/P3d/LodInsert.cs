using System.Reflection;
using BIS.Core.Math;
using BIS.Core.Streams;
using BIS.P3D.ODOL;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class LodInsert
{
    public const byte NoLod = 255;

    private static readonly string[] IndexFields =
    [
        "GeometrySimple", "GeometryPhys", "Memory", "Geometry", "GeometryFire", "GeometryView",
        "GeometryViewPilot", "GeometryViewGunner", "GeometryViewCargo", "LandContact", "Roadway", "Paths", "Hitpoints", "MinShadow",
    ];

    private static readonly string[] ShadowArrays = ["PreferredShadowVolumeLod", "PreferredShadowBufferLod", "PreferredShadowBufferLodVis"];

    public static LOD InsertClone(ODOL odol, LOD source, float resolution, string indexField, string assetPath)
    {
        if (odol.Lods.Any(l => LodNames.Matches(l.Resolution, resolution)))
        {
            throw new PatchException($"{assetPath}: {indexField} LOD already exists");
        }

        var sourceIndex = Array.IndexOf(odol.Lods, source);
        var at = sourceIndex + 1;
        var mi = odol.ModelInfo;
        if (at >= NoLod || IndexFields.Select(name => LibraryAccess.Get<byte>(mi, name)).Any(v => v != NoLod && v >= at && v + 1 >= NoLod))
        {
            throw new PatchException($"{assetPath}: inserting a LOD would push a special LOD index to {NoLod}, which means no LOD");
        }

        LibraryAccess.Set(odol, nameof(ODOL.Lods), Insert(odol.Lods, at, Clone(odol, source, resolution)));
        foreach (var name in IndexFields)
        {
            var v = LibraryAccess.Get<byte>(mi, name);
            if (v != NoLod && v >= at)
            {
                LibraryAccess.Set(mi, name, (byte)(v + 1));
            }
        }

        LibraryAccess.Set(mi, indexField, (byte)at);
        foreach (var name in ShadowArrays)
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
            LibraryAccess.Set(an, nameof(Animations.Bones2Anims), Insert(an.Bones2Anims, at, an.Bones2Anims[sourceIndex]));
            LibraryAccess.Set(an, nameof(Animations.Anims2Bones), Insert(an.Anims2Bones, at, an.Anims2Bones[sourceIndex]));
            if (an.AxisData != null)
            {
                LibraryAccess.Set(an, nameof(Animations.AxisData), Insert(an.AxisData, at, (Vector3P[][])an.AxisData[sourceIndex].Clone()));
            }
        }

        return odol.Lods[at];
    }

    private static LOD Clone(ODOL odol, LOD source, float resolution)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var info = typeof(LOD).GetProperty("LoadableLodInfo", flags)!.GetValue(source)!;
        using var stream = new MemoryStream();
        var writer = new BinaryWriterEx(stream) { UseLZOCompression = true, UseCompressionFlag = true };
        typeof(LOD).GetMethod("Write", flags)!.Invoke(source, [writer, odol.Version]);
        writer.Flush();
        stream.Position = 0;
        var reader = new BinaryReaderEx(stream) { UseLZOCompression = true, UseCompressionFlag = true };
        var ctor = typeof(LOD).GetConstructors(flags).Single(c => c.GetParameters().Length == 4);
        LOD clone = null!;
        OdolFile.WithLibraryTraceSilenced(() => clone = (LOD)ctor.Invoke([reader, resolution, info, (int)odol.Version]));
        return clone;
    }

    private static T[] Insert<T>(T[] source, int index, T value)
    {
        var result = new T[source.Length + 1];
        Array.Copy(source, result, index);
        result[index] = value;
        Array.Copy(source, index, result, index + 1, source.Length - index);
        return result;
    }
}
