using System.Reflection;

namespace ComponentAnalyzer;

public record ComponentField(string Name, Type FieldType, string FriendlyTypeName);

public static class PropertyAnalyzer
{
    public static List<ComponentField> GetPublicFields(Type type)
    {
        var fields = new List<ComponentField>();

        try
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                try
                {
                    fields.Add(new ComponentField(
                        field.Name,
                        field.FieldType,
                        GetFriendlyTypeName(field.FieldType)
                    ));
                }
                catch
                {
                    // Skip fields that can't be analyzed
                }
            }
        }
        catch
        {
            // Skip if we can't get fields
        }

        return fields;
    }

    public static List<PropertyInfo> GetPublicReadonlyProperties(Type type)
    {
        var properties = new List<PropertyInfo>();

        try
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                try
                {
                    var getter = prop.GetGetMethod();
                    var setter = prop.GetSetMethod();

                    // Include if: has public getter AND (no setter OR setter is not public)
                    if (getter != null && getter.IsPublic && (setter == null || !setter.IsPublic))
                    {
                        properties.Add(prop);
                    }
                }
                catch
                {
                    // Skip properties that can't be analyzed
                }
            }
        }
        catch
        {
            // Skip if we can't get properties
        }

        return properties;
    }

    public static string GetFriendlyTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            string baseName = type.Name;
            int backtickIndex = baseName.IndexOf('`');
            if (backtickIndex > 0)
            {
                baseName = baseName[..backtickIndex];
            }

            var genericArgs = type.GetGenericArguments();
            var argNames = genericArgs.Select(GetFriendlyTypeName);
            return $"{baseName}<{string.Join(", ", argNames)}>";
        }

        return type.Name;
    }
}
