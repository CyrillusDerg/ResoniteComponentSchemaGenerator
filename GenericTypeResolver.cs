using System.Reflection;
using System.Runtime.Loader;

namespace ComponentAnalyzer;

/// <summary>
/// Resolves generic type constraints by loading FrooxEngine.dll and calling
/// GenericTypes.GetTypes() to discover allowed types for generic parameters.
/// Also handles interface constraints (e.g., where T : IAsset) by finding implementing types.
/// </summary>
public class GenericTypeResolver : IDisposable
{
    private readonly AssemblyLoadContext _loadContext;
    private readonly Assembly _assembly;
    private readonly Type? _genericTypesAttributeType;
    private readonly Dictionary<string, Type[]> _interfaceImplementorsCache = new();

    private GenericTypeResolver(AssemblyLoadContext loadContext, Assembly assembly)
    {
        _loadContext = loadContext;
        _assembly = assembly;

        // Find the GenericTypesAttribute class
        _genericTypesAttributeType = assembly.GetType("FrooxEngine.GenericTypesAttribute");
    }

    public bool IsAvailable => true; // Always available now that we support constraints

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
    /// Gets the allowed type names for a generic type from its GenericTypesAttribute,
    /// or by finding types that satisfy interface constraints.
    /// Returns fully qualified type names that can be looked up in any context.
    /// </summary>
    public string[]? GetAllowedTypeNamesForGeneric(Type metadataType)
    {
        // First try GenericTypesAttribute
        var attributeTypes = GetTypesFromGenericTypesAttribute(metadataType);
        if (attributeTypes != null)
            return attributeTypes;

        // Fall back to interface constraint resolution
        return GetTypesFromInterfaceConstraints(metadataType);
    }

    /// <summary>
    /// Gets types from the [GenericTypes] attribute if present.
    /// </summary>
    private string[]? GetTypesFromGenericTypesAttribute(Type metadataType)
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
    /// Gets types that satisfy interface constraints on the generic type parameter.
    /// </summary>
    private string[]? GetTypesFromInterfaceConstraints(Type metadataType)
    {
        try
        {
            var fullName = metadataType.FullName;
            if (fullName == null)
                return null;

            var executableType = _assembly.GetType(fullName);
            if (executableType == null)
                return null;

            if (!executableType.IsGenericTypeDefinition)
                return null;

            var genericParams = executableType.GetGenericArguments();
            if (genericParams.Length == 0)
                return null;

            // Get constraints for the first generic parameter
            var param = genericParams[0];
            var constraints = param.GetGenericParameterConstraints();

            if (constraints.Length == 0)
                return null;

            // Find interface constraints
            var interfaceConstraints = constraints.Where(c => c.IsInterface).ToList();
            if (interfaceConstraints.Count == 0)
                return null;

            // Find all types that implement all the interface constraints
            var implementingTypes = FindTypesImplementingInterfaces(interfaceConstraints);

            // Also check for class constraint (must be a class, not struct)
            var attrs = param.GenericParameterAttributes;
            bool mustBeClass = (attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
            bool mustBeStruct = (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;

            if (mustBeClass)
            {
                implementingTypes = implementingTypes.Where(t => t.IsClass).ToList();
            }
            else if (mustBeStruct)
            {
                implementingTypes = implementingTypes.Where(t => t.IsValueType && !t.IsEnum).ToList();
            }

            // Filter out abstract types since we can't instantiate them
            implementingTypes = implementingTypes.Where(t => !t.IsAbstract).ToList();

            return implementingTypes
                .Select(t => t.FullName ?? t.Name)
                .OrderBy(n => n)
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds all types that implement all the specified interfaces.
    /// </summary>
    private List<Type> FindTypesImplementingInterfaces(List<Type> interfaces)
    {
        var result = new List<Type>();

        // Get all loaded assemblies in our context
        var assemblies = new List<Assembly> { _assembly };
        foreach (var assemblyName in _assembly.GetReferencedAssemblies())
        {
            try
            {
                var refAssembly = _loadContext.LoadFromAssemblyName(assemblyName);
                assemblies.Add(refAssembly);
            }
            catch { }
        }

        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in GetLoadableTypes(assembly))
                {
                    try
                    {
                        // Check if type implements all required interfaces
                        bool implementsAll = interfaces.All(iface =>
                            type.GetInterfaces().Any(ti => ti.FullName == iface.FullName));

                        if (implementsAll)
                        {
                            result.Add(type);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return result;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }

    /// <summary>
    /// Gets debug information about a generic type's constraints and allowed types.
    /// </summary>
    public GenericTypeDebugInfo? GetDebugInfo(Type metadataType)
    {
        try
        {
            var fullName = metadataType.FullName;
            if (fullName == null)
                return null;

            var executableType = _assembly.GetType(fullName);
            if (executableType == null)
                return null;

            if (!executableType.IsGenericTypeDefinition)
                return null;

            var info = new GenericTypeDebugInfo
            {
                TypeName = fullName,
                GenericParameters = new List<GenericParameterDebugInfo>()
            };

            // Check for GenericTypesAttribute
            if (_genericTypesAttributeType != null)
            {
                var attributes = executableType.GetCustomAttributes(_genericTypesAttributeType, false);
                info.HasGenericTypesAttribute = attributes.Length > 0;
            }

            foreach (var param in executableType.GetGenericArguments())
            {
                var paramInfo = new GenericParameterDebugInfo
                {
                    Name = param.Name,
                    Attributes = param.GenericParameterAttributes,
                    Constraints = new List<string>()
                };

                foreach (var constraint in param.GetGenericParameterConstraints())
                {
                    paramInfo.Constraints.Add($"{constraint.FullName} (IsInterface: {constraint.IsInterface}, IsClass: {constraint.IsClass})");
                }

                info.GenericParameters.Add(paramInfo);
            }

            // Get allowed types
            info.AllowedTypeNames = GetAllowedTypeNamesForGeneric(metadataType);

            return info;
        }
        catch (Exception ex)
        {
            return new GenericTypeDebugInfo
            {
                TypeName = metadataType.FullName ?? metadataType.Name,
                Error = ex.Message
            };
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

/// <summary>
/// Debug information about a generic type's constraints.
/// </summary>
public class GenericTypeDebugInfo
{
    public string TypeName { get; set; } = "";
    public bool HasGenericTypesAttribute { get; set; }
    public List<GenericParameterDebugInfo> GenericParameters { get; set; } = new();
    public string[]? AllowedTypeNames { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Debug information about a generic parameter.
/// </summary>
public class GenericParameterDebugInfo
{
    public string Name { get; set; } = "";
    public GenericParameterAttributes Attributes { get; set; }
    public List<string> Constraints { get; set; } = new();
}
