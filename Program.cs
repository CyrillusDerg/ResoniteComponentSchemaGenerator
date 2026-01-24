using ComponentAnalyzer;

if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

// Handle validation mode separately (doesn't require Resonite path)
if (args[0] == "-v" || args[0] == "--validate")
{
    return HandleValidation(args);
}

string resonitePath = args[0];
string? filterPattern = null;
string? propsForClass = null;
string? debugClass = null;
string? schemaClass = null;
string outputDir = ".";
bool generateAllSchemas = false;
bool generateCommonSchema = false;
AssemblySource sources = AssemblySource.All;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-l":
        case "--list":
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                filterPattern = args[++i];
            }
            else
            {
                filterPattern = "";
            }
            break;
        case "-p":
        case "--props":
            if (i + 1 < args.Length)
            {
                propsForClass = args[++i];
            }
            else
            {
                Console.WriteLine("Error: --props requires a class name argument");
                return 1;
            }
            break;
        case "-s":
        case "--schema":
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                schemaClass = args[++i];
            }
            else
            {
                generateAllSchemas = true;
            }
            break;
        case "-o":
        case "--output":
            if (i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else
            {
                Console.WriteLine("Error: --output requires a directory path argument");
                return 1;
            }
            break;
        case "-d":
        case "--debug":
            if (i + 1 < args.Length)
            {
                debugClass = args[++i];
            }
            else
            {
                Console.WriteLine("Error: --debug requires a class name argument");
                return 1;
            }
            break;
        case "-c":
        case "--common":
            generateCommonSchema = true;
            break;
        case "--components-only":
            sources = AssemblySource.FrooxEngine;
            break;
        case "--protoflux-only":
            sources = AssemblySource.ProtoFluxBindings;
            break;
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
        default:
            Console.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

try
{
    using var loader = ComponentLoader.Load(resonitePath, sources);
    using var genericResolver = GenericTypeResolver.TryCreate(resonitePath, sources);

    if (debugClass != null)
    {
        var targetType = loader.FindComponent(debugClass);
        if (targetType == null)
        {
            Console.WriteLine($"Error: No component found matching '{debugClass}'");
            return 1;
        }
        DebugMembers.PrintAllMembers(targetType);

        // If it's a generic type, show constraint debug info
        if (targetType.IsGenericTypeDefinition && genericResolver != null)
        {
            Console.WriteLine();
            Console.WriteLine("=== Generic Type Debug Info ===");
            var debugInfo = genericResolver.GetDebugInfo(targetType);
            if (debugInfo != null)
            {
                Console.WriteLine($"Type: {debugInfo.TypeName}");
                Console.WriteLine($"Has [GenericTypes] attribute: {debugInfo.HasGenericTypesAttribute}");

                if (debugInfo.Error != null)
                {
                    Console.WriteLine($"Error: {debugInfo.Error}");
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Generic Parameters:");
                    foreach (var param in debugInfo.GenericParameters)
                    {
                        Console.WriteLine($"  {param.Name}:");
                        Console.WriteLine($"    Attributes: {param.Attributes}");
                        Console.WriteLine($"    Constraints ({param.Constraints.Count}):");
                        foreach (var constraint in param.Constraints)
                        {
                            Console.WriteLine($"      {constraint}");
                        }
                    }

                    Console.WriteLine();
                    if (debugInfo.AllowedTypeNames != null && debugInfo.AllowedTypeNames.Length > 0)
                    {
                        Console.WriteLine($"Allowed Types ({debugInfo.AllowedTypeNames.Length}):");
                        foreach (var typeName in debugInfo.AllowedTypeNames.Take(20))
                        {
                            Console.WriteLine($"  {typeName}");
                        }
                        if (debugInfo.AllowedTypeNames.Length > 20)
                        {
                            Console.WriteLine($"  ... and {debugInfo.AllowedTypeNames.Length - 20} more");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Allowed Types: (none found or no constraints)");
                    }
                }
            }
            else
            {
                Console.WriteLine("Could not get debug info for generic type");
            }
        }
        return 0;
    }

    if (generateAllSchemas || schemaClass != null || generateCommonSchema)
    {
        // -c is implied when -s is used
        bool useCommonSchema = generateCommonSchema || generateAllSchemas || schemaClass != null;
        return GenerateSchemas(loader, genericResolver, schemaClass, outputDir, useCommonSchema, sources);
    }

    if (propsForClass != null)
    {
        return ShowProperties(loader, genericResolver, propsForClass);
    }

    return ListComponents(loader, filterPattern);
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int GenerateSchemas(ComponentLoader loader, GenericTypeResolver? genericResolver, string? className, string outputDir, bool useExternalCommonSchema, AssemblySource sources)
{
    // Ensure output directory exists
    if (!Directory.Exists(outputDir))
    {
        Directory.CreateDirectory(outputDir);
    }

    var generator = new JsonSchemaGenerator(loader, genericResolver);
    generator.UseExternalCommonSchema = useExternalCommonSchema;

    int successCount = 0;
    int errorCount = 0;

    // Generate common schema if requested
    if (useExternalCommonSchema)
    {
        try
        {
            var commonSchema = generator.GenerateCommonSchema();
            string commonJson = generator.SerializeSchema(commonSchema);
            string commonFilePath = Path.Combine(outputDir, generator.CommonSchemaFileName);
            File.WriteAllText(commonFilePath, commonJson);
            Console.WriteLine($"Created: {generator.CommonSchemaFileName}");
            successCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating common schema: {ex.Message}");
            errorCount++;
        }
    }

    // Special case: --protoflux-only with -s generates a single combined schema
    if (sources == AssemblySource.ProtoFluxBindings && className == null)
    {
        Console.WriteLine("Generating combined ProtoFlux schema...");
        Console.WriteLine($"Output directory: {Path.GetFullPath(outputDir)}");
        Console.WriteLine();

        try
        {
            var protoFluxTypes = loader.GetProtoFluxNodes().Where(t => !t.IsAbstract);
            var combinedSchema = generator.GenerateProtoFluxCombinedSchema(protoFluxTypes);
            string json = generator.SerializeSchema(combinedSchema);

            string filePath = Path.Combine(outputDir, "protoflux.schema.json");
            File.WriteAllText(filePath, json);
            Console.WriteLine($"Created: protoflux.schema.json");
            successCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating combined ProtoFlux schema: {ex.Message}");
            errorCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"Generated {successCount} schema(s)");
        if (errorCount > 0)
        {
            Console.WriteLine($"Failed: {errorCount}");
        }
        return errorCount > 0 ? 1 : 0;
    }

    List<Type> typesToProcess;

    if (className != null)
    {
        var targetType = loader.FindComponent(className);
        if (targetType == null)
        {
            var matches = loader.FindComponents(className);
            if (matches.Count == 0)
            {
                Console.WriteLine($"Error: No component found matching '{className}'");
                return 1;
            }
            else if (matches.Count > 1)
            {
                Console.WriteLine($"Error: Multiple components match '{className}':");
                foreach (var m in matches.Take(10))
                {
                    Console.WriteLine($"  {m.FullName}");
                }
                if (matches.Count > 10)
                {
                    Console.WriteLine($"  ... and {matches.Count - 10} more");
                }
                return 1;
            }
            targetType = matches[0];
        }
        typesToProcess = [targetType];
    }
    else if (useExternalCommonSchema && className == null)
    {
        // If only -c was specified without -s, just generate common schema
        Console.WriteLine();
        Console.WriteLine($"Generated {successCount} schema(s)");
        if (errorCount > 0)
        {
            Console.WriteLine($"Failed: {errorCount}");
        }
        return errorCount > 0 ? 1 : 0;
    }
    else
    {
        // Generate for all non-abstract components
        typesToProcess = loader.DerivedTypes.Where(t => !t.IsAbstract).ToList();
    }

    Console.WriteLine($"Generating JSON schemas for {typesToProcess.Count} component(s)...");
    Console.WriteLine($"Output directory: {Path.GetFullPath(outputDir)}");
    Console.WriteLine();

    foreach (var type in typesToProcess)
    {
        try
        {
            var schema = generator.GenerateSchema(type);
            string json = generator.SerializeSchema(schema);

            string fileName = GetSafeFileName(type) + ".schema.json";
            string filePath = Path.Combine(outputDir, fileName);

            File.WriteAllText(filePath, json);
            successCount++;

            if (typesToProcess.Count <= 10)
            {
                Console.WriteLine($"  Created: {fileName}");
            }
        }
        catch (Exception ex)
        {
            errorCount++;
            Console.WriteLine($"  Error generating schema for {type.FullName}: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Generated {successCount} schema(s)");
    if (errorCount > 0)
    {
        Console.WriteLine($"Failed: {errorCount}");
    }

    return errorCount > 0 ? 1 : 0;
}

static string GetSafeFileName(Type type)
{
    string name = type.FullName ?? type.Name;
    // Replace characters that are invalid in file names
    foreach (char c in Path.GetInvalidFileNameChars())
    {
        name = name.Replace(c, '_');
    }
    // Also replace backtick for generic types
    name = name.Replace('`', '_');
    return name;
}

static int ListComponents(ComponentLoader loader, string? filterPattern)
{
    var types = loader.DerivedTypes.AsEnumerable();

    if (!string.IsNullOrEmpty(filterPattern))
    {
        types = loader.FindComponents(filterPattern);
    }

    var results = types.ToList();

    Console.WriteLine($"Found {results.Count} component(s):");
    Console.WriteLine();

    foreach (Type type in results)
    {
        string abstractMarker = type.IsAbstract ? " (abstract)" : "";
        Console.WriteLine($"  {type.FullName}{abstractMarker}");
    }

    return 0;
}

static int ShowProperties(ComponentLoader loader, GenericTypeResolver? genericResolver, string className)
{
    var targetType = loader.FindComponent(className);

    if (targetType == null)
    {
        var matches = loader.FindComponents(className);

        if (matches.Count == 0)
        {
            Console.WriteLine($"Error: No component found matching '{className}'");
            return 1;
        }
        else
        {
            Console.WriteLine($"Error: Multiple components match '{className}':");
            foreach (var m in matches.Take(10))
            {
                Console.WriteLine($"  {m.FullName}");
            }
            if (matches.Count > 10)
            {
                Console.WriteLine($"  ... and {matches.Count - 10} more");
            }
            return 1;
        }
    }

    Console.WriteLine($"Component: {targetType.FullName}");
    Console.WriteLine($"Abstract: {targetType.IsAbstract}");

    // Check for GenericTypes attribute on generic types
    if (targetType.IsGenericTypeDefinition && genericResolver != null)
    {
        var allowedTypes = genericResolver.GetAllowedTypesForGeneric(targetType);
        if (allowedTypes != null && allowedTypes.Length > 0)
        {
            Console.WriteLine($"Generic: Yes (with type constraints)");
            Console.WriteLine();
            Console.WriteLine($"Allowed types for T ({allowedTypes.Length}):");
            foreach (var t in allowedTypes.OrderBy(t => t.Name))
            {
                Console.WriteLine($"  {t.FullName}");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine("Inheritance chain:");
    var chain = loader.GetInheritanceChain(targetType);
    for (int i = 0; i < chain.Count; i++)
    {
        Console.WriteLine($"  {new string(' ', i * 2)}{chain[i]}");
    }

    Console.WriteLine();
    Console.WriteLine("Public fields:");
    Console.WriteLine();

    var fields = PropertyAnalyzer.GetPublicFields(targetType);

    if (fields.Count == 0)
    {
        Console.WriteLine("  (none)");
    }
    else
    {
        foreach (var field in fields.OrderBy(f => f.Name))
        {
            Console.WriteLine($"  {field.Name}: {field.FriendlyTypeName}");
        }
    }

    return 0;
}

static int HandleValidation(string[] args)
{
    string? jsonFile = null;
    string? schemaFile = null;
    string? schemaDir = null;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--schema":
                if (i + 1 < args.Length)
                {
                    schemaFile = args[++i];
                }
                else
                {
                    Console.WriteLine("Error: --schema requires a schema file path");
                    return 1;
                }
                break;
            case "--schema-dir":
                if (i + 1 < args.Length)
                {
                    schemaDir = args[++i];
                }
                else
                {
                    Console.WriteLine("Error: --schema-dir requires a directory path");
                    return 1;
                }
                break;
            case "-h":
            case "--help":
                PrintUsage();
                return 0;
            default:
                if (args[i].StartsWith('-'))
                {
                    Console.WriteLine($"Unknown option: {args[i]}");
                    return 1;
                }
                // Positional argument - assume it's the JSON file
                if (jsonFile == null)
                {
                    jsonFile = args[i];
                }
                else
                {
                    Console.WriteLine($"Unexpected argument: {args[i]}");
                    return 1;
                }
                break;
        }
    }

    if (jsonFile == null)
    {
        Console.WriteLine("Error: JSON file path is required");
        Console.WriteLine("Usage: ComponentAnalyzer -v <json-file> --schema <schema-file> [--schema-dir <dir>]");
        return 1;
    }

    if (schemaFile == null)
    {
        Console.WriteLine("Error: --schema is required for validation");
        Console.WriteLine("Usage: ComponentAnalyzer -v <json-file> --schema <schema-file> [--schema-dir <dir>]");
        return 1;
    }

    if (!File.Exists(jsonFile))
    {
        Console.WriteLine($"Error: JSON file not found: {jsonFile}");
        return 1;
    }

    if (!File.Exists(schemaFile))
    {
        Console.WriteLine($"Error: Schema file not found: {schemaFile}");
        return 1;
    }

    try
    {
        var validator = new SchemaValidator();
        var result = validator.ValidateWithCommonSchema(jsonFile, schemaFile, schemaDir);

        if (result.IsValid)
        {
            Console.WriteLine($"Valid: {Path.GetFileName(jsonFile)}");
            return 0;
        }
        else
        {
            Console.WriteLine($"Invalid: {Path.GetFileName(jsonFile)}");
            Console.WriteLine();
            Console.WriteLine("Errors:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  {error}");
            }
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during validation: {ex.Message}");
        return 1;
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage: ComponentAnalyzer <resonite-directory> [options]");
    Console.WriteLine("       ComponentAnalyzer -v <json-file> --schema <schema-file> [--schema-dir <dir>]");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  <resonite-directory>   Path to Resonite installation directory containing");
    Console.WriteLine("                         FrooxEngine.dll (or direct path to FrooxEngine.dll)");
    Console.WriteLine();
    Console.WriteLine("Schema Generation Options:");
    Console.WriteLine("  -l, --list [pattern]   List components, optionally filtered by pattern");
    Console.WriteLine("  -p, --props <class>    Show public fields of a component");
    Console.WriteLine("  -s, --schema [class]   Generate JSON schema (for specific class or all)");
    Console.WriteLine("                         Automatically generates common.schema.json with shared type defs");
    Console.WriteLine("  -c, --common           Generate only common.schema.json (no component schemas)");
    Console.WriteLine("  -o, --output <dir>     Output directory for schema files (default: current)");
    Console.WriteLine("  -d, --debug <class>    Show debug info (all members, generic constraints)");
    Console.WriteLine("  --components-only      Only load FrooxEngine components (exclude ProtoFlux)");
    Console.WriteLine("  --protoflux-only       Only load ProtoFlux nodes");
    Console.WriteLine();
    Console.WriteLine("Validation Options:");
    Console.WriteLine("  -v, --validate         Validate a JSON file against a schema");
    Console.WriteLine("      --schema <file>    Schema file to validate against (required with -v)");
    Console.WriteLine("      --schema-dir <dir> Directory containing schemas for $ref resolution");
    Console.WriteLine();
    Console.WriteLine("General Options:");
    Console.WriteLine("  -h, --help             Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite              # List all components and ProtoFlux nodes");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -l Audio     # List types containing 'Audio'");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -p AudioOutput");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -s AudioOutput");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -s -o ./schemas");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -s -o ./schemas --components-only");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -s -o ./schemas --protoflux-only");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -c -o ./schemas  # Generate common.schema.json only");
    Console.WriteLine("  ComponentAnalyzer /path/to/Resonite -d \"AssetLoader<1>\"");
    Console.WriteLine();
    Console.WriteLine("  # Validate a component JSON file against its schema");
    Console.WriteLine("  ComponentAnalyzer -v component.json --schema FrooxEngine.AudioOutput.schema.json");
    Console.WriteLine("  ComponentAnalyzer -v component.json --schema schema.json --schema-dir ./schemas");
}
