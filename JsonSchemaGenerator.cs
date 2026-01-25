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

    /// <summary>
    /// When true, common value types reference common.schema.json instead of being embedded in each schema.
    /// Enum types are always embedded since they are component-specific.
    /// </summary>
    public bool UseExternalCommonSchema { get; set; }

    /// <summary>
    /// The filename of the common schema (default: "common.schema.json").
    /// </summary>
    public string CommonSchemaFileName { get; set; } = "common.schema.json";

    public JsonSchemaGenerator(ComponentLoader loader, GenericTypeResolver? genericResolver = null)
    {
        _loader = loader;
        _genericResolver = genericResolver;
    }

    /// <summary>
    /// Computes a hash bucket (0-255) for a componentType string.
    /// This is used to distribute components across 256 schema files for faster validation.
    /// </summary>
    /// <param name="componentType">The componentType string (e.g., "[FrooxEngine]FrooxEngine.AudioOutput").</param>
    /// <returns>A value from 0 to 255.</returns>
    public static int GetComponentTypeBucket(string componentType)
    {
        // Use a simple hash: sum of all character codes modulo 256
        int hash = 0;
        foreach (char c in componentType)
        {
            hash = (hash + c) % 256;
        }
        return hash;
    }

    /// <summary>
    /// Gets the schema filename for a given bucket number.
    /// </summary>
    /// <param name="bucket">Bucket number (0-255).</param>
    /// <param name="prefix">Prefix for the filename ("components" or "protoflux").</param>
    /// <returns>Filename like "components_A3.schema.json".</returns>
    public static string GetBucketSchemaFileName(int bucket, string prefix)
    {
        return $"{prefix}_{bucket:X2}.schema.json";
    }

    /// <summary>
    /// Gets the componentType string for a Type in ResoniteLink format.
    /// </summary>
    private string GetComponentTypeString(Type type)
    {
        return $"{GetAssemblyPrefix(type)}{type.FullName}";
    }

    /// <summary>
    /// Creates a safe schema $id from a type name, replacing characters that cause URI resolution issues.
    /// </summary>
    private static string GetSafeSchemaId(Type type)
    {
        string name = type.FullName ?? type.Name;
        // Replace backtick (used in generic type names like `1) with underscore
        // to avoid URI resolution issues
        return name.Replace('`', '_') + ".schema.json";
    }

    /// <summary>
    /// Generates the common schema containing all standard value type definitions.
    /// This includes primitives, vectors, quaternions, colors, and matrices.
    /// Enum types are NOT included as they are component-specific.
    /// </summary>
    /// <returns>The common type JSON schema.</returns>
    public JsonObject GenerateCommonSchema()
    {
        var defs = new JsonObject();

        // All common types that should be in the shared schema
        var commonTypes = GetAllCommonTypeDefinitions();

        foreach (var (defName, schema) in commonTypes.OrderBy(kvp => kvp.Key))
        {
            defs[defName] = schema;
        }

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = CommonSchemaFileName,
            ["title"] = "Common Value Types",
            ["description"] = "Shared value type definitions for ResoniteLink component schemas",
            ["$defs"] = defs
        };
    }

    /// <summary>
    /// Generates all chunked schemas: components (including ProtoFlux), enums, and the main schema.
    /// Components and ProtoFlux nodes are combined into components_XX.schema.json files.
    /// Enums are placed in enums_XX.schema.json files.
    /// </summary>
    /// <param name="componentTypes">All component types (FrooxEngine components and ProtoFlux nodes).</param>
    /// <returns>Component bucket schemas, enum bucket schemas, and the main all_components schema.</returns>
    public (Dictionary<int, JsonObject> ComponentBuckets, Dictionary<int, JsonObject> EnumBuckets, JsonObject MainSchema)
        GenerateAllChunkedSchemas(IEnumerable<Type> componentTypes)
    {
        // First pass: collect all types and their required enum types
        var typesList = componentTypes.ToList();
        var allEnumTypes = new Dictionary<string, Type>(); // defName -> Type
        var componentsByBucket = new Dictionary<int, List<Type>>();

        for (int i = 0; i < 256; i++)
        {
            componentsByBucket[i] = new List<Type>();
        }

        // Group components by bucket
        foreach (var type in typesList)
        {
            string componentType = GetComponentTypeString(type);
            int bucket = GetComponentTypeBucket(componentType);
            componentsByBucket[bucket].Add(type);
        }

        // Generate component schemas and collect enum types
        var componentBuckets = new Dictionary<int, JsonObject>();
        int totalComponentSuccess = 0;
        int totalComponentError = 0;

        foreach (var (bucket, bucketTypes) in componentsByBucket.Where(b => b.Value.Count > 0))
        {
            var defs = new JsonObject();
            var enumTypesNeeded = new Dictionary<string, Type>();

            foreach (var type in bucketTypes.OrderBy(t => t.FullName))
            {
                try
                {
                    string defName = GetSafeDefName(type);
                    var nodeSchema = GenerateSchemaForDefWithExternalEnums(type, enumTypesNeeded);
                    defs[defName] = nodeSchema;
                    totalComponentSuccess++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error generating schema for {type.FullName}: {ex.Message}");
                    totalComponentError++;
                }
            }

            // Merge enum types into the global collection
            foreach (var (enumDefName, enumType) in enumTypesNeeded)
            {
                allEnumTypes.TryAdd(enumDefName, enumType);
            }

            string schemaFileName = GetBucketSchemaFileName(bucket, "components");
            componentBuckets[bucket] = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$id"] = schemaFileName,
                ["title"] = $"Components (Bucket {bucket:X2})",
                ["description"] = $"Schema chunk {bucket:X2} containing {bucketTypes.Count} component(s)",
                ["$defs"] = defs
            };
        }

        Console.WriteLine($"Generated {totalComponentSuccess} component schema(s) across {componentBuckets.Count} bucket file(s)");
        if (totalComponentError > 0)
        {
            Console.WriteLine($"Failed: {totalComponentError}");
        }

        // Generate enum bucket schemas
        var enumBuckets = new Dictionary<int, JsonObject>();
        var enumsByBucket = new Dictionary<int, List<(string DefName, Type EnumType)>>();

        for (int i = 0; i < 256; i++)
        {
            enumsByBucket[i] = new List<(string, Type)>();
        }

        foreach (var (defName, enumType) in allEnumTypes)
        {
            int bucket = GetComponentTypeBucket(defName);
            enumsByBucket[bucket].Add((defName, enumType));
        }

        foreach (var (bucket, enumDefs) in enumsByBucket.Where(b => b.Value.Count > 0))
        {
            var defs = new JsonObject();

            foreach (var (defName, enumType) in enumDefs.OrderBy(e => e.DefName))
            {
                var enumSchema = GenerateEnumValueDefinition(enumType);
                if (enumSchema != null)
                {
                    defs[defName] = enumSchema;
                }
            }

            string schemaFileName = GetBucketSchemaFileName(bucket, "enums");
            enumBuckets[bucket] = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$id"] = schemaFileName,
                ["title"] = $"Enums (Bucket {bucket:X2})",
                ["description"] = $"Enum definitions chunk {bucket:X2} containing {enumDefs.Count} enum(s)",
                ["$defs"] = defs
            };
        }

        Console.WriteLine($"Generated {allEnumTypes.Count} enum definition(s) across {enumBuckets.Count} bucket file(s)");

        // Generate main schema that references all component bucket files
        var oneOfRefs = new JsonArray();
        foreach (var (bucket, schema) in componentBuckets.OrderBy(b => b.Key))
        {
            var defs = schema["$defs"]?.AsObject();
            if (defs == null) continue;

            string schemaFileName = GetBucketSchemaFileName(bucket, "components");
            foreach (var kvp in defs.OrderBy(k => k.Key))
            {
                var def = kvp.Value?.AsObject();
                if (def != null && IsComponentSchema(def))
                {
                    oneOfRefs.Add(new JsonObject
                    {
                        ["$ref"] = $"{schemaFileName}#/$defs/{kvp.Key}"
                    });
                }
            }
        }

        var mainSchema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "all_components.schema.json",
            ["title"] = "All Components",
            ["description"] = $"Combined schema referencing all component types across {componentBuckets.Count} chunk file(s)",
            ["oneOf"] = oneOfRefs
        };

        return (componentBuckets, enumBuckets, mainSchema);
    }

    /// <summary>
    /// Generates a schema for a component type, collecting enum types separately for external reference.
    /// </summary>
    private JsonObject GenerateSchemaForDefWithExternalEnums(Type componentType, Dictionary<string, Type> enumTypesNeeded)
    {
        // Check if this is a generic type with GenericTypes attribute
        if (componentType.IsGenericTypeDefinition && _genericResolver != null)
        {
            var allowedTypeNames = _genericResolver.GetAllowedTypeNamesForGeneric(componentType);
            if (allowedTypeNames != null && allowedTypeNames.Length > 0)
            {
                return GenerateGenericOneOfSchemaForDefWithExternalEnums(componentType, allowedTypeNames, enumTypesNeeded);
            }
        }

        // For generic type definitions without known allowed types, generate a flexible schema
        if (componentType.IsGenericTypeDefinition)
        {
            return GenerateFlexibleGenericSchemaForDefWithExternalEnums(componentType, enumTypesNeeded);
        }

        return GenerateConcreteSchemaForDefWithExternalEnums(componentType, enumTypesNeeded);
    }

    /// <summary>
    /// Generates a concrete component schema with enum references pointing to external enum bucket files.
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateConcreteSchemaForDefWithExternalEnums(Type componentType, Dictionary<string, Type> enumTypesNeeded)
    {
        string componentTypeName = $"{GetAssemblyPrefix(componentType)}{componentType.FullName}";
        var membersSchema = GenerateMembersSchemaWithExternalEnums(componentType, enumTypesNeeded);

        return new JsonObject
        {
            ["title"] = componentType.Name,
            ["description"] = $"ResoniteLink schema for {componentType.FullName}",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
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
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates a oneOf schema for generic types with external enum references.
    /// </summary>
    private JsonObject GenerateGenericOneOfSchemaForDefWithExternalEnums(Type genericTypeDefinition, string[] allowedTypeNames, Dictionary<string, Type> enumTypesNeeded)
    {
        var oneOfArray = new JsonArray();
        var localDefs = new JsonObject();
        string baseName = GetBaseTypeName(genericTypeDefinition);
        int successCount = 0;

        foreach (var typeName in allowedTypeNames)
        {
            try
            {
                var typeArg = _loader.FindTypeByFullName(typeName);
                if (typeArg == null)
                    continue;

                var concreteType = genericTypeDefinition.MakeGenericType(typeArg);
                string typeArgName = GetSimpleTypeName(typeArg);
                string variantDefName = $"{baseName}_{typeArgName}";

                var concreteSchema = GenerateConcreteSchemaForGenericInstanceWithExternalEnums(concreteType, typeArg, enumTypesNeeded);
                localDefs[variantDefName] = concreteSchema;
                oneOfArray.Add(new JsonObject { ["$ref"] = $"#/$defs/{variantDefName}" });
                successCount++;
            }
            catch
            {
                // Skip types that fail
            }
        }

        var schema = new JsonObject
        {
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} with {successCount} type variant(s)"
        };

        if (oneOfArray.Count > 0)
        {
            schema["oneOf"] = oneOfArray;
        }

        if (localDefs.Count > 0)
        {
            schema["$defs"] = localDefs;
        }

        return schema;
    }

    /// <summary>
    /// Generates a flexible schema for generic types without [GenericTypes] attribute, with external enum references.
    /// </summary>
    private JsonObject GenerateFlexibleGenericSchemaForDefWithExternalEnums(Type genericTypeDefinition, Dictionary<string, Type> enumTypesNeeded)
    {
        string baseName = GetBaseTypeName(genericTypeDefinition);
        string assemblyPrefix = GetAssemblyPrefix(genericTypeDefinition);
        string ns = genericTypeDefinition.Namespace ?? "";

        string escapedAssemblyPrefix = EscapeForJsonRegex(assemblyPrefix);
        string escapedNamespace = EscapeForJsonRegex(ns);
        string escapedBaseName = EscapeForJsonRegex(baseName);
        string componentTypePattern = $"^{escapedAssemblyPrefix}{escapedNamespace}\\.{escapedBaseName}<.+>$";

        var membersSchema = GenerateFlexibleMembersSchemaWithExternalEnums(genericTypeDefinition, enumTypesNeeded);

        return new JsonObject
        {
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} (generic type - accepts any valid type argument)",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["componentType"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["pattern"] = componentTypePattern,
                            ["description"] = "The component type in Resonite notation (matches any valid type argument)"
                        },
                        ["members"] = membersSchema
                    }
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates a concrete schema for a generic type instance with external enum references.
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateConcreteSchemaForGenericInstanceWithExternalEnums(Type concreteType, Type typeArg, Dictionary<string, Type> enumTypesNeeded)
    {
        string typeArgName = GetSimpleTypeName(typeArg);
        string componentTypeName = $"{GetAssemblyPrefix(concreteType.GetGenericTypeDefinition())}{concreteType.GetGenericTypeDefinition().Namespace}.{GetBaseTypeName(concreteType.GetGenericTypeDefinition())}<{typeArgName}>";

        var membersSchema = GenerateMembersSchemaWithExternalEnums(concreteType, enumTypesNeeded);

        return new JsonObject
        {
            ["title"] = $"{GetBaseTypeName(concreteType.GetGenericTypeDefinition())}<{typeArgName}>",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
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
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates members schema with external enum references (pointing to enums_XX.schema.json files).
    /// Uses allOf to combine common member_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateMembersSchemaWithExternalEnums(Type componentType, Dictionary<string, Type> enumTypesNeeded)
    {
        // Common member property names that are defined in member_properties
        var commonMemberNames = new HashSet<string> { "Enabled", "persistent", "UpdateOrder" };

        var componentSpecificProperties = new JsonObject();

        var allFields = PropertyAnalyzer.GetAllSerializableFields(componentType);

        foreach (var field in allFields.OrderBy(f => f.Name))
        {
            // Skip common member properties - they're included via member_properties ref
            if (commonMemberNames.Contains(field.Name))
                continue;

            try
            {
                var fieldSchema = GenerateMemberSchemaWithExternalEnums(field, enumTypesNeeded);
                componentSpecificProperties[field.Name] = fieldSchema;
            }
            catch
            {
                componentSpecificProperties[field.Name] = new JsonObject
                {
                    ["additionalProperties"] = false,
                    ["description"] = $"Type: {field.FriendlyTypeName} (could not analyze)"
                };
            }
        }

        return new JsonObject
        {
            ["description"] = "Component members (fields) and their values",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/member_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = componentSpecificProperties
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates flexible members schema for generic types with external enum references.
    /// Uses allOf to combine common member_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateFlexibleMembersSchemaWithExternalEnums(Type genericTypeDefinition, Dictionary<string, Type> enumTypesNeeded)
    {
        // Common member property names that are defined in member_properties
        var commonMemberNames = new HashSet<string> { "Enabled", "persistent", "UpdateOrder" };

        var componentSpecificProperties = new JsonObject();

        var allFields = PropertyAnalyzer.GetAllSerializableFields(genericTypeDefinition);

        foreach (var field in allFields.OrderBy(f => f.Name))
        {
            // Skip common member properties - they're included via member_properties ref
            if (commonMemberNames.Contains(field.Name))
                continue;

            try
            {
                var fieldSchema = GenerateMemberSchemaWithExternalEnums(field, enumTypesNeeded);
                componentSpecificProperties[field.Name] = fieldSchema;
            }
            catch
            {
                componentSpecificProperties[field.Name] = new JsonObject
                {
                    ["additionalProperties"] = false,
                    ["description"] = $"Type: {field.FriendlyTypeName} (could not analyze)"
                };
            }
        }

        return new JsonObject
        {
            ["description"] = "Component members (fields) and their values",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/member_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = componentSpecificProperties
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates a member schema with external enum references.
    /// </summary>
    private JsonObject GenerateMemberSchemaWithExternalEnums(ComponentField field, Dictionary<string, Type> enumTypesNeeded)
    {
        string wrapperType = GetWrapperTypeName(field.FieldType);
        Type innerType = UnwrapFrooxEngineType(field.FieldType);

        return wrapperType switch
        {
            "SyncList" => GenerateSyncListSchemaWithExternalEnums(field, innerType, enumTypesNeeded),
            "SyncRefList" => GenerateSyncRefListSchema(field, innerType),
            "SyncAssetList" => GenerateSyncAssetListSchema(field, innerType),
            "SyncFieldList" => GenerateSyncFieldListSchemaWithExternalEnums(field, innerType, enumTypesNeeded),
            "SyncRef" or "RelayRef" or "DestroyRelayRef" => GenerateReferenceSchemaWithExternalEnums(field, innerType, enumTypesNeeded),
            "AssetRef" => GenerateAssetRefSchema(field, innerType),
            "FieldDrive" => GenerateFieldDriveSchemaWithExternalEnums(field, innerType, enumTypesNeeded),
            "DriveRef" => GenerateDriveRefSchema(field, innerType),
            "RawOutput" => GenerateRawOutputSchema(field, innerType),
            _ => GenerateFieldSchemaWithExternalEnums(field, innerType, enumTypesNeeded)
        };
    }

    /// <summary>
    /// Generates a field schema with external enum references.
    /// </summary>
    private JsonObject GenerateFieldSchemaWithExternalEnums(ComponentField field, Type innerType, Dictionary<string, Type> enumTypesNeeded)
    {
        string? resoniteLinkType = GetResoniteLinkType(innerType);

        if (resoniteLinkType == null)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["description"] = $"Field type: {field.FriendlyTypeName}"
            };
        }

        string? typeDefName = GetTypeDefinitionName(innerType);
        if (typeDefName != null)
        {
            // Check if it's an enum type
            if (innerType.IsEnum)
            {
                // Add to enum types needed and return ref to enum bucket
                enumTypesNeeded.TryAdd(typeDefName, innerType);
                int enumBucket = GetComponentTypeBucket(typeDefName);
                string enumSchemaFile = GetBucketSchemaFileName(enumBucket, "enums");
                return new JsonObject
                {
                    ["$ref"] = $"{enumSchemaFile}#/$defs/{typeDefName}"
                };
            }
            else if (IsCommonTypeDefinition(typeDefName))
            {
                // Use common schema ref for non-enum common types
                return new JsonObject
                {
                    ["$ref"] = $"{CommonSchemaFileName}#/$defs/{typeDefName}"
                };
            }
        }

        // Fallback: inline the schema
        return GenerateValueSchema(innerType, resoniteLinkType);
    }

    /// <summary>
    /// Generates a reference schema with external enum support.
    /// </summary>
    private JsonObject GenerateReferenceSchemaWithExternalEnums(ComponentField field, Type targetType, Dictionary<string, Type> enumTypesNeeded)
    {
        if (UseExternalCommonSchema && IsIFieldType(targetType, out Type? fieldInnerType) && fieldInnerType != null)
        {
            string? resoniteLinkType = GetResoniteLinkType(fieldInnerType);
            if (resoniteLinkType != null && IsCommonInnerType(resoniteLinkType))
            {
                string refName = $"IField_{resoniteLinkType}_ref";
                return new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/{refName}" };
            }
        }

        return GenerateReferenceSchema(field, targetType);
    }

    /// <summary>
    /// Generates a FieldDrive schema with external enum support.
    /// </summary>
    private JsonObject GenerateFieldDriveSchemaWithExternalEnums(ComponentField field, Type drivenType, Dictionary<string, Type> enumTypesNeeded)
    {
        if (UseExternalCommonSchema)
        {
            string? resoniteLinkType = GetResoniteLinkType(drivenType);
            if (resoniteLinkType != null && IsCommonInnerType(resoniteLinkType))
            {
                string refName = $"IField_{resoniteLinkType}_ref";
                return new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/{refName}" };
            }
        }

        return GenerateFieldDriveSchema(field, drivenType);
    }

    /// <summary>
    /// Generates a SyncList schema with external enum references.
    /// </summary>
    private JsonObject GenerateSyncListSchemaWithExternalEnums(ComponentField field, Type elementType, Dictionary<string, Type> enumTypesNeeded)
    {
        var elementSchema = GenerateFieldSchemaWithExternalEnums(
            new ComponentField("element", elementType, PropertyAnalyzer.GetFriendlyTypeName(elementType)),
            elementType,
            enumTypesNeeded);

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "syncList" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = elementSchema
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Generates a SyncFieldList schema with external enum references.
    /// </summary>
    private JsonObject GenerateSyncFieldListSchemaWithExternalEnums(ComponentField field, Type elementType, Dictionary<string, Type> enumTypesNeeded)
    {
        var elementSchema = GenerateFieldSchemaWithExternalEnums(
            new ComponentField("element", elementType, PropertyAnalyzer.GetFriendlyTypeName(elementType)),
            elementType,
            enumTypesNeeded);

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "syncList" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = elementSchema
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Generates an enum value definition schema for use in enums_XX.schema.json files.
    /// </summary>
    private JsonObject? GenerateEnumValueDefinition(Type enumType)
    {
        if (!enumType.IsEnum)
            return null;

        var valueSchema = new JsonObject { ["type"] = "string" };
        try
        {
            var enumValues = new JsonArray();
            foreach (var name in Enum.GetNames(enumType))
            {
                enumValues.Add(name);
            }
            valueSchema["enum"] = enumValues;
        }
        catch { }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "enum" },
                ["value"] = valueSchema,
                ["enumType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["const"] = enumType.Name,
                    ["description"] = "The enum type name"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Generates the combined ResoniteLink schema that references all chunked component schema files.
    /// </summary>
    /// <param name="outputDir">The directory containing the chunked schema files.</param>
    /// <returns>The combined ResoniteLink schema, or null if no schema files exist.</returns>
    public static JsonObject? GenerateResoniteLinkSchema(string outputDir)
    {
        var oneOfRefs = new JsonArray();

        // Process chunked component schema files (components_00.schema.json through components_FF.schema.json)
        // These now include both FrooxEngine components and ProtoFlux nodes
        int componentCount = AddRefsFromChunkedSchemas(outputDir, "components", oneOfRefs);

        if (oneOfRefs.Count == 0)
        {
            return null;
        }

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "resonitelink.schema.json",
            ["title"] = "ResoniteLink Schema",
            ["description"] = $"Combined schema for validating ResoniteLink component data. References {componentCount} component/node type(s).",
            ["oneOf"] = oneOfRefs
        };
    }

    /// <summary>
    /// Adds $refs from all chunked schema files with the given prefix to the oneOfRefs array.
    /// </summary>
    private static int AddRefsFromChunkedSchemas(string outputDir, string prefix, JsonArray oneOfRefs)
    {
        int count = 0;

        for (int bucket = 0; bucket < 256; bucket++)
        {
            string schemaFileName = GetBucketSchemaFileName(bucket, prefix);
            string schemaPath = Path.Combine(outputDir, schemaFileName);

            if (!File.Exists(schemaPath)) continue;

            try
            {
                var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath));
                var defs = schemaJson?["$defs"]?.AsObject();
                if (defs == null) continue;

                foreach (var kvp in defs.OrderBy(k => k.Key))
                {
                    var def = kvp.Value?.AsObject();
                    if (def != null && IsComponentSchema(def))
                    {
                        oneOfRefs.Add(new JsonObject
                        {
                            ["$ref"] = $"{schemaFileName}#/$defs/{kvp.Key}"
                        });
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not read {schemaFileName}: {ex.Message}");
            }
        }

        return count;
    }

    /// <summary>
    /// Checks if a schema definition is a component schema (has componentType in properties, or is a oneOf of component schemas).
    /// </summary>
    private static bool IsComponentSchema(JsonObject def)
    {
        // Check for direct componentType in properties
        var properties = def["properties"]?.AsObject();
        if (properties != null && properties.ContainsKey("componentType"))
        {
            return true;
        }

        // Check for oneOf (generic components use this pattern)
        if (def.ContainsKey("oneOf"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a safe $defs key name from a type, replacing characters that are problematic in JSON keys.
    /// </summary>
    private static string GetSafeDefName(Type type)
    {
        string name = type.FullName ?? type.Name;
        // Replace backtick with underscore for generic types
        return name.Replace('`', '_');
    }

    /// <summary>
    /// Generates a schema suitable for embedding in $defs (no $schema or $id at top level).
    /// Handles both concrete and generic types.
    /// </summary>
    private JsonObject GenerateSchemaForDef(Type componentType, Dictionary<string, Type> typeDefsNeeded)
    {
        // Check if this is a generic type with GenericTypes attribute
        if (componentType.IsGenericTypeDefinition && _genericResolver != null)
        {
            var allowedTypeNames = _genericResolver.GetAllowedTypeNamesForGeneric(componentType);
            if (allowedTypeNames != null && allowedTypeNames.Length > 0)
            {
                return GenerateGenericOneOfSchemaForDef(componentType, allowedTypeNames, typeDefsNeeded);
            }
        }

        // For generic type definitions without known allowed types, generate a flexible schema
        if (componentType.IsGenericTypeDefinition)
        {
            return GenerateFlexibleGenericSchemaForDef(componentType, typeDefsNeeded);
        }

        return GenerateConcreteSchemaForDef(componentType, typeDefsNeeded);
    }

    /// <summary>
    /// Generates a concrete component schema for embedding in $defs (no $schema or $id).
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateConcreteSchemaForDef(Type componentType, Dictionary<string, Type> typeDefsNeeded)
    {
        string componentTypeName = $"{GetAssemblyPrefix(componentType)}{componentType.FullName}";
        var membersSchema = GenerateMembersSchema(componentType, useRefs: true, typeDefsNeeded);

        return new JsonObject
        {
            ["title"] = componentType.Name,
            ["description"] = $"ResoniteLink schema for {componentType.FullName}",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
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
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates a oneOf schema for generic types, suitable for embedding in $defs.
    /// </summary>
    private JsonObject GenerateGenericOneOfSchemaForDef(Type genericTypeDefinition, string[] allowedTypeNames, Dictionary<string, Type> typeDefsNeeded)
    {
        var oneOfArray = new JsonArray();
        var localDefs = new JsonObject();
        string baseName = GetBaseTypeName(genericTypeDefinition);
        int successCount = 0;

        foreach (var typeName in allowedTypeNames)
        {
            try
            {
                var typeArg = _loader.FindTypeByFullName(typeName);
                if (typeArg == null)
                    continue;

                var concreteType = genericTypeDefinition.MakeGenericType(typeArg);
                string typeArgName = GetSimpleTypeName(typeArg);
                string variantDefName = $"{baseName}_{typeArgName}";

                var concreteSchema = GenerateConcreteSchemaForGenericInstance(concreteType, typeArg, useRefs: true, typeDefsNeeded);
                localDefs[variantDefName] = concreteSchema;
                oneOfArray.Add(new JsonObject { ["$ref"] = $"#/$defs/{variantDefName}" });
                successCount++;
            }
            catch
            {
                // Skip types that fail
            }
        }

        var schema = new JsonObject
        {
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} with {successCount} type variant(s)"
        };

        if (oneOfArray.Count > 0)
        {
            schema["oneOf"] = oneOfArray;
        }

        if (localDefs.Count > 0)
        {
            schema["$defs"] = localDefs;
        }

        return schema;
    }

    /// <summary>
    /// Generates a flexible schema for generic types without [GenericTypes] attribute, suitable for embedding in $defs.
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateFlexibleGenericSchemaForDef(Type genericTypeDefinition, Dictionary<string, Type> typeDefsNeeded)
    {
        string baseName = GetBaseTypeName(genericTypeDefinition);
        string assemblyPrefix = GetAssemblyPrefix(genericTypeDefinition);
        string ns = genericTypeDefinition.Namespace ?? "";

        string escapedAssemblyPrefix = EscapeForJsonRegex(assemblyPrefix);
        string escapedNamespace = EscapeForJsonRegex(ns);
        string escapedBaseName = EscapeForJsonRegex(baseName);
        string componentTypePattern = $"^{escapedAssemblyPrefix}{escapedNamespace}\\.{escapedBaseName}<.+>$";

        var membersSchema = GenerateFlexibleMembersSchema(genericTypeDefinition, typeDefsNeeded);

        return new JsonObject
        {
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} (generic type - accepts any valid type argument)",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["componentType"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["pattern"] = componentTypePattern,
                            ["description"] = "The component type in Resonite notation (matches any valid type argument)"
                        },
                        ["members"] = membersSchema
                    }
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Gets all common type definitions (non-enum types).
    /// </summary>
    /// <returns>A dict from ref name (e.g. nullable_ushort_value) to schema.</returns>
    private Dictionary<string, JsonObject> GetAllCommonTypeDefinitions()
    {
        var result = new Dictionary<string, JsonObject>();

        // Primitive types (non-nullable and nullable)
        string[] primitiveTypes = [
            "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
            "float", "double", "decimal", "string", "char", "DateTime", "TimeSpan", "Uri"];

        foreach (var typeName in primitiveTypes)
        {
            // Non-nullable version
            var nonNullableDef = GenerateCommonTypeDefinition(typeName, isNullable: false);
            if (nonNullableDef != null)
            {
                result[$"{typeName}_value"] = nonNullableDef;
            }

            // Nullable version (except string which is already nullable)
            if (typeName != "string")
            {
                var nullableDef = GenerateCommonTypeDefinition(typeName, isNullable: true);
                if (nullableDef != null)
                {
                    result[$"nullable_{typeName}_value"] = nullableDef;
                }
            }
        }

        // Vector types (2, 3, 4 dimensions)
        string[] vectorPrefixes = [
            "float", "double", "int", "uint", "long", "ulong", "short", "ushort",
            "byte", "sbyte", "bool"];
        int[] dimensions = [2, 3, 4];

        foreach (var prefix in vectorPrefixes)
        {
            foreach (var dim in dimensions)
            {
                string typeName = $"{prefix}{dim}";
                result[$"{typeName}_value"] = GenerateVectorTypeDefinition(
                    typeName, dim, isNullable: false);
                result[$"nullable_{typeName}_value"] = GenerateVectorTypeDefinition(
                    typeName, dim, isNullable: true);
            }
        }

        // Quaternion types
        string[] quaternionTypes = ["floatQ", "doubleQ"];
        foreach (var typeName in quaternionTypes)
        {
            result[$"{typeName}_value"] = GenerateQuaternionTypeDefinition(
                typeName, isNullable: false);
            result[$"nullable_{typeName}_value"] = GenerateQuaternionTypeDefinition(
                typeName, isNullable: true);
        }

        // Color types
        string[] colorTypes = ["color", "colorX", "color32"];
        foreach (var typeName in colorTypes)
        {
            var includeProfile = (typeName is "colorX");
            result[$"{typeName}_value"] = GenerateColorTypeDefinition(
                typeName, isNullable: false, includeProfile: includeProfile);
            result[$"nullable_{typeName}_value"] = GenerateColorTypeDefinition(
                typeName, isNullable: true, includeProfile: includeProfile);
        }

        // Matrix types
        string[] matrixPrefixes = ["float", "double"];
        string[] matrixSizes = ["2x2", "3x3", "4x4"];

        foreach (var prefix in matrixPrefixes)
        {
            foreach (var size in matrixSizes)
            {
                string typeName = $"{prefix}{size}";
                int dim = size[0] - '0'; // Extract dimension from "2x2", "3x3", "4x4"
                result[$"{typeName}_value"] = GenerateMatrixTypeDefinition(
                    typeName, dim, isNullable: false);
                result[$"nullable_{typeName}_value"] = GenerateMatrixTypeDefinition(
                    typeName, dim, isNullable: true);
            }
        }

        // IField<T> reference types for FieldDrive<T> and RelayRef<IField<T>>
        // These use the format [FrooxEngine]FrooxEngine.IField<T>
        foreach (var typeName in primitiveTypes)
        {
            result[$"IField_{typeName}_ref"] = GenerateIFieldReferenceDefinition(typeName);
        }

        foreach (var prefix in vectorPrefixes)
        {
            foreach (var dim in dimensions)
            {
                string typeName = $"{prefix}{dim}";
                result[$"IField_{typeName}_ref"] = GenerateIFieldReferenceDefinition(typeName);
            }
        }

        foreach (var typeName in quaternionTypes)
        {
            result[$"IField_{typeName}_ref"] = GenerateIFieldReferenceDefinition(typeName);
        }

        foreach (var typeName in colorTypes)
        {
            result[$"IField_{typeName}_ref"] = GenerateIFieldReferenceDefinition(typeName);
        }

        foreach (var prefix in matrixPrefixes)
        {
            foreach (var size in matrixSizes)
            {
                string typeName = $"{prefix}{size}";
                result[$"IField_{typeName}_ref"] = GenerateIFieldReferenceDefinition(typeName);
            }
        }

        // Member properties common to all components (inside members object)
        result["member_properties"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["Enabled"] = new JsonObject { ["$ref"] = "#/$defs/bool_value" },
                ["persistent"] = new JsonObject { ["$ref"] = "#/$defs/bool_value" },
                ["UpdateOrder"] = new JsonObject { ["$ref"] = "#/$defs/int_value" }
            }
        };

        // Component properties common to all components (at top level)
        result["component_properties"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Unique identifier for this component instance"
                },
                ["isReferenceOnly"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Whether this is a reference-only component"
                }
            },
            ["required"] = new JsonArray { "id", "isReferenceOnly" }
        };

        return result;
    }

    /// <summary>
    /// Generates an IField reference definition for the common schema.
    /// Used for FieldDrive&lt;T&gt; and RelayRef&lt;IField&lt;T&gt;&gt;.
    /// </summary>
    private static JsonObject GenerateIFieldReferenceDefinition(string typeName)
    {
        string targetType = $"[FrooxEngine]FrooxEngine.IField<{typeName}>";

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Reference to IField<{typeName}>",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the target field (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["const"] = targetType,
                    ["description"] = "Type of the target field"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Generates a primitive type definition for the common schema.
    /// </summary>
    private static JsonObject? GenerateCommonTypeDefinition(string typeName, bool isNullable)
    {
        string schemaTypeName = isNullable ? $"{typeName}?" : typeName;

        var valueSchema = typeName switch
        {
            "bool" => new JsonObject { ["type"] = isNullable ? new JsonArray { "boolean", "null" } : "boolean" },
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                => new JsonObject { ["type"] = isNullable ? new JsonArray { "integer", "null" } : "integer" },
            "float" or "double" or "decimal"
                => new JsonObject { ["type"] = isNullable ? new JsonArray { "number", "null" } : "number" },
            "string" => new JsonObject { ["type"] = new JsonArray { "string", "null" } },
            "char" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string", ["maxLength"] = 1 },
            "DateTime" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },  // No format specified
            "TimeSpan" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },  // No format specified
            "Uri" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },  // No format specified
            _ => null
        };

        if (valueSchema == null)
            return null;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = valueSchema,
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        // Require value for non-nullable types
        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
        }

        return result;
    }

    /// <summary>
    /// Generates a vector type definition for the common schema.
    /// </summary>
    private static JsonObject GenerateVectorTypeDefinition(string typeName, int dimensions, bool isNullable)
    {
        string schemaTypeName = isNullable ? $"{typeName}?" : typeName;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = GenerateVectorSchema(dimensions),
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
        }

        return result;
    }

    /// <summary>
    /// Generates a quaternion type definition for the common schema.
    /// </summary>
    private static JsonObject GenerateQuaternionTypeDefinition(string typeName, bool isNullable)
    {
        string schemaTypeName = isNullable ? $"{typeName}?" : typeName;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = GenerateQuaternionSchema(),
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
        }

        return result;
    }

    /// <summary>
    /// Generates a color type definition for the common schema.
    /// </summary>
    private static JsonObject GenerateColorTypeDefinition(string typeName, bool isNullable, bool includeProfile)
    {
        string schemaTypeName = isNullable ? $"{typeName}?" : typeName;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = GenerateColorSchema(includeProfile),
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
        }

        return result;
    }

    /// <summary>
    /// Generates a color32 type definition for the common schema.
    /// </summary>
    private static JsonObject GenerateColor32TypeDefinition(string typeName, bool isNullable)
    {
        string schemaTypeName = isNullable ? $"{typeName}?" : typeName;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["r"] = new JsonObject { ["type"] = "integer" },
                        ["g"] = new JsonObject { ["type"] = "integer" },
                        ["b"] = new JsonObject { ["type"] = "integer" },
                        ["a"] = new JsonObject { ["type"] = "integer" }
                    },
                    ["required"] = new JsonArray { "r", "g", "b", "a" }
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
        }

        return result;
    }

    /// <summary>
    /// Generates a matrix type definition for the common schema.
    /// </summary>
    private static JsonObject GenerateMatrixTypeDefinition(string typeName, int dimension, bool isNullable)
    {
        string schemaTypeName = isNullable ? $"{typeName}?" : typeName;

        // Matrix is represented as an array of row vectors
        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = GenerateMatrixSchema(dimension),
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
        }

        return result;
    }

    /// <summary>
    /// Generates a matrix value schema (array of row vectors).
    /// </summary>
    private static JsonObject GenerateMatrixSchema(int dimension)
    {
        // Matrix columns: m11, m12, m13, m14, m21, m22, etc.
        var properties = new JsonObject();
        for (int row = 1; row <= dimension; row++)
        {
            for (int col = 1; col <= dimension; col++)
            {
                properties[$"m{row}{col}"] = new JsonObject { ["type"] = "number" };
            }
        }

        var required = new JsonArray();
        for (int row = 1; row <= dimension; row++)
        {
            for (int col = 1; col <= dimension; col++)
            {
                required.Add($"m{row}{col}");
            }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    /// <summary>
    /// Checks if a type definition name is for a common type (not an enum).
    /// Common types go in common.schema.json, enum types stay in component schemas.
    /// </summary>
    private static bool IsCommonTypeDefinition(string typeDefName)
    {
        // Enum types have names like "BlendMode_value" or "nullable_ColorMask_value"
        // Common types have names like "bool_value", "float3_value", "colorX_value", "IField_bool_ref"
        // We identify common types by checking if they match known patterns

        // IField reference types (e.g., IField_bool_ref, IField_float3_ref)
        if (typeDefName.StartsWith("IField_") && typeDefName.EndsWith("_ref"))
        {
            string innerType = typeDefName["IField_".Length..^"_ref".Length];
            return IsCommonInnerType(innerType);
        }

        string baseName = typeDefName;
        if (baseName.StartsWith("nullable_"))
        {
            baseName = baseName["nullable_".Length..];
        }
        if (baseName.EndsWith("_value"))
        {
            baseName = baseName[..^"_value".Length];
        }

        return IsCommonInnerType(baseName);
    }

    /// <summary>
    /// Checks if a type name is a common inner type (primitive, vector, quaternion, color, or matrix).
    /// </summary>
    private static bool IsCommonInnerType(string typeName)
    {
        // Known primitive types
        string[] primitives = ["bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
                               "float", "double", "decimal", "string", "char", "DateTime", "TimeSpan", "Uri"];
        if (primitives.Contains(typeName))
            return true;

        // Vector types (float2, int3, bool4, etc.)
        if (IsVectorType(typeName, out _))
            return true;

        // Quaternion types
        if (typeName is "floatQ" or "doubleQ")
            return true;

        // Color types
        if (typeName is "color" or "colorX" or "color32")
            return true;

        // Matrix types (float2x2, double3x3, etc.)
        if (typeName.Contains("x") && (typeName.StartsWith("float") || typeName.StartsWith("double")))
        {
            // Check if it's a valid matrix pattern
            string prefix = typeName.StartsWith("double") ? "double" : "float";
            string dims = typeName[prefix.Length..];
            if (dims is "2x2" or "3x3" or "4x4")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the $ref path for a type definition.
    /// Returns external reference (common.schema.json#/$defs/xxx) for common types when UseExternalCommonSchema is true,
    /// or local reference (#/$defs/xxx) otherwise.
    /// </summary>
    private string GetRefPath(string typeDefName)
    {
        if (UseExternalCommonSchema && IsCommonTypeDefinition(typeDefName))
        {
            return $"{CommonSchemaFileName}#/$defs/{typeDefName}";
        }
        return $"#/$defs/{typeDefName}";
    }

    /// <summary>
    /// Adds a type definition to the needed defs dictionary if it should be embedded in the schema.
    /// Common types are NOT added when UseExternalCommonSchema is true.
    /// </summary>
    private void AddTypeDefIfNeeded(Dictionary<string, Type>? typeDefsNeeded, string typeDefName, Type type)
    {
        if (typeDefsNeeded == null)
            return;

        // When using external common schema, don't add common types to local $defs
        if (UseExternalCommonSchema && IsCommonTypeDefinition(typeDefName))
            return;

        typeDefsNeeded.TryAdd(typeDefName, type);
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

        // For generic type definitions without known allowed types, generate a flexible schema
        if (componentType.IsGenericTypeDefinition)
        {
            return GenerateFlexibleGenericSchema(componentType);
        }

        return GenerateConcreteSchema(componentType);
    }

    /// <summary>
    /// Generates a flexible schema for generic type definitions that don't have a [GenericTypes] attribute.
    /// Uses patterns instead of const values to allow any valid instantiation.
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateFlexibleGenericSchema(Type genericTypeDefinition)
    {
        string baseName = GetBaseTypeName(genericTypeDefinition);
        string assemblyPrefix = GetAssemblyPrefix(genericTypeDefinition);
        string ns = genericTypeDefinition.Namespace ?? "";

        // Create a pattern that matches any instantiation: [Assembly]Namespace.TypeName<...>
        // Escape regex special characters in the type name and namespace
        string escapedAssemblyPrefix = EscapeForJsonRegex(assemblyPrefix);
        string escapedNamespace = EscapeForJsonRegex(ns);
        string escapedBaseName = EscapeForJsonRegex(baseName);
        string componentTypePattern = $"^{escapedAssemblyPrefix}{escapedNamespace}\\.{escapedBaseName}<.+>$";

        // Collect type definitions needed for this schema
        var typeDefsNeeded = new Dictionary<string, Type>();
        var membersSchema = GenerateFlexibleMembersSchema(genericTypeDefinition, typeDefsNeeded);

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = GetSafeSchemaId(genericTypeDefinition),
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} (generic type - accepts any valid type argument)",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["componentType"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["pattern"] = componentTypePattern,
                            ["description"] = $"The component type in Resonite notation (e.g., {assemblyPrefix}{ns}.{baseName}<[FrooxEngine]FrooxEngine.Slot>)"
                        },
                        ["members"] = membersSchema
                    }
                }
            },
            ["unevaluatedProperties"] = false
        };

        // Add $defs if we have type definitions (excluding common types when using external schema)
        var localDefs = typeDefsNeeded
            .Where(kvp => !UseExternalCommonSchema || !IsCommonTypeDefinition(kvp.Key))
            .ToList();

        if (localDefs.Count > 0)
        {
            var defs = new JsonObject();
            foreach (var (typeDefName, type) in localDefs.OrderBy(kvp => kvp.Key))
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

    /// <summary>
    /// Generates members schema for generic type definitions, using flexible patterns for generic type references.
    /// Uses allOf to combine common member_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateFlexibleMembersSchema(Type genericTypeDefinition, Dictionary<string, Type> typeDefsNeeded)
    {
        // Common member property names that are defined in member_properties
        var commonMemberNames = new HashSet<string> { "Enabled", "persistent", "UpdateOrder" };

        var componentSpecificProperties = new JsonObject();

        var allFields = PropertyAnalyzer.GetAllSerializableFields(genericTypeDefinition);

        foreach (var field in allFields.OrderBy(f => f.Name))
        {
            // Skip common member properties - they're included via member_properties ref
            if (commonMemberNames.Contains(field.Name))
                continue;

            try
            {
                var fieldSchema = GenerateFlexibleMemberSchema(field, typeDefsNeeded);
                componentSpecificProperties[field.Name] = fieldSchema;
            }
            catch
            {
                componentSpecificProperties[field.Name] = new JsonObject
                {
                    ["additionalProperties"] = false,
                    ["description"] = $"Type: {field.FriendlyTypeName} (could not analyze)"
                };
            }
        }

        return new JsonObject
        {
            ["description"] = "Component members (fields) and their values",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/member_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = componentSpecificProperties
                }
            },
            ["unevaluatedProperties"] = false
        };
    }

    /// <summary>
    /// Generates a member schema that handles generic type parameters flexibly.
    /// </summary>
    private JsonObject GenerateFlexibleMemberSchema(ComponentField field, Dictionary<string, Type> typeDefsNeeded)
    {
        string wrapperType = GetWrapperTypeName(field.FieldType);
        Type innerType = UnwrapFrooxEngineType(field.FieldType);

        // Check if the inner type contains a generic type parameter
        bool hasGenericParameter = ContainsGenericParameter(innerType);

        if (hasGenericParameter)
        {
            // For types containing generic parameters, use a flexible reference schema
            return wrapperType switch
            {
                "SyncRef" or "RelayRef" or "DestroyRelayRef" => GenerateFlexibleReferenceSchema(field, innerType),
                "AssetRef" => GenerateFlexibleAssetRefSchema(field, innerType),
                "FieldDrive" => GenerateFlexibleFieldDriveSchema(field, innerType),
                _ => GenerateFieldSchema(field, innerType, true, typeDefsNeeded)
            };
        }

        // No generic parameters, use standard schema generation
        return GenerateMemberSchema(field, true, typeDefsNeeded);
    }

    /// <summary>
    /// Checks if a type contains any generic type parameters.
    /// </summary>
    private static bool ContainsGenericParameter(Type type)
    {
        if (type.IsGenericParameter)
            return true;

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (ContainsGenericParameter(arg))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Generates a flexible reference schema that accepts any targetType matching the pattern.
    /// </summary>
    private JsonObject GenerateFlexibleReferenceSchema(ComponentField field, Type targetType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Reference to {PropertyAnalyzer.GetFriendlyTypeName(targetType)} (generic - accepts any valid type instantiation)",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the target object (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Type of the target (with concrete type argument)"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Generates a flexible asset ref schema that accepts any targetType matching the pattern.
    /// </summary>
    private JsonObject GenerateFlexibleAssetRefSchema(ComponentField field, Type assetType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Asset reference to {PropertyAnalyzer.GetFriendlyTypeName(assetType)} (generic - accepts any valid type instantiation)",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the asset (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Type of the asset provider (with concrete type argument)"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Generates a flexible field drive schema that accepts any targetType matching the pattern.
    /// </summary>
    private JsonObject GenerateFlexibleFieldDriveSchema(ComponentField field, Type drivenType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Field drive targeting {PropertyAnalyzer.GetFriendlyTypeName(drivenType)} (generic - accepts any valid type instantiation)",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the target field to drive (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Type of the driven field (with concrete type argument)"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    /// <summary>
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateConcreteSchema(Type componentType)
    {
        string componentTypeName = $"{GetAssemblyPrefix(componentType)}{componentType.FullName}";

        // Collect type definitions needed for this schema (maps def name to Type)
        var typeDefsNeeded = new Dictionary<string, Type>();
        var membersSchema = GenerateMembersSchema(componentType, useRefs: true, typeDefsNeeded);

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = GetSafeSchemaId(componentType),
            ["title"] = componentType.Name,
            ["description"] = $"ResoniteLink schema for {componentType.FullName}",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
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
                }
            },
            ["unevaluatedProperties"] = false
        };

        // Add $defs if we have type definitions (excluding common types when using external schema)
        var localDefs = typeDefsNeeded
            .Where(kvp => !UseExternalCommonSchema || !IsCommonTypeDefinition(kvp.Key))
            .ToList();

        if (localDefs.Count > 0)
        {
            var defs = new JsonObject();
            foreach (var (typeDefName, type) in localDefs.OrderBy(kvp => kvp.Key))
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

        // Add type value definitions to $defs first (excluding common types when using external schema)
        foreach (var (typeDefName, type) in typeDefsNeeded.OrderBy(kvp => kvp.Key))
        {
            // Skip common types when using external common schema
            if (UseExternalCommonSchema && IsCommonTypeDefinition(typeDefName))
                continue;

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
            ["$id"] = GetSafeSchemaId(genericTypeDefinition),
            ["title"] = baseName,
            ["description"] = $"ResoniteLink schema for {genericTypeDefinition.FullName} with {successCount} type variant(s) for T",
            ["oneOf"] = oneOfArray,
            ["$defs"] = defs
        };

        return resultSchema;
    }

    /// <summary>
    /// Uses allOf to combine common component_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateConcreteSchemaForGenericInstance(Type concreteType, Type typeArg, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
    {
        string componentTypeName = FormatGenericComponentTypeName(concreteType);

        return new JsonObject
        {
            ["title"] = $"{GetBaseTypeName(concreteType.GetGenericTypeDefinition())}<{GetSimpleTypeName(typeArg)}>",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/component_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["componentType"] = new JsonObject
                        {
                            ["const"] = componentTypeName,
                            ["description"] = "The component type in Resonite notation"
                        },
                        ["members"] = GenerateMembersSchema(concreteType, useRefs, typeDefsNeeded)
                    }
                }
            },
            ["unevaluatedProperties"] = false
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

        // Handle enums specially - they use $type: "enum" with enumType property
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
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = isNullable ? "enum?" : "enum" },
                    ["value"] = valueSchema,
                    ["enumType"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["const"] = underlyingType.Name,
                        ["description"] = "The enum type name"
                    },
                    ["id"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "$type", "id" }
            };

            // Also require value for non-nullable types
            if (!isNullable)
            {
                enumResult["required"] = new JsonArray { "$type", "value", "id" };
            }

            return enumResult;
        }

        // Handle special types
        if (IsVectorType(resoniteLinkType, out int dimensions))
        {
            var vectorResult = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                    ["value"] = GenerateVectorSchema(dimensions),
                    ["id"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "$type", "id" }
            };

            if (!isNullable)
            {
                vectorResult["required"] = new JsonArray { "$type", "value", "id" };
            }

            return vectorResult;
        }

        if (resoniteLinkType is "floatQ" or "doubleQ")
        {
            var quatResult = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                    ["value"] = GenerateQuaternionSchema(),
                    ["id"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "$type", "id" }
            };

            if (!isNullable)
            {
                quatResult["required"] = new JsonArray { "$type", "value", "id" };
            }

            return quatResult;
        }

        if (resoniteLinkType is "color" or "colorX")
        {
            bool includeProfile = resoniteLinkType == "colorX";
            var colorResult = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                    ["value"] = GenerateColorSchema(includeProfile),
                    ["id"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "$type", "id" }
            };

            if (!isNullable)
            {
                colorResult["required"] = new JsonArray { "$type", "value", "id" };
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
            "string" => new JsonObject { ["type"] = new JsonArray { "string", "null" } },  // Strings can always be null
            "char" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string", ["maxLength"] = 1 },
            "DateTime" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },  // No format specified
            "TimeSpan" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },
            "Uri" => new JsonObject { ["type"] = isNullable ? new JsonArray { "string", "null" } : "string" },  // URI stored as string, no format specified
            _ => null
        };

        if (primitiveValueSchema == null)
            return null;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = schemaTypeName },
                ["value"] = primitiveValueSchema,
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };

        // Also require value for non-nullable types
        if (!isNullable)
        {
            result["required"] = new JsonArray { "$type", "value", "id" };
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
            bool includeProfile = resoniteLinkType == "colorX";
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                    ["value"] = GenerateColorSchema(includeProfile)
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
            "DateTime" => new JsonObject { ["type"] = "string" },  // No format specified
            "TimeSpan" => new JsonObject { ["type"] = "string" },
            "Uri" => new JsonObject { ["type"] = "string" },  // URI stored as string, no format specified
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
        // Format: [ProtoFluxBindings]FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ObjectRelay<[FrooxEngine]FrooxEngine.Slot>
        var genericDef = concreteType.GetGenericTypeDefinition();
        var typeArgs = concreteType.GetGenericArguments();

        // Use full ResoniteLink notation for type arguments
        var typeArgStrings = typeArgs.Select(FormatResoniteLinkTypeName);

        string assemblyPrefix = GetAssemblyPrefix(genericDef);
        return $"{assemblyPrefix}{genericDef.Namespace}.{GetBaseTypeName(genericDef)}<{string.Join(",", typeArgStrings)}>";
    }

    /// <summary>
    /// Formats a type in full ResoniteLink notation with assembly prefixes.
    /// E.g., "IAssetProvider&lt;LocaleResource&gt;" becomes "[FrooxEngine]FrooxEngine.IAssetProvider&lt;[FrooxEngine]FrooxEngine.LocaleResource&gt;"
    /// Primitive types use simple names (int, bool, float, etc.)
    /// </summary>
    private static string FormatResoniteLinkTypeName(Type type)
    {
        // Check for primitive/simple types first - use simple names
        string? simpleName = GetSimpleTypeNameOrNull(type);
        if (simpleName != null)
            return simpleName;

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var typeArgs = type.GetGenericArguments();

            // Format each type argument recursively
            var typeArgStrings = typeArgs.Select(FormatResoniteLinkTypeName);

            string assemblyPrefix = GetAssemblyPrefix(genericDef);
            return $"{assemblyPrefix}{genericDef.Namespace}.{GetBaseTypeName(genericDef)}<{string.Join(",", typeArgStrings)}>";
        }

        // Non-generic type with assembly prefix
        string prefix = GetAssemblyPrefix(type);
        return $"{prefix}{type.FullName ?? type.Name}";
    }

    /// <summary>
    /// Gets the simple type name for primitives and well-known types, or null if not applicable.
    /// </summary>
    private static string? GetSimpleTypeNameOrNull(Type type)
    {
        // System primitives
        var systemName = type.FullName switch
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
            "System.Uri" => "Uri",
            _ => null
        };

        if (systemName != null)
            return systemName;

        // Elements.Core types - use simple name (e.g., float3, color, floatQ)
        if (type.Namespace == "Elements.Core")
        {
            return type.Name;
        }

        return null;
    }

    /// <summary>
    /// Gets the assembly prefix for a type (e.g., "[FrooxEngine]" or "[Elements.Core]").
    /// </summary>
    private static string GetAssemblyPrefix(Type type)
    {
        var assemblyName = type.Assembly?.GetName()?.Name;
        if (string.IsNullOrEmpty(assemblyName))
            return "";
        return $"[{assemblyName}]";
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

    /// <summary>
    /// Uses allOf to combine common member_properties with component-specific properties.
    /// </summary>
    private JsonObject GenerateMembersSchema(Type componentType, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
    {
        // Common member property names that are defined in member_properties
        var commonMemberNames = new HashSet<string> { "Enabled", "persistent", "UpdateOrder" };

        var componentSpecificProperties = new JsonObject();

        // Get all serializable fields including protected base class fields with NameOverride handling
        var allFields = PropertyAnalyzer.GetAllSerializableFields(componentType);

        foreach (var field in allFields.OrderBy(f => f.Name))
        {
            // Skip common member properties - they're included via member_properties ref
            if (commonMemberNames.Contains(field.Name))
                continue;

            try
            {
                var fieldSchema = GenerateMemberSchema(field, useRefs, typeDefsNeeded);
                componentSpecificProperties[field.Name] = fieldSchema;
            }
            catch
            {
                componentSpecificProperties[field.Name] = new JsonObject
                {
                    ["additionalProperties"] = false,
                    ["description"] = $"Type: {field.FriendlyTypeName} (could not analyze)"
                };
            }
        }

        return new JsonObject
        {
            ["description"] = "Component members (fields) and their values",
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = $"{CommonSchemaFileName}#/$defs/member_properties" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = componentSpecificProperties
                }
            },
            ["unevaluatedProperties"] = false
        };
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
            "SyncAssetList" => GenerateSyncAssetListSchema(field, innerType),
            "SyncFieldList" => GenerateSyncFieldListSchema(field, innerType, useRefs, typeDefsNeeded),
            "SyncRef" or "RelayRef" or "DestroyRelayRef" => GenerateReferenceSchemaOrCommonRef(field, innerType, typeDefsNeeded),
            "AssetRef" => GenerateAssetRefSchema(field, innerType),
            "FieldDrive" => GenerateFieldDriveSchemaOrCommonRef(field, innerType, typeDefsNeeded),
            "DriveRef" => GenerateDriveRefSchema(field, innerType),
            "RawOutput" => GenerateRawOutputSchema(field, innerType),
            _ => GenerateFieldSchema(field, innerType, useRefs, typeDefsNeeded)
        };
    }

    /// <summary>
    /// Generates a reference schema, using a common ref for IField types when appropriate.
    /// </summary>
    private JsonObject GenerateReferenceSchemaOrCommonRef(ComponentField field, Type targetType, Dictionary<string, Type>? typeDefsNeeded)
    {
        // Check if this is a reference to IField<T> where T is a common type
        if (UseExternalCommonSchema && IsIFieldType(targetType, out Type? fieldInnerType) && fieldInnerType != null)
        {
            string? resoniteLinkType = GetResoniteLinkType(fieldInnerType);
            if (resoniteLinkType != null && IsCommonInnerType(resoniteLinkType))
            {
                string refName = $"IField_{resoniteLinkType}_ref";
                return new JsonObject { ["$ref"] = GetRefPath(refName) };
            }
        }

        return GenerateReferenceSchema(field, targetType);
    }

    /// <summary>
    /// Generates a FieldDrive schema, using a common ref when appropriate.
    /// </summary>
    private JsonObject GenerateFieldDriveSchemaOrCommonRef(ComponentField field, Type drivenType, Dictionary<string, Type>? typeDefsNeeded)
    {
        // Check if the driven type is a common type
        if (UseExternalCommonSchema)
        {
            string? resoniteLinkType = GetResoniteLinkType(drivenType);
            if (resoniteLinkType != null && IsCommonInnerType(resoniteLinkType))
            {
                string refName = $"IField_{resoniteLinkType}_ref";
                return new JsonObject { ["$ref"] = GetRefPath(refName) };
            }
        }

        return GenerateFieldDriveSchema(field, drivenType);
    }

    /// <summary>
    /// Checks if a type is IField&lt;T&gt; and extracts the inner type.
    /// </summary>
    private static bool IsIFieldType(Type type, out Type? innerType)
    {
        innerType = null;

        if (!type.IsGenericType)
            return false;

        string typeName = type.Name;
        if (!typeName.StartsWith("IField`"))
            return false;

        var genericArgs = type.GetGenericArguments();
        if (genericArgs.Length == 1)
        {
            innerType = genericArgs[0];
            return true;
        }

        return false;
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
                ["additionalProperties"] = false,
                ["description"] = $"Field type: {field.FriendlyTypeName}"
            };
        }

        // If using refs, return a reference to the type definition
        if (useRefs)
        {
            string? typeDefName = GetTypeDefinitionName(innerType);
            if (typeDefName != null)
            {
                AddTypeDefIfNeeded(typeDefsNeeded, typeDefName, innerType);
                return new JsonObject
                {
                    ["$ref"] = GetRefPath(typeDefName)
                };
            }
        }

        // Otherwise, inline the schema
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
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
            bool includeProfile = resoniteLinkType == "colorX";
            return GenerateColorSchema(includeProfile);
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
            "System.String" => new JsonObject { ["type"] = new JsonArray { "string", "null" } },  // Strings can always be null
            "System.Char" => new JsonObject { ["type"] = "string", ["maxLength"] = 1 },
            "System.DateTime" or "System.DateTimeOffset"
                => new JsonObject { ["type"] = "string" },  // No format specified
            "System.TimeSpan" => new JsonObject { ["type"] = "string" },
            "System.Guid" => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            "System.Uri" => new JsonObject { ["type"] = "string" },  // No format specified
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

    private static JsonObject GenerateColorSchema(bool includeProfile = true)
    {
        var properties = new JsonObject
        {
            ["r"] = new JsonObject { ["type"] = "number" },
            ["g"] = new JsonObject { ["type"] = "number" },
            ["b"] = new JsonObject { ["type"] = "number" },
            ["a"] = new JsonObject { ["type"] = "number" }
        };

        if (includeProfile)
        {
            properties["profile"] = new JsonObject { ["type"] = "string" };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray { "r", "g", "b", "a" }
        };
    }

    private JsonObject GenerateReferenceSchema(ComponentField field, Type targetType)
    {
        // Format the target type in ResoniteLink notation
        string formattedTargetType = FormatResoniteLinkTypeName(targetType);

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Reference to {PropertyAnalyzer.GetFriendlyTypeName(targetType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the target object (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["const"] = formattedTargetType,
                    ["description"] = "Type of the target"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateAssetRefSchema(ComponentField field, Type assetType)
    {
        // AssetRef<T> is a reference to IAssetProvider<T>
        // Format: [FrooxEngine]FrooxEngine.IAssetProvider<[FrooxEngine]FrooxEngine.LocaleResource>
        string formattedAssetType = FormatResoniteLinkTypeName(assetType);
        string targetType = $"[FrooxEngine]FrooxEngine.IAssetProvider<{formattedAssetType}>";

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Asset reference to {PropertyAnalyzer.GetFriendlyTypeName(assetType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the asset (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["const"] = targetType,
                    ["description"] = "Type of the asset provider"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateFieldDriveSchema(ComponentField field, Type drivenType)
    {
        // FieldDrive<T> targets IField<T>
        // Format: [FrooxEngine]FrooxEngine.IField<float3>
        string formattedDrivenType = FormatResoniteLinkTypeName(drivenType);
        string targetType = $"[FrooxEngine]FrooxEngine.IField<{formattedDrivenType}>";

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Field drive targeting IField<{PropertyAnalyzer.GetFriendlyTypeName(drivenType)}>",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the target field to drive (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["const"] = targetType,
                    ["description"] = "Type of the driven field"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateDriveRefSchema(ComponentField field, Type targetType)
    {
        // DriveRef<T> directly references T (not IField<T>)
        string formattedTargetType = FormatResoniteLinkTypeName(targetType);

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Drive reference to {PropertyAnalyzer.GetFriendlyTypeName(targetType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "reference" },
                ["targetId"] = new JsonObject
                {
                    ["type"] = new JsonArray { "string", "null" },
                    ["description"] = "ID of the target object (null if no target)"
                },
                ["targetType"] = new JsonObject
                {
                    ["const"] = formattedTargetType,
                    ["description"] = "Type of the target"
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
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
                AddTypeDefIfNeeded(typeDefsNeeded, typeDefName, elementType);
                elementsItemSchema = new JsonObject
                {
                    ["$ref"] = GetRefPath(typeDefName)
                };
            }
            else
            {
                elementsItemSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
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
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["$type"] = new JsonObject { ["const"] = elementResoniteLinkType },
                    ["value"] = GenerateValueSchema(elementType, elementResoniteLinkType)
                }
            };
        }
        else
        {
            // Unknown element type - likely a SyncObject (e.g., Point<T>, GradientPoint<T>)
            // Generate a permissive syncObject schema
            elementsItemSchema = GenerateSyncObjectSchema(elementType);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Synchronized list of {PropertyAnalyzer.GetFriendlyTypeName(elementType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "list" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = elementsItemSchema
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateSyncRefListSchema(ComponentField field, Type elementType)
    {
        // Format the element type in ResoniteLink notation
        string formattedElementType = FormatResoniteLinkTypeName(elementType);

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Synchronized reference list of {PropertyAnalyzer.GetFriendlyTypeName(elementType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "list" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JsonObject
                        {
                            ["$type"] = new JsonObject { ["const"] = "reference" },
                            ["targetId"] = new JsonObject { ["type"] = new JsonArray { "string", "null" } },
                            ["targetType"] = new JsonObject
                            {
                                ["const"] = formattedElementType,
                                ["description"] = "Type of the target"
                            },
                            ["id"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray { "$type", "id" }
                    }
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateSyncAssetListSchema(ComponentField field, Type assetType)
    {
        // SyncAssetList<T> contains references to IAssetProvider<T>
        string formattedAssetType = FormatResoniteLinkTypeName(assetType);
        string targetType = $"[FrooxEngine]FrooxEngine.IAssetProvider<{formattedAssetType}>";

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Synchronized asset list of {PropertyAnalyzer.GetFriendlyTypeName(assetType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "list" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JsonObject
                        {
                            ["$type"] = new JsonObject { ["const"] = "reference" },
                            ["targetId"] = new JsonObject { ["type"] = new JsonArray { "string", "null" } },
                            ["targetType"] = new JsonObject
                            {
                                ["const"] = targetType,
                                ["description"] = "Type of the asset provider"
                            },
                            ["id"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray { "$type", "id" }
                    }
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateSyncFieldListSchema(ComponentField field, Type elementType, bool useRefs = false, Dictionary<string, Type>? typeDefsNeeded = null)
    {
        // SyncFieldList<T> contains value fields of type T
        string? elementResoniteLinkType = GetResoniteLinkType(elementType);

        JsonObject elementsItemSchema;

        if (useRefs && elementResoniteLinkType != null)
        {
            string? typeDefName = GetTypeDefinitionName(elementType);
            if (typeDefName != null)
            {
                AddTypeDefIfNeeded(typeDefsNeeded, typeDefName, elementType);
                elementsItemSchema = new JsonObject
                {
                    ["$ref"] = GetRefPath(typeDefName)
                };
            }
            else
            {
                elementsItemSchema = GenerateFieldListElementSchema(elementType, elementResoniteLinkType);
            }
        }
        else if (elementResoniteLinkType != null)
        {
            elementsItemSchema = GenerateFieldListElementSchema(elementType, elementResoniteLinkType);
        }
        else
        {
            elementsItemSchema = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["description"] = $"Element type: {PropertyAnalyzer.GetFriendlyTypeName(elementType)}"
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Synchronized field list of {PropertyAnalyzer.GetFriendlyTypeName(elementType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "list" },
                ["elements"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = elementsItemSchema
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateFieldListElementSchema(Type elementType, string resoniteLinkType)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = resoniteLinkType },
                ["value"] = GenerateValueSchema(elementType, resoniteLinkType),
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "value", "id" }
        };
    }

    private JsonObject GenerateRawOutputSchema(ComponentField field, Type outputType)
    {
        // RawOutput<T> serializes as $type: "empty" with just an id - no value is stored
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Raw output of {PropertyAnalyzer.GetFriendlyTypeName(outputType)} (output-only, no stored value)",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "empty" },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "id" }
        };
    }

    private JsonObject GenerateSyncObjectSchema(Type objectType)
    {
        // SyncObject types (like Point<T>, GradientPoint<T>) serialize with $type: "syncObject"
        // and a members object containing their sync fields
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = $"Sync object of type {PropertyAnalyzer.GetFriendlyTypeName(objectType)}",
            ["properties"] = new JsonObject
            {
                ["$type"] = new JsonObject { ["const"] = "syncObject" },
                ["members"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Object members (fields) and their values"
                    // additionalProperties defaults to true, allowing any member fields
                },
                ["id"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "$type", "members", "id" }
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

        string[] wrapperTypes = ["Sync", "FieldDrive", "DriveRef", "AssetRef", "SyncRef", "RelayRef",
                                  "DestroyRelayRef", "SyncList", "SyncRefList", "SyncAssetList", "SyncFieldList", "RawOutput"];

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
            "System.Guid" => "string", // Guids are strings
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

    /// <summary>
    /// Escapes a string for use in a JSON Schema regex pattern.
    /// This properly escapes all regex special characters including brackets.
    /// </summary>
    private static string EscapeForJsonRegex(string input)
    {
        // Regex.Escape handles most special characters but we need to ensure
        // all brackets are properly escaped for JSON Schema regex
        var escaped = System.Text.RegularExpressions.Regex.Escape(input);
        // Ensure closing bracket is escaped (Regex.Escape doesn't escape ] on its own)
        escaped = escaped.Replace("]", "\\]");
        return escaped;
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
