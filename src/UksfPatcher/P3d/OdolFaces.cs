using System.Runtime.CompilerServices;
using BIS.Core;
using BIS.P3D.ODOL;
using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class OdolFaces
{
    public static int Weight(Polygon face) => 4 + 4 * face.VertexIndices.Length;

    public static Polygon NewFace(params int[] vertices)
    {
        var face = (Polygon)RuntimeHelpers.GetUninitializedObject(typeof(Polygon));
        LibraryAccess.Set(face, nameof(Polygon.VertexIndices), vertices);
        return face;
    }

    public static (int First, int End)[] SectionFaceRanges(LOD lod, string assetPath)
    {
        var offsets = new int[lod.Polygons.Faces.Length + 1];
        for (var i = 0; i < lod.Polygons.Faces.Length; i++)
        {
            offsets[i + 1] = offsets[i] + Weight(lod.Polygons.Faces[i]);
        }

        if (offsets[^1] != lod.Polygons.Unused1)
        {
            throw new PatchException($"{assetPath}: LOD {LodNames.Name(lod.Resolution)} face data size {lod.Polygons.Unused1} does not match its faces ({offsets[^1]})");
        }

        var ranges = new (int, int)[lod.Sections.Length];
        var expectedStart = 0;
        for (var s = 0; s < lod.Sections.Length; s++)
        {
            var first = Array.IndexOf(offsets, lod.Sections[s].FaceLowerIndex);
            var end = Array.IndexOf(offsets, lod.Sections[s].FaceUpperIndex);
            if (first != expectedStart || end < first)
            {
                throw new PatchException($"{assetPath}: LOD {LodNames.Name(lod.Resolution)} sections do not partition its faces in order");
            }

            ranges[s] = (first, end);
            expectedStart = end;
        }

        if (expectedStart != lod.Polygons.Faces.Length && lod.Sections.Length > 0)
        {
            throw new PatchException($"{assetPath}: LOD {LodNames.Name(lod.Resolution)} sections do not cover every face");
        }

        return ranges;
    }

    public static void Replace(LOD lod, Polygon[] faces, (int First, int End)[] sectionRanges)
    {
        var offsets = new int[faces.Length + 1];
        for (var i = 0; i < faces.Length; i++)
        {
            offsets[i + 1] = offsets[i] + Weight(faces[i]);
        }

        for (var s = 0; s < lod.Sections.Length; s++)
        {
            LibraryAccess.Set(lod.Sections[s], nameof(Section.FaceLowerIndex), offsets[sectionRanges[s].First]);
            LibraryAccess.Set(lod.Sections[s], nameof(Section.FaceUpperIndex), offsets[sectionRanges[s].End]);
        }

        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Faces), faces);
        LibraryAccess.Set(lod.Polygons, nameof(Polygons.Unused1), (uint)offsets[^1]);
    }

    public static void RequirePartitions(ODOL odol, string assetPath)
    {
        foreach (var lod in odol.Lods)
        {
            SectionFaceRanges(lod, assetPath);
        }
    }

    public static void SetSelectedFaces(NamedSelection selection, IEnumerable<int> faces) =>
        LibraryAccess.Set(selection, nameof(NamedSelection.SelectedFaces), new TrackedArray<int>(faces.Order()));
}
