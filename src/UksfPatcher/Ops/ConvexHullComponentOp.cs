using System.Numerics;
using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class ConvexHullComponentOp : OdolOp
{
    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var component = OpJson.String(op, "component");
        foreach (var lodName in OpJson.Strings(op, "lods"))
        {
            Rebuild(FindLod(odol, lodName, context), component, $"{context.AssetPath}:{lodName}:{component}");
        }
    }

    public static bool FacesPointOutward(LOD lod, NamedSelection except)
    {
        var votes = 0;
        foreach (var c in lod.NamedSelections.Where(s => s != except && s.Name.StartsWith("component", StringComparison.OrdinalIgnoreCase)))
        {
            var centre = c.SelectedVertices.Select(v => Position(lod, v)).Aggregate(Vector3.Zero, (a, b) => a + b) / Math.Max(1, c.SelectedVertices.Count);
            foreach (var f in c.SelectedFaces.Select(i => lod.Polygons.Faces[i]))
            {
                var a = Position(lod, f.VertexIndices[0]);
                var n = Vector3.Cross(Position(lod, f.VertexIndices[1]) - a, Position(lod, f.VertexIndices[2]) - a);
                var side = Vector3.Dot(n, a - centre);
                votes += side > 0 ? 1 : side < 0 ? -1 : 0;
            }
        }

        return votes >= 0;
    }

    public static Vector3 Position(LOD lod, int vertex) => new(lod.Vertices[vertex].X, lod.Vertices[vertex].Y, lod.Vertices[vertex].Z);

    private static void Rebuild(LOD lod, string name, string where)
    {
        var selection = lod.NamedSelections.SingleOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PatchException($"{where}: no such selection");
        if (!selection.Name.StartsWith("component", StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchException($"{where}: not a component selection");
        }

        var ranges = OdolFaces.SectionFaceRanges(lod, where);
        var oldFaces = selection.SelectedFaces.ToHashSet();
        var vertexIds = selection.SelectedVertices.ToList();
        var points = vertexIds.Select(v => Position(lod, v)).ToList();
        IReadOnlyList<HullTriangle> hull;
        try
        {
            hull = ConvexHull.Build(vertexIds, points);
        }
        catch (ArgumentException e)
        {
            throw new PatchException($"{where}: {e.Message}");
        }

        var outward = FacesPointOutward(lod, selection);
        var hullFaces = hull.Select(t => outward ? OdolFaces.NewFace(t.A, t.B, t.C) : OdolFaces.NewFace(t.A, t.C, t.B)).ToArray();
        var insertAt = oldFaces.Min();
        var faces = new List<BIS.P3D.ODOL.Polygon>();
        var map = new int[lod.Polygons.Faces.Length];
        var hullIndices = new List<int>();
        var boundary = new int[lod.Polygons.Faces.Length + 1];
        for (var i = 0; i < lod.Polygons.Faces.Length; i++)
        {
            boundary[i] = faces.Count;
            if (i == insertAt)
            {
                hullIndices.AddRange(Enumerable.Range(faces.Count, hullFaces.Length));
                faces.AddRange(hullFaces);
            }

            map[i] = oldFaces.Contains(i) ? -1 : faces.Count;
            if (!oldFaces.Contains(i)) faces.Add(lod.Polygons.Faces[i]);
        }

        boundary[^1] = faces.Count;
        var newRanges = ranges.Select(r => (boundary[r.First], boundary[r.End])).ToArray();

        foreach (var s in lod.NamedSelections)
        {
            var hit = s.SelectedFaces.Count(oldFaces.Contains);
            if (hit == 0 && s != selection)
            {
                if (s.SelectedFaces.Any(f => map[f] != f)) OdolFaces.SetSelectedFaces(s, s.SelectedFaces.Select(f => map[f]));
                continue;
            }

            if (hit != oldFaces.Count)
            {
                throw new PatchException($"{where}: selection '{s.Name}' holds only some of the component's faces");
            }

            OdolFaces.SetSelectedFaces(s, s.SelectedFaces.Where(f => map[f] >= 0).Select(f => map[f]).Concat(hullIndices));
        }

        OdolFaces.Replace(lod, faces.ToArray(), newRanges);
        RequireConvex(lod, hullIndices, points, where, outward);
    }

    private static void RequireConvex(LOD lod, List<int> hullIndices, List<Vector3> points, string where, bool outward)
    {
        var eps = ConvexHull.Tolerance(points);
        foreach (var f in hullIndices.Select(i => lod.Polygons.Faces[i]))
        {
            var ids = f.VertexIndices;
            var t = outward ? new HullTriangle(ids[0], ids[1], ids[2]) : new HullTriangle(ids[0], ids[2], ids[1]);
            var byId = ids.Distinct().ToDictionary(v => v, v => Position(lod, v));
            if (ConvexHull.MaxOutsideDistance(t, byId, points) > eps)
            {
                throw new PatchException($"{where}: hull face [{string.Join(",", ids)}] has component points outside it");
            }
        }
    }
}
