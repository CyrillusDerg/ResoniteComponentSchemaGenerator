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
    /// Validates a JSON file against a schema, automatically loading common.schema.json if present.
    /// </summary>
    /// <param name="jsonPath">Path to the JSON file to validate.</param>
    /// <param name="schemaPath">Path to the JSON schema file.</param>
    /// <param name="schemaDirectory">Directory containing schemas (for $ref resolution).</param>
    /// <returns>Validation result with details.</returns>
    public ValidationResult ValidateWithCommonSchema(string jsonPath, string schemaPath, string? schemaDirectory = null)
    {
        schemaDirectory ??= Path.GetDirectoryName(schemaPath) ?? ".";

        // Register common.schema.json if it exists
        var commonSchemaPath = Path.Combine(schemaDirectory, "common.schema.json");
        if (File.Exists(commonSchemaPath))
        {
            RegisterSchema(commonSchemaPath);
        }

        return Validate(jsonPath, schemaPath);
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
