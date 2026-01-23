using System.Text.Json.Nodes;
using Xunit;

namespace ComponentAnalyzer.Tests;

/// <summary>
/// Shared test fixture that loads the FrooxEngine DLL once for all tests.
/// </summary>
public class TestFixture : IDisposable
{
    public ComponentLoader Loader { get; }
    public GenericTypeResolver? GenericResolver { get; }
    public JsonSchemaGenerator SchemaGenerator { get; }

    // Default path - can be overridden via environment variable
    private static readonly string DefaultDllPath = @"F:\Steam\steamapps\common\Resonite\FrooxEngine.dll";

    public TestFixture()
    {
        var dllPath = Environment.GetEnvironmentVariable("FROOXENGINE_DLL_PATH") ?? DefaultDllPath;

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(
                $"FrooxEngine.dll not found at '{dllPath}'. " +
                "Set the FROOXENGINE_DLL_PATH environment variable to the correct path.");
        }

        Loader = ComponentLoader.Load(dllPath);
        GenericResolver = GenericTypeResolver.TryCreate(dllPath);
        SchemaGenerator = new JsonSchemaGenerator(Loader, GenericResolver);
    }

    public void Dispose()
    {
        GenericResolver?.Dispose();
        Loader.Dispose();
    }
}

/// <summary>
/// Collection definition for sharing the fixture across test classes.
/// </summary>
[CollectionDefinition("FrooxEngine")]
public class FrooxEngineCollection : ICollectionFixture<TestFixture>
{
}
