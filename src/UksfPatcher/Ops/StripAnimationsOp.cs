using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class StripAnimationsOp : IAssetOp
{
    private const int FirstVersion = 73;
    private const int KeyedVersion = 75;

    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var expected = OpJson.Strings(op, "sources").Select(s => s.ToLowerInvariant()).Distinct().Order().ToList();
        if (expected.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: strip-animations \"sources\" is empty");
        }

        var (version, lods, headerEnd) = ReadHeader(asset, context);
        var blocks = Candidates(asset, version, lods, headerEnd, expected).ToList();
        if (blocks.Count != 1)
        {
            throw new PatchException($"{context.AssetPath}: found {blocks.Count} animation blocks with sources [{string.Join(", ", expected)}]; expected exactly 1");
        }

        var block = blocks[0];
        var removed = block.End - block.Start - 1;
        var output = new byte[asset.Length - removed];
        asset.AsSpan(0, block.Start).CopyTo(output);
        output[block.Start] = 0;
        asset.AsSpan(block.End).CopyTo(output.AsSpan(block.Start + 1));
        for (var i = 0; i < 2 * lods; i++)
        {
            var at = output.AsSpan(block.Start + 1 + 4 * i);
            BinaryPrimitives.WriteUInt32LittleEndian(at, BinaryPrimitives.ReadUInt32LittleEndian(at) - (uint)removed);
        }

        return output;
    }

    private static (int Version, int Lods, int End) ReadHeader(byte[] b, OpContext context)
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

        r.Skip(4);
        r.Asciiz();
        var lods = r.Int();
        r.Skip(4 * lods);
        return (version, lods, r.At);
    }

    private static IEnumerable<Block> Candidates(byte[] b, int version, int lods, int from, List<string> expected)
    {
        var limit = IndexOf(b, Encoding.Latin1.GetBytes(expected[0] + "\0"), from);
        for (var start = from; start < limit; start++)
        {
            if (b[start] == 1 && TryParse(b, start, version, lods, out var end, out var sources) && sources.SequenceEqual(expected))
            {
                yield return new Block(start, end);
            }
        }
    }

    private static bool TryParse(byte[] b, int start, int version, int lods, out int end, out List<string> sources)
    {
        end = 0;
        sources = [];
        try
        {
            var r = new Cursor(b, start + 1);
            var count = r.Int();
            if (count is <= 0 or > 4096)
            {
                return false;
            }

            var types = new uint[count];
            var names = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                types[i] = (uint)r.Int();
                if (types[i] > 9)
                {
                    return false;
                }

                r.Asciiz();
                names.Add(r.Asciiz().ToLowerInvariant());
                r.Skip(4 * 4 + (version >= 56 ? 8 : 0) + 4 + (types[i] == 8 ? 32 : 8));
            }

            if (r.Int() != lods)
            {
                return false;
            }

            for (var lod = 0; lod < lods; lod++)
            {
                for (var bones = r.Int(); bones > 0; bones--)
                {
                    r.Skip(4 * r.Int());
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

            end = r.At;
            for (var i = 0; i < lods; i++)
            {
                var lodStart = r.UIntAt(end + 4 * i);
                var lodEnd = r.UIntAt(end + 4 * (lods + i));
                if (lodStart <= end + 8 * lods || lodStart > lodEnd || lodEnd > b.Length)
                {
                    return false;
                }
            }

            sources = [.. names];
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int IndexOf(byte[] b, byte[] needle, int from)
    {
        var i = b.AsSpan(from).IndexOf(needle);
        return i < 0 ? from : from + i;
    }

    private sealed record Block(int Start, int End);

    private sealed class Cursor(byte[] b, int at)
    {
        public int At { get; private set; } = at;

        public int Int()
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(At, 4));
            At += 4;
            return v;
        }

        public uint UIntAt(int pos) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos, 4));

        public void Skip(int n)
        {
            if (n < 0 || At + n > b.Length)
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
