using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UksfPatcher.Pbo;

public sealed class PboEntry
{
    public required int Index { get; init; }
    public required byte[] NameBytes { get; init; }
    public required int FieldsOffset { get; init; }
    public required uint Packing { get; init; }
    public required uint OriginalSize { get; init; }
    public required uint Timestamp { get; init; }
    public required uint DataSize { get; init; }
    public required long DataOffset { get; init; }

    public string Name => Encoding.Latin1.GetString(NameBytes);
    public bool IsCompressed => Packing == RawPbo.CompressedMagic;
}

public sealed class RawPbo
{
    public const uint VersionMagic = 0x56657273;
    public const uint CompressedMagic = 0x43707273;
    private const int FieldsLength = 20;
    private const int TrailerLength = 21;

    private RawPbo(byte[] bytes, List<PboEntry> entries, int headerLength, long dataLength, string prefix)
    {
        Prefix = prefix;
        Bytes = bytes;
        Entries = entries;
        HeaderLength = headerLength;
        DataLength = dataLength;
    }

    public string Prefix { get; }
    public byte[] Bytes { get; }
    public IReadOnlyList<PboEntry> Entries { get; }
    public int HeaderLength { get; }
    public long DataLength { get; }

    public static RawPbo Read(string path) => Parse(File.ReadAllBytes(path));

    public static RawPbo Parse(byte[] bytes)
    {
        var entries = new List<PboEntry>();
        var pos = 0;
        long dataOffset = 0;
        var prefix = "";
        var offsets = new List<(int Fields, byte[] Name, uint Packing, uint Original, uint Time, uint Size)>();
        while (true)
        {
            var name = ReadAsciiz(bytes, ref pos);
            if (pos + FieldsLength > bytes.Length)
            {
                throw new PboFormatException("header runs past the end of the file");
            }

            var fields = pos;
            var packing = U32(bytes, pos);
            var original = U32(bytes, pos + 4);
            var time = U32(bytes, pos + 12);
            var size = U32(bytes, pos + 16);
            pos += FieldsLength;
            if (name.Length == 0 && packing == VersionMagic)
            {
                for (var key = ReadAsciiz(bytes, ref pos); key.Length > 0; key = ReadAsciiz(bytes, ref pos))
                {
                    var value = ReadAsciiz(bytes, ref pos);
                    if (Encoding.Latin1.GetString(key).Equals("prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        prefix = Encoding.Latin1.GetString(value);
                    }
                }

                continue;
            }

            if (name.Length == 0)
            {
                break;
            }

            offsets.Add((fields, name, packing, original, time, size));
        }

        foreach (var o in offsets)
        {
            entries.Add(new PboEntry
            {
                Index = entries.Count,
                NameBytes = o.Name,
                FieldsOffset = o.Fields,
                Packing = o.Packing,
                OriginalSize = o.Original,
                Timestamp = o.Time,
                DataSize = o.Size,
                DataOffset = pos + dataOffset
            });
            dataOffset += o.Size;
        }

        if (pos + dataOffset + TrailerLength != bytes.Length)
        {
            throw new PboFormatException(
                $"data blocks ({dataOffset} bytes after a {pos}-byte header) plus the 21-byte trailer do not fill the {bytes.Length}-byte file");
        }

        if (bytes[^TrailerLength] != 0)
        {
            throw new PboFormatException("trailer does not start with a zero byte");
        }

        return new RawPbo(bytes, entries, pos, dataOffset, prefix);
    }

    public bool TrailerHashValid()
    {
        var hash = SHA1.HashData(Bytes.AsSpan(0, Bytes.Length - TrailerLength));
        return hash.AsSpan().SequenceEqual(Bytes.AsSpan(Bytes.Length - 20));
    }

    public static void RequireValidTrailer(byte[] bytes, string label)
    {
        RawPbo pbo;
        try
        {
            pbo = Parse(bytes);
        }
        catch (PboFormatException e)
        {
            throw new Patching.PatchException($"{label}: cannot parse: {e.Message}");
        }

        pbo.RequireValidTrailer(label);
    }

    public void RequireValidTrailer(string label)
    {
        if (!TrailerHashValid())
        {
            throw new Patching.PatchException($"{label}: the SHA-1 trailer is invalid; the PBO is damaged");
        }
    }

    public PboEntry? Find(string path)
    {
        var wanted = Normalise(path);
        var prefix = Normalise(Prefix);
        if (prefix.Length > 0 && wanted.StartsWith(prefix + "\\"))
        {
            wanted = wanted[(prefix.Length + 1)..];
        }

        var matches = Entries.Where(e => Normalise(e.Name) == wanted).ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new PboFormatException($"{matches.Count} entries are named '{path}'")
        };
    }

    public ReadOnlySpan<byte> RawData(PboEntry entry) => Bytes.AsSpan((int)entry.DataOffset, (int)entry.DataSize);

    public byte[] ReadData(PboEntry entry)
    {
        var raw = RawData(entry).ToArray();
        if (!entry.IsCompressed)
        {
            return raw;
        }

        using var stream = new MemoryStream(raw);
        BIS.Core.Compression.LZSS.ReadLZSS(stream, out var data, entry.OriginalSize, false);
        return data;
    }

    public byte[] Rebuild(IReadOnlyDictionary<int, byte[]> replacements) => Rebuild(replacements, []);

    public byte[] Rebuild(IReadOnlyDictionary<int, byte[]> replacements, IReadOnlyList<PboAddition> additions)
    {
        var header = Bytes.AsSpan(0, HeaderLength).ToArray();
        if (additions.Count > 0)
        {
            header = WithAddedEntries(header, additions);
        }

        foreach (var (index, data) in replacements)
        {
            var e = Entries[index];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(e.FieldsOffset), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(e.FieldsOffset + 4), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(e.FieldsOffset + 16), (uint)data.Length);
        }

        using var output = new MemoryStream();
        output.Write(header);
        foreach (var e in Entries)
        {
            if (replacements.TryGetValue(e.Index, out var data))
            {
                output.Write(data);
            }
            else
            {
                output.Write(RawData(e));
            }
        }

        foreach (var addition in additions)
        {
            output.Write(addition.Data);
        }

        var hash = SHA1.HashData(output.GetBuffer().AsSpan(0, (int)output.Length));
        output.WriteByte(0);
        output.Write(hash);
        return output.ToArray();
    }

    public string EntryName(string path)
    {
        var name = path.Replace('/', '\\').TrimStart('\\');
        var prefix = Normalise(Prefix);
        return prefix.Length > 0 && Normalise(name).StartsWith(prefix + "\\") ? name[(prefix.Length + 1)..] : name;
    }

    private static byte[] WithAddedEntries(byte[] header, IReadOnlyList<PboAddition> additions)
    {
        var terminator = header.Length - (1 + FieldsLength);
        if (header[terminator] != 0)
        {
            throw new PboFormatException("the header does not end with an empty terminator entry");
        }

        using var output = new MemoryStream();
        output.Write(header.AsSpan(0, terminator));
        foreach (var addition in additions)
        {
            output.Write(Encoding.Latin1.GetBytes(addition.Name));
            output.WriteByte(0);
            Span<byte> fields = stackalloc byte[FieldsLength];
            fields.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(fields[4..], (uint)addition.Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(fields[12..], addition.Timestamp);
            BinaryPrimitives.WriteUInt32LittleEndian(fields[16..], (uint)addition.Data.Length);
            output.Write(fields);
        }

        output.Write(header.AsSpan(terminator));
        return output.ToArray();
    }

    public static string Normalise(string path) => path.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();

    private static uint U32(byte[] b, int pos) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos));

    private static byte[] ReadAsciiz(byte[] bytes, ref int pos)
    {
        var end = Array.IndexOf(bytes, (byte)0, pos);
        if (end < 0)
        {
            throw new PboFormatException("unterminated name in header");
        }

        var value = bytes.AsSpan(pos, end - pos).ToArray();
        pos = end + 1;
        return value;
    }
}

public sealed record PboAddition(string Name, uint Timestamp, byte[] Data);

public sealed class PboFormatException(string message) : Exception(message);
