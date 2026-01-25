using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

[Collection("FrooxEngine")]
public class AudioOutputSchemaTests
{
    private readonly TestFixture _fixture;
    private readonly JsonObject _schema;
    private readonly JsonObject _componentProperties;
    private readonly JsonObject _members;
    private readonly JsonObject _defs;
    private readonly JsonObject _commonSchema;
    private readonly JsonObject _commonDefs;

    public AudioOutputSchemaTests(TestFixture fixture)
    {
        _fixture = fixture;

        var audioOutputType = fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutputType);

        _schema = fixture.SchemaGenerator.GenerateSchema(audioOutputType);

        // Navigate the allOf structure to get component-specific properties
        var allOf = _schema["allOf"]?.AsArray()
            ?? throw new InvalidOperationException("Schema missing allOf");
        _componentProperties = allOf[1]?["properties"]?.AsObject()
            ?? throw new InvalidOperationException("Schema missing component properties in allOf");

        // Get members from the component-specific properties, then navigate its allOf
        var membersAllOf = _componentProperties["members"]?["allOf"]?.AsArray()
            ?? throw new InvalidOperationException("Schema missing members/allOf");
        _members = membersAllOf[1]?["properties"]?.AsObject()
            ?? throw new InvalidOperationException("Schema missing members/properties");

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
        Assert.Equal("FrooxEngine.AudioOutput.schema.json", _schema["$id"]?.GetValue<string>());
        Assert.Equal("AudioOutput", _schema["title"]?.GetValue<string>());
        // With allOf, type is inside the second allOf element
        var componentSchema = _schema["allOf"]?[1]?.AsObject();
        Assert.Equal("object", componentSchema?["type"]?.GetValue<string>());
    }

    [Fact]
    public void Schema_HasCorrectComponentType()
    {
        var componentType = _componentProperties["componentType"]?["const"]?.GetValue<string>();
        Assert.Equal("[FrooxEngine]FrooxEngine.AudioOutput", componentType);
    }

    [Fact]
    public void Schema_ContainsVolumeField_AsFloat()
    {
        var volumeRef = _members["Volume"]?["$ref"]?.GetValue<string>();
        Assert.Equal("common.schema.json#/$defs/float_value", volumeRef);
    }

    [Fact]
    public void Schema_ContainsPriorityField_AsInt()
    {
        var priorityRef = _members["Priority"]?["$ref"]?.GetValue<string>();
        Assert.Equal("common.schema.json#/$defs/int_value", priorityRef);
    }

    [Fact]
    public void Schema_ContainsSpatializeField_AsBool()
    {
        var spatializeRef = _members["Spatialize"]?["$ref"]?.GetValue<string>();
        Assert.Equal("common.schema.json#/$defs/bool_value", spatializeRef);
    }

    [Fact]
    public void Schema_ContainsGlobalField_AsNullableBool()
    {
        var globalRef = _members["Global"]?["$ref"]?.GetValue<string>();
        Assert.Equal("common.schema.json#/$defs/nullable_bool_value", globalRef);
    }

    [Fact]
    public void Schema_ContainsAudioTypeGroupField_AsEnum()
    {
        // Enums stay in local $defs
        var audioTypeGroupRef = _members["AudioTypeGroup"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/$defs/AudioTypeGroup_value", audioTypeGroupRef);
    }

    [Fact]
    public void Schema_ContainsSourceField_AsReference()
    {
        var sourceSchema = _members["Source"]?.AsObject();
        Assert.NotNull(sourceSchema);
        Assert.Equal("object", sourceSchema["type"]?.GetValue<string>());
        Assert.Equal("reference", sourceSchema["properties"]?["$type"]?["const"]?.GetValue<string>());
        var properties = sourceSchema["properties"]?.AsObject();
        Assert.NotNull(properties);
        Assert.Contains("targetId", properties.Select(p => p.Key));
    }

    [Fact]
    public void Schema_ContainsExcludedListenersField_AsSyncRefList()
    {
        var excludedListeners = _members["ExcludedListeners"]?.AsObject();
        Assert.NotNull(excludedListeners);
        Assert.Equal("list", excludedListeners["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("array", excludedListeners["properties"]?["elements"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void CommonDefs_FloatValue_HasCorrectStructure()
    {
        var floatDef = _commonDefs["float_value"]?.AsObject();
        Assert.NotNull(floatDef);
        Assert.Equal("object", floatDef["type"]?.GetValue<string>());
        Assert.Equal("float", floatDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("number", floatDef["properties"]?["value"]?["type"]?.GetValue<string>());

        var required = floatDef["required"]?.AsArray();
        Assert.NotNull(required);
        Assert.Contains("$type", required.Select(r => r?.GetValue<string>()));
        Assert.Contains("value", required.Select(r => r?.GetValue<string>()));
    }

    [Fact]
    public void CommonDefs_IntValue_HasCorrectStructure()
    {
        var intDef = _commonDefs["int_value"]?.AsObject();
        Assert.NotNull(intDef);
        Assert.Equal("int", intDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("integer", intDef["properties"]?["value"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void CommonDefs_BoolValue_HasCorrectStructure()
    {
        var boolDef = _commonDefs["bool_value"]?.AsObject();
        Assert.NotNull(boolDef);
        Assert.Equal("bool", boolDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("boolean", boolDef["properties"]?["value"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void CommonDefs_NullableBoolValue_HasCorrectStructure()
    {
        var nullableBoolDef = _commonDefs["nullable_bool_value"]?.AsObject();
        Assert.NotNull(nullableBoolDef);
        Assert.Equal("bool?", nullableBoolDef["properties"]?["$type"]?["const"]?.GetValue<string>());

        // Value type should be array with boolean and null
        var valueType = nullableBoolDef["properties"]?["value"]?["type"]?.AsArray();
        Assert.NotNull(valueType);
        Assert.Contains("boolean", valueType.Select(t => t?.GetValue<string>()));
        Assert.Contains("null", valueType.Select(t => t?.GetValue<string>()));

        // $type and id are required for nullable (value can be omitted)
        var required = nullableBoolDef["required"]?.AsArray();
        Assert.NotNull(required);
        Assert.Contains("$type", required.Select(r => r?.GetValue<string>()));
        Assert.Contains("id", required.Select(r => r?.GetValue<string>()));
        Assert.Equal(2, required.Count);
    }

    [Fact]
    public void CommonDefs_MemberProperties_HasCorrectStructure()
    {
        var memberProps = _commonDefs["member_properties"]?.AsObject();
        Assert.NotNull(memberProps);
        Assert.Equal("object", memberProps["type"]?.GetValue<string>());

        var properties = memberProps["properties"]?.AsObject();
        Assert.NotNull(properties);

        // Should have Enabled, persistent, and UpdateOrder
        Assert.NotNull(properties["Enabled"]);
        Assert.NotNull(properties["persistent"]);
        Assert.NotNull(properties["UpdateOrder"]);

        // Each should reference the correct type in common schema
        Assert.Equal("#/$defs/bool_value", properties["Enabled"]?["$ref"]?.GetValue<string>());
        Assert.Equal("#/$defs/bool_value", properties["persistent"]?["$ref"]?.GetValue<string>());
        Assert.Equal("#/$defs/int_value", properties["UpdateOrder"]?["$ref"]?.GetValue<string>());
    }

    [Fact]
    public void CommonDefs_ComponentProperties_HasCorrectStructure()
    {
        var componentProps = _commonDefs["component_properties"]?.AsObject();
        Assert.NotNull(componentProps);
        Assert.Equal("object", componentProps["type"]?.GetValue<string>());

        var properties = componentProps["properties"]?.AsObject();
        Assert.NotNull(properties);

        // Should have id and isReferenceOnly
        Assert.NotNull(properties["id"]);
        Assert.NotNull(properties["isReferenceOnly"]);

        Assert.Equal("string", properties["id"]?["type"]?.GetValue<string>());
        Assert.Equal("boolean", properties["isReferenceOnly"]?["type"]?.GetValue<string>());

        // Required should include id and isReferenceOnly
        var required = componentProps["required"]?.AsArray();
        Assert.NotNull(required);
        Assert.Contains("id", required.Select(r => r?.GetValue<string>()));
        Assert.Contains("isReferenceOnly", required.Select(r => r?.GetValue<string>()));
    }

    [Fact]
    public void Schema_UsesAllOfForComponentProperties()
    {
        // Verify the schema structure uses allOf
        var allOf = _schema["allOf"]?.AsArray();
        Assert.NotNull(allOf);
        Assert.Equal(2, allOf.Count);

        // First element should be $ref to component_properties
        Assert.Equal("common.schema.json#/$defs/component_properties", allOf[0]?["$ref"]?.GetValue<string>());

        // Second element should be the component-specific properties
        Assert.Equal("object", allOf[1]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void Schema_MembersUsesAllOfForMemberProperties()
    {
        // Verify members uses allOf with member_properties
        var membersAllOf = _componentProperties["members"]?["allOf"]?.AsArray();
        Assert.NotNull(membersAllOf);
        Assert.Equal(2, membersAllOf.Count);

        // First element should be $ref to member_properties
        Assert.Equal("common.schema.json#/$defs/member_properties", membersAllOf[0]?["$ref"]?.GetValue<string>());
    }

    [Fact]
    public void Defs_AudioTypeGroupEnum_HasEnumValues()
    {
        // Enums stay in local $defs
        var enumDef = _defs["AudioTypeGroup_value"]?.AsObject();
        Assert.NotNull(enumDef);
        Assert.Equal("enum", enumDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("string", enumDef["properties"]?["value"]?["type"]?.GetValue<string>());
        Assert.Equal("AudioTypeGroup", enumDef["properties"]?["enumType"]?["const"]?.GetValue<string>());

        var enumValues = enumDef["properties"]?["value"]?["enum"]?.AsArray();
        Assert.NotNull(enumValues);
        Assert.Contains("SoundEffect", enumValues.Select(v => v?.GetValue<string>()));
        Assert.Contains("Multimedia", enumValues.Select(v => v?.GetValue<string>()));
        Assert.Contains("Voice", enumValues.Select(v => v?.GetValue<string>()));
        Assert.Contains("UI", enumValues.Select(v => v?.GetValue<string>()));
    }

    [Fact]
    public void Defs_AudioRolloffCurveEnum_HasEnumValues()
    {
        // Enums stay in local $defs
        var enumDef = _defs["AudioRolloffCurve_value"]?.AsObject();
        Assert.NotNull(enumDef);

        var enumValues = enumDef["properties"]?["value"]?["enum"]?.AsArray();
        Assert.NotNull(enumValues);
        Assert.Contains("Linear", enumValues.Select(v => v?.GetValue<string>()));
        Assert.Contains("Logarithmic", enumValues.Select(v => v?.GetValue<string>()));
    }
}
