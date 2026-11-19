using System.Reflection;
using System.Text.Json;

namespace Gkms.Sdk;

public static class Interop
{
    public static Type RequireType(this IPluginContext context, string assembly, string name) =>
        context.FindType(assembly, name) ?? throw new TypeLoadException($"{assembly}: {name}");

    public static MethodInfo Require(this IPluginContext context, string assembly, string type, string name, params string[] parameters) =>
        context.Find(assembly, type, name, parameters) ?? throw new MissingMethodException(type, name);

    public static Dictionary<string, string> ReadDictionary(string file) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ??
        throw new InvalidDataException(file);

    public static bool IsByteArray(Type type) => type == typeof(byte[]) || type.IsGenericType &&
        type.GetGenericTypeDefinition().FullName == "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1" &&
        type.GetGenericArguments()[0] == typeof(byte);

    public static object WrapBytes(Type type, byte[] data) =>
        type == typeof(byte[]) ? data : Activator.CreateInstance(type, new object[] { data })!;

    // Interop-generated generic delegates use Il2CppSystem.String even when ordinary setters take System.String.
    public static object? ConvertString(Type type, string value)
    {
        if (type == typeof(string)) return value;
        var convert = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == type &&
                m.GetParameters() is [{ ParameterType: var input }] && input == typeof(string));
        return convert?.Invoke(null, new object[] { value }) ?? throw new InvalidCastException(type.FullName);
    }

    public static string? LocalFile(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains(':')) return null;
        var path = Path.GetFullPath(Path.Combine(root, name));
        return Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] == ".."
            ? null : path;
    }
}
