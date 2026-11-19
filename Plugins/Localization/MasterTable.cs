using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Gkms.Plugins.Localization;

public sealed class MasterTable
{
    private readonly string[] keys;
    private readonly Dictionary<string, string[]> childKeys;
    private readonly Dictionary<string, List<JsonElement>> rows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(Type, string), PropertyInfo> properties = new();

    public MasterTable(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var primary = doc.RootElement.GetProperty("rules").GetProperty("primaryKeys").EnumerateArray()
            .Select(p => p.GetString()!).ToArray();
        keys = primary.Where(p => !p.Contains('.')).ToArray();
        if (keys.Length == 0) throw new InvalidDataException($"主键为空: {file}");
        childKeys = primary.Where(p => p.Contains('.')).Select(p => p.Split('.', 2))
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Select(p => p[1]).ToArray());
        foreach (var row in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!keys.All(key => row.TryGetProperty(key, out _))) continue;
            var id = Key(keys.Select(k => row.GetProperty(k).ToString()));
            if (!rows.TryGetValue(id, out var matches)) rows.Add(id, matches = new());
            matches.Add(row.Clone());
        }
    }

    private static string Key(IEnumerable<string?> values) =>
        string.Concat(values.Select(v => $"{v?.Length ?? -1}:{v}"));

    private PropertyInfo? Property(object obj, string name)
    {
        var type = obj.GetType();
        if (properties.TryGetValue((type, name), out var cached)) return cached;
        var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop != null) properties.TryAdd((type, name), prop);
        return prop;
    }

    private static IEnumerable<object> Elements(object container)
    {
        if (container is System.Collections.IEnumerable items && container is not string)
        {
            foreach (var item in items) if (item != null) yield return item;
            yield break;
        }
        var indexer = container.GetType().GetProperty("Item", new[] { typeof(int) });
        var count = container.GetType().GetProperty("Count");
        if (indexer == null || count == null) { yield return container; yield break; }
        var n = (int)count.GetValue(container)!;
        for (var i = 0; i < n; i++)
            if (indexer.GetValue(container, new object[] { i }) is { } value) yield return value;
    }

    private static string? ReadKey(object? value) => value is Enum kind
        ? kind.GetType().Name + "_" + kind : Convert.ToString(value, CultureInfo.InvariantCulture);

    public void Apply(object obj)
    {
        var id = Key(keys.Select(k => ReadKey(Property(obj, k)?.GetValue(obj))));
        if (!rows.TryGetValue(id, out var matches)) return;
        foreach (var row in matches) ApplyFields(obj, row, keys);
    }

    private void ApplyFields(object obj, JsonElement row, string[] primary)
    {
        foreach (var entry in row.EnumerateObject())
        {
            if (primary.Contains(entry.Name)) continue;
            var prop = Property(obj, entry.Name);
            if (prop == null) continue;
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                if (prop.CanWrite && prop.PropertyType == typeof(string)) prop.SetValue(obj, entry.Value.GetString());
                continue;
            }
            if (entry.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)) continue;
            var child = prop.GetValue(obj);
            if (child == null) continue;
            var data = entry.Value.ValueKind == JsonValueKind.Object ? new[] { entry.Value } : entry.Value.EnumerateArray().ToArray();
            if (data.Length == 0) continue;
            if (data.All(v => v.ValueKind == JsonValueKind.String))
            {
                var type = child.GetType();
                var clear = type.GetMethod("Clear", Type.EmptyTypes);
                var add = type.GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.FullName is "System.String" or "Il2CppSystem.String");
                if (clear == null || add == null) continue;
                var values = data.Select(v => Gkms.Sdk.Interop.ConvertString(add.GetParameters()[0].ParameterType, v.GetString()!)).ToArray();
                clear.Invoke(child, null);
                foreach (var value in values) add.Invoke(child, new[] { value });
                continue;
            }
            var parts = childKeys.GetValueOrDefault(entry.Name) ?? Array.Empty<string>();
            var index = 0;
            foreach (var item in Elements(child))
            {
                var selected = parts.Length == 0 ? data.ElementAtOrDefault(index) : data.FirstOrDefault(v =>
                    v.ValueKind == JsonValueKind.Object && parts.All(k => v.TryGetProperty(k, out var expected) &&
                        expected.ToString() == ReadKey(Property(item, k)?.GetValue(item))));
                if (selected.ValueKind == JsonValueKind.Object) ApplyFields(item, selected, parts);
                index++;
            }
        }
    }
}
