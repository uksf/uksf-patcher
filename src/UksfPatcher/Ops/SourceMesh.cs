using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed record SourceCorner(int Point, Vector3 Normal);

public sealed class SourceMesh
{
    public required Vector3[] Points { get; init; }

    public required SourceCorner[][] Faces { get; init; }

    public required bool[] Weld { get; init; }

    public static SourceMesh Load(JsonElement op, OpContext context)
    {
        var path = Path.Join(context.ManifestDirectory, OpJson.String(op, "source"));
        if (!File.Exists(path))
        {
            throw new PatchException($"{context.AssetPath}: add-mesh source '{path}' does not exist");
        }

        var lod = new MLOD(path).Lods.Single();
        var s = OpJson.Floats(op, "sourceAxis", 6);
        var target = AxialFrame.From(op, context);
        var scale = OpJson.Float(op, "scale");
        Vector3 sourceOrigin = new(s[0], s[1], s[2]), sourceFwd = Vector3.Normalize(new(s[3], s[4], s[5]));
        var sourceUp = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, sourceFwd) * sourceFwd);
        var targetUp = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, target.Direction) * target.Direction);
        var sourceBasis = Rows(Vector3.Cross(sourceUp, sourceFwd), sourceUp, sourceFwd);
        var targetBasis = Rows(Vector3.Cross(targetUp, target.Direction), targetUp, target.Direction);
        var attach = target.Origin + target.Direction * OpJson.Float(op, "attachAlong");
        Vector3 Rotate(Vector3 v) => Vector3.TransformNormal(Vector3.TransformNormal(v, Matrix4x4.Transpose(sourceBasis)), targetBasis);
        bool OnLoop(Vector3 p, JsonElement l) =>
            Math.Abs(Vector3.Dot(p - sourceOrigin, sourceFwd) - (l[0].GetSingle() - Vector3.Dot(sourceOrigin, sourceFwd))) < 5e-4f
            && Math.Abs((p - sourceOrigin - Vector3.Dot(p - sourceOrigin, sourceFwd) * sourceFwd).Length() - l[1].GetSingle()) < 5e-4f;
        var loopRadii = op.TryGetProperty("loopRadii", out var lr) ? lr.EnumerateArray().ToArray() : [];
        var weld = op.GetProperty("weldLoops").EnumerateArray().ToArray();
        var points = lod.Points.Select(p =>
        {
            var rel = p.Vector3 - sourceOrigin;
            var axial = Vector3.Dot(rel, sourceFwd) * sourceFwd;
            var radial = (rel - axial) * scale;
            foreach (var l in loopRadii.Where(l => OnLoop(p.Vector3, l)))
            {
                radial = Vector3.Normalize(radial) * l[2].GetSingle();
            }

            return attach + Rotate(axial * scale + radial);
        }).ToArray();
        var welded = lod.Points.Select(p => weld.Any(l => OnLoop(p.Vector3, l))).ToArray();
        if (weld.Any(l => !lod.Points.Any(p => OnLoop(p.Vector3, l))))
        {
            throw new PatchException($"{context.AssetPath}: an add-mesh weld loop matches no source point");
        }

        var faces = lod.Faces.Select(f => f.RealVertices.Select(v => new SourceCorner(v.PointIndex, Vector3.Zero)).ToArray()).ToList();
        if (faces.Any(f => f.Length != 3 && f.Length != 4))
        {
            throw new PatchException($"{context.AssetPath}: add-mesh source has a face that is not a triangle or quad");
        }

        var tris = faces.SelectMany(f => f.Length == 3 ? [f] : new[] { new[] { f[0], f[1], f[2] }, new[] { f[0], f[2], f[3] } }).ToArray();
        Vector3 Centroid(SourceCorner[] f) => (points[f[0].Point] + points[f[1].Point] + points[f[2].Point]) / 3;
        var rim = tris.Max(f => target.Radius(Centroid(f))) * 0.9f;
        var outward = tris.Where(f => target.Radius(Centroid(f)) > rim).Sum(f => Math.Sign(Vector3.Dot(
            Vector3.Cross(points[f[1].Point] - points[f[0].Point], points[f[2].Point] - points[f[0].Point]),
            Centroid(f) - target.Origin - target.Along(Centroid(f)) * target.Direction)));
        var oriented = tris.Select(f => outward >= 0 ? f : [f[0], f[2], f[1]]).ToArray();
        return new SourceMesh { Points = points, Faces = Smooth(oriented, points), Weld = welded };
    }

    private static SourceCorner[][] Smooth(SourceCorner[][] tris, Vector3[] points)
    {
        var hardEdge = MathF.Cos(MathF.PI * SmoothingAngleDegrees / 180);
        var area = tris.Select(f => Vector3.Cross(points[f[1].Point] - points[f[0].Point], points[f[2].Point] - points[f[0].Point])).ToArray();
        var facing = area.Select(Vector3.Normalize).ToArray();
        (int, int, int) Key(int point) => ((int)MathF.Round(points[point].X * 1e4f), (int)MathF.Round(points[point].Y * 1e4f), (int)MathF.Round(points[point].Z * 1e4f));
        var around = Enumerable.Range(0, tris.Length).SelectMany(f => tris[f].Select(c => (Key: Key(c.Point), Face: f))).ToLookup(x => x.Key, x => x.Face);
        return tris.Select((f, i) => f.Select(c =>
        {
            var sum = around[Key(c.Point)].Distinct().Where(h => Vector3.Dot(facing[h], facing[i]) > hardEdge).Aggregate(Vector3.Zero, (s, h) => s + area[h]);
            return new SourceCorner(c.Point, Vector3.Normalize(sum));
        }).ToArray()).ToArray();
    }

    private const float SmoothingAngleDegrees = 40;

    private static Matrix4x4 Rows(Vector3 a, Vector3 b, Vector3 c) => new(a.X, a.Y, a.Z, 0, b.X, b.Y, b.Z, 0, c.X, c.Y, c.Z, 0, 0, 0, 0, 1);
}
