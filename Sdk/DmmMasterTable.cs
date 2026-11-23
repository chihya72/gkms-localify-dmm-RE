using Gkms.Sdk;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Gkms.Sdk;

public class DmmMasterTable
{
    private readonly string[] keys;
    private sealed record Field(string? Parent, int Index, string Name);
    private readonly Dictionary<string, Dictionary<Field, JsonElement>> rows = new(StringComparer.Ordinal);
    public IEnumerable<string> TranslatedValues => rows.Values.SelectMany(r => r.Values).SelectMany(v =>
        v.ValueKind == JsonValueKind.String ? new[] { v.GetString()! } :
        v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!) : Array.Empty<string>());

    public DmmMasterTable(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var primary = doc.RootElement.GetProperty("rules").GetProperty("primaryKeys").EnumerateArray().Select(p => p.GetString()!).ToArray();
        keys = primary.Where(p => !p.Contains('.')).ToArray();
        if (keys.Length == 0) throw new InvalidDataException($"主键为空: {file}");
        var children = primary.Where(p => p.Count(c => c == '.') == 1).Select(p => p.Split('.'))
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Select(p => p[1]).ToArray());
        var data = doc.RootElement.GetProperty("data").EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).ToArray();
        // MasterLocal::BuildObjectItemLocalRule stops at the first row with supported fields.
        var main = Array.Empty<string>();
        var nested = new Dictionary<string, string[]>();
        foreach (var row in data)
        {
            main = LocalFields(row, keys);
            foreach (var field in row.EnumerateObject())
            {
                var candidates = field.Value.ValueKind == JsonValueKind.Object ? new[] { field.Value } :
                    field.Value.ValueKind == JsonValueKind.Array ? field.Value.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                var names = candidates.Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(x => LocalFields(x, children.GetValueOrDefault(field.Name) ?? Array.Empty<string>())).FirstOrDefault(x => x.Length != 0);
                if (names != null) nested.TryAdd(field.Name, names);
            }
            if (main.Length != 0 || nested.Count != 0) break;
        }
        foreach (var row in data)
        {
            if (!keys.All(k => row.TryGetProperty(k, out _))) continue;
            var id = Key(keys.Select(k => row.GetProperty(k).ToString()));
            if (!rows.TryGetValue(id, out var values)) rows.Add(id, values = new());
            foreach (var name in main)
                if (row.TryGetProperty(name, out var value) && Supported(value)) values.TryAdd(new(null, -1, name), value.Clone());
            foreach (var (parent, names) in nested)
            {
                if (!row.TryGetProperty(parent, out var value)) continue;
                if (value.ValueKind == JsonValueKind.Object) Add(value, -1);
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray()) { if (item.ValueKind == JsonValueKind.Object) Add(item, index); index++; }
                }
                void Add(JsonElement item, int index)
                {
                    foreach (var name in names)
                        if (item.TryGetProperty(name, out var child) && Supported(child))
                            values.TryAdd(new(parent, index, name), child.Clone());
                }
            }
        }
    }
    private static bool Supported(JsonElement value) => value.ValueKind == JsonValueKind.String ||
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.String;
    private static string[] LocalFields(JsonElement row, string[] primary) => row.EnumerateObject()
        .Where(f => !primary.Contains(f.Name) && Supported(f.Value)).Select(f => f.Name).ToArray();
    private static string Key(IEnumerable<string?> values) => string.Concat(values.Select(v => v + "|"));
    private static string? ReadKey(object? value) => value is Enum kind
        ? Interop.TypeOf(kind).Name + "_" + kind : Convert.ToString(value, CultureInfo.InvariantCulture);
    private static PropertyInfo? Property(object obj, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        return Interop.TypeOf(obj).GetProperty(name, flags);
    }
    private static FieldInfo? Backing(object obj, string name) => Interop.TypeOf(obj).GetField(name + "_",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
    private static object? Get(object obj, string name) => Property(obj, name)?.GetValue(obj) ?? Backing(obj, name)?.GetValue(obj);
    private static Action<object?>? Setter(object obj, string name)
    {
        if (Property(obj, name) is { CanWrite: true } p) return value => p.SetValue(obj, value);
        if (Backing(obj, name) is { } f) return value => f.SetValue(obj, value);
        return null;
    }
    public void Apply(object obj)
    {
        var id = Key(keys.Select(k => ReadKey(Get(obj, k))));
        if (!rows.TryGetValue(id, out var values)) return;
        foreach (var (field, value) in values)
        {
            var target = field.Parent == null ? obj : Get(obj, field.Parent);
            if (target == null) continue;
            // Sub-primary fields only exclude translated fields; DMM matches array elements by index.
            if (field.Index >= 0) target = Interop.Items(target).ElementAtOrDefault(field.Index);
            if (target == null) continue;
            var original = Get(target, field.Name);
            var setter = Setter(target, field.Name);
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()!;
                if (text.Length == 0 || setter == null || original != null && original is not string) continue;
                setter(text);
            }
            else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && setter != null)
            {
                // Replace the collection reference, like FieldController::SetStringListField.
                // Mutating the old collection changes game-owned aliases and breaks rollback.
                var collectionType = original == null ? Property(target, field.Name)?.PropertyType ?? Backing(target, field.Name)?.FieldType : Interop.TypeOf(original);
                if (collectionType == null) continue;
                var replacement = Interop.New(collectionType);
                if (replacement == null) continue;
                var add = Interop.TypeOf(replacement).GetMethods().FirstOrDefault(m => m.Name == "Add" &&
                    m.GetParameters() is [{ ParameterType.FullName: "System.String" }]);
                if (add == null) continue;
                foreach (var item in value.EnumerateArray()) add.Invoke(replacement, new object?[] { item.GetString() });
                setter(replacement);
            }
        }
    }
}
