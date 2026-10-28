namespace UksfPatcher.Pbo;

public static class PboRemoval
{
    public static byte[] WithoutEntries(byte[] header, IEnumerable<PboEntry> removed, int fieldsLength)
    {
        using var output = new MemoryStream();
        var pos = 0;
        foreach (var e in removed.OrderBy(e => e.FieldsOffset))
        {
            var start = e.FieldsOffset - e.NameBytes.Length - 1;
            output.Write(header.AsSpan(pos, start - pos));
            pos = e.FieldsOffset + fieldsLength;
        }

        output.Write(header.AsSpan(pos));
        return output.ToArray();
    }
}
