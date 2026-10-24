using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class StretchAlongAxisOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var frame = AxialFrame.From(op, context);
        var bones = op.TryGetProperty("bones", out _) ? OpJson.Strings(op, "bones") : null;
        var maxRadius = op.TryGetProperty("maxRadius", out _) ? OpJson.Float(op, "maxRadius") : float.PositiveInfinity;
        var cut = OpJson.Float(op, "cut");
        var shift = frame.Direction * OpJson.Float(op, "length");
        var expected = OpJson.Int(op, "vertices", -1);
        var moved = 0;
        foreach (var name in OpJson.Strings(op, "lods"))
        {
            var lod = FindLod(odol, name, context);
            Func<int, bool> bound = bones == null ? _ => true : BoneWeights.BoundOnlyTo(odol, lod, bones, context);
            var positions = lod.Vertices.ToArray();
            for (var v = 0; v < positions.Length; v++)
            {
                if (bound(v) && frame.Along(positions[v]) > cut && frame.Radius(positions[v].Vector3) <= maxRadius)
                {
                    positions[v] = new Vector3P(positions[v].Vector3 + shift);
                    moved++;
                }
            }

            LibraryAccess.Set(lod, nameof(LOD.Vertices), new TrackedArray<Vector3P>(positions));
            if (bones != null)
            {
                MoveProxies(odol, lod, bones, frame, cut, shift);
            }

            OdolBounds.Grow(odol, lod);
        }

        if (moved != expected)
        {
            throw new PatchException($"{context.AssetPath}: stretch-along-axis moved {moved} vertices, the patch expects {expected}");
        }

        MoveMemoryPoints(odol, op, shift, context);
    }

    private static void MoveProxies(ODOL odol, LOD lod, IReadOnlyList<string> bones, AxialFrame frame, float cut, Vector3 shift)
    {
        var names = odol.ModelInfo.Skeleton.SkeletonBoneNames.Select(b => b.BoneName).ToArray();
        foreach (var proxy in OdolGraph.RawProxies(lod))
        {
            var bone = LibraryAccess.Get<int>(proxy, "BoneIndex");
            var matrix = LibraryAccess.Get<Matrix4P>(proxy, "Transformation").Matrix;
            if (bone < 0 || bone >= names.Length || !bones.Contains(names[bone], StringComparer.OrdinalIgnoreCase) || frame.Along(matrix.Translation) <= cut)
            {
                continue;
            }

            matrix.Translation += shift;
            LibraryAccess.Set(proxy, "Transformation", new Matrix4P(matrix));
        }
    }

    private static void MoveMemoryPoints(ODOL odol, JsonElement op, Vector3 shift, OpContext context)
    {
        if (!op.TryGetProperty("memoryPoints", out _))
        {
            return;
        }

        var mem = FindLod(odol, "memory", context);
        var vertices = OpJson.Strings(op, "memoryPoints")
            .SelectMany(n => OdolGraph.Selection(mem, n, context.AssetPath).SelectedVertices)
            .ToHashSet();
        var positions = mem.Vertices.ToArray();
        foreach (var v in vertices)
        {
            positions[v] = new Vector3P(positions[v].Vector3 + shift);
        }

        LibraryAccess.Set(mem, nameof(LOD.Vertices), new TrackedArray<Vector3P>(positions));
    }
}

public sealed class RemoveFacesOp : OdolOp
{
    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var frame = AxialFrame.From(op, context);
        Func<int, bool> bound = op.TryGetProperty("bones", out _) ? BoneWeights.BoundOnlyTo(odol, lod, OpJson.Strings(op, "bones"), context) : _ => true;
        var allBeyond = OpJson.Float(op, "allBeyond");
        var anyBeyond = OpJson.Float(op, "anyBeyond");
        var expected = OpJson.Int(op, "faces", -1);

        var faces = lod.Polygons.Faces;
        var along = lod.Vertices.Select(frame.Along).ToArray();
        var parts = op.TryGetProperty("wholeParts", out var w) && w.GetBoolean() ? Parts(lod, bound) : null;
        bool Doomed(Polygon f) =>
            f.VertexIndices.All(bound) && (parts?[f.VertexIndices[0]] ?? f.VertexIndices).All(v => along[v] > allBeyond)
            && (parts?[f.VertexIndices[0]] ?? f.VertexIndices).Any(v => along[v] > anyBeyond);

        var ranges = OdolFaces.SectionFaceRanges(lod, context.AssetPath);
        var keep = new List<Polygon>();
        var newIndex = new int[faces.Length];
        var newRanges = new (int First, int End)[ranges.Length];
        for (var s = 0; s < ranges.Length; s++)
        {
            var first = keep.Count;
            for (var f = ranges[s].First; f < ranges[s].End; f++)
            {
                newIndex[f] = Doomed(faces[f]) ? -1 : keep.Count;
                if (newIndex[f] >= 0)
                {
                    keep.Add(faces[f]);
                }
            }

            newRanges[s] = (first, keep.Count);
        }

        var removed = faces.Length - keep.Count;
        if (removed != expected)
        {
            throw new PatchException($"{context.AssetPath}: remove-faces matches {removed} faces in LOD {LodNames.Name(lod.Resolution)}, the patch expects {expected}");
        }

        foreach (var selection in lod.NamedSelections.Where(n => n.SelectedFaces.Count > 0))
        {
            OdolFaces.SetSelectedFaces(selection, selection.SelectedFaces.Select(f => newIndex[f]).Where(f => f >= 0));
        }

        OdolFaces.Replace(lod, keep.ToArray(), newRanges);
    }

    private static int[][] Parts(LOD lod, Func<int, bool> bound)
    {
        var parent = Enumerable.Range(0, lod.Vertices.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                x = parent[x] = parent[parent[x]];
            }

            return x;
        }

        var byPosition = new Dictionary<Vector3, int>();
        for (var v = 0; v < parent.Length; v++)
        {
            if (bound(v) && !byPosition.TryAdd(lod.Vertices[v].Vector3, v))
            {
                parent[Find(v)] = Find(byPosition[lod.Vertices[v].Vector3]);
            }
        }

        foreach (var f in lod.Polygons.Faces.Where(f => f.VertexIndices.All(bound)))
        {
            foreach (var v in f.VertexIndices.Skip(1))
            {
                parent[Find(v)] = Find(f.VertexIndices[0]);
            }
        }

        var members = Enumerable.Range(0, parent.Length).GroupBy(Find).ToDictionary(g => g.Key, g => g.ToArray());
        return Enumerable.Range(0, parent.Length).Select(v => members[Find(v)]).ToArray();
    }
}
