using System.Text.Json;

namespace Gkms.Sdk;

/// <summary>Version of the SDK contract declared in plugin.json, independent of plugin versions.</summary>
public static class PluginApi
{
    public const int Current = 2;
    public static bool Supports(int version) => version == Current;
}

/// <summary>Host-owned services guaranteed to API 2 plugins, including child scopes.</summary>
public interface IPluginServices : IPluginScope, IPluginRuntime, IAdvancedPluginContext
{
    string Id { get; }
    string PackageDirectory { get; }
    /// <summary>Private writable cache/data directory, created lazily. Downloaded packs may use PackageDirectory/resources.</summary>
    string StorageDirectory { get; }
    /// <summary>Own a subscription or other resource. Released in reverse order on stop or startup failure.</summary>
    T Own<T>(T resource) where T : IDisposable;
}

public static class PluginServices
{
    public static IPluginServices Services(this IPluginContext context) => context as IPluginServices
        ?? throw new NotSupportedException("此功能需要提供 API 2 服务的宿主。");

    /// <summary>Deserialize the plugin's settings snapshot. Property initializers supply omitted defaults.</summary>
    public static T ReadSettings<T>(this IPluginContext context) where T : class, new() =>
        context.Settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? new T() :
        context.Settings.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException("插件配置必须是 JSON 对象。");
}
