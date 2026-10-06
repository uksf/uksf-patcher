namespace UksfPatcher.Patching;

public static class AssetFormat
{
    public static T Guard<T>(string where, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is ArgumentException or EndOfStreamException or IndexOutOfRangeException or FormatException or InvalidDataException or OverflowException)
        {
            throw new PatchException($"{where}: cannot read the asset: {e.Message}");
        }
    }
}
