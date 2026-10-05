using System.Text.Json;
using BIS.P3D.MLOD;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class RemoveDegenerateFacesOp : IAssetOp
{
    public const double DefaultMinArea = 1e-8;

    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var mlod = ReadMlod(asset, context);
        var minArea = op.TryGetProperty("minArea", out var m) ? m.GetDouble() : DefaultMinArea;
        var lodFilter = op.TryGetProperty("lods", out var l)
            ? l.EnumerateArray().Select(x => LodNames.Resolution(x.GetString()!)).ToList()
            : null;
        var expected = op.TryGetProperty("expectedRemoved", out var e) ? e.GetInt32() : (int?)null;
        var removed = 0;
        foreach (var lod in mlod.Lods.Where(x => lodFilter == null || lodFilter.Any(r => LodNames.Matches(x.Resolution, r))))
        {
            removed += RemoveFrom(lod, minArea, context);
        }

        if (expected != null && removed != expected)
        {
            throw new PatchException($"{context.AssetPath}: expected to remove {expected} degenerate faces, found {removed}");
        }

        if (removed == 0)
        {
            throw new PatchException($"{context.AssetPath}: no degenerate faces to remove");
        }

        var output = mlod.WriteToMemory().ToArray();
        ReadMlod(output, context);
        return output;
    }

    public static double FaceArea(P3DM_LOD lod, Face face)
    {
        double nx = 0, ny = 0, nz = 0;
        var points = face.RealVertices.Select(v => lod.Points[v.PointIndex]).ToArray();
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            nx += ((double)a.Y - b.Y) * ((double)a.Z + b.Z);
            ny += ((double)a.Z - b.Z) * ((double)a.X + b.X);
            nz += ((double)a.X - b.X) * ((double)a.Y + b.Y);
        }

        return Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2;
    }

    private static int RemoveFrom(P3DM_LOD lod, double minArea, OpContext context)
    {
        var keep = lod.Faces.Select(f => FaceArea(lod, f) >= minArea).ToArray();
        var removed = keep.Count(k => !k);
        if (removed == 0)
        {
            return 0;
        }

        foreach (var tagg in lod.Taggs)
        {
            switch (tagg)
            {
                case NamedSelectionTagg s:
                    s.Faces = Filter(s.Faces, keep, tagg.Name, context);
                    break;
                case SelectedTagg s:
                    s.Faces = Filter(s.Faces, keep, tagg.Name, context);
                    break;
                case LockTagg s:
                    s.LockedFaces = Filter(s.LockedFaces, keep, tagg.Name, context);
                    break;
                case UVSetTagg s:
                    s.FaceUVs = Filter(s.FaceUVs, keep, tagg.Name, context);
                    break;
            }

            tagg.UpdateDataSize();
        }

        lod.Faces = Filter(lod.Faces, keep, "faces", context);
        return removed;
    }

    private static T[] Filter<T>(T[] values, bool[] keep, string what, OpContext context)
    {
        if (values.Length != keep.Length)
        {
            throw new PatchException($"{context.AssetPath}: {what} has {values.Length} face entries for {keep.Length} faces");
        }

        return values.Where((_, i) => keep[i]).ToArray();
    }

    private static MLOD ReadMlod(byte[] bytes, OpContext context)
    {
        if (bytes.Length < 4 || bytes[0] != 'M' || bytes[1] != 'L' || bytes[2] != 'O' || bytes[3] != 'D')
        {
            throw new PatchException($"{context.AssetPath}: remove-degenerate-faces needs an unbinarised (MLOD) P3D");
        }

        var mlod = new MLOD(new MemoryStream(bytes, writable: false));
        foreach (var tagg in mlod.Lods.SelectMany(x => x.Taggs))
        {
            if (tagg.DataSize != tagg.ComputeDataSize())
            {
                throw new PatchException($"{context.AssetPath}: tagg '{tagg.Name}' is not understood by the MLOD library");
            }
        }

        if (!mlod.WriteToMemory().ToArray().AsSpan().SequenceEqual(bytes))
        {
            throw new PatchException($"{context.AssetPath}: the MLOD library does not re-serialise this model byte-identically; refusing to patch it");
        }

        return mlod;
    }
}
