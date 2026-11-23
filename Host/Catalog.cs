using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Gkms.Sdk;

namespace Gkms.Host;

public sealed record Manifest(string Id, string Name, int ApiVersion, string Assembly, string EntryPoint)
{
    public string? ResourceUpdater { get; init; }
    public string? Author { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? Punchline { get; init; }
    public string? Category { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public Dictionary<string, string>? Names { get; init; }
    public bool CanReloadResources { get; init; }
    public bool RequiresRestart { get; init; }
    public bool? CanHotEnable { get; init; }
    public bool? CanHotDisable { get; init; }
    public bool? CanReloadSettings { get; init; }
    [JsonIgnore] public bool SupportsHotEnable => CanHotEnable ?? !RequiresRestart;
    [JsonIgnore] public bool SupportsHotDisable => CanHotDisable ?? !RequiresRestart;
    [JsonIgnore] public bool SupportsSettingsReload => CanReloadSettings ?? (SupportsHotEnable && SupportsHotDisable);
    public string? SettingsUi { get; init; }
    [JsonIgnore] public bool HasSettingsUi => Settings.Length > 0 || !string.IsNullOrWhiteSpace(SettingsUi);
    public string DisplayName(string language) => Names?.GetValueOrDefault(language) ?? Name;
    public PluginSetting[] Settings { get; init; } = Array.Empty<PluginSetting>();
    public PluginPreset[] Presets { get; init; } = Array.Empty<PluginPreset>();
}
public sealed record Package(string Directory, Manifest Manifest);
public sealed record Profile(string[] Enabled, Dictionary<string, JsonElement>? Settings = null)
{
    public static Profile Empty => new(Array.Empty<string>());
}

public static class Catalog
{
    public const int ApiVersion = PluginApi.Current;
    public static bool Supports(int version) => PluginApi.Supports(version);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex IdPattern = new("^[a-z](?:[a-z0-9.-]*[a-z0-9-])?$", RegexOptions.CultureInvariant);

    public static Profile ReadProfile(string file) => ReadProfile(file, null);

    public static Profile ReadProfile(string file, IReadOnlyDictionary<string, Package>? catalog)
    {
        if (!File.Exists(file)) return Profile.Empty;
        var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), Json);
        if (p?.Enabled == null || p.Enabled.Any(id => id == null || !IdPattern.IsMatch(id)))
            throw new InvalidDataException("enabled 必须是插件 ID 数组。");
        return PackageUpgrade.Convert(p, catalog);
    }

    public static Manifest ReadManifest(string path) => ParseManifest(File.ReadAllText(path));
    public static Manifest ParseManifest(string json)
    {
        var m = JsonSerializer.Deserialize<Manifest>(json, Json);
        if (m == null || string.IsNullOrWhiteSpace(m.Id) || !IdPattern.IsMatch(m.Id) ||
            string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.EntryPoint) ||
            string.IsNullOrWhiteSpace(m.Assembly) || Path.GetFileName(m.Assembly) != m.Assembly ||
            m.Assembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件清单缺少有效的 ID、名称、程序集或入口。");
        if (!Supports(m.ApiVersion)) throw new InvalidDataException($"仅支持当前 API {ApiVersion}。");
        if (string.IsNullOrWhiteSpace(m.Author) || string.IsNullOrWhiteSpace(m.Description) ||
            m.Version == null || !Regex.IsMatch(m.Version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"))
            throw new InvalidDataException("API 2 清单需要 author、description 与 version（例如 1.0.0）。");
        if (m.Settings == null || m.Presets == null) throw new InvalidDataException("settings 与 presets 不能为 null。");
        PluginSettings.ValidateDefinition(m.Settings, m.Presets);
        return m;
    }

    public static IReadOnlyDictionary<string, Package> Discover(string directory, Action<string> log)
    {
        var packages = new Dictionary<string, Package>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        if (!System.IO.Directory.Exists(directory)) return packages;
        foreach (var folder in System.IO.Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            var path = Path.Combine(folder, "plugin.json");
            if (!File.Exists(path)) continue;
            try
            {
                var m = ReadManifest(path);
                if (!packages.TryAdd(m.Id, new(folder, m))) duplicates.Add(m.Id);
            }
            catch (Exception error) { log($"{path}: {error.Message}"); }
        }
        foreach (var id in duplicates)
        {
            packages.Remove(id);
            log($"{id}: 插件 ID 重复，已跳过所有同名包。");
        }
        foreach (var old in packages.Values.Where(p => PackageUpgrade.IsRetired(p.Manifest, packages)).ToArray())
            packages.Remove(old.Manifest.Id);
        return packages;
    }
}
