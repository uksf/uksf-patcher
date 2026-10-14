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

    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var newSelection = OpJson.String(op, "newSelection");
        var newTexture = OpJson.String(op, "texture");
        var target = OdolGraph.Selection(lod, OpJson.String(op, "selection"), context.AssetPath);
        if (!target.IsSectional)
        {
            throw new PatchException($"{context.AssetPath}: uv-split selection '{target.Name}' is not sectional");
        }

        if (lod.NamedSelections.Any(n => n.Name.Equals(newSelection, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} already has selection '{newSelection}'");
        }

        if (lod.Textures.Length >= short.MaxValue)
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no room for another texture index");
        }

        var plan = UvSplitPlan.Read(OpJson.SourceJson(op, context), lod.Vertices.Count, context.AssetPath);
        var owner = new Dictionary<int, UvIsland>();
        foreach (var i in plan)
        {
            foreach (var v in i.Verts)
            {
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
            var sheets = faces[f].VertexIndices.Select(v => owner.TryGetValue(v, out var i) ? (bool?)i.OnB : null).Distinct().ToList();
            return sheets.Count == 1
                ? sheets[0] == true
                : throw new PatchException($"{context.AssetPath}: uv-split face {f} spans islands on different sheets");
        }

        float AreaOverTex(float aot, List<int> fs)
        {
            var before = fs.Sum(f => UvData.Area(old, faces[f].VertexIndices));
            if (before <= 1e-12f)
            {
                return aot;
            }

            var ratio = fs.Sum(f => UvData.Area(uv, faces[f].VertexIndices)) / before;
            var result = aot / ratio;
            return ratio > 0 && float.IsFinite(result)
                ? result
                : throw new PatchException($"{context.AssetPath}: uv-split collapses a section's UV area to {ratio}");
        }

        var textures = lod.Textures.Append(newTexture).ToArray();
        LibraryAccess.Set(lod, nameof(LOD.Textures), textures);
        var texB = (short)(textures.Length - 1);
        var perm = Enumerable.Range(0, faces.Length).ToArray();
        var sections = lod.Sections.ToList();
        var selections = lod.NamedSelections.ToList();
        var bSections = new List<int>();
        var split = new HashSet<int>();
        var proxies = OdolGraph.RawProxies(lod).Cast<object>().ToList();
        var sheet = target.Sections.Select(x => sections[x].TextureIndex).ToHashSet();

        for (var s = sections.Count - 1; s >= 0; s--)
        {
            var sec = sections[s];
            var inSec = OdolGraph.FacesInSection(lod, sec);
            if (!inSec.SelectMany(f => faces[f].VertexIndices).Any(owner.ContainsKey))
            {
                continue;
            }

            if (!sheet.Contains(sec.TextureIndex))
            {
                throw new PatchException($"{context.AssetPath}: uv-split plan touches section {s}, whose texture is not one that '{target.Name}' uses");
            }

            var a = inSec.Where(f => !OnB(f)).ToList();
            var b = inSec.Where(OnB).ToList();
            var aot = sec.AreaOverTex[0];
            if (a.Count > 0)
            {
                SetAreaOverTex(sec, AreaOverTex(aot, a));
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

            var splitAt = sec.FaceLowerIndex + a.Sum(f => OdolFaces.Weight(faces[f]));
            var secB = (Section)OdolGraph.Clone(sec);
            LibraryAccess.Set(secB, nameof(Section.FaceLowerIndex), splitAt);
            LibraryAccess.Set(secB, nameof(Section.TextureIndex), texB);
            LibraryAccess.Set(secB, nameof(Section.AreaOverTex), (float[])sec.AreaOverTex.Clone());
            SetAreaOverTex(secB, AreaOverTex(aot, b));
            WrapIfRepeating(secB, b);
            if (a.Count == 0)
            {
                sections[s] = secB;
                bSections.Add(s);
                LibraryAccess.Set(target, nameof(NamedSelection.Sections), new TrackedArray<int>(target.Sections.Where(x => x != s)));
                continue;
            }

            LibraryAccess.Set(sec, nameof(Section.FaceUpperIndex), splitAt);
            sections.Insert(s + 1, secB);
            foreach (var n in selections)
            {
                ShiftSections(n, s, n == target ? null : s + 1);
            }

            foreach (var p in proxies.Where(p => LibraryAccess.Get<int>(p, "SectionIndex") > s))
            {
                LibraryAccess.Set(p, "SectionIndex", LibraryAccess.Get<int>(p, "SectionIndex") + 1);
            }

            bSections = bSections.Select(x => x + 1).Append(s + 1).ToList();
            split = split.Select(x => x + 1).Append(s).Append(s + 1).ToHashSet();
        }

        if (bSections.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: uv-split plan puts no faces on sheet B");
        }

        var inv = new int[perm.Length];
        for (var k = 0; k < perm.Length; k++)
        {
            inv[perm[k]] = k;
        }

        var permuted = perm.Select(k => faces[k]).ToArray();
        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Faces), permuted);
        foreach (var n in selections.Where(n => n.SelectedFaces?.Count > 0))
        {
            LibraryAccess.Set(n, nameof(NamedSelection.SelectedFaces), new TrackedArray<int>(n.SelectedFaces.Select(f => inv[f]).OrderBy(f => f)));
        }

        foreach (var p in proxies.Where(p => split.Contains(LibraryAccess.Get<int>(p, "SectionIndex"))))
        {
            var face = selections[LibraryAccess.Get<int>(p, "NamedSelectionIndex")].SelectedFaces.FirstOrDefault(-1);
            LibraryAccess.Set(p, "SectionIndex", SectionOfFace(permuted, sections, face, context));
        }

        selections.Add(new NamedSelection(newSelection, true, bSections.OrderBy(s => s)));
        LibraryAccess.Set(lod, nameof(LOD.Sections), sections.ToArray());
        lod.NamedSelections = selections.ToArray();
    }

    private static int SectionOfFace(Polygon[] faces, List<Section> sections, int face, OpContext context)
    {
        if (face < 0)
        {
            throw new PatchException($"{context.AssetPath}: uv-split moved a section holding a proxy with no face");
        }

        var offset = faces.Take(face).Sum(OdolFaces.Weight);
        var s = sections.FindIndex(x => offset >= x.FaceLowerIndex && offset < x.FaceUpperIndex);
        return s >= 0 ? s : throw new PatchException($"{context.AssetPath}: uv-split left proxy face {face} outside every section");
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
