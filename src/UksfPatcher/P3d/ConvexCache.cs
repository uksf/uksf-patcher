using System.Buffers.Binary;
using System.Numerics;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class ConvexCache
{
    private static ReadOnlySpan<byte> Signature => "CLHL"u8;

    public static int Refresh(byte[] extra, Vector3 min, Vector3 max, float shear)
    {
        if (!float.IsFinite(shear))
        {
            throw new PatchException("convex cache shear is not finite");
        }

        var updated = 0;
        var zMid = (min.Z + max.Z) * 0.5f;
        for (var at = 0; at + 24 < extra.Length; at++)
        {
            if (!extra.AsSpan(at, 4).SequenceEqual(Signature) || !Valid(extra, at, out var chunk))
            {
                continue;
            }

            var mirrored = Mirror(chunk, min, max);
            if (!Direct(chunk, min, max) && !mirrored)
            {
                continue;
            }

            var oldMin = chunk.Min;
            var oldMax = chunk.Max;
            if (shear != 0)
            {
                Shift(extra, chunk, shear, zMid, mirrored);
            }

            RecomputePlanes(extra, chunk);
            WriteBounds(extra, chunk, oldMin, oldMax, Bounds(extra, chunk.VertAt, chunk.Verts));
            updated++;
        }

        return updated;
    }

    private static bool Valid(byte[] extra, int at, out Chunk chunk)
    {
        chunk = default;
        var version = BinaryPrimitives.ReadInt32LittleEndian(extra.AsSpan(at + 4));
        var verts = BinaryPrimitives.ReadInt32LittleEndian(extra.AsSpan(at + 8));
        var polys = BinaryPrimitives.ReadInt32LittleEndian(extra.AsSpan(at + 16));
        var indices = BinaryPrimitives.ReadInt32LittleEndian(extra.AsSpan(at + 20));
        if (version is < 1 or > 32 || verts is < 4 or > 64 || polys is < 1 or > 64 || indices < polys || indices > polys * 16)
        {
            return false;
        }

        var vertAt = at + 24;
        var polyAt = vertAt + verts * 12;
        var indexAt = polyAt + polys * 20;
        if (indexAt + indices > extra.Length || !Finite(extra, vertAt))
        {
            return false;
        }

        chunk = new Chunk(vertAt, verts, polyAt, polys, indexAt, indices, Bounds(extra, vertAt, verts));
        return true;
    }

    private static bool Finite(byte[] extra, int at) =>
        float.IsFinite(Read(extra, at)) && float.IsFinite(Read(extra, at + 4)) && float.IsFinite(Read(extra, at + 8));

    private static (Vector3 Min, Vector3 Max) Bounds(byte[] extra, int at, int count)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < count; i++)
        {
            var p = ReadV(extra, at + i * 12);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (min, max);
    }

    private static bool Direct(Chunk chunk, Vector3 min, Vector3 max) => Near(chunk.Min, min) && Near(chunk.Max, max);

    private static bool Mirror(Chunk chunk, Vector3 min, Vector3 max) =>
        Near(chunk.Min, new Vector3(-max.X, min.Y, -max.Z)) && Near(chunk.Max, new Vector3(-min.X, max.Y, -min.Z));

    private static bool Near(Vector3 a, Vector3 b) => (a - b).Length() < 0.25f;

    private static void Shift(byte[] extra, Chunk chunk, float shear, float zMid, bool mirrored)
    {
        for (var i = 0; i < chunk.Verts; i++)
        {
            var o = chunk.VertAt + i * 12;
            var z = Read(extra, o + 8);
            var y = Read(extra, o + 4) + shear * ((mirrored ? -z : z) - zMid);
            Write(extra, o + 4, y);
        }

    }

    private static void RecomputePlanes(byte[] extra, Chunk chunk)
    {
        for (var i = 0; i < chunk.Polys; i++)
        {
            var o = chunk.PolyAt + i * 20;
            var vref = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(o + 16));
            var count = extra[o + 18];
            if (vref + count > chunk.Indices || count < 3)
            {
                continue;
            }

            var ids = new int[count];
            for (var k = 0; k < count; k++)
            {
                ids[k] = extra[chunk.IndexAt + vref + k];
            }

            if (ids.Any(id => id >= chunk.Verts) || Normal(extra, chunk, ids) is not Vector3 normal)
            {
                continue;
            }

            var old = new Vector3(Read(extra, o), Read(extra, o + 4), Read(extra, o + 8));
            if (Vector3.Dot(normal, old) < 0)
            {
                normal = -normal;
            }

            var p = ReadV(extra, chunk.VertAt + ids[0] * 12);
            Write(extra, o, normal.X);
            Write(extra, o + 4, normal.Y);
            Write(extra, o + 8, normal.Z);
            Write(extra, o + 12, -Vector3.Dot(normal, p));
        }
    }

    private static Vector3? Normal(byte[] extra, Chunk chunk, int[] ids)
    {
        var a = ReadV(extra, chunk.VertAt + ids[0] * 12);
        for (var i = 1; i + 1 < ids.Length; i++)
        {
            var n = Vector3.Cross(
                ReadV(extra, chunk.VertAt + ids[i] * 12) - a,
                ReadV(extra, chunk.VertAt + ids[i + 1] * 12) - a);
            if (n.LengthSquared() > 1e-8f)
            {
                return Vector3.Normalize(n);
            }
        }

        return null;
    }

    private static void WriteBounds(byte[] extra, Chunk chunk, Vector3 oldMin, Vector3 oldMax, (Vector3 Min, Vector3 Max) now)
    {
        var end = Math.Min(extra.Length, chunk.IndexAt + chunk.Indices + 400);
        for (var i = chunk.IndexAt + chunk.Indices; i + 24 <= end; i += 4)
        {
            if (!Near(ReadV(extra, i), oldMin) || !Near(ReadV(extra, i + 12), oldMax))
            {
                continue;
            }

            WriteV(extra, i, now.Min);
            WriteV(extra, i + 12, now.Max);
            return;
        }
    }

    private static Vector3 ReadV(byte[] extra, int at) => new(Read(extra, at), Read(extra, at + 4), Read(extra, at + 8));

    private static float Read(byte[] extra, int at) => BinaryPrimitives.ReadSingleLittleEndian(extra.AsSpan(at));

    private static void Write(byte[] extra, int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(extra.AsSpan(at), value);

    private static void WriteV(byte[] extra, int at, Vector3 value)
    {
        Write(extra, at, value.X);
        Write(extra, at + 4, value.Y);
        Write(extra, at + 8, value.Z);
    }

    private struct Chunk
    {
        public Chunk(int vertAt, int verts, int polyAt, int polys, int indexAt, int indices, (Vector3 Min, Vector3 Max) bounds)
        {
            VertAt = vertAt;
            Verts = verts;
            PolyAt = polyAt;
            Polys = polys;
            IndexAt = indexAt;
            Indices = indices;
            Bounds = bounds;
        }

        public int VertAt { get; }
        public int Verts { get; }
        public int PolyAt { get; }
        public int Polys { get; }
        public int IndexAt { get; }
        public int Indices { get; }
        public (Vector3 Min, Vector3 Max) Bounds { get; set; }
        public Vector3 Min => Bounds.Min;
        public Vector3 Max => Bounds.Max;
    }
}
