using System.Text.Json;
using System.Text.Json.Nodes;
using Gkms.Host;
using Gkms.Sdk;

namespace Gkms.Manager;

internal static class ProfileEditor
{
    public static void Apply(string root, IReadOnlyDictionary<string, Package> catalog, IReadOnlyDictionary<string, bool> changes,
        IReadOnlyDictionary<string, JsonObject>? importedSettings = null, KeyValuePair<string, JsonObject>? replace = null, bool merge = false)
    {
        using var packageLock = PluginInstallation.AcquireLock(root);
        ApplyLocked(root, catalog, changes, importedSettings, replace, merge);
    }

    internal static void ApplyLocked(string root, IReadOnlyDictionary<string, Package> catalog, IReadOnlyDictionary<string, bool> changes,
        IReadOnlyDictionary<string, JsonObject>? importedSettings = null, KeyValuePair<string, JsonObject>? replace = null, bool merge = false)
    {
        using var editLock = new FileStream(Path.Combine(root, "profile.edit"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        var file = Path.Combine(root, "profile.json");
        var current = Catalog.ReadProfile(file, catalog);
        var enabled = current.Enabled.Distinct(StringComparer.Ordinal).ToList();
        foreach (var (id, selected) in changes)
        {
            if (!selected) { enabled.Remove(id); continue; }
            if (!catalog.TryGetValue(id, out var p) || !Catalog.Supports(p.Manifest.ApiVersion) ||
                !File.Exists(Path.Combine(p.Directory, "plugin.json")))
                throw new InvalidDataException($"找不到兼容插件 {id}");
            if (!enabled.Contains(id)) enabled.Add(id);
        }
        var profile = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : new JsonObject();
        profile["enabled"] = JsonSerializer.SerializeToNode(enabled);
        if (current.Settings != null) profile["settings"] = JsonSerializer.SerializeToNode(current.Settings);
        if (importedSettings != null)
        {
            var options = (profile["settings"] ??= new JsonObject()).AsObject();
            foreach (var (id, values) in importedSettings.Where(p => catalog.ContainsKey(p.Key)))
            {
                var existing = (options[id] ??= new JsonObject()).AsObject();
                foreach (var (key, value) in values) existing[key] = value == null ? null : JsonNode.Parse(value.ToJsonString());
            }
        }
        if (replace is { } edit)
        {
            var options = (profile["settings"] ??= new JsonObject()).AsObject();
            if (!merge) options[edit.Key] = edit.Value;
            else
            {
                var existing = (options[edit.Key] ??= new JsonObject()).AsObject();
                foreach (var pair in edit.Value) existing[pair.Key] = pair.Value == null ? null : JsonNode.Parse(pair.Value.ToJsonString());
            }
        }
        var edited = changes.Where(p => p.Value).Select(p => p.Key)
            .Concat(importedSettings?.Keys ?? Enumerable.Empty<string>())
            .Concat(replace is { } changed ? new[] { changed.Key } : Array.Empty<string>()).Distinct();
        foreach (var id in edited)
            if (catalog.TryGetValue(id, out var package) && profile["settings"]?[id] is { } value)
                PluginSettings.Validate(package.Manifest.Settings, JsonSerializer.SerializeToElement(value));
        var temp = file + ".tmp";
        try
        {
            File.WriteAllText(temp, profile.ToJsonString(Catalog.Json));
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
