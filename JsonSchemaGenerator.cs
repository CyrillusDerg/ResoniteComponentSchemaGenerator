using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComponentAnalyzer;

/// <summary>
/// Generates JSON Schema compatible with ResoniteLink's addComponent/updateComponent commands.
/// </summary>
public class JsonSchemaGenerator
{
    private readonly ComponentLoader _loader;
    private readonly GenericTypeResolver? _genericResolver;

    public JsonSchemaGenerator(ComponentLoader loader, GenericTypeResolver? genericResolver = null)
    {
        _loader = loader;
        _genericResolver = genericResolver;
    }

    public JsonObject GenerateSchema(Type componentType)
    {
        // Check if this is a generic type with GenericTypes attribute
        if (componentType.IsGenericTypeDefinition && _genericResolver != null)
        {
            var allowedTypeNames = _genericResolver.GetAllowedTypeNamesForGeneric(componentType);
            if (allowedTypeNames != null && allowedTypeNames.Length > 0)
            {
                return GenerateGenericOneOfSchema(componentType, allowedTypeNames);
            }
        }

        return GenerateConcreteSchema(componentType);
    }

    private JsonObject GenerateConcreteSchema(Type componentType)
    {
        string componentTypeName = $"[FrooxEngine]{componentType.FullName}";

        // Collect type definitions needed for this schema (maps def name to Type)
        var typeDefsNeeded = new Dictionary<string, Type>();
        var membersSchema = GenerateMembersSchema(componentType, useRefs: true, typeDefsNeeded);

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
                ["members"] = membersSchema
            }
        };

        // Add $defs if we have type definitions
        if (typeDefsNeeded.Count > 0)
        {
            var defs = new JsonObject();
            foreach (var (typeDefName, type) in typeDefsNeeded.OrderBy(kvp => kvp.Key))
            {
                var typeDef = GenerateTypeValueDefinitionFromType(type);
                if (typeDef != null)
                {
                    defs[typeDefName] = typeDef;
                }
            }
            schema["$defs"] = defs;
        }

        return schema;
    }

    private JsonObject GenerateGenericOneOfSchema(Type genericTypeDefinition, string[] allowedTypeNames)
    {
        var oneOfArray = new JsonArray();
        var defs = new JsonObject();
        var typeDefsNeeded = new Dictionary<string, Type>();

        string baseName = GetBaseTypeName(genericTypeDefinition);
        int successCount = 0;

        // First pass: generate component variant schemas and collect needed type definitions
        var variantSchemas = new List<(string defName, JsonObject schema, Type typeArg)>();

        foreach (var typeName in allowedTypeNames)
        {
            try
            {
                var typeArg = _loader.FindTypeByFullName(typeName);
                if (typeArg == null)
                {
                    Console.WriteLine($"  Skipping {typeName}: Type not found in metadata context");
                    continue;
                }

                Type concreteType = genericTypeDefinition.MakeGenericType(typeArg);
                string typeArgName = GetSimpleTypeName(typeArg);
                string defName = $"{baseName}_{typeArgName}";

                var concreteSchema = GenerateConcreteSchemaForGenericInstance(concreteType, typeArg, useRefs: true, typeDefsNeeded);
                variantSchemas.Add((defName, concreteSchema, typeArg));
                successCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Skipping {typeName}: {ex.Message}");
            }
        }

        // Add type value definitions to $defs first
        foreach (var (typeDefName, type) in typeDefsNeeded.OrderBy(kvp => kvp.Key))
        {
            var typeDef = GenerateTypeValueDefinitionFromType(type);
            if (typeDef != null)
            {
                defs[typeDefName] = typeDef;
            }
        }

        // Add component variant schemas to $defs
        foreach (var (defName, schema, _) in variantSchemas)
        {
            defs[defName] = schema;
            oneOfArray.Add(new JsonObject
            {
                ["$ref"] = $"#/$defs/{defName}"
            });
        }

        var resultSchema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"{genericTypeDefinition.FullName}.schema.json",
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} with {successCount} type variant(s) for T",
            ["oneOf"] = oneOfArray,
            ["$defs"] = defs
        };

        return resultSchema;
    }

    private JsonObject GenerateConcreteSchemaForGenericInstance(Type concreteType, Type typeArg, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
    {
        string componentTypeName = FormatGenericComponentTypeName(concreteType);

        return new JsonObject
        {
            ["type"] = "object",
            ["title"] = $"{GetBaseTypeName(concreteType.GetGenericTypeDefinition())}<{GetSimpleTypeName(typeArg)}>",
            ["properties"] = new JsonObject
            {
                ["componentType"] = new JsonObject
                {
                    ["const"] = componentTypeName,
                    ["description"] = "The component type in Resonite notation"
                },
                ["members"] = GenerateMembersSchema(concreteType, useRefs, typeDefsNeeded)
            }
        };
    }

    /// <summary>
    /// Gets the $defs name for a type's value schema, or null if no definition is needed.
    /// </summary>
    private static string? GetTypeDefinitionName(Type type)
    {
        Type? nullableUnderlying = GetNullableUnderlyingType(type);
        bool isNullable = nullableUnderlying != null;
        Type underlyingType = nullableUnderlying ?? type;

        // Enums get their own definition based on enum name
        if (underlyingType.IsEnum)
        {
            string enumName = underlyingType.Name;
            return isNullable ? $"nullable_{enumName}_value" : $"{enumName}_value";
        }

        string? resoniteLinkType = GetResoniteLinkType(underlyingType);
        if (resoniteLinkType == null)
            return null;

        return isNullable ? $"nullable_{resoniteLinkType}_value" : $"{resoniteLinkType}_value";
    }

    /// <summary>
    /// Generates a type value definition for use in $defs, given the actual Type.
    /// </summary>
    private JsonObject? GenerateTypeValueDefinitionFromType(Type type)
    {
        Type? nullableUnderlying = GetNullableUnderlyingType(type);
        bool isNullable = nullableUnderlying != null;
        Type underlyingType = nullableUnderlying ?? type;

        string? resoniteLinkType = GetResoniteLinkType(underlyingType);
        if (resoniteLinkType == null)
            return null;

        // For nullable types, append ? to the type name
        string schemaTypeName = isNullable ? $"{resoniteLinkType}?" : resoniteLinkType;

        // Handle enums specially - they need their enum values
        if (underlyingType.IsEnum)
        {
            var valueSchema = new JsonObject { ["type"] = "string" };
            try
            {
                var enumValues = new JsonArray();
                foreach (var name in Enum.GetNames(underlyingType))
                {
                    enumValues.Add(name);
                }
                valueSchema["enum"] = enumValues;
            }
            catch { }

            // For nullable enums, value can also be null
            if (isNullable)
            {
                valueSchema["type"] = new JsonArray { "string", "null" };
            }

            var enumResult = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = isNullable ? "string?" : "string" },
                    ["value"] = valueSchema
                }
            };

            // Only require value for non-nullable types
            if (!isNullable)
            {
                enumResult["required"] = new JsonArray { "$type", "value" };
            }
            else
            {
                enumResult["required"] = new JsonArray { "$type" };
            }

            return enumResult;
        }

        // Handle special types
        if (IsVectorType(resoniteLinkType, out int dimensions))
        {
            var vectorResult = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                    ["value"] = GenerateVectorSchema(dimensions)
                }
            };

            if (!isNullable)
            {
                vectorResult["required"] = new JsonArray { "$type", "value" };
            }
            else
            {
                vectorResult["required"] = new JsonArray { "$type" };
            }

            return vectorResult;
        }

        if (resoniteLinkType is "floatQ" or "doubleQ")
        {
            var quatResult = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                    ["value"] = GenerateQuaternionSchema()
                }
            };

            if (!isNullable)
            {
                quatResult["required"] = new JsonArray { "$type", "value" };
            }
            else
            {
                quatResult["required"] = new JsonArray { "$type" };
            }

            return quatResult;
        }

        if (resoniteLinkType is "color" or "colorX")
        {
            var colorResult = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                    ["value"] = GenerateColorSchema()
                }
            };

            if (!isNullable)
            {
                colorResult["required"] = new JsonArray { "$type", "value" };
            }
            else
            {
                colorResult["required"] = new JsonArray { "$type" };
            }

            return colorResult;
        }

        // Primitive types
        var primitiveValueSchema = resoniteLinkType switch
        {
            "bool" => new JsonObject { ["type"] = isNullable ? new JsonArray { "boolean", "null" } : "boolean" },
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                => new JsonObject { ["type"] = isNullable ? new JsonArray { "integer", "null" } : "integer" },
            "float" or "double" or "decimal"
                => new JsonObject { ["type"] = isNullable ? new JsonArray { "number", "null" } : "number" },
            "string" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },
            "char" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string", ["maxLength"] = 1 },
            "DateTime" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string", ["format"] = "date-time" },
            "TimeSpan" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },
            "Uri" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string", ["format"] = "uri" },
            _ => null
        };

        if (primitiveValueSchema == null)
            return null;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = primitiveValueSchema
            }
        };

        // Only require value for non-nullable types
        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value" };
        }
        else
        {
            result["required"] = new JsonArray { "$type" };
        }

        return result;
    }

    /// <summary>
    /// Generates a type value definition for use in $defs (legacy - used by generic schemas).
    /// </summary>
    [Obsolete("Use GenerateTypeValueDefinitionFromType instead")]
    private JsonObject? GenerateTypeValueDefinition(string typeDefName)
    {
        // Extract the resonite type from the def name (remove "_value" suffix)
        string resoniteLinkType = typeDefName[..^6]; // Remove "_value"

        // Handle special types
        if (IsVectorType(resoniteLinkType, out int dimensions))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                    ["value"] = GenerateVectorSchema(dimensions)
                },
                ["required"] = new JsonArray { "$type", "value" }
            };
        }

        if (resoniteLinkType is "floatQ" or "doubleQ")
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                    ["value"] = GenerateQuaternionSchema()
                },
                ["required"] = new JsonArray { "$type", "value" }
            };
        }

        if (resoniteLinkType is "color" or "colorX")
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                    ["value"] = GenerateColorSchema()
                },
                ["required"] = new JsonArray { "$type", "value" }
            };
        }

        // Primitive types
        var valueSchema = resoniteLinkType switch
        {
            "bool" => new JsonObject { ["type"] = "boolean" },
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                => new JsonObject { ["type"] = "integer" },
            "float" or "double" or "decimal"
                => new JsonObject { ["type"] = "number" },
            "string" => new JsonObject { ["type"] = "string" },
            "char" => new JsonObject { ["type"] = "string", ["maxLength"] = 1 },
            "DateTime" => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            "TimeSpan" => new JsonObject { ["type"] = "string" },
            "Uri" => new JsonObject { ["type"] = "string", ["format"] = "uri" },
            _ => null
        };

        if (valueSchema == null)
            return null;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                ["value"] = valueSchema
            },
            ["required"] = new JsonArray { "$type", "value" }
        };
    }

    private static string FormatGenericComponentTypeName(Type concreteType)
    {
        // Format: [FrooxEngine]FrooxEngine.ValueField<int>
        var genericDef = concreteType.GetGenericTypeDefinition();
        var typeArgs = concreteType.GetGenericArguments();

        var typeArgStrings = typeArgs.Select(t => GetSimpleTypeName(t));

        return $"[FrooxEngine]{genericDef.Namespace}.{GetBaseTypeName(genericDef)}<{string.Join(",", typeArgStrings)}>";
    }

    private static string GetBaseTypeName(Type type)
    {
        string name = type.Name;
        int backtickIndex = name.IndexOf('`');
        return backtickIndex > 0 ? name[..backtickIndex] : name;
    }

    private static string GetSimpleTypeName(Type type)
    {
        // Return a schema-friendly name for the type
        return type.FullName switch
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
            _ => type.Name
        };
    }

    private JsonObject GenerateMembersSchema(Type componentType, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
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
                var fieldSchema = GenerateMemberSchema(field, useRefs, typeDefsNeeded);
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

    private JsonObject GenerateMemberSchema(ComponentField field, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
    {
        // Determine the FrooxEngine wrapper type and inner type
        string wrapperType = GetWrapperTypeName(field.FieldType);
        Type innerType = UnwrapFrooxEngineType(field.FieldType);

        return wrapperType switch
        {
            "SyncList" => GenerateSyncListSchema(field, innerType, useRefs, typeDefsNeeded),
            "SyncRefList" => GenerateSyncRefListSchema(field, innerType),
            "SyncRef" or "RelayRef" or "DestroyRelayRef" => GenerateReferenceSchema(field, innerType),
            "AssetRef" => GenerateAssetRefSchema(field, innerType),
            "FieldDrive" => GenerateFieldDriveSchema(field, innerType),
            _ => GenerateFieldSchema(field, innerType, useRefs, typeDefsNeeded)
        };
    }

    private JsonObject GenerateFieldSchema(ComponentField field, Type innerType, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
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

        // If using refs, return a reference to the type definition
        if (useRefs)
        {
            string? typeDefName = GetTypeDefinitionName(innerType);
            if (typeDefName != null)
            {
                typeDefsNeeded?.TryAdd(typeDefName, innerType);
                return new JsonObject
                {
                    ["$ref"] = $"#/$defs/{typeDefName}"
                };
            }
        }

        // Otherwise, inline the schema
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
        Type underlyingType = GetNullableUnderlyingType(type) ?? type;

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

    private JsonObject GenerateSyncListSchema(ComponentField field, Type elementType, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
    {
        string? elementResoniteLinkType = GetResoniteLinkType(elementType);

        JsonObject elementsItemSchema;

        if (useRefs && elementResoniteLinkType != null)
        {
            string? typeDefName = GetTypeDefinitionName(elementType);
            if (typeDefName != null)
            {
                typeDefsNeeded?.TryAdd(typeDefName, elementType);
                elementsItemSchema = new JsonObject
                {
                    ["$ref"] = $"#/$defs/{typeDefName}"
                };
            }
            else
            {
                elementsItemSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["$type"] = new JsonObject { ["const"] = elementResoniteLinkType },
                        ["value"] = GenerateValueSchema(elementType, elementResoniteLinkType)
                    }
                };
            }
        }
        else if (elementResoniteLinkType != null)
        {
            elementsItemSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = elementResoniteLinkType },
                    ["value"] = GenerateValueSchema(elementType, elementResoniteLinkType)
                }
            };
        }
        else
        {
            elementsItemSchema = new JsonObject
            {
                ["type"] = "object",
                ["description"] = $"Element type: {PropertyAnalyzer.GetFriendlyTypeName(elementType)}"
            };
        }

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
        // Handle nullable - Nullable.GetUnderlyingType doesn't work with MetadataLoadContext types
        Type underlyingType = GetNullableUnderlyingType(type) ?? type;

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

    /// <summary>
    /// Gets the underlying type of a Nullable&lt;T&gt;, handling MetadataLoadContext types.
    /// </summary>
    private static Type? GetNullableUnderlyingType(Type type)
    {
        // First try the built-in method (works for runtime types)
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            return underlying;

        // For MetadataLoadContext types, check manually
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef.FullName == "System.Nullable`1")
            {
                var args = type.GetGenericArguments();
                if (args.Length == 1)
                    return args[0];
            }
        }

        return null;
    }

    private static bool IsVectorType(string typeName, out int dimensions)
    {
        dimensions = 0;

        // Order matters - longer prefixes must come before shorter ones (e.g., "ushort" before "short")
        string[] prefixes = ["ushort", "short", "ubyte", "sbyte", "byte", "uint", "ulong", "int", "long", "float", "double", "bool"];

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
