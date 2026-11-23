using System.Reflection;
using System.Text.Json;

namespace Gkms.Sdk;

public static partial class Interop
{
    public const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static object? Get(object instance, string name)
    {
        for (var type = TypeOf(instance); type != null; type = type.BaseType)
        {
            if (type.GetProperty(name, Members | BindingFlags.DeclaredOnly) is { } property) return property.GetValue(instance);
            if (type.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(instance);
        }
        throw new MissingMemberException(instance.GetType().FullName, name);
    }
    public static void Set(object instance, string name, object? value)
    {
        for (var type = TypeOf(instance); type != null; type = type.BaseType)
        {
            if (type.GetProperty(name, Members | BindingFlags.DeclaredOnly) is { CanWrite: true } property)
            { property.SetValue(instance, ConvertValue(property.PropertyType, value)); return; }
            if (type.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field)
            { field.SetValue(instance, ConvertValue(field.FieldType, value)); return; }
        }
        throw new MissingMemberException(instance.GetType().FullName, name);
    }
    public static object? ConvertValue(Type type, object? value) => value == null || type.IsInstanceOfType(value) ? value :
        type.IsEnum ? Enum.ToObject(type, value) : Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);

    public static object? Call(object instance, string name, params object?[] arguments)
    {
        var method = TypeOf(instance).GetMethods(Members).FirstOrDefault(m => m.Name == name && !m.ContainsGenericParameters &&
            m.GetParameters().Length == arguments.Length && m.GetParameters().Select((p, i) => arguments[i] == null ||
                p.ParameterType.IsInstanceOfType(arguments[i]) || p.ParameterType.IsEnum && arguments[i] is int).All(x => x))
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        return method.Invoke(instance, method.GetParameters().Select((p, i) => ConvertValue(p.ParameterType, arguments[i])).ToArray());
    }
    public static IEnumerable<object> Items(object? collection)
    {
        if (collection == null) yield break;
        var type = TypeOf(collection);
        var count = type.GetProperty("Count") ?? type.GetProperty("Length");
        var index = type.GetProperty("Item", new[] { typeof(int) });
        if (count != null && index != null)
        {
            for (var i = 0; i < Convert.ToInt32(count.GetValue(collection)); i++)
                if (index.GetValue(collection, new object[] { i }) is { } item) yield return item;
        }
        else if (collection is System.Collections.IEnumerable values)
            foreach (var item in values) if (item != null) yield return item;
    }
    public static Type RequireType(this IPluginContext context, string assembly, string name) =>
        context.FindType(assembly, name) ?? throw new TypeLoadException($"{assembly}: {name}");

    // DMM Hook.cpp MessageExtensions_MergeFrom: translate/observe only after the
    // complete IMessage has been read, never after a nested or partial parse method.
    public static MethodInfo RequireMessageReader(this IPluginContext context) =>
        context.RequireType("Google.Protobuf", "Google.Protobuf.MessageExtensions").GetMethods(Members)
            .Single(m => m.Name == "MergeFrom" && m.IsStatic && m.GetParameters() is
                [{ ParameterType.FullName: "Google.Protobuf.IMessage" }, { ParameterType: var span },
                 { ParameterType: var flag }, { ParameterType.FullName: "Google.Protobuf.ExtensionRegistry" }]
                && span.Namespace == "System" && span.Name.StartsWith("ReadOnlySpan", StringComparison.Ordinal) && flag == typeof(bool));

    public static MethodInfo Require(this IPluginContext context, string assembly, string type, string name, params string[] parameters) =>
        context.Find(assembly, type, name, parameters) ?? throw new MissingMethodException(type, name);

    public static Dictionary<string, string> ReadDictionary(string file)
    {
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ??
            throw new InvalidDataException(file);
        if (entries.Any(p => p.Value == null)) throw new InvalidDataException("词条译文必须为字符串: " + file);
        return entries;
    }

    public static bool IsByteArray(Type type) => type == typeof(byte[]) || type.IsArray && type.GetElementType() == typeof(byte);

    public static object WrapBytes(Type type, byte[] data) => data;
    public static byte[] ReadBytes(object? value) => value switch
    {
        byte[] bytes => bytes.ToArray(),
        INativeByteBuffer native => native.CopyBytes(),
        _ => Items(value).Select(Convert.ToByte).ToArray()
    };

    // Both native runtime methods and managed test fixtures expose strings as System.String.
    public static object? ConvertString(Type type, string value)
    {
        if (type == typeof(string)) return value;
        throw new InvalidCastException(type.FullName);
    }

    public static string? LocalFile(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains(':')) return null;
        var path = Path.GetFullPath(Path.Combine(root, name));
        return Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] == ".."
            ? null : path;
    }
}
