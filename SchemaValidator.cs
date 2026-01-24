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
        var schema = JsonSchema.FromText(schemaJson);

        // Get the $id from the schema, or use the filename
        var schemaNode = JsonNode.Parse(schemaJson);
        var schemaId = schemaNode?["$id"]?.GetValue<string>();

        if (schemaId != null)
        {
            var uri = new Uri(schemaId, UriKind.RelativeOrAbsolute);
            if (!uri.IsAbsoluteUri)
            {
                // Make it absolute for the registry
                uri = new Uri($"file:///{schemaId}");
            }
            SchemaRegistry.Global.Register(uri, schema);
        }

        // Also register by filename for relative $ref resolution
        var fileName = Path.GetFileName(schemaPath);
        SchemaRegistry.Global.Register(new Uri($"file:///{fileName}"), schema);
    }

    /// <summary>
    /// Validates a JSON file against a schema file.
    /// </summary>
    /// <param name="jsonPath">Path to the JSON file to validate.</param>
    /// <param name="schemaPath">Path to the JSON schema file.</param>
    /// <returns>Validation result with details.</returns>
    public ValidationResult Validate(string jsonPath, string schemaPath)
    {
        // Load the schema
        var schemaJson = File.ReadAllText(schemaPath);
        var schema = JsonSchema.FromText(schemaJson);

        // Load the JSON to validate
        var jsonText = File.ReadAllText(jsonPath);

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
                Errors = [$"Failed to parse JSON file: {ex.Message}"]
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
