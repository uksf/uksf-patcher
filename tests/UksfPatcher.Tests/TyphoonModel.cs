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
}
