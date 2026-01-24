using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

[Collection("FrooxEngine")]
public class ValueFieldSchemaTests
{
    private readonly TestFixture _fixture;
    private readonly JsonObject _schema;
    private readonly JsonObject _defs;
    private readonly JsonObject _commonSchema;
    private readonly JsonObject _commonDefs;

    public ValueFieldSchemaTests(TestFixture fixture)
    {
        _fixture = fixture;

        var valueFieldType = fixture.Loader.FindComponent("ValueField`1");
        Assert.NotNull(valueFieldType);

        _schema = fixture.SchemaGenerator.GenerateSchema(valueFieldType);
        _defs = _schema["$defs"]?.AsObject()
            ?? throw new InvalidOperationException("Schema missing $defs");

        // Get common schema for checking common type definitions
        _commonSchema = fixture.SchemaGenerator.GenerateCommonSchema();
        _commonDefs = _commonSchema["$defs"]?.AsObject()
            ?? throw new InvalidOperationException("Common schema missing $defs");
    }

    [Fact]
    public void Schema_HasCorrectMetadata()
    {
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", _schema["$schema"]?.GetValue<string>());
        Assert.Contains("ValueField", _schema["$id"]?.GetValue<string>());
        Assert.Equal("ValueField", _schema["title"]?.GetValue<string>());
    }

    [Fact]
    public void Schema_HasOneOfArray()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);
        Assert.NotEmpty(oneOf);
    }

    [Fact]
    public void Schema_HasMultipleTypeVariants()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);
        // Should have many variants (bool, int, float, string, vectors, etc.)
        Assert.True(oneOf.Count >= 10, $"Expected at least 10 variants, got {oneOf.Count}");
    }

    [Fact]
    public void Schema_ContainsBoolVariant()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);

        var boolRef = oneOf.FirstOrDefault(item =>
            item?["$ref"]?.GetValue<string>()?.Contains("ValueField_bool") == true);
        Assert.NotNull(boolRef);
    }

    [Fact]
    public void Schema_ContainsIntVariant()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);

        var intRef = oneOf.FirstOrDefault(item =>
            item?["$ref"]?.GetValue<string>() == "#/$defs/ValueField_int");
        Assert.NotNull(intRef);
    }

    [Fact]
    public void Schema_ContainsFloatVariant()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);

        var floatRef = oneOf.FirstOrDefault(item =>
            item?["$ref"]?.GetValue<string>() == "#/$defs/ValueField_float");
        Assert.NotNull(floatRef);
    }

    [Fact]
    public void Schema_ContainsStringVariant()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);

        var stringRef = oneOf.FirstOrDefault(item =>
            item?["$ref"]?.GetValue<string>() == "#/$defs/ValueField_string");
        Assert.NotNull(stringRef);
    }

    [Fact]
    public void Schema_ContainsFloat3Variant()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);

        var float3Ref = oneOf.FirstOrDefault(item =>
            item?["$ref"]?.GetValue<string>() == "#/$defs/ValueField_float3");
        Assert.NotNull(float3Ref);
    }

    [Fact]
    public void Schema_ContainsColorVariant()
    {
        var oneOf = _schema["oneOf"]?.AsArray();
        Assert.NotNull(oneOf);

        var colorRef = oneOf.FirstOrDefault(item =>
            item?["$ref"]?.GetValue<string>() == "#/$defs/ValueField_color");
        Assert.NotNull(colorRef);
    }

    [Fact]
    public void Defs_ValueFieldBool_HasCorrectComponentType()
    {
        var boolVariant = _defs["ValueField_bool"]?.AsObject();
        Assert.NotNull(boolVariant);

        var componentType = boolVariant["properties"]?["componentType"]?["const"]?.GetValue<string>();
        Assert.Equal("[FrooxEngine]FrooxEngine.ValueField<bool>", componentType);
    }

    [Fact]
    public void Defs_ValueFieldInt_HasCorrectComponentType()
    {
        var intVariant = _defs["ValueField_int"]?.AsObject();
        Assert.NotNull(intVariant);

        var componentType = intVariant["properties"]?["componentType"]?["const"]?.GetValue<string>();
        Assert.Equal("[FrooxEngine]FrooxEngine.ValueField<int>", componentType);
    }

    [Fact]
    public void Defs_ValueFieldFloat3_HasCorrectComponentType()
    {
        var float3Variant = _defs["ValueField_float3"]?.AsObject();
        Assert.NotNull(float3Variant);

        var componentType = float3Variant["properties"]?["componentType"]?["const"]?.GetValue<string>();
        Assert.Equal("[FrooxEngine]FrooxEngine.ValueField<float3>", componentType);
    }

    [Fact]
    public void CommonDefs_BoolValue_CorrectlyDefined()
    {
        var boolValue = _commonDefs["bool_value"]?.AsObject();
        Assert.NotNull(boolValue);
        Assert.Equal("bool", boolValue["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("boolean", boolValue["properties"]?["value"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void CommonDefs_Float3Value_HasVectorStructure()
    {
        var float3Value = _commonDefs["float3_value"]?.AsObject();
        Assert.NotNull(float3Value);
        Assert.Equal("float3", float3Value["properties"]?["$type"]?["const"]?.GetValue<string>());

        var valueSchema = float3Value["properties"]?["value"]?.AsObject();
        Assert.NotNull(valueSchema);
        Assert.Equal("object", valueSchema["type"]?.GetValue<string>());

        // Check vector properties (x, y, z)
        var valueProperties = valueSchema["properties"]?.AsObject();
        Assert.NotNull(valueProperties);
        Assert.Contains("x", valueProperties.Select(p => p.Key));
        Assert.Contains("y", valueProperties.Select(p => p.Key));
        Assert.Contains("z", valueProperties.Select(p => p.Key));

        // Check required components
        var required = valueSchema["required"]?.AsArray();
        Assert.NotNull(required);
        Assert.Equal(3, required.Count);
    }

    [Fact]
    public void CommonDefs_Float4Value_HasVectorStructure()
    {
        var float4Value = _commonDefs["float4_value"]?.AsObject();
        Assert.NotNull(float4Value);
        Assert.Equal("float4", float4Value["properties"]?["$type"]?["const"]?.GetValue<string>());

        var valueSchema = float4Value["properties"]?["value"]?.AsObject();
        Assert.NotNull(valueSchema);

        var valueProperties = valueSchema["properties"]?.AsObject();
        Assert.NotNull(valueProperties);
        Assert.Contains("x", valueProperties.Select(p => p.Key));
        Assert.Contains("y", valueProperties.Select(p => p.Key));
        Assert.Contains("z", valueProperties.Select(p => p.Key));
        Assert.Contains("w", valueProperties.Select(p => p.Key));

        var required = valueSchema["required"]?.AsArray();
        Assert.NotNull(required);
        Assert.Equal(4, required.Count);
    }

    [Fact]
    public void CommonDefs_ColorValue_HasColorStructure()
    {
        var colorValue = _commonDefs["color_value"]?.AsObject();
        Assert.NotNull(colorValue);
        Assert.Equal("color", colorValue["properties"]?["$type"]?["const"]?.GetValue<string>());

        var valueSchema = colorValue["properties"]?["value"]?.AsObject();
        Assert.NotNull(valueSchema);

        var valueProperties = valueSchema["properties"]?.AsObject();
        Assert.NotNull(valueProperties);
        Assert.Contains("r", valueProperties.Select(p => p.Key));
        Assert.Contains("g", valueProperties.Select(p => p.Key));
        Assert.Contains("b", valueProperties.Select(p => p.Key));
        Assert.Contains("a", valueProperties.Select(p => p.Key));
    }

    [Fact]
    public void CommonDefs_FloatQValue_HasQuaternionStructure()
    {
        var floatQValue = _commonDefs["floatQ_value"]?.AsObject();
        Assert.NotNull(floatQValue);
        Assert.Equal("floatQ", floatQValue["properties"]?["$type"]?["const"]?.GetValue<string>());

        var valueSchema = floatQValue["properties"]?["value"]?.AsObject();
        Assert.NotNull(valueSchema);

        var valueProperties = valueSchema["properties"]?.AsObject();
        Assert.NotNull(valueProperties);
        Assert.Contains("x", valueProperties.Select(p => p.Key));
        Assert.Contains("y", valueProperties.Select(p => p.Key));
        Assert.Contains("z", valueProperties.Select(p => p.Key));
        Assert.Contains("w", valueProperties.Select(p => p.Key));
    }

    [Fact]
    public void CommonDefs_StringValue_HasCorrectStructure()
    {
        var stringValue = _commonDefs["string_value"]?.AsObject();
        Assert.NotNull(stringValue);
        Assert.Equal("string", stringValue["properties"]?["$type"]?["const"]?.GetValue<string>());

        // String values can be null, so type is an array ["string", "null"]
        var valueType = stringValue["properties"]?["value"]?["type"]?.AsArray();
        Assert.NotNull(valueType);
        Assert.Contains("string", valueType.Select(t => t?.GetValue<string>()));
        Assert.Contains("null", valueType.Select(t => t?.GetValue<string>()));
    }
}
