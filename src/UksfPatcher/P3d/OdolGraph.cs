using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BIS.Core;
using BIS.P3D.ODOL;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class OdolGraph
{
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static Type ProxyType { get; } = typeof(LOD).Assembly.GetType("BIS.P3D.ODOL.Proxy", throwOnError: true)!;

    public static Type BoneRefType { get; } = typeof(LOD).Assembly.GetType("BIS.P3D.ODOL.AnimationRTWeight", throwOnError: true)!;

    public static object Clone(object o) => typeof(object).GetMethod("MemberwiseClone", InstanceAny)!.Invoke(o, null)!;

    public static object New(Type type) => RuntimeHelpers.GetUninitializedObject(type);

    public static Array RawProxies(LOD lod) => LibraryAccess.Get<Array>(lod, "RawProxies");

    public static void SetRawProxies(LOD lod, IReadOnlyList<object> proxies) => LibraryAccess.Set(lod, "RawProxies", TypedArray(ProxyType, proxies));

    public static List<object> BoneRefs(LOD lod) => LibraryAccess.Get<IEnumerable>(lod, "VertexBoneRef").Cast<object>().ToList();

    public static void SetBoneRefs(LOD lod, IReadOnlyList<object> refs) =>
        LibraryAccess.Set(lod, "VertexBoneRef", Activator.CreateInstance(typeof(TrackedArray<>).MakeGenericType(BoneRefType), TypedArray(BoneRefType, refs)));

    public static object EmptyBoneRef()
    {
        var r = New(BoneRefType);
        LibraryAccess.Set(r, "Count", 0);
        LibraryAccess.Set(r, "Data", new byte[8]);
        return r;
    }

    public static int BoneCount(object boneRef) => LibraryAccess.Get<int>(boneRef, "Count");

    public static List<int> FacesInSection(LOD lod, Section s)
    {
        var faces = new List<int>();
        var offset = 0;
        for (var f = 0; f < lod.Polygons.Faces.Length; f++)
        {
            if (offset >= s.FaceLowerIndex && offset < s.FaceUpperIndex)
            {
                faces.Add(f);
            }

            offset += OdolFaces.Weight(lod.Polygons.Faces[f]);
        }

        return faces;
    }

    public static NamedSelection Selection(LOD lod, string name, string assetPath) =>
        lod.NamedSelections.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PatchException($"{assetPath}: LOD {LodNames.Name(lod.Resolution)} has no selection '{name}'");

    private static Array TypedArray(Type type, IReadOnlyList<object> items)
    {
        var array = Array.CreateInstance(type, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            array.SetValue(items[i], i);
        }

        return array;
    }
}
