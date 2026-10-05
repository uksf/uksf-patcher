using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class RtmStripBonesOp : IAssetOp
{
    private const string Magic = "RTM_0101";
    private const int NameLength = 32;
    private const int MatrixLength = 48;
    private const int HeaderLength = 8 + 12;

    public byte[] Apply(byte[] asset, JsonElement op, OpContext context)
    {
        var strip = OpJson.Strings(op, "bones").Select(b => b.ToLowerInvariant()).ToHashSet();
        var rtm = Parse(asset, context);
        var missing = strip.Where(b => !rtm.Bones.Any(n => n.ToLowerInvariant() == b)).ToList();
        if (missing.Count > 0)
        {
            throw new PatchException($"{context.AssetPath}: RTM has no bone(s) {string.Join(", ", missing)}");
        }

        var keep = rtm.Bones.Select(b => !strip.Contains(b.ToLowerInvariant())).ToArray();
        using var output = new MemoryStream();
        output.Write(asset, 0, HeaderLength);
        WriteInt(output, rtm.Frames);
        WriteInt(output, keep.Count(k => k));
        for (var b = 0; b < rtm.Bones.Count; b++)
        {
            if (keep[b])
            {
                output.Write(asset, rtm.NameTableOffset + b * NameLength, NameLength);
            }
        }

        var frameLength = 4 + rtm.Bones.Count * (NameLength + MatrixLength);
        for (var f = 0; f < rtm.Frames; f++)
        {
            var frame = rtm.FramesOffset + f * frameLength;
            output.Write(asset, frame, 4);
            for (var b = 0; b < rtm.Bones.Count; b++)
            {
                if (keep[b])
                {
                    output.Write(asset, frame + 4 + b * (NameLength + MatrixLength), NameLength + MatrixLength);
                }
            }
        }

        var result = output.ToArray();
        Parse(result, context);
        return result;
    }

    public static (IReadOnlyList<string> Bones, int Frames, int NameTableOffset, int FramesOffset) Parse(byte[] asset, OpContext context)
    {
        if (asset.Length < HeaderLength + 8 || Encoding.ASCII.GetString(asset, 0, 8) != Magic)
        {
            throw new PatchException($"{context.AssetPath}: rtm-strip-bones needs an {Magic} RTM (binarised RTMs are not supported)");
        }

        var frames = BinaryPrimitives.ReadInt32LittleEndian(asset.AsSpan(HeaderLength));
        var boneCount = BinaryPrimitives.ReadInt32LittleEndian(asset.AsSpan(HeaderLength + 4));
        var names = HeaderLength + 8;
        var framesOffset = names + boneCount * NameLength;
        var expected = framesOffset + (long)frames * (4 + boneCount * (NameLength + MatrixLength));
        if (frames < 0 || boneCount < 0 || expected != asset.Length)
        {
            throw new PatchException($"{context.AssetPath}: RTM layout ({frames} frames, {boneCount} bones) does not match its {asset.Length} bytes");
        }

        var bones = Enumerable.Range(0, boneCount).Select(b => ReadName(asset, names + b * NameLength)).ToList();
        for (var f = 0; f < frames; f++)
        {
            for (var b = 0; b < boneCount; b++)
            {
                var at = framesOffset + f * (4 + boneCount * (NameLength + MatrixLength)) + 4 + b * (NameLength + MatrixLength);
                if (!string.Equals(ReadName(asset, at), bones[b], StringComparison.OrdinalIgnoreCase))
                {
                    throw new PatchException($"{context.AssetPath}: frame {f} bone {b} is '{ReadName(asset, at)}', not '{bones[b]}'");
                }
            }
        }

        return (bones, frames, names, framesOffset);
    }

    private static string ReadName(byte[] asset, int offset)
    {
        var span = asset.AsSpan(offset, NameLength);
        var end = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? span : span[..end]);
    }

    private static void WriteInt(Stream s, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        s.Write(b);
    }
}
