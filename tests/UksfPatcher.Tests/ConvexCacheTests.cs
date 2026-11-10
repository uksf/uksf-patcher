using System.Buffers.Binary;
using System.Numerics;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

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
        Assert.Equal(0.9f, Read(extra, 122), 3);
        Assert.Equal(1.1f, Read(extra, 134), 3);
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

    [Fact]
    public void SymmetricHullRejectsAmbiguousOrientation()
    {
        var extra = Chunk(new Vector3(-1, 1, -2), new Vector3(1, 1, -2), new Vector3(1, 1, 2), new Vector3(-1, 1, 2));

        Assert.Throws<PatchException>(() => ConvexCache.Refresh(extra, new Vector3(-1, 1, -2), new Vector3(1, 1, 2), 0.05f));
    }

    [Fact]
    public void MissingBoundsRecordFailsRefresh()
    {
        var extra = Chunk(new Vector3(0, 1, 0), new Vector3(2, 1, 0), new Vector3(2, 1, 4), new Vector3(0, 1, 4));
        Array.Clear(extra, extra.Length - 24, 24);

        Assert.Throws<PatchException>(() => ConvexCache.Refresh(extra, new Vector3(0, 1, 0), new Vector3(2, 1, 4), 0.05f));
    }

    [Fact]
    public void A400TailplaneBoundsFollowTheCookedRecord()
    {
        var (extra, min, max) = A400Tailplane();
        var direct = AabbsBefore(extra, min, max);
        var mirror = AabbsBefore(extra, new Vector3(-max.X, min.Y, -max.Z), new Vector3(-min.X, max.Y, -min.Z));

        var updated = ConvexCache.Refresh(extra, min, max, 0.05f);

        Assert.Equal(2, updated);
        Assert.Single(direct);
        Assert.Single(mirror);
        Assert.Equal(13.614f, Read(extra, direct[0] + 4), 3);
        Assert.Equal(14.513f, Read(extra, direct[0] + 16), 3);
        Assert.Equal(-10.852f, Read(extra, direct[0]), 3);
        Assert.Equal(23.840f, Read(extra, direct[0] + 8), 3);
        Assert.Equal(13.614f, Read(extra, mirror[0] + 4), 3);
        Assert.Equal(14.513f, Read(extra, mirror[0] + 16), 3);
        Assert.Equal(-28.109f, Read(extra, mirror[0] + 8), 3);
    }

    [Fact]
    public void A400TailplaneWithoutItsBoundsRecordFails()
    {
        var (extra, min, max) = A400Tailplane();
        foreach (var at in AabbsBefore(extra, min, max).Concat(AabbsBefore(extra, new Vector3(-max.X, min.Y, -max.Z), new Vector3(-min.X, max.Y, -min.Z))))
        {
            Array.Clear(extra, at, 24);
        }

        var error = Assert.Throws<PatchException>(() => ConvexCache.Refresh(extra, min, max, 0.05f));
        Assert.Equal("convex cache bounds record not found", error.Message);
    }

    private static (byte[] Extra, Vector3 Min, Vector3 Max) A400Tailplane()
    {
        var odol = OdolFile.Parse(Models.Load("a400m.pbo", "A400M_F.p3d"), "a400");
        var fire = odol.Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution("geometryFire")));
        var points = fire.NamedSelections.Single(s => s.Name == "component06").SelectedVertices
            .Select(v => new Vector3(fire.Vertices[v].X, fire.Vertices[v].Y, fire.Vertices[v].Z)).ToList();
        var min = points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        return (odol.Extra.ToArray(), min, max);
    }

    private static List<int> AabbsBefore(byte[] extra, Vector3 min, Vector3 max)
    {
        var hits = new List<int>();
        ReadOnlySpan<byte> footer = "NXS\x01"u8;
        for (var i = 100; i + 4 <= extra.Length; i++)
        {
            if (!extra.AsSpan(i, 4).SequenceEqual(footer) || !Near(ReadV(extra, i - 100), min) || !Near(ReadV(extra, i - 88), max))
            {
                continue;
            }

            hits.Add(i - 100);
        }

        return hits;
    }

    private static bool Near(Vector3 a, Vector3 b) => (a - b).Length() < 0.25f;

    private static Vector3 ReadV(byte[] extra, int at) => new(Read(extra, at), Read(extra, at + 4), Read(extra, at + 8));

    private static byte[] Chunk(params Vector3[] verts)
    {
        const int polys = 1;
        const int indices = 4;
        var edges = verts.Length + polys - 2;
        var gap = edges * 2 + verts.Length * 3 + 4;
        var extra = new byte[24 + verts.Length * 12 + polys * 20 + indices + gap + 24];
        "CLHL"u8.CopyTo(extra);
        WriteInt(extra, 4, 9);
        WriteInt(extra, 8, verts.Length);
        WriteInt(extra, 12, edges);
        WriteInt(extra, 16, polys);
        WriteInt(extra, 20, indices);
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
        WriteV(extra, indexAt + indices + gap, min);
        WriteV(extra, indexAt + indices + gap + 12, max);
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
