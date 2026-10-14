using BIS.P3D.ODOL;
using UksfPatcher.P3d;

namespace UksfPatcher.Tests;

public static class TyphoonModel
{
    public static LOD Lod0() => OdolFile.Parse(Models.Load(Models.EawsPbo, Models.EawsFuselage), "typhoon").Lods[0];

    public static int FirstVertex(LOD lod, int section) =>
        SectionVertices(lod, section)[0];

    public static List<int> SectionVertices(LOD lod, int section) =>
        OdolGraph.FacesInSection(lod, lod.Sections[section]).SelectMany(f => lod.Polygons.Faces[f].VertexIndices).Distinct().ToList();

    public static bool Isolated(LOD lod, int s)
    {
        var own = OdolGraph.FacesInSection(lod, lod.Sections[s]).ToHashSet();
        var verts = own.SelectMany(f => lod.Polygons.Faces[f].VertexIndices).ToHashSet();
        return own.Count > 0 && !Enumerable.Range(0, lod.Polygons.Faces.Length).Where(f => !own.Contains(f))
            .Any(f => lod.Polygons.Faces[f].VertexIndices.Any(verts.Contains));
    }

    public static bool Unweighted(LOD lod, int s)
    {
        var refs = OdolGraph.BoneRefs(lod);
        return SectionVertices(lod, s).All(v => OdolGraph.BoneCount(refs[v]) == 0);
    }
}
