using System.Reflection;
using System.Runtime.InteropServices;

namespace ComponentAnalyzer;

public class ComponentLoader : IDisposable
{
    private readonly MetadataLoadContext _mlc;
    private readonly Assembly _assembly;
    private readonly Type _componentType;
    private readonly List<Type> _derivedTypes;

    public IReadOnlyList<Type> DerivedTypes => _derivedTypes;
    public Type ComponentType => _componentType;

    private ComponentLoader(MetadataLoadContext mlc, Assembly assembly, Type componentType, List<Type> derivedTypes)
    {
        _mlc = mlc;
        _assembly = assembly;
        _componentType = componentType;
        _derivedTypes = derivedTypes;
    }

    public static ComponentLoader Load(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"File not found: {dllPath}");
        }

        string dllDirectory = Path.GetDirectoryName(Path.GetFullPath(dllPath)) ?? ".";

        // Get runtime assemblies for resolving base types
        string[] runtimeAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");

        // Get all DLLs in the same directory as the target DLL
        string[] localAssemblies = Directory.GetFiles(dllDirectory, "*.dll");

        // Combine all assembly paths
        var allPaths = runtimeAssemblies.Concat(localAssemblies).Distinct().ToList();

        var resolver = new PathAssemblyResolver(allPaths);
        var mlc = new MetadataLoadContext(resolver);

        try
        {
            Assembly assembly = mlc.LoadFromAssemblyPath(Path.GetFullPath(dllPath));

            // Find the Component base class
            Type? componentType = assembly.GetType("FrooxEngine.Component");

            if (componentType == null)
            {
                mlc.Dispose();
                throw new InvalidOperationException("Could not find FrooxEngine.Component type in the assembly.");
            }

            // Get all types, handling loading errors gracefully
            Type[] types = GetLoadableTypes(assembly);

            // Find all types that derive from Component
            var derivedTypes = new List<Type>();

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

            // Sort by full name for consistent output
            derivedTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));

            return new ComponentLoader(mlc, assembly, componentType, derivedTypes);
        }
        catch
        {
            mlc.Dispose();
            throw;
        }
    }

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
    /// Looks up a type by its full name in the loaded assembly or its references.
    /// </summary>
    public Type? FindTypeByFullName(string fullName)
    {
        // Try in the main assembly first
        var type = _assembly.GetType(fullName);
        if (type != null) return type;

        // Try in referenced assemblies
        foreach (var assemblyName in _assembly.GetReferencedAssemblies())
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
