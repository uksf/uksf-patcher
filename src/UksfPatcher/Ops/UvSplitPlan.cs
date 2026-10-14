using System.Text.Json;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed record UvIsland(bool OnB, float S, float[]? Box, float[]? Pos, int[] Verts, float[][]? Uv, float[][]? St);

public static class UvSplitPlan
{
    public static List<UvIsland> Read(JsonElement plan, int vertexCount, string assetPath)
    {
        if (!plan.TryGetProperty("islands", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            throw new PatchException($"{assetPath}: uv-split plan needs a non-empty \"islands\" array");
        }

        return list.EnumerateArray().Select((e, n) => Island(e, n, vertexCount, assetPath)).ToList();
    }

    private static UvIsland Island(JsonElement e, int n, int vertexCount, string assetPath)
    {
        PatchException Bad(string what) => new($"{assetPath}: uv-split island {n} {what}");

        var sheet = e.TryGetProperty("sheet", out var sh) && sh.ValueKind == JsonValueKind.String ? sh.GetString() : null;
        if (sheet is not ("A" or "B"))
        {
            throw Bad("needs \"sheet\" A or B");
        }

        var verts = e.TryGetProperty("verts", out var vs) && vs.ValueKind == JsonValueKind.Array
            ? vs.EnumerateArray().Select(x => x.GetInt32()).ToArray()
            : throw Bad("needs a \"verts\" array");
        if (verts.Length == 0 || verts.Any(v => v < 0 || v >= vertexCount))
        {
            throw Bad($"needs vertices inside the LOD's {vertexCount}");
        }

        var uv = Rows(e, "uv", verts.Length, 2, Bad);
        var st = Rows(e, "st", verts.Length, 6, Bad);
        if (uv != null)
        {
            return new UvIsland(sheet == "B", 1, null, null, verts, uv, st);
        }

        var s = e.TryGetProperty("s", out var sv) ? sv.GetSingle() : 1;
        var box = Row(e, "box", 2, Bad);
        var pos = Row(e, "pos", 2, Bad);
        if (!float.IsFinite(s) || s <= 0 || box == null || pos == null)
        {
            throw Bad("needs explicit \"uv\" rows or a positive \"s\" with \"box\" and \"pos\"");
        }

        return new UvIsland(sheet == "B", s, box, pos, verts, null, st);
    }

    private static float[][]? Rows(JsonElement e, string name, int rows, int width, Func<string, PatchException> bad)
    {
        if (!e.TryGetProperty(name, out var p))
        {
            return null;
        }

        var result = p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(r => Floats(r, width)).ToArray() : null;
        return result != null && result.Length == rows && result.All(r => r != null) ? result! : throw bad($"needs \"{name}\" as {rows} rows of {width} finite numbers");
    }

    private static float[]? Row(JsonElement e, string name, int width, Func<string, PatchException> bad) =>
        !e.TryGetProperty(name, out var p) ? null : Floats(p, width) ?? throw bad($"needs \"{name}\" as {width} finite numbers");

    private static float[]? Floats(JsonElement e, int width)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != width || e.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
        {
            return null;
        }

        var values = e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return values.All(float.IsFinite) ? values : null;
    }
}
