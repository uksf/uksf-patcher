using System.Text.Json;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class RestoreLodProxiesOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var filter = OpJson.String(op, "filter");
        var from = FindLod(odol, OpJson.String(op, "from"), context);
        var sources = Matching(from, filter);
        if (sources.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: no LOD {LodNames.Name(from.Resolution)} proxies match '{filter}'");
        }

        foreach (var name in OpJson.Strings(op, "lods"))
        {
            Restore(FindLod(odol, name, context), from, sources, context);
        }
    }

    private static List<object> Matching(LOD lod, string filter) =>
        OdolGraph.RawProxies(lod).Cast<object>().Where(p => Model(p).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

    private static string Model(object proxy) => LibraryAccess.Get<string>(proxy, "ProxyModel");

    private static int Seq(object proxy) => LibraryAccess.Get<int>(proxy, "SequenceID");

    private static string Key(object proxy) => $"{Model(proxy).ToLowerInvariant()}.{Seq(proxy):000}";

    private static void Restore(LOD lod, LOD from, IReadOnlyList<object> sources, OpContext context)
    {
        var lodName = LodNames.Name(lod.Resolution);
        var proxies = OdolGraph.RawProxies(lod).Cast<object>().ToList();
        var present = proxies.Select(Key).ToHashSet();
        var missing = sources.Where(p => !present.Contains(Key(p))).ToList();
        if (missing.Count == 0)
        {
            throw new PatchException($"{context.AssetPath}: LOD {lodName} already has every matching proxy");
        }

        foreach (var source in missing)
        {
            var selectionName = $"proxy:{Model(source)}.{Seq(source):000}";
            var selection = Array.FindIndex(lod.NamedSelections, s => s.Name.Equals(selectionName, StringComparison.OrdinalIgnoreCase));
            if (selection < 0)
            {
                throw new PatchException($"{context.AssetPath}: LOD {lodName} has no selection '{selectionName}'");
            }

            var proxy = OdolGraph.New(OdolGraph.ProxyType);
            LibraryAccess.Set(proxy, "ProxyModel", Model(source));
            LibraryAccess.Set(proxy, "Transformation", LibraryAccess.Get<Matrix4P>(source, "Transformation"));
            LibraryAccess.Set(proxy, "SequenceID", Seq(source));
            LibraryAccess.Set(proxy, "NamedSelectionIndex", selection);
            LibraryAccess.Set(proxy, "BoneIndex", LibraryAccess.Get<int>(source, "BoneIndex"));
            LibraryAccess.Set(proxy, "SectionIndex", Section(lod, from, source, proxies, context));
            proxies.Add(proxy);
        }

        OdolGraph.SetRawProxies(lod, proxies);
    }

    private static int Section(LOD lod, LOD from, object source, IReadOnlyList<object> proxies, OpContext context)
    {
        var sibling = proxies.FirstOrDefault(p => Model(p).Equals(Model(source), StringComparison.OrdinalIgnoreCase));
        if (sibling != null)
        {
            return LibraryAccess.Get<int>(sibling, "SectionIndex");
        }

        var texture = Texture(from, from.Sections[LibraryAccess.Get<int>(source, "SectionIndex")]);
        var section = Array.FindIndex(lod.Sections, s => Texture(lod, s).Equals(texture, StringComparison.OrdinalIgnoreCase));
        return section >= 0
            ? section
            : throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no section textured '{texture}' for {Model(source)}");
    }

    private static string Texture(LOD lod, Section section) => section.TextureIndex < 0 ? "" : lod.Textures[section.TextureIndex];
}
