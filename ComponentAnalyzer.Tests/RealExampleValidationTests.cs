using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

/// <summary>
/// Tests that validate real example JSON files against generated schemas using JsonSchema.Net.
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

    /// <summary>
    /// Creates a SchemaValidator with the common schema registered.
    /// </summary>
    private SchemaValidator CreateValidatorWithCommonSchema()
    {
        var validator = new SchemaValidator();
        var commonSchema = _fixture.SchemaGenerator.GenerateCommonSchema();
        var commonSchemaJson = _fixture.SchemaGenerator.SerializeSchema(commonSchema);
        validator.RegisterSchemaFromText(commonSchemaJson, "common.schema.json");
        return validator;
    }

    [Theory]
    [InlineData("FrooxEngine.StaticLocaleProvider", "FrooxEngine.StaticLocaleProvider_example.json")]
    [InlineData("FrooxEngine.GradientStripTexture", "FrooxEngine.GradientStripTexture_example.json")]
    [InlineData("FrooxEngine.UI_UnlitMaterial", "FrooxEngine.UI_UnlitMaterial_example.json")]
    [InlineData("FrooxEngine.TrackedDevicePositioner", "FrooxEngine.TrackedDevicePositioner_example.json")]
    [InlineData("FrooxEngine.ItemShelf", "FrooxEngine.ItemShelf_example.json")]
    [InlineData("FrooxEngine.SkinnedMeshRenderer", "FrooxEngine.SkinnedMeshRenderer_example.json")]
    [InlineData("FrooxEngine.VisemeAnalyzer", "FrooxEngine.VisemeAnalyzer_example.json")]
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

        // Validate using SchemaValidator
        var validator = CreateValidatorWithCommonSchema();
        var result = validator.ValidateJson(exampleJson, schemaJson);

        Assert.True(result.IsValid,
            $"Validation failed for {componentName}:\n{string.Join("\n", result.Errors)}");
    }

    [Fact]
    public void ProtoFluxObjectRelay_ValidatesRealExample()
    {
        // This is a generic ProtoFlux node
        var componentType = _fixture.Loader.FindComponent("ObjectRelay`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);
        var schemaJson = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.ObjectRelay_Slot_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);

        // Validate using SchemaValidator
        var validator = CreateValidatorWithCommonSchema();
        var result = validator.ValidateJson(exampleJson, schemaJson);

        Assert.True(result.IsValid,
            $"Validation failed for ObjectRelay<Slot>:\n{string.Join("\n", result.Errors)}");

        // Also verify the example has expected structure
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(exampleComponentType);
        Assert.Contains("ObjectRelay", exampleComponentType);
    }

    [Fact]
    public void AssetLoaderLocaleResource_ValidatesRealExample()
    {
        // This is a generic FrooxEngine component
        var componentType = _fixture.Loader.FindComponent("AssetLoader`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);
        var schemaJson = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.AssetLoader_FrooxEngine.LocaleResource_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);

        // Validate using SchemaValidator
        var validator = CreateValidatorWithCommonSchema();
        var result = validator.ValidateJson(exampleJson, schemaJson);

        Assert.True(result.IsValid,
            $"Validation failed for AssetLoader<LocaleResource>:\n{string.Join("\n", result.Errors)}");

        // Also verify the example has expected structure
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);
        var exampleComponentType = example["componentType"]?.GetValue<string>();
        Assert.NotNull(exampleComponentType);
        Assert.Contains("AssetLoader", exampleComponentType);
        Assert.Contains("LocaleResource", exampleComponentType);

        // Validate Asset is a reference type
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);
        var asset = members["Asset"]?.AsObject();
        Assert.NotNull(asset);
        Assert.Equal("reference", asset["$type"]?.GetValue<string>());
    }

    [Fact]
    public void ValueCopy_ValidatesRealExample()
    {
        // This is a generic FrooxEngine component with FieldDrive and RelayRef members
        var componentType = _fixture.Loader.FindComponent("ValueCopy`1");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);
        var schemaJson = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.ValueCopy_bool_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);

        // Validate using SchemaValidator
        var validator = CreateValidatorWithCommonSchema();
        var result = validator.ValidateJson(exampleJson, schemaJson);

        Assert.True(result.IsValid,
            $"Validation failed for ValueCopy<bool>:\n{string.Join("\n", result.Errors)}");

        // Also verify the example has expected IField references
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);

        // Validate Source and Target are reference types targeting IField<bool>
        var source = members["Source"]?.AsObject();
        Assert.NotNull(source);
        Assert.Equal("reference", source["$type"]?.GetValue<string>());
        Assert.Equal("[FrooxEngine]FrooxEngine.IField<bool>", source["targetType"]?.GetValue<string>());

        var target = members["Target"]?.AsObject();
        Assert.NotNull(target);
        Assert.Equal("reference", target["$type"]?.GetValue<string>());
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
        var schemaJson = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Load example
        var examplePath = Path.Combine(ExamplesPath, "FrooxEngine.BooleanValueDriver_float2_example.json");
        Assert.True(File.Exists(examplePath), $"Example file not found: {examplePath}");
        var exampleJson = File.ReadAllText(examplePath);

        // Validate using SchemaValidator
        var validator = CreateValidatorWithCommonSchema();
        var result = validator.ValidateJson(exampleJson, schemaJson);

        Assert.True(result.IsValid,
            $"Validation failed for BooleanValueDriver<float2>:\n{string.Join("\n", result.Errors)}");

        // Also verify the example has expected structure
        var example = JsonNode.Parse(exampleJson);
        Assert.NotNull(example);
        var members = example["members"]?.AsObject();
        Assert.NotNull(members);

        // Validate TargetField is a reference type targeting IField<float2>
        var targetField = members["TargetField"]?.AsObject();
        Assert.NotNull(targetField);
        Assert.Equal("reference", targetField["$type"]?.GetValue<string>());
        Assert.Equal("[FrooxEngine]FrooxEngine.IField<float2>", targetField["targetType"]?.GetValue<string>());

        // Validate FalseValue and TrueValue are float2 types
        var falseValue = members["FalseValue"]?.AsObject();
        Assert.NotNull(falseValue);
        Assert.Equal("float2", falseValue["$type"]?.GetValue<string>());

        var trueValue = members["TrueValue"]?.AsObject();
        Assert.NotNull(trueValue);
        Assert.Equal("float2", trueValue["$type"]?.GetValue<string>());
    }

    [Fact]
    public void InvalidJson_FailsValidation()
    {
        // Find a component type
        var componentType = _fixture.Loader.FindComponent("FrooxEngine.StaticLocaleProvider");
        Assert.NotNull(componentType);

        // Generate schema
        var schema = _fixture.SchemaGenerator.GenerateSchema(componentType);
        var schemaJson = _fixture.SchemaGenerator.SerializeSchema(schema);

        // Create invalid JSON (missing required fields)
        var invalidJson = """{"componentType": "wrong", "id": "test"}""";

        // Validate using SchemaValidator
        var validator = CreateValidatorWithCommonSchema();
        var result = validator.ValidateJson(invalidJson, schemaJson);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }
}
