using System.Reflection;
using System.Runtime.Loader;

namespace ComponentAnalyzer;

/// <summary>
/// Resolves generic type constraints by loading FrooxEngine.dll and calling
/// GenericTypes.GetTypes() to discover allowed types for generic parameters.
/// </summary>
public class GenericTypeResolver : IDisposable
{
    private readonly AssemblyLoadContext _loadContext;
    private readonly Assembly _assembly;
    private readonly Type? _genericTypesAttributeType;

    private GenericTypeResolver(AssemblyLoadContext loadContext, Assembly assembly)
    {
        _loadContext = loadContext;
        _assembly = assembly;

        // Find the GenericTypesAttribute class
        _genericTypesAttributeType = assembly.GetType("FrooxEngine.GenericTypesAttribute");
    }

    public bool IsAvailable => _genericTypesAttributeType != null;

    public static GenericTypeResolver? TryCreate(string dllPath)
    {
        try
        {
            string dllDirectory = Path.GetDirectoryName(Path.GetFullPath(dllPath)) ?? ".";

            // Create a custom load context that can resolve dependencies
            var loadContext = new FrooxEngineLoadContext(dllDirectory);
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));

            var resolver = new GenericTypeResolver(loadContext, assembly);

            if (!resolver.IsAvailable)
            {
                Console.WriteLine("Warning: GenericTypes support not available - could not find required types/methods");
                resolver.Dispose();
                return null;
            }

            return resolver;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load assembly for generic type resolution: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a type has the GenericTypesAttribute.
    /// </summary>
    public bool HasGenericTypesAttribute(Type metadataType)
    {
        if (_genericTypesAttributeType == null)
            return false;

        try
        {
            var fullName = metadataType.FullName;
            if (fullName == null) return false;

            var executableType = _assembly.GetType(fullName);
            if (executableType == null) return false;

            var attributes = executableType.GetCustomAttributes(_genericTypesAttributeType, false);
            return attributes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the allowed types for a generic type from its GenericTypesAttribute.
    /// Returns the types from the AssemblyLoadContext (for display purposes).
    /// </summary>
    public Type[]? GetAllowedTypesForGeneric(Type metadataType)
    {
        var typeNames = GetAllowedTypeNamesForGeneric(metadataType);
        if (typeNames == null) return null;

        // Return the actual types from our loaded assembly
        var types = new List<Type>();
        foreach (var name in typeNames)
        {
            var type = GetTypeByName(name);
            if (type != null) types.Add(type);
        }
        return types.ToArray();
    }

    /// <summary>
    /// Gets the allowed type names for a generic type from its GenericTypesAttribute.
    /// Returns fully qualified type names that can be looked up in any context.
    /// </summary>
    public string[]? GetAllowedTypeNamesForGeneric(Type metadataType)
    {
        if (_genericTypesAttributeType == null)
            return null;

        try
        {
            var fullName = metadataType.FullName;
            if (fullName == null)
                return null;

            var executableType = _assembly.GetType(fullName);
            if (executableType == null)
                return null;

            var attributes = executableType.GetCustomAttributes(_genericTypesAttributeType, false);
            if (attributes.Length == 0)
                return null;

            var attribute = attributes[0];

            // Get the Types property which returns IEnumerable<Type>
            var typesProperty = _genericTypesAttributeType.GetProperty("Types");
            if (typesProperty == null)
                return null;

            var typesValue = typesProperty.GetValue(attribute);

            if (typesValue is IEnumerable<Type> typesList)
            {
                return typesList.Select(t => t.FullName ?? t.Name).ToArray();
            }

            // Try casting to non-generic IEnumerable and filtering
            if (typesValue is System.Collections.IEnumerable enumerable)
            {
                return enumerable.Cast<object>().OfType<Type>().Select(t => t.FullName ?? t.Name).ToArray();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Looks up a type by its full name in the loaded assembly or its references.
    /// </summary>
    private Type? GetTypeByName(string fullName)
    {
        // Try in the main assembly first
        var type = _assembly.GetType(fullName);
        if (type != null) return type;

        // Try in referenced assemblies
        foreach (var assemblyName in _assembly.GetReferencedAssemblies())
        {
            try
            {
                var refAssembly = _loadContext.LoadFromAssemblyName(assemblyName);
                type = refAssembly.GetType(fullName);
                if (type != null) return type;
            }
            catch { }
        }

        return null;
    }


    public void Dispose()
    {
        _loadContext.Unload();
    }

    /// <summary>
    /// Custom AssemblyLoadContext that resolves dependencies from the FrooxEngine directory.
    /// </summary>
    private class FrooxEngineLoadContext : AssemblyLoadContext
    {
        private readonly string _basePath;

        public FrooxEngineLoadContext(string basePath) : base(isCollectible: true)
        {
            _basePath = basePath;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Try to find the assembly in the base path
            string assemblyPath = Path.Combine(_basePath, $"{assemblyName.Name}.dll");

            if (File.Exists(assemblyPath))
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            // Fall back to default loading
            return null;
        }
    }
}
