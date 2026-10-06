using System.Buffers.Binary;
using UksfPatcher.P3d;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Tests;

public sealed class AssetFormatErrorTests : IDisposable
{
    private const string Model = @"objects\v2.p3d";

    private readonly RunnerSandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void UnsupportedMlodVersionIsAControlledErrorNamingThePboAndAsset()
    {
        var pbo = RawPbo.Parse(_box.Pristine);
        var entry = pbo.Find(Model)!;
        var model = pbo.ReadData(entry);
        BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(4), 999);
        var source = pbo.Rebuild(new Dictionary<int, byte[]> { [entry.Index] = model });
        File.WriteAllBytes(_box.PboPath, source);
        _box.WriteManifest(Model, StateStore.Sha256(model), """{ "op": "remove-degenerate-faces" }""", StateStore.Sha256(source));

        var result = _box.Run("apply");

        Assert.Equal(1, result.Code);
        Assert.Contains($"{RunnerSandbox.PboName}:{Model}", result.Error);
        Assert.Contains("Unknown MLOD version", result.Error);
        Assert.Equal(source, File.ReadAllBytes(_box.PboPath));
    }

    [Fact]
    public void UnsupportedOdolVersionIsRefusedBeforeTheLibraryParsesIt()
    {
        byte[] header = [(byte)'O', (byte)'D', (byte)'O', (byte)'L', 75, 0, 0, 0];

        var e = Assert.Throws<PatchException>(() => OdolFile.Parse(header, "model.p3d"));

        Assert.Contains("ODOL version 75 is not supported", e.Message);
    }

    [Fact]
    public void TruncatedOdolIsAControlledError()
    {
        byte[] header = [(byte)'O', (byte)'D', (byte)'O', (byte)'L', 73, 0, 0, 0, 1, 2];

        var e = Assert.Throws<PatchException>(() => OdolFile.Parse(header, "model.p3d"));

        Assert.Contains("model.p3d", e.Message);
    }

    [Fact]
    public void PlainLibraryExceptionBecomesAControlledError()
    {
        // BIS ODOL parsing throws a plain Exception for an animation type it does not know.
        var e = Assert.Throws<PatchException>(() => AssetFormat.Guard<int>("x.pbo:model.p3d", () => throw new Exception("Unknown AnimType encountered: 9")));

        Assert.Contains("x.pbo:model.p3d", e.Message);
        Assert.Contains("Unknown AnimType", e.Message);
    }
}
