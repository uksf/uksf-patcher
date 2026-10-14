using UksfPatcher.Patching;

namespace UksfPatcher.P3d;

public static class LodNames
{
    private static readonly Dictionary<string, float> Special = new(StringComparer.OrdinalIgnoreCase)
    {
        ["geometry"] = 1e13f,
        ["geometrySimple"] = 2e13f,
        ["geometryPhys"] = 4e13f,
        ["geometryPhysOld"] = 3e13f,
        ["viewPilot"] = 1100f,
        ["memory"] = 1e15f,
        ["landContact"] = 2e15f,
        ["roadway"] = 3e15f,
        ["paths"] = 4e15f,
        ["hitpoints"] = 5e15f,
        ["geometryView"] = 6e15f,
        ["geometryFire"] = 7e15f,
        ["viewGeometryCargo"] = 8e15f,
        ["viewGeometryPilot"] = 1.3e16f,
        ["viewGeometryGunner"] = 1.5e16f,
    };

    public static float Resolution(string name)
    {
        if (Special.TryGetValue(name, out var value))
        {
            return value;
        }

        if (float.TryParse(name, System.Globalization.CultureInfo.InvariantCulture, out var visual) && visual < 1000)
        {
            return visual;
        }

        throw new PatchException($"unknown LOD name '{name}' (use {string.Join(", ", Special.Keys)} or a visual resolution)");
    }

    public static string Name(float resolution)
    {
        foreach (var (name, value) in Special)
        {
            if (Matches(resolution, value))
            {
                return name;
            }
        }

        return resolution.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool Matches(float resolution, float wanted) =>
        resolution == wanted || (wanted > 1000 && Math.Abs(resolution - wanted) <= wanted * 1e-6f);
}
