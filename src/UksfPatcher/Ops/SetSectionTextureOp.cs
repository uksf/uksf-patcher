using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class SetSectionTextureOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var lod = FindLod(odol, OpJson.String(op, "lod"), context);
        var section = OpJson.Int(op, "section", -1);
        var texture = OpJson.String(op, "texture");
        if (section < 0 || section >= lod.Sections.Length)
        {
            throw new PatchException($"{context.AssetPath}: LOD {LodNames.Name(lod.Resolution)} has no section {section}");
        }

        var index = Array.FindIndex(lod.Textures, t => t.Equals(texture, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = lod.Textures.Length;
            LibraryAccess.Set(lod, nameof(LOD.Textures), lod.Textures.Append(texture).ToArray());
        }

        LibraryAccess.Set(lod.Sections[section], nameof(Section.TextureIndex), (short)index);
    }
}
