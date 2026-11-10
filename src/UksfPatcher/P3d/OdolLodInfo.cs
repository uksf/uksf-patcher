using BIS.P3D.ODOL;

namespace UksfPatcher.P3d;

public static class OdolLodInfo
{
    public static void Sync(LOD lod)
    {
        var info = typeof(LOD).GetProperty("LoadableLodInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(lod);
        if (info == null)
        {
            return;
        }

        LibraryAccess.Set(info, "NumberOfFaces", lod.Polygons.Faces.Length);
        LibraryAccess.Set(info, "NumberOfVertices", (int)lod.VertexCount);
        LibraryAccess.Set(info, "OrHints", (uint)lod.OrHints);
    }
}
