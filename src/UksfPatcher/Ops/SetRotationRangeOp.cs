using System.Text.Json;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class SetRotationRangeOp : OdolOp
{
    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var name = OpJson.String(op, "animation");
        var angle0 = OpJson.Float(op, "angle0");
        var angle1 = OpJson.Float(op, "angle1");
        var hits = (odol.Animations?.AnimationClasses ?? []).Where(a => a.AnimName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hits.Length != 1 || hits[0].AnimType > 3)
        {
            throw new PatchException($"{context.AssetPath}: expected one rotation animation '{name}'");
        }

        LibraryAccess.Set(hits[0], nameof(AnimationClass.Angle0), angle0);
        LibraryAccess.Set(hits[0], nameof(AnimationClass.Angle1), angle1);
    }
}
