using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComponentAnalyzer;

/// <summary>
/// Generates JSON Schema compatible with ResoniteLink's addComponent/updateComponent commands.
/// </summary>
public class JsonSchemaGenerator
{
    private readonly ComponentLoader _loader;

    public JsonSchemaGenerator(ComponentLoader loader)
    {
        _loader = loader;
    }

    public JsonObject GenerateSchema(Type componentType)
    {
        string componentTypeName = $"[FrooxEngine]{componentType.FullName}";

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"{componentType.FullName}.schema.json",
            ["title"] = componentType.Name,
            ["description"] = $"ResoniteLink schema for {componentType.FullName}",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["componentType"] = new JsonObject
                {
                    ["const"] = componentTypeName,
                    ["description"] = "The component type in Resonite notation"
                },
                ["members"] = GenerateMembersSchema(componentType)
            }
        };

        return schema;
    }

    private JsonObject GenerateMembersSchema(Type componentType)
    {
        var membersSchema = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Component members (fields) and their values",
            ["properties"] = new JsonObject()
        };

        var properties = (JsonObject)membersSchema["properties"]!;
        var fields = PropertyAnalyzer.GetPublicFields(componentType);

        foreach (var field in fields.OrderBy(f => f.Name))
        {
            try
            {
                var fieldSchema = GenerateMemberSchema(field);
                properties[field.Name] = fieldSchema;
            }
            catch
            {
                properties[field.Name] = new JsonObject
                {
                    ["description"] = $"Type: {field.FriendlyTypeName} (could not analyze)"
                };
            }
        }

        return membersSchema;
    }

    private JsonObject GenerateMemberSchema(ComponentField field)
    {
        // Determine the FrooxEngine wrapper type and inner type
        string wrapperType = GetWrapperTypeName(field.FieldType);
        Type innerType = UnwrapFrooxEngineType(field.FieldType);

        return wrapperType switch
        {
            "SyncList" => GenerateSyncListSchema(field, innerType),
            "SyncRefList" => GenerateSyncRefListSchema(field, innerType),
            "SyncRef" or "RelayRef" or "DestroyRelayRef" => GenerateReferenceSchema(field, innerType),
            "AssetRef" => GenerateAssetRefSchema(field, innerType),
            "FieldDrive" => GenerateFieldDriveSchema(field, innerType),
            _ => GenerateFieldSchema(field, innerType)
        };
    }

    private JsonObject GenerateFieldSchema(ComponentField field, Type innerType)
    {
        string? resoniteLinkType = GetResoniteLinkType(innerType);

        if (resoniteLinkType == null)
        {
            // Unknown type - provide description only
            return new JsonObject
            {
                ["type"] = "object",
                ["description"] = $"Field type: {field.FriendlyTypeName}"
            };
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                ["value"] = GenerateValueSchema(innerType, resoniteLinkType)
            },
            ["required"] = new JsonArray { "$type", "value" }
        };

        return schema;
    }

    private JsonObject GenerateValueSchema(Type type, string resoniteLinkType)
    {
        // Handle nullable
        Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Enums
        if (underlyingType.IsEnum)
        {
            var schema = new JsonObject { ["type"] = "string" };
            try
            {
                var enumValues = new JsonArray();
                foreach (var name in Enum.GetNames(underlyingType))
                {
                    enumValues.Add(name);
                }
                schema["enum"] = enumValues;
            }
            catch { }
            return schema;
        }

        // Vector types (float2, float3, float4, etc.)
        if (IsVectorType(resoniteLinkType, out int dimensions))
        {
            return GenerateVectorSchema(dimensions);
        }

        // Quaternion types
        if (resoniteLinkType is "floatQ" or "doubleQ")
        {
            return GenerateQuaternionSchema();
        }

        // Color types
        if (resoniteLinkType is "color" or "colorX")
        {
            return GenerateColorSchema();
        }

        // Primitive types
        return GetPrimitiveValueSchema(underlyingType);
    }

    private static JsonObject GetPrimitiveValueSchema(Type type)
    {
        return type.FullName switch
        {
            "System.Boolean" => new JsonObject { ["type"] = "boolean" },
            "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or
            "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
                => new JsonObject { ["type"] = "integer" },
            "System.Single" or "System.Double" or "System.Decimal"
                => new JsonObject { ["type"] = "number" },
            "System.String" => new JsonObject { ["type"] = "string" },
            "System.Char" => new JsonObject { ["type"] = "string", ["maxLength"] = 1 },
            "System.DateTime" or "System.DateTimeOffset"
                => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            "System.TimeSpan" => new JsonObject { ["type"] = "string" },
            "System.Guid" => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            "System.Uri" => new JsonObject { ["type"] = "string", ["format"] = "uri" },
            _ => new JsonObject { ["type"] = "object" }
        };
    }

    private static JsonObject GenerateVectorSchema(int dimensions)
    {
        var properties = new JsonObject();
        string[] components = ["x", "y", "z", "w"];

        for (int i = 0; i < dimensions; i++)
        {
            properties[components[i]] = new JsonObject { ["type"] = "number" };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(components.Take(dimensions).Select(c => JsonValue.Create(c)).ToArray())
        };
    }

    private static JsonObject GenerateQuaternionSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "number" },
                ["y"] = new JsonObject { ["type"] = "number" },
                ["z"] = new JsonObject { ["type"] = "number" },
                ["w"] = new JsonObject { ["type"] = "number" }
            },
            ["required"] = new JsonArray { "x", "y", "z", "w" }
        };
    }

    private static JsonObject GenerateColorSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["r"] = new JsonObject { ["type"] = "number" },
                ["g"] = new JsonObject { ["type"] = "number" },
                ["b"] = new JsonObject { ["type"] = "number" },
                ["a"] = new JsonObject { ["type"] = "number" }
            },
            ["required"] = new JsonArray { "r", "g", "b", "a" }
        };
    }

    private JsonObject GenerateReferenceSchema(ComponentField field, Type targetType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Reference to {PropertyAnalyzer.GetFriendlyTypeName(targetType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "ID of the target object"
                },
                ["targetType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Type of the target (informational)"
                }
            },
            ["required"] = new JsonArray { "$type" }
        };
    }

    private JsonObject GenerateAssetRefSchema(ComponentField field, Type assetType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Asset reference to {PropertyAnalyzer.GetFriendlyTypeName(assetType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "ID of the asset"
                },
                ["targetType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Type of the asset (informational)"
                }
            },
            ["required"] = new JsonArray { "$type" }
        };
    }

    private JsonObject GenerateFieldDriveSchema(ComponentField field, Type drivenType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Field drive targeting {PropertyAnalyzer.GetFriendlyTypeName(drivenType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "ID of the target field to drive"
                },
                ["targetType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Type of the driven field (informational)"
                }
            },
            ["required"] = new JsonArray { "$type" }
        };
    }

    private JsonObject GenerateSyncListSchema(ComponentField field, Type elementType)
    {
        string? elementResoniteLinkType = GetResoniteLinkType(elementType);

        var elementsItemSchema = elementResoniteLinkType != null
            ? new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = elementResoniteLinkType },
                    ["value"] = GenerateValueSchema(elementType, elementResoniteLinkType)
                }
            }
            : new JsonObject
            {
                ["type"] = "object",
                ["description"] = $"Element type: {PropertyAnalyzer.GetFriendlyTypeName(elementType)}"
            };

        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Synchronized list of {PropertyAnalyzer.GetFriendlyTypeName(elementType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "syncList" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = elementsItemSchema
                }
            },
            ["required"] = new JsonArray { "$type" }
        };
    }

    private JsonObject GenerateSyncRefListSchema(ComponentField field, Type elementType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Synchronized reference list of {PropertyAnalyzer.GetFriendlyTypeName(elementType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "syncList" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["$type"] = new JsonObject { ["const"] = "reference" },
                            ["targetId"] = new JsonObject { ["type"] = "string" },
                            ["targetType"] = new JsonObject { ["type"] = "string" }
                        }
                    }
                }
            },
            ["required"] = new JsonArray { "$type" }
        };
    }

    private static string GetWrapperTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        string typeName = type.Name;
        int backtickIndex = typeName.IndexOf('`');
        return backtickIndex > 0 ? typeName[..backtickIndex] : typeName;
    }

    private static Type UnwrapFrooxEngineType(Type type)
    {
        if (!type.IsGenericType)
            return type;

        string typeName = GetWrapperTypeName(type);

        string[] wrapperTypes = ["Sync", "FieldDrive", "AssetRef", "SyncRef", "RelayRef",
                                  "DestroyRelayRef", "SyncList", "SyncRefList", "SyncAssetList"];

        if (wrapperTypes.Contains(typeName))
        {
            var genericArgs = type.GetGenericArguments();
            if (genericArgs.Length > 0)
            {
                return genericArgs[0];
            }
        }

        return type;
    }

    private static string? GetResoniteLinkType(Type type)
    {
        // Handle nullable
        Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Handle enums - they use the enum type name
        if (underlyingType.IsEnum)
        {
            return "string"; // Enums are sent as strings
        }

        // Map System types to ResoniteLink type names
        string? typeName = underlyingType.FullName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Decimal" => "decimal",
            "System.String" => "string",
            "System.Char" => "char",
            "System.DateTime" => "DateTime",
            "System.TimeSpan" => "TimeSpan",
            "System.Guid" => "string", // Guids are typically strings
            "System.Uri" => "Uri",
            _ => null
        };

        if (typeName != null)
            return typeName;

        // Check for Elements.Core types (vectors, colors, etc.)
        string? shortName = underlyingType.Name;

        // Vector types: int2, float3, double4, etc.
        if (IsVectorType(shortName, out _))
            return shortName;

        // Quaternion types
        if (shortName is "floatQ" or "doubleQ")
            return shortName;

        // Color types
        if (shortName is "color" or "colorX")
            return shortName;

        // Matrix types
        if (shortName.Contains("x") && (shortName.StartsWith("float") || shortName.StartsWith("double")))
            return shortName;

        return null;
    }

    private static bool IsVectorType(string typeName, out int dimensions)
    {
        dimensions = 0;

        string[] prefixes = ["int", "uint", "long", "ulong", "float", "double", "bool"];

        foreach (var prefix in prefixes)
        {
            if (typeName.StartsWith(prefix) && typeName.Length == prefix.Length + 1)
            {
                char lastChar = typeName[^1];
                if (lastChar >= '2' && lastChar <= '4')
                {
                    dimensions = lastChar - '0';
                    return true;
                }
            }
        }

        return false;
    }

    public string SerializeSchema(JsonObject schema)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        return schema.ToJsonString(options);
    }
}
