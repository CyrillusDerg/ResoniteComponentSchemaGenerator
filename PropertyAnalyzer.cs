using System.Reflection;

namespace ComponentAnalyzer;

public record ComponentField(string Name, Type FieldType, string FriendlyTypeName);

public static class PropertyAnalyzer
{
    public static List<ComponentField> GetPublicFields(Type type, bool includeInherited = false)
    {
        var fields = new List<ComponentField>();

        try
        {
            var bindingFlags = BindingFlags.Public | BindingFlags.Instance;
            if (!includeInherited)
            {
                bindingFlags |= BindingFlags.DeclaredOnly;
            }
            foreach (var field in type.GetFields(bindingFlags))
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

    /// <summary>
    /// Gets all serializable Sync fields from a component type, including protected fields from base classes.
    /// Handles [NameOverride] attribute to get the correct serialized field name.
    /// </summary>
    public static List<ComponentField> GetAllSerializableFields(Type type)
    {
        var fields = new List<ComponentField>();
        var seenNames = new HashSet<string>();

        try
        {
            // Walk up the inheritance chain to get all fields including protected ones
            Type? currentType = type;
            while (currentType != null && currentType.FullName != "System.Object")
            {
                var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                foreach (var field in currentType.GetFields(bindingFlags))
                {
                    try
                    {
                        // Only include Sync<T> wrapper types (serializable fields)
                        if (!IsSyncWrapperType(field.FieldType))
                            continue;

                        // Get the serialized name (check for NameOverride attribute)
                        string serializedName = GetSerializedFieldName(field);

                        // Skip if we've already seen this name (from a derived class)
                        if (!seenNames.Add(serializedName))
                            continue;

                        fields.Add(new ComponentField(
                            serializedName,
                            field.FieldType,
                            GetFriendlyTypeName(field.FieldType)
                        ));
                    }
                    catch
                    {
                        // Skip fields that can't be analyzed
                    }
                }

                currentType = currentType.BaseType;
            }
        }
        catch
        {
            // Skip if we can't get fields
        }

        return fields;
    }

    /// <summary>
    /// Checks if a type is a Sync wrapper type (Sync, SyncRef, SyncList, etc.)
    /// </summary>
    private static bool IsSyncWrapperType(Type type)
    {
        if (!type.IsGenericType)
            return false;

        string typeName = type.Name;
        int backtickIndex = typeName.IndexOf('`');
        if (backtickIndex > 0)
        {
            typeName = typeName[..backtickIndex];
        }

        string[] syncWrapperTypes = ["Sync", "SyncRef", "SyncList", "SyncRefList", "SyncAssetList", "SyncFieldList",
                                      "AssetRef", "FieldDrive", "DriveRef", "RelayRef", "DestroyRelayRef", "RawOutput"];

        return syncWrapperTypes.Contains(typeName);
    }

    /// <summary>
    /// Gets the serialized field name, checking for [NameOverride] attribute.
    /// </summary>
    private static string GetSerializedFieldName(FieldInfo field)
    {
        try
        {
            // Look for NameOverride attribute
            var nameOverrideAttr = field.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == "NameOverride" ||
                                     a.AttributeType.Name == "NameOverrideAttribute");

            if (nameOverrideAttr != null && nameOverrideAttr.ConstructorArguments.Count > 0)
            {
                var overrideName = nameOverrideAttr.ConstructorArguments[0].Value as string;
                if (!string.IsNullOrEmpty(overrideName))
                {
                    return overrideName;
                }
            }
        }
        catch
        {
            // Fall back to field name if attribute can't be read
        }

        return field.Name;
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
