using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

[Collection("FrooxEngine")]
public class JsonSchemaGeneratorTests
{
    private readonly TestFixture _fixture;

    public JsonSchemaGeneratorTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Helper to get component-specific properties from schema with allOf structure.
    /// </summary>
    private static JsonObject? GetComponentProperties(JsonObject schema)
    {
        return schema["allOf"]?[1]?["properties"]?.AsObject();
    }

    /// <summary>
    /// Helper to get members properties from schema with allOf structure.
    /// </summary>
    private static JsonObject? GetMembersProperties(JsonObject schema)
    {
        var componentProps = GetComponentProperties(schema);
        return componentProps?["members"]?["allOf"]?[1]?["properties"]?.AsObject();
    }

    [Fact]
    public void GenerateSchema_NonGenericComponent_ReturnsValidSchema()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);

        Assert.NotNull(schema);
        Assert.NotNull(schema["$schema"]);
        Assert.NotNull(schema["$id"]);
        Assert.NotNull(schema["title"]);
        Assert.NotNull(schema["allOf"]); // Now uses allOf instead of direct properties
    }

    [Fact]
    public void GenerateSchema_GenericComponent_ReturnsOneOfSchema()
    {
        var valueField = _fixture.Loader.FindComponent("ValueField`1");
        Assert.NotNull(valueField);

        var schema = _fixture.SchemaGenerator.GenerateSchema(valueField);

        Assert.NotNull(schema);
        Assert.NotNull(schema["oneOf"]);
        Assert.NotNull(schema["$defs"]);
    }

    [Fact]
    public void GenerateSchema_SchemasHaveRequiredSchemaProperty()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]?.GetValue<string>());
    }

    [Fact]
    public void SerializeSchema_ProducesValidJson()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var json = _fixture.SchemaGenerator.SerializeSchema(schema);

        Assert.NotNull(json);
        Assert.NotEmpty(json);

        // Verify it's valid JSON by parsing it back
        var parsed = JsonNode.Parse(json);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void SerializeSchema_IsIndented()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var json = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Indented JSON should contain newlines
        Assert.Contains("\n", json);
    }

    [Fact]
    public void GenerateSchema_ComponentTypeFormat_IsCorrect()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var componentProps = GetComponentProperties(schema);
        var componentType = componentProps?["componentType"]?["const"]?.GetValue<string>();

        // Should be in format [FrooxEngine]FrooxEngine.ClassName
        Assert.StartsWith("[FrooxEngine]", componentType);
        Assert.Contains("FrooxEngine.AudioOutput", componentType);
    }

    [Fact]
    public void GenerateSchema_HasMembersObject()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var componentProps = GetComponentProperties(schema);
        var members = componentProps?["members"];

        Assert.NotNull(members);
        // Members now uses allOf structure
        Assert.NotNull(members["allOf"]);
        var membersProps = GetMembersProperties(schema);
        Assert.NotNull(membersProps);
    }

    [Fact]
    public void GenerateSchema_DefsAreSortedAlphabetically()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var defs = schema["$defs"]?.AsObject();
        Assert.NotNull(defs);

        var defNames = defs.Select(d => d.Key).ToList();
        var sortedDefNames = defNames.OrderBy(n => n).ToList();

        Assert.Equal(sortedDefNames, defNames);
    }

    [Fact]
    public void GenerateSchema_ReferenceFields_HaveCorrectStructure()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var members = GetMembersProperties(schema);
        var source = members?["Source"]?.AsObject();

        Assert.NotNull(source);
        Assert.Equal("object", source["type"]?.GetValue<string>());
        Assert.Equal("reference", source["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.NotNull(source["properties"]?["targetId"]);
        Assert.NotNull(source["properties"]?["targetType"]);
    }

    [Fact]
    public void GenerateSchema_SyncRefList_HasCorrectStructure()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var members = GetMembersProperties(schema);
        var excludedListeners = members?["ExcludedListeners"]?.AsObject();

        Assert.NotNull(excludedListeners);
        Assert.Equal("object", excludedListeners["type"]?.GetValue<string>());
        Assert.Equal("list", excludedListeners["properties"]?["$type"]?["const"]?.GetValue<string>());

        var elements = excludedListeners["properties"]?["elements"]?.AsObject();
        Assert.NotNull(elements);
        Assert.Equal("array", elements["type"]?.GetValue<string>());
        Assert.NotNull(elements["items"]);
    }

    [Fact]
    public void GenerateSchema_UsesRefsForRepeatedTypes()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var schema = _fixture.SchemaGenerator.GenerateSchema(audioOutput);
        var members = GetMembersProperties(schema);
        Assert.NotNull(members);

        // Multiple float fields should use $ref to common schema
        var volumeRef = members["Volume"]?["$ref"]?.GetValue<string>();
        var pitchRef = members["Pitch"]?["$ref"]?.GetValue<string>();

        Assert.Equal("common.schema.json#/$defs/float_value", volumeRef);
        Assert.Equal("common.schema.json#/$defs/float_value", pitchRef);
    }
}
