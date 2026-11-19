using System.Text.Json;
using System.Text.Json.Nodes;
using Gkms.Host;

namespace Gkms.Manager;

internal static class ProfileEditor
{
    public static void Apply(string root, IReadOnlyDictionary<string, Package> catalog, IReadOnlyDictionary<string, bool> changes,
        IReadOnlyDictionary<string, JsonObject>? importedSettings = null, KeyValuePair<string, JsonObject>? replace = null)
    {
        using var editLock = new FileStream(Path.Combine(root, "profile.edit"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        var file = Path.Combine(root, "profile.json");
        var current = Catalog.ReadProfile(file, catalog);
        var enabled = current.Enabled.Distinct(StringComparer.Ordinal).ToList();
        foreach (var (id, selected) in changes)
        {
            if (!selected) { enabled.Remove(id); continue; }
            if (!catalog.TryGetValue(id, out var p) || p.Manifest.ApiVersion != Catalog.ApiVersion)
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
            (profile["settings"] ??= new JsonObject()).AsObject()[edit.Key] = edit.Value;
        var temp = file + ".tmp";
        try
        {
            File.WriteAllText(temp, profile.ToJsonString(Catalog.Json));
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
