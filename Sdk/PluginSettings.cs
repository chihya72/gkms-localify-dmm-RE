using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gkms.Sdk;

/// <summary>Renderer-independent configuration field. The manager reads it without loading plugin code.</summary>
public sealed record PluginSetting(string Key, string Label, string Type, JsonElement Default)
{
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public string[]? Choices { get; init; }
    public string[]? ElementLabels { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
    public bool Advanced { get; init; }
    public string? Section { get; init; }
    // Persist immediately, but retain the running value until the next plugin start.
    public bool RequiresRestart { get; init; }
    public string DisplayLabel(string language) => Labels?.GetValueOrDefault(language) ?? Label;
}

public sealed record PluginPreset(string Name, JsonElement Values);

public static class PluginSettings
{
    private static readonly Regex KeyPattern = new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant);

    public static void ValidateDefinition(IReadOnlyList<PluginSetting> fields, IReadOnlyList<PluginPreset> presets)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.Key) || !KeyPattern.IsMatch(field.Key) ||
                !keys.Add(field.Key) || string.IsNullOrWhiteSpace(field.Label))
                throw new InvalidDataException("配置字段需要唯一的 key 与 label。");
            if (field.Min > field.Max || (field.Min != null || field.Max != null) && field.Type is "string" or "boolean")
                throw new InvalidDataException($"{field.Key}: 数值范围无效。");
            if (field.Choices != null && (field.Type != "string" || field.Choices.Length == 0 ||
                field.Choices.Any(string.IsNullOrEmpty) || field.Choices.Distinct().Count() != field.Choices.Length))
                throw new InvalidDataException($"{field.Key}: choices 必须是非空、无重复的字符串选项。");
            if (field.ElementLabels != null && (field.Default.ValueKind != JsonValueKind.Array ||
                field.ElementLabels.Length != field.Default.GetArrayLength()))
                throw new InvalidDataException($"{field.Key}: elementLabels 与数组长度不一致。");
            ValidateValue(field, field.Default);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in presets)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || !names.Add(preset.Name) || preset.Values.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("预设需要唯一名称及 values 对象。");
            if (preset.Values.EnumerateObject().Any(p => !keys.Contains(p.Name)))
                throw new InvalidDataException($"{preset.Name}: 预设包含未声明的字段。");
            Validate(fields, preset.Values);
        }
    }

    // Unknown keys survive edits and upgrades. Missing values remain missing, allowing plugin-specific fallbacks.
    public static void Validate(IReadOnlyList<PluginSetting> fields, JsonElement settings)
    {
        if (settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        if (settings.ValueKind != JsonValueKind.Object) throw new InvalidDataException("插件配置必须是 JSON 对象。");
        foreach (var field in fields)
            if (settings.TryGetProperty(field.Key, out var value)) ValidateValue(field, value);
    }

    private static void ValidateValue(PluginSetting field, JsonElement value)
    {
        bool valid;
        switch (field.Type)
        {
            case "boolean": valid = value.ValueKind is JsonValueKind.True or JsonValueKind.False; break;
            case "string": valid = value.ValueKind == JsonValueKind.String && (field.Choices == null || field.Choices.Contains(value.GetString())); break;
            case "integer": valid = Number(value, true, field); break;
            case "number": valid = Number(value, false, field); break;
            case "integer-array":
            case "number-array":
                valid = value.ValueKind == JsonValueKind.Array && field.Default.ValueKind == JsonValueKind.Array &&
                    value.GetArrayLength() == field.Default.GetArrayLength() && value.GetArrayLength() is > 0 and <= 16 &&
                    value.EnumerateArray().All(v => Number(v, field.Type == "integer-array", field));
                break;
            default: throw new InvalidDataException($"{field.Key}: 不支持的配置类型 {field.Type}。");
        }
        if (!valid) throw new InvalidDataException($"{field.Key}: 需要 {field.Type} 类型、合法选项及声明范围内的值。");
    }

    private static bool Number(JsonElement value, bool integer, PluginSetting field) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) &&
        (!integer || value.TryGetInt32(out _)) &&
        (field.Min == null || number >= field.Min) && (field.Max == null || number <= field.Max);
}
