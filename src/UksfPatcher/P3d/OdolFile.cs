using System.Buffers.Binary;
using BIS.Core.Streams;
using BIS.P3D.ODOL;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class OdolFile
{
    private static readonly Lock ConsoleLock = new();
    private const int V75EncryptionFieldsOffset = 8;
    private const int V75EncryptionFieldsLength = 8;

    public static ODOL Parse(byte[] bytes, string assetPath)
    {
        if (bytes.Length < 4 || bytes[0] != 'O' || bytes[1] != 'D' || bytes[2] != 'O' || bytes[3] != 'L')
        {
            throw new PatchException($"{assetPath}: not a binarised (ODOL) P3D");
        }

        var version = bytes.Length < 8 ? -1 : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        if (version is < 73 or > 75)
        {
            throw new PatchException($"{assetPath}: ODOL version {version} is not supported (73-75)");
        }

        if (version == 75 && (bytes.Length < 16 || BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)) != 0))
        {
            throw new PatchException($"{assetPath}: ODOL v75 model is encrypted");
        }

        return AssetFormat.Guard(assetPath, () =>
        {
            var odol = new ODOL();
            WithLibraryTraceSilenced(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new BinaryReaderEx(stream);
                odol.Read(reader);
            });
            return odol;
        });
    }

    public static byte[] Serialise(ODOL odol)
    {
        var encryptionFields = odol.Version >= 75 ? V75EncryptionFieldsLength : 0;
        using var stream = new MemoryStream();
        stream.Write(new byte[encryptionFields]);
        using (var writer = new BinaryWriterEx(stream, leaveOpen: true))
        {
            odol.Write(writer);
        }

        var bytes = stream.ToArray().AsSpan(encryptionFields);
        return [.. bytes[..V75EncryptionFieldsOffset], .. new byte[encryptionFields], .. bytes[V75EncryptionFieldsOffset..]];
    }

    public static void RequireRoundTrip(byte[] original, string assetPath)
    {
        var copy = Serialise(Parse(original, assetPath));
        if (!copy.AsSpan().SequenceEqual(original))
        {
            throw new PatchException($"{assetPath}: the ODOL library does not re-serialise this model byte-identically; refusing to patch it");
        }
    }

    internal static void WithLibraryTraceSilenced(Action action)
    {
        lock (ConsoleLock)
        {
            var error = Console.Error;
            Console.SetError(TextWriter.Null);
            try
            {
                action();
            }
            finally
            {
                Console.SetError(error);
            }
        }
    }
}
