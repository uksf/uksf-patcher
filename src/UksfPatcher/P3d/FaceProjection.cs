using System.Numerics;

namespace UksfPatcher.P3d;

public sealed record ProjectedCorner(Vector2 Uv, Vector3 DPdUnwrapped, Vector3 DPdAlong);

public static class FaceProjection
{
    public static ProjectedCorner[] Project(CylinderUv fit, AxialFrame frame, IReadOnlyList<Vector3> pts, float fold)
    {
        var normal = Vector3.Normalize(Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]));
        var centroid = pts.Aggregate(Vector3.Zero, (sum, p) => sum + p) / pts.Count;
        var facingAxis = Vector3.Dot(normal, frame.Direction);
        var facingOut = Vector3.Dot(normal, Radial(frame, centroid));
        var facingAround = Vector3.Dot(normal, Vector3.Normalize(fit.AroundAxis(centroid)));
        var unwrapped = fit.Unwrap(pts);
        var mean = unwrapped.Average();
        var corners = new (float T, float L, Vector3 DT, Vector3 DL)[pts.Count];
        for (var k = 0; k < pts.Count; k++)
        {
            var p = pts[k];
            var r = frame.Radius(p);
            var along = frame.Along(p);
            var outward = Radial(frame, p);
            (float T, float L, Vector3 DT, Vector3 DL) c;
            if (Math.Abs(facingAround) >= Math.Abs(facingAxis) && Math.Abs(facingAround) >= Math.Abs(facingOut))
            {
                var s = Sign(facingAround);
                c = (mean + s * (r - fit.Radius) / fit.Radius, along, s * fit.Radius * outward, frame.Direction);
            }
            else if (Math.Abs(facingAxis) >= Math.Abs(facingOut))
            {
                var s = Sign(facingAxis);
                c = (unwrapped[k], along + s * (fit.Radius - r), fit.AroundAxis(p), -s * outward);
            }
            else
            {
                c = (unwrapped[k], along, fit.AroundAxis(p), frame.Direction);
            }

            corners[k] = c.L > fold ? (c.T, 2 * fold - c.L, c.DT, -c.DL) : c;
        }

        var shift = Math.Max(0, -corners.Min(c => c.T)) - Math.Max(0, corners.Max(c => c.T) - fit.Turn);
        return corners.Select(c => new ProjectedCorner(fit.Map(c.T + shift, c.L), c.DT, c.DL)).ToArray();
    }

    private static float Sign(float x) => x >= 0 ? 1 : -1;

    private static Vector3 Radial(AxialFrame frame, Vector3 p)
    {
        var rel = p - frame.Origin;
        var off = rel - Vector3.Dot(rel, frame.Direction) * frame.Direction;
        return off.Length() > 1e-6f ? Vector3.Normalize(off) : Vector3.UnitY;
    }
}
