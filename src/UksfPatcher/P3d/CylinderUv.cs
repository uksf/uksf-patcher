using System.Numerics;
using BIS.P3D.ODOL;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public sealed class CylinderUv
{
    private readonly AxialFrame frame;
    private readonly Vector3 right;
    private readonly Vector3 up;
    private readonly float seam;
    private readonly Vector2[] a;

    private CylinderUv(AxialFrame frame, float seam, Vector2[] coefficients)
    {
        this.frame = frame;
        up = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, frame.Direction) * frame.Direction);
        right = Vector3.Cross(up, frame.Direction);
        this.seam = seam;
        a = coefficients;
    }

    public float Angle(Vector3 p)
    {
        var rel = p - frame.Origin;
        return MathF.Atan2(Vector3.Dot(rel, up), Vector3.Dot(rel, right));
    }

    public float[] Unwrap(IReadOnlyList<Vector3> face)
    {
        var t = face.Select(p => Wrap(Angle(p) - seam)).ToArray();
        if (t.Max() - t.Min() > MathF.PI)
        {
            for (var k = 0; k < t.Length; k++)
            {
                if (t[k] < MathF.PI)
                {
                    t[k] += 2 * MathF.PI;
                }
            }
        }

        return t;
    }

    public Vector2 Map(float unwrapped, float along) => a[0] + a[1] * unwrapped + a[2] * along;

    public (Vector3 S, Vector3 T) Tangents(Vector3 p, Vector3 normal, float alongSign)
    {
        var theta = Angle(p);
        var dPdTheta = Math.Max(frame.Radius(p), 1e-3f) * (MathF.Cos(theta) * up - MathF.Sin(theta) * right);
        var det = a[1].X * a[2].Y - a[2].X * a[1].Y;
        var axis = frame.Direction * alongSign;
        var dPdu = dPdTheta * (a[2].Y / det) + axis * (-a[1].Y / det);
        var dPdv = dPdTheta * (-a[2].X / det) + axis * (a[1].X / det);
        return (Orthogonal(dPdu, normal), Orthogonal(dPdv, normal));
    }

    private static Vector3 Orthogonal(Vector3 v, Vector3 n)
    {
        var o = v - Vector3.Dot(v, n) * n;
        return o.Length() > 1e-6f ? Vector3.Normalize(o) : Vector3.Normalize(v);
    }

    private static float Wrap(float t) => ((t % (2 * MathF.PI)) + 2 * MathF.PI) % (2 * MathF.PI);

    public static CylinderUv Fit(LOD lod, Vector2[] uv, IReadOnlyCollection<int> faces, AxialFrame frame, string assetPath)
    {
        var probe = new CylinderUv(frame, 0, [Vector2.Zero, Vector2.Zero, Vector2.Zero]);
        var seam = FindSeam(lod, uv, faces, probe);
        probe = new CylinderUv(frame, seam, probe.a);
        var rows = new List<(float T, float L, Vector2 Uv)>();
        foreach (var f in faces)
        {
            var idx = lod.Polygons.Faces[f].VertexIndices;
            var t = probe.Unwrap(idx.Select(v => lod.Vertices[v].Vector3).ToArray());
            for (var k = 0; k < idx.Length; k++)
            {
                rows.Add((t[k], frame.Along(lod.Vertices[idx[k]]), uv[idx[k]]));
            }
        }

        if (rows.Count < 3)
        {
            throw new PatchException($"{assetPath}: UV fit needs barrel faces to sample, found {rows.Count} vertices");
        }

        var coefficients = Solve(rows);
        var residual = rows.Max(r => (coefficients[0] + coefficients[1] * r.T + coefficients[2] * r.L - r.Uv).Length());
        if (residual > 0.02f)
        {
            throw new PatchException($"{assetPath}: the barrel UVs are not a cylindrical unwrap (worst residual {residual:F4})");
        }

        return new CylinderUv(frame, seam, coefficients);
    }

    private static float FindSeam(LOD lod, Vector2[] uv, IReadOnlyCollection<int> faces, CylinderUv probe)
    {
        var verts = faces.SelectMany(f => lod.Polygons.Faces[f].VertexIndices).Distinct().ToList();
        foreach (var v in verts)
        {
            foreach (var w in verts)
            {
                if (v != w && Vector3.Distance(lod.Vertices[v].Vector3, lod.Vertices[w].Vector3) < 1e-4f && Vector2.Distance(uv[v], uv[w]) > 0.05f)
                {
                    return probe.Angle(lod.Vertices[v].Vector3);
                }
            }
        }

        return 0;
    }

    private static Vector2[] Solve(List<(float T, float L, Vector2 Uv)> rows)
    {
        var m = new double[3, 3];
        var bu = new double[3];
        var bv = new double[3];
        foreach (var (t, l, uv) in rows)
        {
            double[] x = [1, t, l];
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    m[i, j] += x[i] * x[j];
                }

                bu[i] += x[i] * uv.X;
                bv[i] += x[i] * uv.Y;
            }
        }

        var cu = Gauss(m, bu);
        var cv = Gauss(m, bv);
        return [new((float)cu[0], (float)cv[0]), new((float)cu[1], (float)cv[1]), new((float)cu[2], (float)cv[2])];
    }

    private static double[] Gauss(double[,] m0, double[] b0)
    {
        var m = (double[,])m0.Clone();
        var b = (double[])b0.Clone();
        for (var c = 0; c < 3; c++)
        {
            var p = Enumerable.Range(c, 3 - c).MaxBy(r => Math.Abs(m[r, c]));
            for (var j = 0; j < 3; j++)
            {
                (m[c, j], m[p, j]) = (m[p, j], m[c, j]);
            }

            (b[c], b[p]) = (b[p], b[c]);
            for (var r = c + 1; r < 3; r++)
            {
                var k = m[r, c] / m[c, c];
                for (var j = c; j < 3; j++)
                {
                    m[r, j] -= k * m[c, j];
                }

                b[r] -= k * b[c];
            }
        }

        var x = new double[3];
        for (var r = 2; r >= 0; r--)
        {
            x[r] = (b[r] - Enumerable.Range(r + 1, 2 - r).Sum(j => m[r, j] * x[j])) / m[r, r];
        }

        return x;
    }
}
