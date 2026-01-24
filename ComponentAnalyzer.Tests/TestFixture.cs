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
    private static readonly string DefaultResonitePath = @"F:\Steam\steamapps\common\Resonite";

    public TestFixture()
    {
        // Support both RESONITE_PATH (directory) and FROOXENGINE_DLL_PATH (file) for backwards compatibility
        var resonitePath = Environment.GetEnvironmentVariable("RESONITE_PATH");
        if (string.IsNullOrEmpty(resonitePath))
        {
            var dllPath = Environment.GetEnvironmentVariable("FROOXENGINE_DLL_PATH");
            if (!string.IsNullOrEmpty(dllPath))
            {
                resonitePath = Path.GetDirectoryName(dllPath);
            }
        }
        resonitePath ??= DefaultResonitePath;

        var frooxEnginePath = Path.Combine(resonitePath, "FrooxEngine.dll");
        if (!Directory.Exists(resonitePath) || !File.Exists(frooxEnginePath))
        {
            throw new DirectoryNotFoundException(
                $"Resonite installation not found at '{resonitePath}'. " +
                "Set the RESONITE_PATH environment variable to the Resonite directory, " +
                "or FROOXENGINE_DLL_PATH to the path of FrooxEngine.dll.");
        }

        Loader = ComponentLoader.Load(resonitePath);
        GenericResolver = GenericTypeResolver.TryCreate(resonitePath);
        SchemaGenerator = new JsonSchemaGenerator(Loader, GenericResolver)
        {
            UseExternalCommonSchema = true
        };
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
