using System.Text.Json;

namespace Gkms.Sdk;

/// <summary>Separate settings entry, instantiated in the in-game panel without starting the game plugin.</summary>
public interface IPluginSettingsUi : IDisposable
{
    void Initialize(ISettingsUiContext context);
}

/// <summary>Callbacks run on the UI thread with the manager's current ImGui context.</summary>
public interface IPluginUiBuilder
{
    event Action Draw;
    event Action OpenConfigUi;
    void Enqueue(Action action);
}

public interface ISettingsUiContext
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Language { get; }
    JsonElement Settings { get; }
    IReadOnlyList<PluginSetting> Fields { get; }
    IReadOnlyList<PluginPreset> Presets { get; }
    IPluginUiBuilder UiBuilder { get; }
    CancellationToken Stopping { get; }
    bool CanReloadResources { get; }
    void ReloadSettings();
    /// <summary>Merge provided top-level keys into this plugin's latest settings and notify the runtime.</summary>
    void SaveSettings(JsonElement values);
    /// <summary>Explicitly replace this plugin's settings, for custom UIs that need to remove keys.</summary>
    void ReplaceSettings(JsonElement values);
    Task<string> ReloadResourcesAsync();
    T Own<T>(T resource) where T : IDisposable;
    void Log(string message);
}

public static class SettingsUiExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    public static T ReadSettings<T>(this ISettingsUiContext context) where T : class, new() =>
        context.Settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? new T() :
        context.Settings.Deserialize<T>(Json) ?? throw new InvalidDataException("配置必须为 JSON 对象。");
    public static void SaveSettings<T>(this ISettingsUiContext context, T values) =>
        context.SaveSettings(JsonSerializer.SerializeToElement(values, Json));
}
