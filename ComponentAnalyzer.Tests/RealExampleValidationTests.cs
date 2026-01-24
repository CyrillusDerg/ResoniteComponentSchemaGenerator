using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

/// <summary>
/// Tests that validate real example JSON files against generated schemas.
/// </summary>
[Collection("FrooxEngine")]
public class RealExampleValidationTests
{
    private readonly TestFixture _fixture;

    public RealExampleValidationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly string ExamplesPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "real_examples");

    [Theory]
    [InlineData("FrooxEngine.StaticLocaleProvider", "FrooxEngine.StaticLocaleProvider_example.json")]
    [InlineData("FrooxEngine.GradientStripTexture", "FrooxEngine.GradientStripTexture_example.json")]
    [InlineData("FrooxEngine.UI_UnlitMaterial", "FrooxEngine.UI_UnlitMaterial_example.json")]
    [InlineData("FrooxEngine.TrackedDevicePositioner", "FrooxEngine.TrackedDevicePositioner_example.json")]
    [InlineData("FrooxEngine.ItemShelf", "FrooxEngine.ItemShelf_example.json")]
    public void Schema_ValidatesRealExample(string componentName, string exampleFileName)
    {
        // Find the component type
        var componentType = _fixture.Loader.FindComponent(componentName);
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);
        var schemaJson = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, exampleFileName);
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);

        // Validate structure manually (since we don't have a full JSON Schema validator in .NET)
        ValidateComponentStructure(schema, example, componentName);
    }

    [Fact]
    public void ProtoFluxObjectRelay_ValidatesRealExample()
    {
        // This is a generic ProtoFlux node
        var componentType = _fixture.Loader.FindComponent("ObjectRelay`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ObjectRelay_Slot_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);

        // For generic types, check that the example's componentType matches a valid variant
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(exampleComponentType);
        Assert.Contains("ObjectRelay", exampleComponentType);

        // Check members exist
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);
        Assert.True(members.ContainsKey("persistent"));
        Assert.True(members.ContainsKey("UpdateOrder"));
        Assert.True(members.ContainsKey("Enabled"));
    }

    [Fact]
    public void AssetLoaderLocaleResource_ValidatesRealExample()
    {
        // This is a generic FrooxEngine component
        var componentType = _fixture.Loader.FindComponent("AssetLoader`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.AssetLoader_FrooxEngine.LocaleResource_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);

        // For generic types, check that the example's componentType matches a valid variant
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(exampleComponentType);
        Assert.Contains("AssetLoader", exampleComponentType);
        Assert.Contains("LocaleResource", exampleComponentType);

        // Check members exist
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);
        Assert.True(members.ContainsKey("persistent"));
        Assert.True(members.ContainsKey("UpdateOrder"));
        Assert.True(members.ContainsKey("Enabled"));
        Assert.True(members.ContainsKey("Asset"));

        // Validate Asset is a reference type
        var asset = members["Asset"]?.AsObject();
        Assert.NotNull(asset);
        Assert.Equal("reference", asset["$type"]?.GetValue<string>());
        Assert.True(asset.ContainsKey("targetId"));
        Assert.True(asset.ContainsKey("targetType"));
    }

    [Fact]
    public void ValueCopy_ValidatesRealExample()
    {
        // This is a generic FrooxEngine component with FieldDrive and RelayRef members
        var componentType = _fixture.Loader.FindComponent("ValueCopy`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.ValueCopy_bool_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);

        // For generic types, check that the example's componentType matches a valid variant
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(exampleComponentType);
        Assert.Contains("ValueCopy", exampleComponentType);
        Assert.Contains("bool", exampleComponentType);

        // Check members exist
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);
        Assert.True(members.ContainsKey("persistent"));
        Assert.True(members.ContainsKey("UpdateOrder"));
        Assert.True(members.ContainsKey("Enabled"));
        Assert.True(members.ContainsKey("Source"));  // RelayRef<IField<T>>
        Assert.True(members.ContainsKey("Target"));  // FieldDrive<T>
        Assert.True(members.ContainsKey("WriteBack"));

        // Validate Source and Target are reference types targeting IField<bool>
        var source = members["Source"]?.AsObject();
        Assert.NotNull(source);
        Assert.Equal("reference", source["$type"]?.GetValue<string>());
        Assert.True(source.ContainsKey("targetId"));
        Assert.Equal("[FrooxEngine]FrooxEngine.IField<bool>", source["targetType"]?.GetValue<string>());

        var target = members["Target"]?.AsObject();
        Assert.NotNull(target);
        Assert.Equal("reference", target["$type"]?.GetValue<string>());
        Assert.True(target.ContainsKey("targetId"));
        Assert.Equal("[FrooxEngine]FrooxEngine.IField<bool>", target["targetType"]?.GetValue<string>());
    }

    [Fact]
    public void BooleanValueDriver_ValidatesRealExample()
    {
        // This is a generic FrooxEngine component with FieldDrive member
        var componentType = _fixture.Loader.FindComponent("BooleanValueDriver`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.BooleanValueDriver_float2_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);

        // For generic types, check that the example's componentType matches a valid variant
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(exampleComponentType);
        Assert.Contains("BooleanValueDriver", exampleComponentType);
        Assert.Contains("float2", exampleComponentType);

        // Check members exist
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);
        Assert.True(members.ContainsKey("persistent"));
        Assert.True(members.ContainsKey("UpdateOrder"));
        Assert.True(members.ContainsKey("Enabled"));
        Assert.True(members.ContainsKey("State"));
        Assert.True(members.ContainsKey("TargetField"));  // FieldDrive<T>
        Assert.True(members.ContainsKey("FalseValue"));
        Assert.True(members.ContainsKey("TrueValue"));

        // Validate TargetField is a reference type targeting IField<float2>
        var targetField = members["TargetField"]?.AsObject();
        Assert.NotNull(targetField);
        Assert.Equal("reference", targetField["$type"]?.GetValue<string>());
        Assert.True(targetField.ContainsKey("targetId"));
        Assert.Equal("[FrooxEngine]FrooxEngine.IField<float2>", targetField["targetType"]?.GetValue<string>());

        // Validate FalseValue and TrueValue are float2 types
        var falseValue = members["FalseValue"]?.AsObject();
        Assert.NotNull(falseValue);
        Assert.Equal("float2", falseValue["$type"]?.GetValue<string>());

        var trueValue = members["TrueValue"]?.AsObject();
        Assert.NotNull(trueValue);
        Assert.Equal("float2", trueValue["$type"]?.GetValue<string>());
    }

    private void ValidateComponentStructure(JsonObject schema, JsonNode example, string componentName)
    {
        // Check componentType matches
        var schemaComponentType = schema["properties"]?["componentType"]?["const"]?.GetValue<string>();
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(schemaComponentType);
        Assert.NotNull(exampleComponentType);
        Assert.Equal(schemaComponentType, exampleComponentType);

        // Check id field exists
        var exampleId = example["id"]?.GetValue<string>();
        Assert.NotNull(exampleId);

        // Check isReferenceOnly exists
        var isRefOnly = example["isReferenceOnly"];
        Assert.NotNull(isRefOnly);

        // Check members
        var schemaMembers = schema["properties"]?["members"]?["properties"]?.AsObject();
        var exampleMembers = example["members"]?.AsObject();
        Assert.NotNull(schemaMembers);
        Assert.NotNull(exampleMembers);

        // Every member in the example should have a corresponding schema entry
        foreach (var (memberName, memberValue) in exampleMembers)
        {
            Assert.True(schemaMembers.ContainsKey(memberName),
                $"Schema for {componentName} is missing member '{memberName}'");

            // Validate member structure
            ValidateMemberValue(memberValue, memberName, componentName);
        }
    }

    private void ValidateMemberValue(JsonNode? memberValue, string memberName, string componentName)
    {
        Assert.NotNull(memberValue);
        var memberObj = memberValue.AsObject();

        // Every member must have $type and id
        var type = memberObj["$type"]?.GetValue<string>();
        Assert.NotNull(type);

        var id = memberObj["id"]?.GetValue<string>();
        Assert.NotNull(id);

        // Validate type-specific structure
        switch (type)
        {
            case "reference":
                // References can have null targetId
                Assert.True(memberObj.ContainsKey("targetId"));
                Assert.True(memberObj.ContainsKey("targetType"));
                break;

            case "syncList":
                Assert.True(memberObj.ContainsKey("elements"));
                break;

            case "enum":
            case "enum?":
                Assert.True(memberObj.ContainsKey("value"));
                Assert.True(memberObj.ContainsKey("enumType"));
                var enumType = memberObj["enumType"]?.GetValue<string>();
                Assert.False(string.IsNullOrEmpty(enumType),
                    $"Enum member '{memberName}' in {componentName} should have enumType");
                break;

            default:
                // Value types should have a value (except nullable types can have null)
                if (!type.EndsWith("?"))
                {
                    Assert.True(memberObj.ContainsKey("value"),
                        $"Member '{memberName}' of type '{type}' in {componentName} should have value");
                }
                break;
        }
    }
}
