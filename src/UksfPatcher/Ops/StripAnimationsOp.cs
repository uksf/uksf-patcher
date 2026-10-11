using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using BIS.Core.Streams;
using BIS.P3D;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class StripAnimationsOp : IAssetOp
{
    private const int FirstVersion = 73;
    private const int LastLibraryVersion = 74;
    private const int KeyedVersion = 75;

    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var expected = SourceSet(OpJson.Strings(op, "sources"));
        if (expected.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: strip-animations \"sources\" is empty");
        }

        var (version, lods) = ReadHeader(asset, context);
        var start = AnimationsOffset(asset, context);
        if (asset[start] != 1)
        {
            throw new PatchException($"{context.AssetPath}: the model has no animations");
        }

        var (end, sources) = ParseBlock(asset, start, version, lods, context);
        if (!sources.SetEquals(expected))
        {
            throw new PatchException(
                $"{context.AssetPath}: the model's animation sources are [{string.Join(", ", sources.Order())}], not [{string.Join(", ", expected.Order())}]");
        }

        var output = Strip(asset, start, end, lods);
        if (version <= LastLibraryVersion && OdolFile.Parse(output, context.AssetPath).Animations != null)
        {
            throw new PatchException($"{context.AssetPath}: the stripped model still has animations");
        }

        return output;
    }

    private static HashSet<string> SourceSet(IEnumerable<string> names) => new(names, StringComparer.OrdinalIgnoreCase);

    private static byte[] Strip(byte[] asset, int start, int end, int lods)
    {
        var removed = end - start - 1;
        var output = new byte[asset.Length - removed];
        asset.AsSpan(0, start).CopyTo(output);
        output[start] = 0;
        asset.AsSpan(end).CopyTo(output.AsSpan(start + 1));
        for (var i = 0; i < 2 * lods; i++)
        {
            var at = output.AsSpan(start + 1 + 4 * i);
            BinaryPrimitives.WriteUInt32LittleEndian(at, BinaryPrimitives.ReadUInt32LittleEndian(at) - (uint)removed);
        }

        return output;
    }

    private static (int Version, int Lods) ReadHeader(byte[] b, OpContext context)
    {
        if (b.Length < 8 || !b.AsSpan(0, 4).SequenceEqual("ODOL"u8))
        {
            throw new PatchException($"{context.AssetPath}: not a binarised (ODOL) P3D");
        }

        var r = new Cursor(b, 4);
        var version = r.Int();
        if (version is < FirstVersion or > KeyedVersion)
        {
            throw new PatchException($"{context.AssetPath}: ODOL version {version} is not supported ({FirstVersion}-{KeyedVersion})");
        }

        if (version == KeyedVersion && (r.Int() != 0 || r.Int() != 0))
        {
            throw new PatchException($"{context.AssetPath}: ODOL v75 model is encrypted");
        }

        return AssetFormat.Guard(context.AssetPath, () =>
        {
            r.Skip(4);
            r.Asciiz();
            return (version, r.Int());
        });
    }

    private static int AnimationsOffset(byte[] b, OpContext context) =>
        AssetFormat.Guard(context.AssetPath, () =>
        {
            var position = 0L;
            OdolFile.WithLibraryTraceSilenced(() =>
            {
                using var stream = new MemoryStream(b, writable: false);
                using var reader = new BinaryReaderEx(stream);
                new P3DInfosOnly().Read(reader);
                position = stream.Position;
            });
            if (position >= b.Length)
            {
                throw new PatchException($"{context.AssetPath}: the model ends after its model info");
            }

            return (int)position;
        });

    private static (int End, HashSet<string> Sources) ParseBlock(byte[] b, int start, int version, int lods, OpContext context)
    {
        string Fail(string why) => $"{context.AssetPath}: the animation block at offset {start} is malformed: {why}";

        try
        {
            var r = new Cursor(b, start + 1);
            var count = r.Int();
            if (count is <= 0 or > 4096)
            {
                throw new PatchException(Fail($"animation count {count}"));
            }

            var types = new int[count];
            var sources = SourceSet([]);
            for (var i = 0; i < count; i++)
            {
                types[i] = r.Int();
                if (types[i] is < 0 or > 9)
                {
                    throw new PatchException(Fail($"animation {i} has type {types[i]}"));
                }

                r.Asciiz();
                sources.Add(r.Asciiz());
                r.Skip(4 * 4 + (version >= 56 ? 8 : 0) + 4 + (types[i] == 8 ? 32 : 8));
            }

            if (r.Int() is var bonesLods && bonesLods != lods)
            {
                throw new PatchException(Fail($"bone table covers {bonesLods} LODs, the model has {lods}"));
            }

            for (var lod = 0; lod < lods; lod++)
            {
                for (var bones = r.Count(4); bones > 0; bones--)
                {
                    r.Skip(4 * r.Count(4));
                }
            }

            for (var lod = 0; lod < lods; lod++)
            {
                foreach (var type in types)
                {
                    if (r.Int() != -1 && type is not (8 or 9))
                    {
                        r.Skip(24);
                    }
                }
            }

            RequireLodTable(b, r.At, lods, Fail);
            return (r.At, sources);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new PatchException(Fail("an array length is negative or runs past the end of the model"));
        }
    }

    private static void RequireLodTable(byte[] b, int at, int lods, Func<string, string> fail)
    {
        var tableEnd = (long)at + 9L * lods;
        var ranges = Enumerable.Range(0, lods)
            .Select(i => (Start: (long)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at + 4 * i)), End: (long)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at + 4 * (lods + i)))))
            .OrderBy(x => x.Start)
            .ToList();
        var previousEnd = tableEnd;
        foreach (var (lodStart, lodEnd) in ranges)
        {
            if (lodStart < previousEnd || lodEnd <= lodStart || lodEnd > b.Length)
            {
                throw new PatchException(fail($"the LOD address table after it is invalid (LOD {lodStart}-{lodEnd})"));
            }

            previousEnd = lodEnd;
        }
    }

    private sealed class Cursor(byte[] b, int at)
    {
        public int At { get; private set; } = at;

        public int Int()
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(At, 4));
            At += 4;
            return v;
        }

        public int Count(int elementSize)
        {
            var count = Int();
            if (count < 0 || (long)count * elementSize > b.Length - At)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return count;
        }

        public void Skip(int n)
        {
            if (n < 0 || (long)At + n > b.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(n));
            }

            At += n;
        }

        public string Asciiz()
        {
            var end = b.AsSpan(At).IndexOf((byte)0);
            if (end < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(At));
            }

            var s = Encoding.Latin1.GetString(b, At, end);
            At += end + 1;
            return s;
        }
    }
}
