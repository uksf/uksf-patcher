using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class RebuildShadowVolumeOp : OdolOp
{
    private static readonly Type NeighbourType = typeof(LOD).Assembly.GetType("BIS.P3D.ODOL.VertexNeighborInfo", throwOnError: true)!;

    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var source = FindLod(odol, OpJson.String(op, "from"), context);
        var volume = FindLod(odol, OpJson.String(op, "lod"), context);
        RequireSupported(source, volume, context);
        var faces = source.Polygons.Faces;
        if (faces.Any(f => f.VertexIndices.Length != 3))
        {
            throw new PatchException($"{context.AssetPath}: rebuild-shadow-volume needs a triangulated source LOD");
        }

        var origin = new List<int>();
        var corners = faces.Select(f => f.VertexIndices.Select(v =>
        {
            origin.Add(v);
            return origin.Count - 1;
        }).ToArray()).ToArray();
        if (origin.Count > ushort.MaxValue)
        {
            throw new PatchException($"{context.AssetPath}: a shadow volume of {origin.Count} vertices overflows the 16-bit neighbour index");
        }

        var pos = source.Vertices.Select(v => v.Vector3).ToArray();
        var edges = Enumerable.Range(0, faces.Length).SelectMany(f => Enumerable.Range(0, 3).Select(k => (F: f, K: k)))
            .ToLookup(c => (pos[faces[c.F].VertexIndices[c.K]], pos[faces[c.F].VertexIndices[(c.K + 1) % 3]]));
        var ranges = OdolFaces.SectionFaceRanges(source, context.AssetPath);
        var newFaces = new List<Polygon>();
        var newRanges = new (int First, int End)[ranges.Length];
        for (var s = 0; s < ranges.Length; s++)
        {
            var first = newFaces.Count;
            for (var f = ranges[s].First; f < ranges[s].End; f++)
            {
                var c = corners[f];
                newFaces.Add(OdolFaces.NewFace(c));
                foreach (var k in new[] { 2, 0, 1 })
                {
                    var twins = edges[(pos[origin[c[(k + 1) % 3]]], pos[origin[c[k]]])].ToList();
                    if (twins.Count > 1)
                    {
                        throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(source.Resolution)} has a non-manifold edge at {pos[origin[c[k]]]}");
                    }

                    if (twins.Count == 1)
                    {
                        newFaces.Add(OdolFaces.NewFace(c[k], corners[twins[0].F][(twins[0].K + 1) % 3], c[(k + 1) % 3]));
                    }
                }
            }

            newRanges[s] = (first, newFaces.Count);
        }

        var bones = OdolGraph.BoneRefs(source);
        var neighbours = Array.CreateInstance(NeighbourType, bones.Count == 0 ? 0 : origin.Count);
        for (var f = 0; f < corners.Length && bones.Count > 0; f++)
        {
            for (var k = 0; k < 3; k++)
            {
                neighbours.SetValue(Neighbour(corners[f][(k + 1) % 3], corners[f][(k + 2) % 3], bones, origin), corners[f][k]);
            }
        }

        LibraryAccess.Set(volume, nameof(LOD.Vertices), new TrackedArray<Vector3P>(origin.Select(v => source.Vertices[v])));
        LibraryAccess.Set(volume, nameof(LOD.NormalsCompressed), new TrackedArray<Vector3PCompressed>(origin.Select(v => source.NormalsCompressed[v])));
        LibraryAccess.Set(volume, nameof(LOD.Clip), new TrackedArray<int>(origin.Select(v => source.Clip[v])));
        if (bones.Count > 0)
        {
            OdolGraph.SetBoneRefs(volume, origin.Select(v => OdolGraph.Clone(bones[v])).ToList());
        }

        LibraryAccess.Set(volume, "NeighborBoneRef", Activator.CreateInstance(typeof(TrackedArray<>).MakeGenericType(NeighbourType), neighbours));
        LibraryAccess.Set(volume.UvSets[0], nameof(UVSet.NVertices), (uint)origin.Count);
        LibraryAccess.Set(volume, nameof(LOD.VertexCount), (uint)origin.Count);
        OdolFaces.Replace(volume, newFaces.ToArray(), newRanges);
        OdolBounds.Grow(odol, volume);
    }

    private static object Neighbour(int a, int b, List<object> bones, List<int> origin)
    {
        var n = OdolGraph.New(NeighbourType);
        LibraryAccess.Set(n, "PosA", (ushort)a);
        LibraryAccess.Set(n, "Unused1", new byte[2]);
        LibraryAccess.Set(n, "RtwA", OdolGraph.Clone(bones[origin[a]]));
        LibraryAccess.Set(n, "PosB", (ushort)b);
        LibraryAccess.Set(n, "Unused2", new byte[2]);
        LibraryAccess.Set(n, "RtwB", OdolGraph.Clone(bones[origin[b]]));
        return n;
    }

    private static bool Unskinned(LOD source, LOD volume) =>
        source.SubSkeletonsToSkeleton.Length == 0 && OdolGraph.BoneRefs(source).Count == 0 && OdolGraph.BoneRefs(volume).Count == 0;

    private static void RequireSupported(LOD source, LOD volume, OpContext context)
    {
        string? Problem() =>
            volume.Polygons.Faces.Length == 0 ? "the target LOD is empty"
            : volume.UvSets.Length != 1 || !volume.UvSets[0].DefaultFill ? "the target LOD has real UVs"
            : volume.STCoordsCompressed.Count != 0 || source.STCoordsCompressed.Count != 0 ? "a LOD has tangents"
            : volume.VertexToPoint.Count != 0 || source.VertexToPoint.Count != 0 ? "a LOD has a vertex-to-point map"
            : LibraryAccess.Get<Array?>(volume, "Frames")?.Length > 0 || LibraryAccess.Get<Array?>(source, "Frames")?.Length > 0 ? "a LOD has keyframes"
            : !volume.SubSkeletonsToSkeleton.SequenceEqual(source.SubSkeletonsToSkeleton) ? "the LODs map bones differently"
            : !Unskinned(source, volume) && OdolGraph.BoneRefs(source).Count != source.Vertices.Count ? "the source LOD lacks per-vertex bones"
            : source.Clip.Count != source.Vertices.Count ? "the source LOD lacks per-vertex clip flags"
            : volume.Sections.Length != source.Sections.Length ? "the LODs have different section counts"
            : volume.NamedSelections.Concat(source.NamedSelections).Any(s => s.SelectedFaces.Count > 0 || s.SelectedVertices.Count > 0) ? "a named selection holds faces or vertices"
            : null;
        if (Problem() is { } problem)
        {
            throw new PatchException($"{context.AssetPath}: rebuild-shadow-volume does not support this model: {problem}");
        }
    }
}
