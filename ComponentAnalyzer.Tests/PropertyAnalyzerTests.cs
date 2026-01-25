using Xunit;

namespace ComponentAnalyzer.Tests;

[Collection("FrooxEngine")]
public class PropertyAnalyzerTests
{
    private readonly TestFixture _fixture;

    public PropertyAnalyzerTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetPublicFields_ReturnsFieldsForAudioOutput()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var fields = PropertyAnalyzer.GetPublicFields(audioOutput);
        Assert.NotEmpty(fields);
    }

    [Fact]
    public void GetPublicFields_ContainsVolumeField()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var fields = PropertyAnalyzer.GetPublicFields(audioOutput);
        var volumeField = fields.FirstOrDefault(f => f.Name == "Volume");
        Assert.NotNull(volumeField);
    }

    [Fact]
    public void GetPublicFields_ContainsSpatializeField()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var fields = PropertyAnalyzer.GetPublicFields(audioOutput);
        var spatializeField = fields.FirstOrDefault(f => f.Name == "Spatialize");
        Assert.NotNull(spatializeField);
    }

    [Fact]
    public void GetPublicFields_ContainsSourceField()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var fields = PropertyAnalyzer.GetPublicFields(audioOutput);
        var sourceField = fields.FirstOrDefault(f => f.Name == "Source");
        Assert.NotNull(sourceField);
    }

    [Fact]
    public void GetFriendlyTypeName_FormatsGenericTypes()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var fields = PropertyAnalyzer.GetPublicFields(audioOutput);
        var volumeField = fields.FirstOrDefault(f => f.Name == "Volume");
        Assert.NotNull(volumeField);

        // Volume should be Sync<float>, so friendly name should include angle brackets
        Assert.Contains("<", volumeField.FriendlyTypeName);
        Assert.Contains(">", volumeField.FriendlyTypeName);
    }

    [Fact]
    public void GetFriendlyTypeName_NonGenericType_ReturnsSimpleName()
    {
        // For a non-generic type, GetFriendlyTypeName should return just the name
        var friendlyName = PropertyAnalyzer.GetFriendlyTypeName(typeof(string));
        Assert.Equal("String", friendlyName);
    }

    [Fact]
    public void GetFriendlyTypeName_GenericType_FormatsCorrectly()
    {
        var friendlyName = PropertyAnalyzer.GetFriendlyTypeName(typeof(List<int>));
        Assert.Equal("List<Int32>", friendlyName);
    }

    [Fact]
    public void GetFriendlyTypeName_NestedGenericType_FormatsCorrectly()
    {
        var friendlyName = PropertyAnalyzer.GetFriendlyTypeName(typeof(Dictionary<string, List<int>>));
        Assert.Equal("Dictionary<String, List<Int32>>", friendlyName);
    }

    [Fact]
    public void GetNonGenericBaseClass_ForFeedEntityInterface_ReturnsFeedItemInterface()
    {
        var feedEntityInterface = _fixture.Loader.FindComponent("FeedEntityInterface`1");
        Assert.NotNull(feedEntityInterface);

        var nonGenericBase = PropertyAnalyzer.GetNonGenericBaseClass(feedEntityInterface);
        Assert.NotNull(nonGenericBase);
        Assert.Equal("FeedItemInterface", nonGenericBase.Name);
    }

    [Fact]
    public void GetFieldsFromNonGenericBaseClasses_ForAudioOutput_ReturnsComponentFields()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        // AudioOutput inherits from Component (non-generic), so this method returns
        // Component's fields. These are Enabled, persistent, UpdateOrder - which are
        // already handled by member_properties. The schema generation filters these out
        // so no separate _base_members def is created for Component-level fields.
        var baseFields = PropertyAnalyzer.GetFieldsFromNonGenericBaseClasses(audioOutput);
        var fieldNames = baseFields.Select(f => f.Name).ToHashSet();

        // Component fields should be present
        Assert.Contains("Enabled", fieldNames);
        Assert.Contains("persistent", fieldNames);
        Assert.Contains("UpdateOrder", fieldNames);

        // But these are the ONLY base fields (no intermediate base class fields)
        Assert.Equal(3, baseFields.Count);
    }

    [Fact]
    public void GetFieldsFromNonGenericBaseClasses_ForFeedEntityInterface_ReturnsBaseFields()
    {
        var feedEntityInterface = _fixture.Loader.FindComponent("FeedEntityInterface`1");
        Assert.NotNull(feedEntityInterface);

        var baseFields = PropertyAnalyzer.GetFieldsFromNonGenericBaseClasses(feedEntityInterface);
        Assert.NotEmpty(baseFields);

        // FeedItemInterface should have fields like HasData, HasDescription, etc.
        var fieldNames = baseFields.Select(f => f.Name).ToList();
        Assert.Contains("HasData", fieldNames);
    }

    [Fact]
    public void GetFieldsExcludingNonGenericBaseClasses_ForFeedEntityInterface_ExcludesBaseFields()
    {
        var feedEntityInterface = _fixture.Loader.FindComponent("FeedEntityInterface`1");
        Assert.NotNull(feedEntityInterface);

        var allFields = PropertyAnalyzer.GetAllSerializableFields(feedEntityInterface);
        var excludedFields = PropertyAnalyzer.GetFieldsExcludingNonGenericBaseClasses(feedEntityInterface);
        var baseFields = PropertyAnalyzer.GetFieldsFromNonGenericBaseClasses(feedEntityInterface);

        // Excluded fields + base fields should equal all fields (minus common ones)
        var excludedNames = excludedFields.Select(f => f.Name).ToHashSet();
        var baseNames = baseFields.Select(f => f.Name).ToHashSet();

        // Base field names should not be in excluded fields
        foreach (var baseName in baseNames)
        {
            Assert.DoesNotContain(baseName, excludedNames);
        }
    }
}
