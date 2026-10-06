using UksfPatcher.P3d;
using UksfPatcher.Patching;
using UksfPatcher.Pbo;

namespace UksfPatcher.Cli;

public static class P3dInfo
{
    public static int Run(string pboPath, string asset, string? lodName, TextWriter output)
    {
        var pbo = RawPbo.Read(pboPath);
        var entry = pbo.Find(asset) ?? throw new PatchException($"{asset} is not in {pboPath}");
        var where = $"{pboPath}:{asset}";
        var bytes = AssetFormat.Guard(where, () => pbo.ReadData(entry));
        var odol = OdolFile.Parse(bytes, where);
        var identical = OdolFile.Serialise(odol).AsSpan().SequenceEqual(bytes);
        output.WriteLine($"ODOL v{odol.Version}, {odol.Lods.Length} LODs, {bytes.Length} bytes, sha256 {StateStore.Sha256(bytes)}, re-serialises identically: {identical}");
        var skeleton = odol.ModelInfo.Skeleton;
        output.WriteLine($"skeleton '{skeleton.SkeletonName}': {skeleton.SkeletonBoneNames?.Length ?? 0} bones");
        foreach (var bone in skeleton.SkeletonBoneNames ?? [])
        {
            output.WriteLine($"  bone {bone.BoneName} <- {bone.ParentBoneName}");
        }

        if (lodName != null)
        {
            return Detail(odol, lodName, output);
        }

        foreach (var lod in odol.Lods)
        {
            var components = lod.NamedSelections.Count(s => s.Name.StartsWith("component", StringComparison.OrdinalIgnoreCase));
            output.WriteLine($"lod {LodNames.Name(lod.Resolution)} ({lod.Resolution:G9}): {lod.Vertices.Count} vertices, {lod.Polygons.Faces.Length} faces, {lod.Sections.Length} sections, {lod.NamedSelections.Length} selections ({components} components)");
        }

        return 0;
    }

    private static int Detail(BIS.P3D.ODOL.ODOL odol, string lodName, TextWriter output)
    {
        var lod = odol.Lods.Single(l => LodNames.Matches(l.Resolution, LodNames.Resolution(lodName)));
        var faceBytes = lod.Polygons.Faces.Select(f => 1 + f.VertexIndices.Length * (odol.Version >= 69 ? 4 : 2)).ToArray();
        output.WriteLine($"faces {faceBytes.Length}, face bytes {faceBytes.Sum()}, polygons unused {lod.Polygons.Unused1}/{lod.Polygons.Unused2}, face area {lod.FaceArea}");
        foreach (var s in lod.Sections)
        {
            output.WriteLine($"section {s.FaceLowerIndex}..{s.FaceUpperIndex} material {s.MaterialIndex} texture {s.TextureIndex}");
        }

        foreach (var s in lod.NamedSelections)
        {
            output.WriteLine($"selection {s.Name}: {s.SelectedFaces.Count} faces, {s.SelectedVertices.Count} vertices, {s.SelectedVerticesWeights.Count} weights, sectional {s.IsSectional}, sections [{string.Join(",", s.Sections)}]");
        }

        return 0;
    }
}
