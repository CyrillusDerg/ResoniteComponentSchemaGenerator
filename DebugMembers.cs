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
        Console.WriteLine("Properties (DeclaredOnly):");
        try
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var getter = prop.GetGetMethod(true);
                var setter = prop.GetSetMethod(true);
                string accessors = "";
                if (getter != null) accessors += (getter.IsPublic ? "get; " : "private get; ");
                if (setter != null) accessors += (setter.IsPublic ? "set; " : "private set; ");
                Console.WriteLine($"  {prop.Name}: {prop.PropertyType.Name} {{ {accessors}}}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }

        Console.WriteLine();
    }
}
