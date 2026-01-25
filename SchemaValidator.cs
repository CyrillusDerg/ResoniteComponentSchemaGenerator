using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace ComponentAnalyzer;

/// <summary>
/// Validates JSON files against JSON schemas using JsonSchema.Net.
/// </summary>
public class SchemaValidator
{
    /// <summary>
    /// Registers a schema file for use in $ref resolution.
    /// </summary>
    public void RegisterSchema(string schemaPath)
    {
        var schemaJson = File.ReadAllText(schemaPath);
        var fileName = Path.GetFileName(schemaPath);
        RegisterSchemaFromText(schemaJson, fileName);
    }

    /// <summary>
    /// Registers a schema from JSON text for use in $ref resolution.
    /// </summary>
    /// <param name="schemaJson">The JSON schema text.</param>
    /// <param name="fileName">Optional filename for registration (used if schema has no $id).</param>
    public void RegisterSchemaFromText(string schemaJson, string? fileName = null)
    {
        // Get the $id from the schema
        var schemaNode = JsonNode.Parse(schemaJson);
        var schemaId = schemaNode?["$id"]?.GetValue<string>();

        // Build URIs for registration
        var urisToRegister = new List<Uri>();

        if (schemaId != null)
        {
            var uri = new Uri(schemaId, UriKind.RelativeOrAbsolute);
            if (!uri.IsAbsoluteUri)
            {
                urisToRegister.Add(new Uri($"file:///{schemaId}"));
                // Also register under the default base URI that JsonSchema.Net uses
                urisToRegister.Add(new Uri($"https://json-everything.lib/{schemaId}"));
            }
            else
            {
                urisToRegister.Add(uri);
            }
        }

        if (fileName != null)
        {
            urisToRegister.Add(new Uri($"file:///{fileName}"));
            urisToRegister.Add(new Uri($"https://json-everything.lib/{fileName}"));
        }

        // Parse schema and register under all URIs
        // Use global registry so refs work during evaluation
        foreach (var uri in urisToRegister.Distinct())
        {
            try
            {
                var schema = JsonSchema.FromText(schemaJson, baseUri: uri);
                // Schema is auto-registered during FromText when baseUri is provided
            }
            catch (JsonSchemaException)
            {
                // Schema already registered under this URI, ignore
            }
        }
    }

    /// <summary>
    /// Validates a JSON file against a schema file.
    /// </summary>
    /// <param name="jsonPath">Path to the JSON file to validate.</param>
    /// <param name="schemaPath">Path to the JSON schema file.</param>
    /// <returns>Validation result with details.</returns>
    public ValidationResult Validate(string jsonPath, string schemaPath)
    {
        var schemaJson = File.ReadAllText(schemaPath);
        var jsonText = File.ReadAllText(jsonPath);
        return ValidateJson(jsonText, schemaJson);
    }

    /// <summary>
    /// Validates JSON text against a schema string.
    /// </summary>
    /// <param name="jsonText">JSON text to validate.</param>
    /// <param name="schemaJson">JSON schema text.</param>
    /// <returns>Validation result with details.</returns>
    public ValidationResult ValidateJson(string jsonText, string schemaJson)
    {
        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaJson);
        }
        catch (JsonSchemaException)
        {
            // Schema may already be registered, try to get it from text without registration
            // Parse to get the $id and retrieve from registry
            var schemaNode = JsonNode.Parse(schemaJson);
            var schemaId = schemaNode?["$id"]?.GetValue<string>();
            if (schemaId != null)
            {
                var uri = new Uri(schemaId, UriKind.RelativeOrAbsolute);
                if (!uri.IsAbsoluteUri)
                {
                    uri = new Uri($"https://json-everything.lib/{schemaId}");
                }
                var registered = SchemaRegistry.Global.Get(uri);
                if (registered is JsonSchema js)
                {
                    schema = js;
                }
                else
                {
                    throw;
                }
            }
            else
            {
                throw;
            }
        }

        JsonElement jsonElement;
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            // Clone to avoid disposal issues
            jsonElement = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = [$"Failed to parse JSON: {ex.Message}"]
            };
        }

        // Configure evaluation options
        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        };

        // Evaluate
        var result = schema.Evaluate(jsonElement, options);

        return new ValidationResult
        {
            IsValid = result.IsValid,
            Errors = ExtractErrors(result)
        };
    }

    /// <summary>
    /// Validates a JSON file against a schema, automatically loading supporting schemas if present.
    /// </summary>
    /// <param name="jsonPath">Path to the JSON file to validate.</param>
    /// <param name="schemaPath">Path to the JSON schema file.</param>
    /// <param name="schemaDirectory">Directory containing schemas (for $ref resolution).</param>
    /// <returns>Validation result with details.</returns>
    public ValidationResult ValidateWithCommonSchema(string jsonPath, string schemaPath, string? schemaDirectory = null)
    {
        schemaDirectory ??= Path.GetDirectoryName(schemaPath) ?? ".";

        // Register common schema
        var commonSchemaPath = Path.Combine(schemaDirectory, "common.schema.json");
        if (File.Exists(commonSchemaPath))
        {
            RegisterSchema(commonSchemaPath);
        }

        // Register all chunked schema files (components_XX.schema.json and enums_XX.schema.json)
        foreach (var prefix in new[] { "components", "enums" })
        {
            for (int bucket = 0; bucket < 256; bucket++)
            {
                string bucketFileName = JsonSchemaGenerator.GetBucketSchemaFileName(bucket, prefix);
                var bucketSchemaPath = Path.Combine(schemaDirectory, bucketFileName);
                if (File.Exists(bucketSchemaPath))
                {
                    RegisterSchema(bucketSchemaPath);
                }
            }
        }

        return Validate(jsonPath, schemaPath);
    }

    /// <summary>
    /// Validates a JSON file against resonitelink.schema.json by extracting the componentType,
    /// computing the hash bucket, and validating against the specific chunked schema file.
    /// This is much faster than validating against the entire oneOf array.
    /// </summary>
    /// <param name="jsonPath">Path to the JSON file to validate.</param>
    /// <param name="schemaDirectory">Directory containing the schema files.</param>
    /// <returns>Validation result with details.</returns>
    public ValidationResult ValidateAgainstResoniteLink(string jsonPath, string schemaDirectory)
    {
        // Read and parse the JSON to extract componentType
        string jsonText;
        try
        {
            jsonText = File.ReadAllText(jsonPath);
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = [$"Failed to read JSON file: {ex.Message}"]
            };
        }

        JsonNode? jsonNode;
        try
        {
            jsonNode = JsonNode.Parse(jsonText);
        }
        catch (JsonException ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = [$"Failed to parse JSON: {ex.Message}"]
            };
        }

        // Extract componentType
        var componentType = jsonNode?["componentType"]?.GetValue<string>();
        if (string.IsNullOrEmpty(componentType))
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = ["JSON does not contain a 'componentType' property"]
            };
        }

        // Parse componentType to get the type name and assembly prefix
        // Format: "[AssemblyName]Namespace.TypeName" or "[AssemblyName]Namespace.TypeName<GenericArg>"
        string assemblyPrefix = "";
        string typeName = componentType;
        if (componentType.StartsWith('['))
        {
            int closeBracket = componentType.IndexOf(']');
            if (closeBracket > 0 && closeBracket < componentType.Length - 1)
            {
                assemblyPrefix = componentType[..(closeBracket + 1)];
                typeName = componentType[(closeBracket + 1)..];
            }
        }

        // Convert to safe def name (replace backtick with underscore for generic types)
        string defName = typeName.Replace('`', '_');

        // For generic types with type arguments like "FrooxEngine.ValueField<bool>",
        // we need to find the base generic type definition and compute bucket based on it
        string baseDefName = defName;
        string baseComponentType = componentType; // componentType for bucket computation
        int angleIndex = defName.IndexOf('<');
        if (angleIndex > 0)
        {
            // Extract base type name and count type arguments
            string baseName = defName[..angleIndex];
            string argsSection = defName[(angleIndex + 1)..^1]; // Remove < and >
            int argCount = argsSection.Split(',').Length;
            baseDefName = $"{baseName}_{argCount}";

            // Reconstruct the componentType for the generic definition (for bucket hash)
            // e.g., "[FrooxEngine]FrooxEngine.ValueCopy<bool>" -> "[FrooxEngine]FrooxEngine.ValueCopy`1"
            baseComponentType = $"{assemblyPrefix}{baseName}`{argCount}";
        }

        // Compute the bucket hash based on the generic definition componentType (if generic) or the actual componentType
        int bucket = JsonSchemaGenerator.GetComponentTypeBucket(baseComponentType);

        // Register common schema
        var commonSchemaPath = Path.Combine(schemaDirectory, "common.schema.json");
        if (File.Exists(commonSchemaPath))
        {
            RegisterSchema(commonSchemaPath);
        }

        // Register all enum bucket files (needed for $ref resolution)
        for (int enumBucket = 0; enumBucket < 256; enumBucket++)
        {
            string enumBucketFileName = JsonSchemaGenerator.GetBucketSchemaFileName(enumBucket, "enums");
            var enumSchemaPath = Path.Combine(schemaDirectory, enumBucketFileName);
            if (File.Exists(enumSchemaPath))
            {
                RegisterSchema(enumSchemaPath);
            }
        }

        // Try to find the schema definition in the component bucket file
        // (ProtoFlux and FrooxEngine components are now combined)
        string? schemaJson = null;

        foreach (var prefix in new[] { "components" })
        {
            string bucketFileName = JsonSchemaGenerator.GetBucketSchemaFileName(bucket, prefix);
            var schemaPath = Path.Combine(schemaDirectory, bucketFileName);
            if (!File.Exists(schemaPath)) continue;

            // Register this bucket schema for $ref resolution
            RegisterSchema(schemaPath);

            try
            {
                var schemaNode = JsonNode.Parse(File.ReadAllText(schemaPath));
                var defs = schemaNode?["$defs"]?.AsObject();
                if (defs == null) continue;

                // Try exact match first, then base type name for generics
                JsonNode? defNode = null;
                if (defs.ContainsKey(defName))
                {
                    defNode = defs[defName];
                }
                else if (defName != baseDefName && defs.ContainsKey(baseDefName))
                {
                    defNode = defs[baseDefName];
                }

                if (defNode != null)
                {
                    // Create a standalone schema from the definition
                    var standaloneSchema = new JsonObject
                    {
                        ["$schema"] = "https://json-schema.org/draft/2020-12/schema"
                    };

                    // Copy all properties from the def
                    foreach (var prop in defNode.AsObject())
                    {
                        standaloneSchema[prop.Key] = prop.Value?.DeepClone();
                    }

                    schemaJson = standaloneSchema.ToJsonString();
                    break;
                }
            }
            catch
            {
                continue;
            }
        }

        if (schemaJson == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = [$"No schema found for component type '{componentType}' in bucket {bucket:X2} (looked for '{defName}' and '{baseDefName}')"]
            };
        }

        // Validate
        return ValidateJson(jsonText, schemaJson);
    }

    private static List<string> ExtractErrors(EvaluationResults result)
    {
        var errors = new List<string>();

        if (result.IsValid)
            return errors;

        CollectErrors(result, errors, "");
        return errors;
    }

    private static void CollectErrors(EvaluationResults result, List<string> errors, string path)
    {
        if (result.Errors != null && result.Errors.Count > 0)
        {
            foreach (var error in result.Errors)
            {
                var location = result.InstanceLocation.ToString();
                if (string.IsNullOrEmpty(location))
                {
                    location = path;
                }
                errors.Add($"{location}: {error.Key} - {error.Value}");
            }
        }

        if (result.Details != null)
        {
            foreach (var detail in result.Details)
            {
                CollectErrors(detail, errors, path);
            }
        }
    }
}

/// <summary>
/// Result of schema validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
}
