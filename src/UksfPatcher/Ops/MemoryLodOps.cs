using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public static class MemoryLod
{
    private const int ClipAll = 63;
    private const int ProxyNormal = 525312;
    private const uint ProxySectionSpecial = 268443648;

    public static int AppendVertex(LOD mem, Vector3 p, int clip, Vector3PCompressed normal, object boneRef, List<object> boneRefs)
    {
        if (mem.UvSets.Any(s => !s.DefaultFill))
        {
            throw new PatchException("memory LOD has per-vertex UV data, which memory edits do not support");
        }

        var v = mem.Vertices.Count;
        LibraryAccess.Set(mem, nameof(LOD.Vertices), new TrackedArray<Vector3P>(mem.Vertices.Append(new Vector3P(p.X, p.Y, p.Z))));
        LibraryAccess.Set(mem, nameof(LOD.Clip), new TrackedArray<int>(mem.Clip.Append(clip)));
        LibraryAccess.Set(mem, nameof(LOD.NormalsCompressed), new TrackedArray<Vector3PCompressed>(mem.NormalsCompressed.Append(normal)));
        boneRefs.Add(boneRef);
        LibraryAccess.Set(mem, nameof(LOD.VertexCount), (uint)(v + 1));
        foreach (var set in mem.UvSets)
        {
            LibraryAccess.Set(set, nameof(UVSet.NVertices), (uint)(v + 1));
        }
        return v;
    }

    public static int AppendPoint(LOD mem, Vector3 p, List<object> boneRefs) =>
        AppendVertex(mem, p, ClipAll, new Vector3PCompressed(0), OdolGraph.EmptyBoneRef(), boneRefs);

    public static int AppendProxyVertex(LOD mem, Vector3 p, List<object> boneRefs) =>
        AppendVertex(mem, p, ClipAll, new Vector3PCompressed(ProxyNormal), OdolGraph.EmptyBoneRef(), boneRefs);

    public static NamedSelection NewSelection(string name, int[] faces, int[] vertices)
    {
        var ns = new NamedSelection(name, false, []);
        LibraryAccess.Set(ns, nameof(NamedSelection.SelectedFaces), new TrackedArray<int>(faces));
        LibraryAccess.Set(ns, nameof(NamedSelection.SelectedVertices), new TrackedArray<int>(vertices));
        return ns;
    }

    public static Section NewProxySection(int faceBytes)
    {
        var s = (Section)OdolGraph.New(typeof(Section));
        LibraryAccess.Set(s, nameof(Section.FaceLowerIndex), 0);
        LibraryAccess.Set(s, nameof(Section.FaceUpperIndex), faceBytes);
        LibraryAccess.Set(s, nameof(Section.TextureIndex), (short)-1);
        LibraryAccess.Set(s, nameof(Section.Special), ProxySectionSpecial);
        LibraryAccess.Set(s, nameof(Section.MaterialIndex), -1);
        LibraryAccess.Set(s, nameof(Section.Material), "");
        LibraryAccess.Set(s, nameof(Section.AreaOverTex), new[] { 1f, -1000f });
        return s;
    }
}

public sealed class AddMemoryProxiesOp : OdolOp
{
    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var filter = OpJson.String(op, "filter");
        var src = FindLod(odol, "0", context);
        var mem = FindLod(odol, "memory", context);
        if (mem.Polygons.Faces.Length != 0 || mem.Sections.Length != 0)
        {
            throw new PatchException($"{context.AssetPath}: memory LOD already has faces");
        }

        var sources = OdolGraph.RawProxies(src).Cast<object>()
            .Where(p => LibraryAccess.Get<string>(p, "ProxyModel").Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => LibraryAccess.Get<int>(p, "SequenceID")).ToList();
        if (sources.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: no LOD 0 proxies match '{filter}'");
        }

        var boneRefs = OdolGraph.BoneRefs(mem);
        var faces = new List<Polygon>();
        var selections = mem.NamedSelections.ToList();
        var proxies = new List<object>();
        foreach (var sp in sources)
        {
            var matrix = LibraryAccess.Get<Matrix4P>(sp, "Transformation");
            var m = matrix.Matrix;
            var pos = new Vector3(m.M41, m.M42, m.M43);
            var v0 = MemoryLod.AppendProxyVertex(mem, pos, boneRefs);
            MemoryLod.AppendProxyVertex(mem, pos + new Vector3(m.M21, m.M22, m.M23), boneRefs);
            MemoryLod.AppendProxyVertex(mem, pos + 0.5f * new Vector3(m.M31, m.M32, m.M33), boneRefs);
            faces.Add(OdolFaces.NewFace(v0 + 1, v0, v0 + 2));

            var seq = LibraryAccess.Get<int>(sp, "SequenceID");
            var model = LibraryAccess.Get<string>(sp, "ProxyModel");
            selections.Add(MemoryLod.NewSelection($"proxy:{model}.{seq:000}", [faces.Count - 1], [v0, v0 + 1, v0 + 2]));

            var proxy = OdolGraph.New(OdolGraph.ProxyType);
            LibraryAccess.Set(proxy, "ProxyModel", model);
            LibraryAccess.Set(proxy, "Transformation", matrix);
            LibraryAccess.Set(proxy, "SequenceID", seq);
            LibraryAccess.Set(proxy, "NamedSelectionIndex", selections.Count - 1);
            LibraryAccess.Set(proxy, "BoneIndex", -1);
            LibraryAccess.Set(proxy, "SectionIndex", 0);
            proxies.Add(proxy);
        }

        var faceBytes = faces.Sum(OdolFaces.Weight);
        var polys = (Polygons)OdolGraph.Clone(mem.Polygons);
        LibraryAccess.Set(polys, nameof(Polygons.Faces), faces.ToArray());
        LibraryAccess.Set(polys, nameof(Polygons.Unused1), (uint)faceBytes);
        LibraryAccess.Set(polys, nameof(Polygons.Unused2), (ushort)0);
        OdolGraph.SetRawProxies(mem, proxies);
        OdolGraph.SetBoneRefs(mem, boneRefs);
        LibraryAccess.Set(mem, nameof(LOD.Polygons), polys);
        LibraryAccess.Set(mem, nameof(LOD.Sections), new[] { MemoryLod.NewProxySection(faceBytes) });
        mem.NamedSelections = selections.ToArray();
        if (mem.Textures.Length == 0)
        {
            LibraryAccess.Set(mem, nameof(LOD.Textures), new[] { "" });
        }
    }
}

public sealed class AddMemoryPointOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "name");
        var p = OpJson.Floats(op, "position", 3);
        var mem = FindLod(odol, "memory", context);
        if (mem.NamedSelections.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: memory LOD already has selection '{name}'");
        }

        var boneRefs = OdolGraph.BoneRefs(mem);
        var v = MemoryLod.AppendPoint(mem, new Vector3(p[0], p[1], p[2]), boneRefs);
        OdolGraph.SetBoneRefs(mem, boneRefs);
        mem.NamedSelections = mem.NamedSelections.Append(MemoryLod.NewSelection(name, [], [v])).ToArray();
    }
}
