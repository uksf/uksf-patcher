using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddReverseFaceOp : OdolOp
{
    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var face = OpJson.Int(op, "face", -1);
        var expected = op.GetProperty("vertices").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var faces = lod.Polygons.Faces;
        if (face < 0 || face >= faces.Length || !faces[face].VertexIndices.SequenceEqual(expected) || expected.Length < 3)
        {
            throw new PatchException($"{context.AssetPath}: face {face} does not match the expected vertices");
        }

        var reversed = expected.Reverse().ToArray();
        if (faces.Any(f => SameCycle(f.VertexIndices, reversed)))
        {
            throw new PatchException($"{context.AssetPath}: face {face} already has a reverse face");
        }

        var ranges = OdolFaces.SectionFaceRanges(lod, context.AssetPath);
        var section = Array.FindIndex(ranges, r => face >= r.First && face < r.End);
        if (section < 0)
        {
            throw new PatchException($"{context.AssetPath}: face {face} has no section");
        }

        var at = face + 1;
        var result = faces.Take(at).Append(OdolFaces.NewFace(reversed)).Concat(faces.Skip(at)).ToArray();
        var updated = ranges.Select((r, s) => s < section ? r : s == section ? (r.First, r.End + 1) : (r.First + 1, r.End + 1)).ToArray();
        foreach (var selection in lod.NamedSelections)
        {
            var selected = selection.SelectedFaces.Select(f => f >= at ? f + 1 : f).ToList();
            if (selection.SelectedFaces.Contains(face))
            {
                selected.Add(at);
            }

            OdolFaces.SetSelectedFaces(selection, selected);
        }

        OdolFaces.Replace(lod, result, updated);
    }

    private static bool SameCycle(int[] a, int[] b) =>
        a.Length == b.Length && Enumerable.Range(0, a.Length).Any(start =>
            Enumerable.Range(0, a.Length).All(i => a[(start + i) % a.Length] == b[i]));
}
