using System.Reflection;
using System.Text.Json;

namespace Gkms.Sdk;

public interface ILocalifyPlugin : IDisposable
{
    void Start(IPluginContext context);
}

public interface IPluginContext
{
    string DataDirectory { get; }
    JsonElement Settings { get; }
    void Log(string message);
    Type? FindType(string assembly, string fullName);
    void Patch(MethodInfo target, MethodInfo? prefix = null, MethodInfo? postfix = null, int priority = 400);
}

public interface IPluginScope : IPluginContext, IDisposable
{
    IPluginScope OpenScope();
}

public static class GameMethods
{
    public const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static MethodInfo? Find(this IPluginContext context, string assembly, string type,
        string name, params string[] parameters)
    {
        return context.FindType(assembly, type)?.GetMethods(Declared).SingleOrDefault(method =>
            method.Name == name && !method.ContainsGenericParameters &&
            method.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
    }

    public static MethodInfo Handler<T>(string name) =>
        typeof(T).GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        ?? throw new MissingMethodException(typeof(T).FullName, name);

    public static T Option<T>(this IPluginContext context, string key, T fallback)
    {
        if (context.Settings.ValueKind != JsonValueKind.Object || !context.Settings.TryGetProperty(key, out var value))
            return fallback;
        return value.Deserialize<T>() ?? throw new InvalidDataException($"Invalid option {key}");
    }
}
