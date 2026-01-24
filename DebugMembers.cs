using System.Reflection;

namespace ComponentAnalyzer;

public static class DebugMembers
{
    public static void PrintAllMembers(Type type)
    {
        Console.WriteLine($"=== All members of {type.Name} ===");
        Console.WriteLine();

        Console.WriteLine("Fields (DeclaredOnly):");
        try
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Console.WriteLine($"  {field.Name}: {field.FieldType.Name} [{(field.IsPublic ? "public" : "private")}]");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("All Public Fields (Including Inherited):");
        try
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Console.WriteLine($"  {field.Name}: {field.FieldType.Name} [from {field.DeclaringType?.Name}]");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("All Public Properties (Including Inherited):");
        try
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var getter = prop.GetGetMethod(true);
                var setter = prop.GetSetMethod(true);
                string accessors = "";
                if (getter != null) accessors += "get; ";
                if (setter != null) accessors += "set; ";
                Console.WriteLine($"  {prop.Name}: {prop.PropertyType.Name} {{ {accessors}}} [from {prop.DeclaringType?.Name}]");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }

        Console.WriteLine();
    }
}
