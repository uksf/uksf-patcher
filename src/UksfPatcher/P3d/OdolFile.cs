using BIS.Core.Streams;
using BIS.P3D.ODOL;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class OdolFile
{
    private static readonly Lock ConsoleLock = new();

    public static ODOL Parse(byte[] bytes, string assetPath)
    {
        if (bytes.Length < 4 || bytes[0] != 'O' || bytes[1] != 'D' || bytes[2] != 'O' || bytes[3] != 'L')
        {
            throw new PatchException($"{assetPath}: not a binarised (ODOL) P3D");
        }

        var odol = new ODOL();
        WithLibraryTraceSilenced(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReaderEx(stream);
            odol.Read(reader);
        });
        if (odol.Version is < 73 or > 74)
        {
            throw new PatchException($"{assetPath}: ODOL version {odol.Version} is not supported (73-74)");
        }

        return odol;
    }

    public static byte[] Serialise(ODOL odol)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriterEx(stream, leaveOpen: true))
        {
            odol.Write(writer);
        }

        return stream.ToArray();
    }

    public static void RequireRoundTrip(byte[] original, string assetPath)
    {
        var copy = Serialise(Parse(original, assetPath));
        if (!copy.AsSpan().SequenceEqual(original))
        {
            throw new PatchException($"{assetPath}: the ODOL library does not re-serialise this model byte-identically; refusing to patch it");
        }
    }

    private static void WithLibraryTraceSilenced(Action action)
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
