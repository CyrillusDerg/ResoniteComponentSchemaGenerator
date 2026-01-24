using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

[Collection("FrooxEngine")]
public class AudioOutputSchemaTests
{
    private readonly TestFixture _fixture;
    private readonly JsonObject _schema;
    private readonly JsonObject _members;
    private readonly JsonObject _defs;

    public AudioOutputSchemaTests(TestFixture fixture)
    {
        _fixture = fixture;

        var audioOutputType = fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutputType);

        _schema = fixture.SchemaGenerator.GenerateSchema(audioOutputType);
        _members = _schema["properties"]?["members"]?["properties"]?.AsObject()
            ?? throw new InvalidOperationException("Schema missing members/properties");
        _defs = _schema["$defs"]?.AsObject()
            ?? throw new InvalidOperationException("Schema missing $defs");
    }

    [Fact]
    public void Schema_HasCorrectMetadata()
    {
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", _schema["$schema"]?.GetValue<string>());
        Assert.Equal("FrooxEngine.AudioOutput.schema.json", _schema["$id"]?.GetValue<string>());
        Assert.Equal("AudioOutput", _schema["title"]?.GetValue<string>());
        Assert.Equal("object", _schema["type"]?.GetValue<string>());
    }

    [Fact]
    public void Schema_HasCorrectComponentType()
    {
        var componentType = _schema["properties"]?["componentType"]?["const"]?.GetValue<string>();
        Assert.Equal("[FrooxEngine]FrooxEngine.AudioOutput", componentType);
    }

    [Fact]
    public void Schema_ContainsVolumeField_AsFloat()
    {
        var volumeRef = _members["Volume"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/$defs/float_value", volumeRef);
    }

    [Fact]
    public void Schema_ContainsPriorityField_AsInt()
    {
        var priorityRef = _members["Priority"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/$defs/int_value", priorityRef);
    }

    [Fact]
    public void Schema_ContainsSpatializeField_AsBool()
    {
        var spatializeRef = _members["Spatialize"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/$defs/bool_value", spatializeRef);
    }

    [Fact]
    public void Schema_ContainsGlobalField_AsNullableBool()
    {
        var globalRef = _members["Global"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/$defs/nullable_bool_value", globalRef);
    }

    [Fact]
    public void Schema_ContainsAudioTypeGroupField_AsEnum()
    {
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
        Assert.Contains("targetId", sourceSchema["properties"]?.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Schema_ContainsExcludedListenersField_AsSyncRefList()
    {
        var excludedListeners = _members["ExcludedListeners"]?.AsObject();
        Assert.NotNull(excludedListeners);
        Assert.Equal("syncList", excludedListeners["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("array", excludedListeners["properties"]?["elements"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void Defs_FloatValue_HasCorrectStructure()
    {
        var floatDef = _defs["float_value"]?.AsObject();
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
    public void Defs_IntValue_HasCorrectStructure()
    {
        var intDef = _defs["int_value"]?.AsObject();
        Assert.NotNull(intDef);
        Assert.Equal("int", intDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("integer", intDef["properties"]?["value"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void Defs_BoolValue_HasCorrectStructure()
    {
        var boolDef = _defs["bool_value"]?.AsObject();
        Assert.NotNull(boolDef);
        Assert.Equal("bool", boolDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("boolean", boolDef["properties"]?["value"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void Defs_NullableBoolValue_HasCorrectStructure()
    {
        var nullableBoolDef = _defs["nullable_bool_value"]?.AsObject();
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
    public void Defs_AudioTypeGroupEnum_HasEnumValues()
    {
        var enumDef = _defs["AudioTypeGroup_value"]?.AsObject();
        Assert.NotNull(enumDef);
        Assert.Equal("string", enumDef["properties"]?["$type"]?["const"]?.GetValue<string>());
        Assert.Equal("string", enumDef["properties"]?["value"]?["type"]?.GetValue<string>());

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
        var enumDef = _defs["AudioRolloffCurve_value"]?.AsObject();
        Assert.NotNull(enumDef);

        var enumValues = enumDef["properties"]?["value"]?["enum"]?.AsArray();
        Assert.NotNull(enumValues);
        Assert.Contains("Linear", enumValues.Select(v => v?.GetValue<string>()));
        Assert.Contains("Logarithmic", enumValues.Select(v => v?.GetValue<string>()));
    }
}
