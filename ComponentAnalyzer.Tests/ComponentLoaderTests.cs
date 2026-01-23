using Xunit;

namespace ComponentAnalyzer.Tests;

[Collection("FrooxEngine")]
public class ComponentLoaderTests
{
    private readonly TestFixture _fixture;

    public ComponentLoaderTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Loader_FindsComponents()
    {
        Assert.NotEmpty(_fixture.Loader.DerivedTypes);
    }

    [Fact]
    public void Loader_FindsAudioOutput()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);
        Assert.Equal("FrooxEngine.AudioOutput", audioOutput.FullName);
    }

    [Fact]
    public void Loader_FindsValueFieldGeneric()
    {
        var valueField = _fixture.Loader.FindComponent("ValueField`1");
        Assert.NotNull(valueField);
        Assert.True(valueField.IsGenericTypeDefinition);
    }

    [Fact]
    public void Loader_FindsValueField_WithAngleBracketSyntax()
    {
        var valueField = _fixture.Loader.FindComponent("ValueField<1>");
        Assert.NotNull(valueField);
        Assert.True(valueField.IsGenericTypeDefinition);
    }

    [Fact]
    public void Loader_FindsValueField_WithSquareBracketSyntax()
    {
        var valueField = _fixture.Loader.FindComponent("ValueField[1]");
        Assert.NotNull(valueField);
        Assert.True(valueField.IsGenericTypeDefinition);
    }

    [Fact]
    public void Loader_FindsValueField_WithGenericTSyntax()
    {
        var valueField = _fixture.Loader.FindComponent("ValueField<T>");
        Assert.NotNull(valueField);
        Assert.True(valueField.IsGenericTypeDefinition);
    }

    [Fact]
    public void Loader_FindComponents_PartialMatch()
    {
        var audioComponents = _fixture.Loader.FindComponents("Audio");
        Assert.NotEmpty(audioComponents);
        Assert.All(audioComponents, c => Assert.Contains("Audio", c.FullName));
    }

    [Fact]
    public void Loader_FindComponents_IsCaseInsensitive()
    {
        var lowerCase = _fixture.Loader.FindComponents("audio");
        var upperCase = _fixture.Loader.FindComponents("AUDIO");
        Assert.Equal(lowerCase.Count, upperCase.Count);
    }

    [Fact]
    public void Loader_GetInheritanceChain_ReturnsValidChain()
    {
        var audioOutput = _fixture.Loader.FindComponent("AudioOutput");
        Assert.NotNull(audioOutput);

        var chain = _fixture.Loader.GetInheritanceChain(audioOutput);
        Assert.NotEmpty(chain);
        Assert.Equal(audioOutput.FullName, chain.First());
        Assert.Contains("FrooxEngine.Component", chain);
    }

    [Fact]
    public void Loader_FindTypeByFullName_FindsSystemTypes()
    {
        var boolType = _fixture.Loader.FindTypeByFullName("System.Boolean");
        Assert.NotNull(boolType);
    }

    [Fact]
    public void Loader_FindsReferenceField()
    {
        // Test that we can find a component with reference fields
        var referenceField = _fixture.Loader.FindComponent("ReferenceField`1");
        Assert.NotNull(referenceField);
        Assert.True(referenceField.IsGenericTypeDefinition);
    }

    [Fact]
    public void Loader_ReturnsNullForNonexistentComponent()
    {
        var notFound = _fixture.Loader.FindComponent("NonExistentComponentThatDoesNotExist12345");
        Assert.Null(notFound);
    }

    [Fact]
    public void Loader_ComponentTypeIsNotNull()
    {
        Assert.NotNull(_fixture.Loader.ComponentType);
        Assert.Equal("FrooxEngine.Component", _fixture.Loader.ComponentType.FullName);
    }
}
