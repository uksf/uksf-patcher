using System.Buffers.Binary;
using System.Numerics;
using UksfPatcher.P3d;

namespace UksfPatcher.Tests;

public class ConvexCacheTests
{
    [Fact]
    public void ShearMovesTheCookedHullAndTiltsItsTopPlane()
    {
        var extra = Chunk(new Vector3(0, 1, 0), new Vector3(2, 1, 0), new Vector3(2, 1, 4), new Vector3(0, 1, 4));
        var min = new Vector3(0, 1, 0);
        var max = new Vector3(2, 1, 4);

        var updated = ConvexCache.Refresh(extra, min, max, 0.05f);

        Assert.Equal(1, updated);
        Assert.Equal(0.9f, Read(extra, 24 + 4), 3);
        Assert.Equal(1.1f, Read(extra, 24 + 2 * 12 + 4), 3);
        Assert.True(Math.Abs(Read(extra, 24 + 4 * 12 + 8)) > 0.01f);
        Assert.Equal(0.9f, Read(extra, 108), 3);
        Assert.Equal(1.1f, Read(extra, 120), 3);
    }

    [Fact]
    public void ShearAlsoUpdatesTheMirroredCopy()
    {
        var extra = Chunk(new Vector3(0, 1, 0), new Vector3(-2, 1, 0), new Vector3(-2, 1, -4), new Vector3(0, 1, -4));

        var updated = ConvexCache.Refresh(extra, new Vector3(0, 1, 0), new Vector3(2, 1, 4), 0.05f);

        Assert.Equal(1, updated);
        Assert.Equal(0.9f, Read(extra, 24 + 4), 3);
        Assert.Equal(1.1f, Read(extra, 24 + 2 * 12 + 4), 3);
    }

    [Fact]
    public void AChunkOutsideTheComponentIsLeftAlone()
    {
        var extra = Chunk(new Vector3(10, 1, 10), new Vector3(12, 1, 10), new Vector3(12, 1, 14), new Vector3(10, 1, 14));
        var before = extra.ToArray();

        var updated = ConvexCache.Refresh(extra, new Vector3(0, 1, 0), new Vector3(2, 1, 4), 0.05f);

        Assert.Equal(0, updated);
        Assert.Equal(before, extra);
    }

    private static byte[] Chunk(params Vector3[] verts)
    {
        var extra = new byte[24 + verts.Length * 12 + 20 + 4 + 8 + 24];
        "CLHL"u8.CopyTo(extra);
        WriteInt(extra, 4, 9);
        WriteInt(extra, 8, verts.Length);
        WriteInt(extra, 16, 1);
        WriteInt(extra, 20, 4);
        for (var i = 0; i < verts.Length; i++)
        {
            WriteV(extra, 24 + i * 12, verts[i]);
        }

        var polyAt = 24 + verts.Length * 12;
        Write(extra, polyAt + 4, 1);
        Write(extra, polyAt + 12, -1);
        extra[polyAt + 18] = 4;
        var indexAt = polyAt + 20;
        extra[indexAt] = 0;
        extra[indexAt + 1] = 1;
        extra[indexAt + 2] = 2;
        extra[indexAt + 3] = 3;
        var min = verts.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = verts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        WriteV(extra, indexAt + 4 + 8, min);
        WriteV(extra, indexAt + 4 + 8 + 12, max);
        return extra;
    }

    private static float Read(byte[] extra, int at) => BinaryPrimitives.ReadSingleLittleEndian(extra.AsSpan(at));

    private static void Write(byte[] extra, int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(extra.AsSpan(at), value);

    private static void WriteInt(byte[] extra, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(extra.AsSpan(at), value);

    private static void WriteV(byte[] extra, int at, Vector3 value)
    {
        Write(extra, at, value.X);
        Write(extra, at + 4, value.Y);
        Write(extra, at + 8, value.Z);
    }
}
