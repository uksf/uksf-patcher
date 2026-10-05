using System.Numerics;

namespace UksfPatcher.P3d;

public sealed record HullTriangle(int A, int B, int C);

public static class ConvexHull
{
    public const int MaxPoints = 96;

    public static double Tolerance(IReadOnlyList<Vector3> points)
    {
        var min = points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        return Math.Max(1e-6, 1e-5 * (max - min).Length());
    }

    public static IReadOnlyList<HullTriangle> Build(IReadOnlyList<int> ids, IReadOnlyList<Vector3> points)
    {
        var unique = Dedupe(ids, points);
        if (unique.Count > MaxPoints)
        {
            throw new ArgumentException($"{unique.Count} distinct points; the hull builder handles up to {MaxPoints}");
        }

        var p = unique.Select(u => ToDouble(u.Point)).ToArray();
        var eps = Tolerance(points);
        var planes = new List<(Vector3d Normal, double D)>();
        for (var i = 0; i < p.Length; i++)
        for (var j = i + 1; j < p.Length; j++)
        for (var k = j + 1; k < p.Length; k++)
        {
            var n = Vector3d.Cross(p[j] - p[i], p[k] - p[i]);
            var length = n.Length();
            if (length < eps * eps)
            {
                continue;
            }

            n /= length;
            var d = Vector3d.Dot(n, p[i]);
            var front = p.Count(x => Vector3d.Dot(n, x) - d > eps);
            var back = p.Count(x => Vector3d.Dot(n, x) - d < -eps);
            if (front > 0 && back > 0)
            {
                continue;
            }

            var outward = front == 0 ? n : -n;
            var od = Vector3d.Dot(outward, p[i]);
            if (!planes.Any(q => Vector3d.Dot(q.Normal, outward) > 1 - 1e-9 && Math.Abs(q.D - od) <= eps))
            {
                planes.Add((outward, od));
            }
        }

        if (planes.Count < 4)
        {
            throw new ArgumentException("the points are coplanar or collinear, so their convex hull has no volume");
        }

        var triangles = new List<HullTriangle>();
        foreach (var (normal, d) in planes)
        {
            var onPlane = Enumerable.Range(0, p.Length).Where(i => Math.Abs(Vector3d.Dot(normal, p[i]) - d) <= eps).ToList();
            var ring = PolygonOnPlane(onPlane, p, normal);
            for (var t = 1; t + 1 < ring.Count; t++)
            {
                triangles.Add(new HullTriangle(unique[ring[0]].Id, unique[ring[t]].Id, unique[ring[t + 1]].Id));
            }
        }

        return triangles;
    }

    public static double MaxOutsideDistance(HullTriangle t, IReadOnlyDictionary<int, Vector3> byId, IEnumerable<Vector3> points)
    {
        var a = ToDouble(byId[t.A]);
        var n = Vector3d.Cross(ToDouble(byId[t.B]) - a, ToDouble(byId[t.C]) - a);
        n /= n.Length();
        return points.Max(x => Vector3d.Dot(n, ToDouble(x) - a));
    }

    private static List<int> PolygonOnPlane(List<int> indices, Vector3d[] p, Vector3d normal)
    {
        var u = Vector3d.Cross(normal, Math.Abs(normal.X) < 0.9 ? new Vector3d(1, 0, 0) : new Vector3d(0, 1, 0));
        u /= u.Length();
        var v = Vector3d.Cross(normal, u);
        var flat = indices.Select(i => (Index: i, X: Vector3d.Dot(p[i], u), Y: Vector3d.Dot(p[i], v)))
            .OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        var lower = new List<(int Index, double X, double Y)>();
        var upper = new List<(int Index, double X, double Y)>();
        foreach (var q in flat)
        {
            while (lower.Count >= 2 && Turn(lower[^2], lower[^1], q) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(q);
        }

        for (var i = flat.Count - 1; i >= 0; i--)
        {
            var q = flat[i];
            while (upper.Count >= 2 && Turn(upper[^2], upper[^1], q) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(q);
        }

        return lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)).Select(q => q.Index).ToList();
    }

    private static double Turn((int, double X, double Y) o, (int, double X, double Y) a, (int, double X, double Y) b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static List<(int Id, Vector3 Point)> Dedupe(IReadOnlyList<int> ids, IReadOnlyList<Vector3> points)
    {
        var eps = (float)Tolerance(points);
        var result = new List<(int, Vector3)>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (!result.Any(r => Vector3.Distance(r.Item2, points[i]) <= eps))
            {
                result.Add((ids[i], points[i]));
            }
        }

        return result;
    }

    private static Vector3d ToDouble(Vector3 v) => new(v.X, v.Y, v.Z);
}

public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator -(Vector3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3d Cross(Vector3d a, Vector3d b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public double Length() => Math.Sqrt(Dot(this, this));
}
