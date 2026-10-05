using System.Reflection;

namespace UksfPatcher.P3d;

public static class LibraryAccess
{
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static T Get<T>(object target, string property)
    {
        var info = target.GetType().GetProperty(property, InstanceAny)
            ?? throw new MissingMemberException(target.GetType().Name, property);
        return (T)info.GetValue(target)!;
    }

    public static void Set(object target, string property, object? value)
    {
        var field = FindBackingField(target.GetType(), property)
            ?? throw new MissingMemberException(target.GetType().Name, property);
        field.SetValue(target, value);
    }

    private static FieldInfo? FindBackingField(Type? type, string property)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField($"<{property}>k__BackingField", InstanceAny);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }
}
