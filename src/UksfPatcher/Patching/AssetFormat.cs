namespace UksfPatcher.Patching;

public static class AssetFormat
{
    public static T Guard<T>(string where, Func<T> read)
    {
        try
        {
            return read();
        }
        // The BIS library signals bad input with several exception types, some of them plain Exception.
        catch (Exception e) when (e is not PatchException and not OutOfMemoryException)
        {
            throw new PatchException($"{where}: cannot read the asset ({e.GetType().Name}): {e.Message}");
        }
    }
}
