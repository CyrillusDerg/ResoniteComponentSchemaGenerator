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
}
