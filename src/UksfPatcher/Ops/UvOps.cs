using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public static class UvData
{
    public static void Write(UVSet set, Vector2[] uv, OpContext context)
    {
        if (set.DefaultFill)
        {
            throw new PatchException($"{context.AssetPath}: UV set uses a default fill, which UV edits do not support");
        }

        float minU = uv.Min(t => t.X), minV = uv.Min(t => t.Y);
        float maxU = minU + (uv.Max(t => t.X) - minU) * 65536f / 65534f, maxV = minV + (uv.Max(t => t.Y) - minV) * 65536f / 65534f;
        static short Q(float x, float min, float max) => (short)Math.Clamp(Math.Round((x - min) / (max - min) * 65536.0 - 32767.0), -32767, 32767);
        var data = new byte[uv.Length * 4];
        for (var i = 0; i < uv.Length; i++)
        {
            BitConverter.TryWriteBytes(data.AsSpan(i * 4), Q(uv[i].X, minU, maxU));
            BitConverter.TryWriteBytes(data.AsSpan(i * 4 + 2), Q(uv[i].Y, minV, maxV));
        }

        LibraryAccess.Set(set, nameof(UVSet.MinU), minU);
        LibraryAccess.Set(set, nameof(UVSet.MinV), minV);
        LibraryAccess.Set(set, nameof(UVSet.MaxU), maxU);
        LibraryAccess.Set(set, nameof(UVSet.MaxV), maxV);
        LibraryAccess.Set(set, nameof(UVSet.UvData), new TrackedArray<byte>(data));
    }

    public static float Area(Vector2[] uv, int[] f)
    {
        float a = 0;
        for (var i = 1; i + 1 < f.Length; i++)
        {
            Vector2 e1 = uv[f[i]] - uv[f[0]], e2 = uv[f[i + 1]] - uv[f[0]];
            a += Math.Abs(e1.X * e2.Y - e1.Y * e2.X) / 2;
        }

        return a;
    }

    public static Vector3PCompressed Pack(float[] v, int o)
    {
        static int C(float x) => (int)Math.Clamp(Math.Round(-x * 511), -511, 511) & 0x3FF;
        return new Vector3PCompressed(C(v[o]) | C(v[o + 1]) << 10 | C(v[o + 2]) << 20);
    }
}

public sealed class MoveSectionsOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var texture = OpJson.String(op, "texture");
        var newSelection = OpJson.String(op, "newSelection");
        var from = OpJson.Strings(op, "from");
        var secs = Enumerable.Range(0, lod.Sections.Length)
            .Where(s => lod.Sections[s].TextureIndex >= 0 && lod.Textures[lod.Sections[s].TextureIndex].Equals(texture, StringComparison.OrdinalIgnoreCase)).ToList();
        if (secs.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no sections using '{texture}'");
        }

        if (lod.NamedSelections.Any(n => n.Name.Equals(newSelection, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} already has selection '{newSelection}'");
        }

        foreach (var name in from)
        {
            var n = OdolGraph.Selection(lod, name, context.AssetPath);
            LibraryAccess.Set(n, nameof(NamedSelection.Sections), new TrackedArray<int>(n.Sections.Where(s => !secs.Contains(s))));
        }

        lod.NamedSelections = lod.NamedSelections.Append(new NamedSelection(newSelection, true, secs)).ToArray();
    }
}
