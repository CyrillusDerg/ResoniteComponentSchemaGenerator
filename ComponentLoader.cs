using System.Reflection;
using System.Runtime.InteropServices;

namespace ComponentAnalyzer;

/// <summary>
/// Specifies which assemblies to load components from.
/// </summary>
[Flags]
public enum AssemblySource
{
    /// <summary>Load components from FrooxEngine.dll only.</summary>
    FrooxEngine = 1,

    /// <summary>Load ProtoFlux nodes from ProtoFluxBindings.dll.</summary>
    ProtoFluxBindings = 2,

    /// <summary>Load from all supported assemblies.</summary>
    All = FrooxEngine | ProtoFluxBindings
}

public class ComponentLoader : IDisposable
{
    private readonly MetadataLoadContext _mlc;
    private readonly Assembly _primaryAssembly;
    private readonly List<Assembly> _sourceAssemblies;
    private readonly Type _componentType;
    private readonly List<Type> _derivedTypes;
    private readonly string _resoniteDirectory;

    public IReadOnlyList<Type> DerivedTypes => _derivedTypes;
    public Type ComponentType => _componentType;
    public string ResoniteDirectory => _resoniteDirectory;

    private ComponentLoader(
        MetadataLoadContext mlc,
        Assembly primaryAssembly,
        List<Assembly> sourceAssemblies,
        Type componentType,
        List<Type> derivedTypes,
        string resoniteDirectory)
    {
        _mlc = mlc;
        _primaryAssembly = primaryAssembly;
        _sourceAssemblies = sourceAssemblies;
        _componentType = componentType;
        _derivedTypes = derivedTypes;
        _resoniteDirectory = resoniteDirectory;
    }

    /// <summary>
    /// Loads components from the specified Resonite installation directory.
    /// </summary>
    /// <param name="resoniteDirectory">Path to the Resonite installation directory containing FrooxEngine.dll.</param>
    /// <param name="sources">Which assemblies to load components from.</param>
    public static ComponentLoader Load(string resoniteDirectory, AssemblySource sources = AssemblySource.All)
    {
        // Support both directory path and direct DLL path for backwards compatibility
        string directory;
        if (File.Exists(resoniteDirectory) && resoniteDirectory.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(resoniteDirectory)) ?? ".";
        }
        else if (Directory.Exists(resoniteDirectory))
        {
            directory = Path.GetFullPath(resoniteDirectory);
        }
        else
        {
            throw new DirectoryNotFoundException($"Directory not found: {resoniteDirectory}");
        }

        string frooxEnginePath = Path.Combine(directory, "FrooxEngine.dll");
        if (!File.Exists(frooxEnginePath))
        {
            throw new FileNotFoundException($"FrooxEngine.dll not found in: {directory}");
        }

        // Get runtime assemblies for resolving base types
        string[] runtimeAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");

        // Get all DLLs in the Resonite directory
        string[] localAssemblies = Directory.GetFiles(directory, "*.dll");

        // Combine all assembly paths
        var allPaths = runtimeAssemblies.Concat(localAssemblies).Distinct().ToList();

        var resolver = new PathAssemblyResolver(allPaths);
        var mlc = new MetadataLoadContext(resolver);

        try
        {
            // Always load FrooxEngine.dll as the primary assembly (contains Component base type)
            Assembly primaryAssembly = mlc.LoadFromAssemblyPath(frooxEnginePath);

            // Find the Component base class
            Type? componentType = primaryAssembly.GetType("FrooxEngine.Component");
            if (componentType == null)
            {
                mlc.Dispose();
                throw new InvalidOperationException("Could not find FrooxEngine.Component type in the assembly.");
            }

            // Determine which assemblies to scan for components
            var sourceAssemblies = new List<Assembly>();
            if (sources.HasFlag(AssemblySource.FrooxEngine))
            {
                sourceAssemblies.Add(primaryAssembly);
            }
            if (sources.HasFlag(AssemblySource.ProtoFluxBindings))
            {
                string protoFluxBindingsPath = Path.Combine(directory, "ProtoFluxBindings.dll");
                if (File.Exists(protoFluxBindingsPath))
                {
                    var protoFluxBindings = mlc.LoadFromAssemblyPath(protoFluxBindingsPath);
                    sourceAssemblies.Add(protoFluxBindings);
                }
            }

            // Find all types that derive from Component across all source assemblies
            var derivedTypes = new List<Type>();

            foreach (var assembly in sourceAssemblies)
            {
                Type[] types = GetLoadableTypes(assembly);

                foreach (Type type in types)
                {
                    try
                    {
                        if (type != componentType && IsAssignableFrom(componentType, type))
                        {
                            derivedTypes.Add(type);
                        }
                    }
                    catch
                    {
                        // Skip types that can't be checked
                    }
                }
            }

            // Sort by full name for consistent output
            derivedTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));

            return new ComponentLoader(mlc, primaryAssembly, sourceAssemblies, componentType, derivedTypes, directory);
        }
        catch
        {
            mlc.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Checks if a type is a ProtoFlux node (derives from ProtoFluxNode).
    /// </summary>
    public bool IsProtoFluxNode(Type type)
    {
        try
        {
            Type? current = type.BaseType;
            while (current != null)
            {
                if (current.FullName == "FrooxEngine.ProtoFlux.ProtoFluxNode")
                    return true;
                current = current.BaseType;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets only regular components (not ProtoFlux nodes).
    /// </summary>
    public IEnumerable<Type> GetComponents() => _derivedTypes.Where(t => !IsProtoFluxNode(t));

    /// <summary>
    /// Gets only ProtoFlux nodes.
    /// </summary>
    public IEnumerable<Type> GetProtoFluxNodes() => _derivedTypes.Where(t => IsProtoFluxNode(t));

    public Type? FindComponent(string className)
    {
        // Normalize the input to handle alternative generic syntax
        // e.g., "ValueField<1>" or "ValueField[1]" -> "ValueField`1"
        string normalizedName = NormalizeGenericName(className);

        // Try exact match on full name first
        Type? targetType = _derivedTypes.FirstOrDefault(t =>
            t.FullName != null && t.FullName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

        if (targetType != null)
            return targetType;

        // Try exact match on short name
        targetType = _derivedTypes.FirstOrDefault(t =>
            t.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

        if (targetType != null)
            return targetType;

        // Try partial match
        var matches = _derivedTypes.Where(t =>
            t.FullName != null &&
            t.FullName.Contains(normalizedName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 1)
            return matches[0];

        return null;
    }

    private static string NormalizeGenericName(string name)
    {
        // Convert alternative generic notations to the CLR backtick format
        // "ValueField<1>" -> "ValueField`1"
        // "ValueField[1]" -> "ValueField`1"
        // "ValueField<T>" -> "ValueField`1" (single type param)
        // "Dictionary<K,V>" -> "Dictionary`2" (multiple type params)

        // Handle angle bracket notation: TypeName<1> or TypeName<T> or TypeName<T,U>
        var angleBracketMatch = System.Text.RegularExpressions.Regex.Match(
            name, @"^(.+)<(\d+|[^>]+)>$");
        if (angleBracketMatch.Success)
        {
            string baseName = angleBracketMatch.Groups[1].Value;
            string content = angleBracketMatch.Groups[2].Value;

            // If it's already a number, use it directly
            if (int.TryParse(content, out int arity))
            {
                return $"{baseName}`{arity}";
            }

            // Otherwise count type parameters (comma-separated)
            int typeParamCount = content.Split(',').Length;
            return $"{baseName}`{typeParamCount}";
        }

        // Handle square bracket notation: TypeName[1]
        var squareBracketMatch = System.Text.RegularExpressions.Regex.Match(
            name, @"^(.+)\[(\d+)\]$");
        if (squareBracketMatch.Success)
        {
            string baseName = squareBracketMatch.Groups[1].Value;
            string arity = squareBracketMatch.Groups[2].Value;
            return $"{baseName}`{arity}";
        }

        return name;
    }

    public List<Type> FindComponents(string pattern)
    {
        return _derivedTypes.Where(t =>
            t.FullName != null &&
            t.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public List<string> GetInheritanceChain(Type type)
    {
        var chain = new List<string>();
        Type? current = type;

        while (current != null)
        {
            chain.Add(current.FullName ?? current.Name);
            if (current.FullName == _componentType.FullName)
                break;
            try
            {
                current = current.BaseType;
            }
            catch
            {
                break;
            }
        }

        return chain;
    }

    /// <summary>
    /// Looks up a type by its full name in the loaded assemblies or their references.
    /// </summary>
    public Type? FindTypeByFullName(string fullName)
    {
        // Try in the primary assembly first
        var type = _primaryAssembly.GetType(fullName);
        if (type != null) return type;

        // Try in other source assemblies
        foreach (var assembly in _sourceAssemblies)
        {
            if (assembly == _primaryAssembly) continue;
            type = assembly.GetType(fullName);
            if (type != null) return type;
        }

        // Try in referenced assemblies
        foreach (var assemblyName in _primaryAssembly.GetReferencedAssemblies())
        {
            try
            {
                var refAssembly = _mlc.LoadFromAssemblyName(assemblyName);
                type = refAssembly.GetType(fullName);
                if (type != null) return type;
            }
            catch { }
        }

        return null;
    }

    public void Dispose()
    {
        _mlc.Dispose();
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

    private static bool IsAssignableFrom(Type baseType, Type derivedType)
    {
        try
        {
            Type? current = derivedType.BaseType;

            while (current != null)
            {
                if (current.FullName == baseType.FullName)
                {
                    return true;
                }
                current = current.BaseType;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
