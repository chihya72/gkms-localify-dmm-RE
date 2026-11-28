using System.Reflection;
using System.Text.Json;

namespace Gkms.Sdk;

/// <summary>Construct without side effects. Start once per instance; Dispose also runs after a failed Start.</summary>
public interface ILocalifyPlugin : IDisposable
{
    void Start(IPluginContext context);
}

// Invoked by the manager without starting the game or registering any callbacks.
public interface IResourceUpdater
{
    Task<int> UpdateAsync(string dataDirectory, string? source);
}

public sealed record ResourceRelease(string Source, string? Download, string? Version, string? InstalledVersion, string Description)
{
    public bool IsCurrent => !string.IsNullOrEmpty(Version) && Version == InstalledVersion;
}
public sealed record ResourceProgress(long Received, long? Total);
public interface IResourceUpdateService : IResourceUpdater
{
    Task<ResourceRelease> CheckAsync(string dataDirectory, string? source, CancellationToken cancellationToken = default);
    Task<int> InstallAsync(string dataDirectory, ResourceRelease release, IProgress<ResourceProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
public interface IResourceReloadable { void ReloadResources(); }
// Optional in-place settings reload. Throw without changing the current working
// settings when validation/application fails. The host retains the old settings.
public interface ISettingsReloadable { void ReloadSettings(JsonElement settings); }
// Optional diagnostics; existing API 2 plugins remain compatible.
public interface IPluginDiagnostics { IReadOnlyList<string> Warnings { get; } }

// Optional API 2 extension. After ordinary disable has removed all feature hooks,
// the host may run bounded, frame-polled cleanup on the Unity thread. Re-enabling
// the same plugin or shutting down the host cancels it before any new hooks run.
public interface IPluginStopCleanup { IPluginCleanup? CreateStopCleanup(); }
public interface IPluginCleanup : ILocalifyPlugin
{
    /// <returns>True when cleanup is complete. Throw to report failed restoration.</returns>
    bool Update();
}

// Runtime and advanced hook services are provided by the current plugin scope.
public interface IPluginRuntime
{
    CancellationToken Stopping { get; }
    void Enqueue(Action action);
}
public interface IAdvancedPluginContext
{
    void PatchAdvanced(MethodBase target, MethodInfo? prefix = null, MethodInfo? postfix = null,
        MethodInfo? finalizer = null, int priority = 400);
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
    public static void PatchAdvanced(this IPluginContext context, MethodBase target, MethodInfo? prefix = null,
        MethodInfo? postfix = null, MethodInfo? finalizer = null, int priority = 400) =>
        (context as IAdvancedPluginContext ?? throw new NotSupportedException("宿主未提供高级回调服务。"))
            .PatchAdvanced(target, prefix, postfix, finalizer, priority);
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
        => context.Settings.Option(key, fallback);

    public static T Option<T>(this JsonElement settings, string key, T fallback)
    {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty(key, out var value))
            return fallback;
        return value.Deserialize<T>() ?? throw new InvalidDataException($"Invalid option {key}");
    }
}
