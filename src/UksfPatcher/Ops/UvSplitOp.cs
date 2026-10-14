using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class UvSplitOp : OdolOp
{
    private const uint NoClamp = 0x2000, ClampU = 0x4000, ClampV = 0x8000;

    private sealed record Island(string Sheet, float S, float[]? Box, float[]? Pos, int[] Verts, float[][]? Uv, float[][]? St);

    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var selection = OpJson.String(op, "selection");
        var newSelection = OpJson.String(op, "newSelection");
        var newTexture = OpJson.String(op, "texture");
        var plan = ReadPlan(OpJson.SourceJson(op, context));
        var owner = new Dictionary<int, Island>();
        foreach (var i in plan)
        {
            foreach (var v in i.Verts)
            {
                if (v < 0 || v >= lod.Vertices.Count)
                {
                    throw new PatchException($"{context.AssetPath}: uv-split island vertex {v} is outside the LOD");
                }

                owner[v] = i;
            }
        }

        var old = lod.UvSets[0].GetUV();
        var uv = (Vector2[])old.Clone();
        var st = lod.STCoordsCompressed.ToArray();
        foreach (var i in plan)
        {
            for (var k = 0; k < i.Verts.Length; k++)
            {
                var v = i.Verts[k];
                uv[v] = i.Uv != null ? new Vector2(i.Uv[k][0], i.Uv[k][1])
                    : new Vector2((old[v].X - i.Box![0]) * i.S + i.Pos![0], (old[v].Y - i.Box[1]) * i.S + i.Pos[1]);
                if (i.St != null)
                {
                    st[v] = Tuple.Create(UvData.Pack(i.St[k], 0), UvData.Pack(i.St[k], 3));
                }
            }
        }

        UvData.Write(lod.UvSets[0], uv, context);
        LibraryAccess.Set(lod, nameof(LOD.STCoordsCompressed), new TrackedArray<Tuple<Vector3PCompressed, Vector3PCompressed>>(st));

        var faces = lod.Polygons.Faces;
        void WrapIfRepeating(Section s, List<int> fs)
        {
            if (fs.SelectMany(f => faces[f].VertexIndices).Any(v => uv[v].X < 0 || uv[v].X > 1 || uv[v].Y < 0 || uv[v].Y > 1))
            {
                LibraryAccess.Set(s, nameof(Section.Special), (s.Special & ~(ClampU | ClampV)) | NoClamp);
            }
        }

        bool OnB(int f)
        {
            var sheets = faces[f].VertexIndices.Select(v => owner.TryGetValue(v, out var i) ? i.Sheet : null).Distinct().ToList();
            return sheets.Count == 1
                ? sheets[0] == "B"
                : throw new PatchException($"{context.AssetPath}: uv-split face {f} spans islands on sheets {string.Join(',', sheets)}");
        }

        float Ratio(List<int> fs) => fs.Sum(f => UvData.Area(uv, faces[f].VertexIndices)) / Math.Max(fs.Sum(f => UvData.Area(old, faces[f].VertexIndices)), 1e-12f);

        var textures = lod.Textures.Append(newTexture).ToArray();
        LibraryAccess.Set(lod, nameof(LOD.Textures), textures);
        var texB = (short)(textures.Length - 1);
        var perm = Enumerable.Range(0, faces.Length).ToArray();
        var sections = lod.Sections.ToList();
        var selections = lod.NamedSelections.ToList();
        var bSections = new List<int>();
        var target = OdolGraph.Selection(lod, selection, context.AssetPath);

        for (var s = sections.Count - 1; s >= 0; s--)
        {
            var sec = sections[s];
            var inSec = OdolGraph.FacesInSection(lod, sec);
            if (!inSec.SelectMany(f => faces[f].VertexIndices).Any(owner.ContainsKey))
            {
                continue;
            }

            var a = inSec.Where(f => !OnB(f)).ToList();
            var b = inSec.Where(OnB).ToList();
            var aot = sec.AreaOverTex[0];
            if (a.Count > 0)
            {
                SetAreaOverTex(sec, aot / Ratio(a));
                WrapIfRepeating(sec, a);
            }

            if (b.Count == 0)
            {
                continue;
            }

            var order = a.Concat(b).ToList();
            for (var k = 0; k < order.Count; k++)
            {
                perm[inSec[0] + k] = order[k];
            }

            var split = sec.FaceLowerIndex + a.Sum(f => OdolFaces.Weight(faces[f]));
            var secB = (Section)OdolGraph.Clone(sec);
            LibraryAccess.Set(secB, nameof(Section.FaceLowerIndex), split);
            LibraryAccess.Set(secB, nameof(Section.TextureIndex), texB);
            LibraryAccess.Set(secB, nameof(Section.AreaOverTex), (float[])sec.AreaOverTex.Clone());
            SetAreaOverTex(secB, aot / Ratio(b));
            WrapIfRepeating(secB, b);
            if (a.Count == 0)
            {
                sections[s] = secB;
                bSections.Add(s);
                LibraryAccess.Set(target, nameof(NamedSelection.Sections), new TrackedArray<int>(target.Sections.Where(x => x != s)));
                continue;
            }

            LibraryAccess.Set(sec, nameof(Section.FaceUpperIndex), split);
            sections.Insert(s + 1, secB);
            foreach (var n in selections)
            {
                ShiftSections(n, s, n == target ? null : s + 1);
            }

            foreach (var p in OdolGraph.RawProxies(lod).Cast<object>())
            {
                if (LibraryAccess.Get<int>(p, "SectionIndex") > s)
                {
                    LibraryAccess.Set(p, "SectionIndex", LibraryAccess.Get<int>(p, "SectionIndex") + 1);
                }
            }

            for (var k = 0; k < bSections.Count; k++)
            {
                bSections[k]++;
            }

            bSections.Add(s + 1);
        }

        var inv = new int[perm.Length];
        for (var k = 0; k < perm.Length; k++)
        {
            inv[perm[k]] = k;
        }

        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Faces), perm.Select(k => faces[k]).ToArray());
        foreach (var n in selections.Where(n => n.SelectedFaces?.Count > 0))
        {
            LibraryAccess.Set(n, nameof(NamedSelection.SelectedFaces), new TrackedArray<int>(n.SelectedFaces.Select(f => inv[f]).OrderBy(f => f)));
        }

        selections.Add(new NamedSelection(newSelection, true, bSections.OrderBy(s => s)));
        LibraryAccess.Set(lod, nameof(LOD.Sections), sections.ToArray());
        lod.NamedSelections = selections.ToArray();
    }

    private static List<Island> ReadPlan(JsonElement plan)
    {
        static float[] Floats(JsonElement e) => e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        static float[][]? Rows(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.EnumerateArray().Select(Floats).ToArray() : null;
        return plan.GetProperty("islands").EnumerateArray()
            .Select(e => new Island(e.GetProperty("sheet").GetString()!,
                e.TryGetProperty("s", out var s) ? s.GetSingle() : 1,
                e.TryGetProperty("box", out var b) ? Floats(b) : null,
                e.TryGetProperty("pos", out var p) ? Floats(p) : null,
                e.GetProperty("verts").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                Rows(e, "uv"), Rows(e, "st"))).ToList();
    }

    private static void ShiftSections(NamedSelection n, int s, int? join)
    {
        var list = n.Sections.Select(x => x > s ? x + 1 : x).ToList();
        if (join is { } j && list.Contains(s))
        {
            list.Add(j);
        }

        LibraryAccess.Set(n, nameof(NamedSelection.Sections), new TrackedArray<int>(list.OrderBy(x => x)));
    }

    private static void SetAreaOverTex(Section s, float value)
    {
        var a = (float[])s.AreaOverTex.Clone();
        a[0] = value;
        LibraryAccess.Set(s, nameof(Section.AreaOverTex), a);
    }
}
