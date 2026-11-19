using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gkms.Host;

public sealed record Manifest(string Id, string Name, int ApiVersion, string Assembly, string EntryPoint);
public sealed record Package(string Directory, Manifest Manifest);
public sealed record Profile(string[] Enabled, Dictionary<string, JsonElement>? Settings = null)
{
    public static Profile Empty => new(Array.Empty<string>());
}

public static class Catalog
{
    public const int ApiVersion = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9.-]*$", RegexOptions.CultureInvariant);

    public static Profile ReadProfile(string file)
    {
        if (!File.Exists(file)) return Profile.Empty;
        var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), Json);
        if (p?.Enabled == null || p.Enabled.Any(id => id == null || !IdPattern.IsMatch(id)))
            throw new InvalidDataException("enabled 必须是插件 ID 数组。");
        return p;
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
                var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Json);
                if (m == null || string.IsNullOrWhiteSpace(m.Id) || !IdPattern.IsMatch(m.Id) ||
                    string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.EntryPoint) ||
                    string.IsNullOrWhiteSpace(m.Assembly) || Path.GetFileName(m.Assembly) != m.Assembly ||
                    m.Assembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    !m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("插件清单缺少有效的 ID、名称、程序集或入口。");
                if (!packages.TryAdd(m.Id, new(folder, m))) duplicates.Add(m.Id);
            }
            catch (Exception error) { log($"{path}: {error.Message}"); }
        }
        foreach (var id in duplicates)
        {
            packages.Remove(id);
            log($"{id}: 插件 ID 重复，已跳过所有同名包。");
        }
        return packages;
    }
}
