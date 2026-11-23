using System.Globalization;
using System.Text.Json;
using Gkms.Sdk;

namespace Gkms.Sdk;

public class DmmTextDictionary
{
    private const string Marker = "[__split__]";
    private readonly Dictionary<string, string> exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> split = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> format = new(StringComparer.Ordinal);
    private readonly HashSet<string> translated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> keyed = new(StringComparer.Ordinal);
    public int Count => exact.Count + split.Count + format.Count;
    private readonly List<string> warnings = new();
    public IReadOnlyList<string> Warnings => warnings;
    public IReadOnlyDictionary<string, string> I18n => keyed;

    public DmmTextDictionary(string root) : this(root, false, null) { }

    // The localization plugin shares these parsed tables with its actual writers.
    // Capture keeps its existing independent, read-only dictionary construction.
    protected DmmTextDictionary(string root, bool requireLocalization, Action<string, DmmMasterTable>? masterLoaded)
    {
        void Load(string file, Dictionary<string, string> target, bool markers)
        {
            if (!ResourceFiles.TryRead(file, Interop.ReadDictionary, Warn, out var entries)) return;
            foreach (var (key, value) in entries)
                if (markers && key.StartsWith(Marker, StringComparison.Ordinal) && value.StartsWith(Marker, StringComparison.Ordinal))
                { split[key[Marker.Length..]] = value[Marker.Length..]; translated.Add(value[Marker.Length..]); }
                else { target[key] = value; translated.Add(value); }
        }
        var localization = Path.Combine(root, "localization.json");
        if (requireLocalization && !File.Exists(localization))
            Warn($"缺少 {localization}，按原 DMM 跳过界面及通用词典加载；主数据和剧情独立处理。");
        else LoadGeneric();

        void LoadGeneric()
        {
            Load(localization, keyed, false);
            Load(Path.Combine(root, "generic.json"), exact, true);
            // Local::LoadData clears genericSplitText after loading generic.json.
            split.Clear();
            Load(Path.Combine(root, "generic.split.json"), split, true);
            var genericDir = Path.Combine(root, "genericTrans");
            foreach (var file in Directory.Exists(genericDir) ? Directory.EnumerateFiles(genericDir, "*", SearchOption.AllDirectories) : Array.Empty<string>())
            {
                if (!Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file) == "home_voice_subtitles.json") continue;
                if (file.EndsWith(".split.json", StringComparison.Ordinal)) Load(file, split, true);
                if (file.EndsWith(".fmt.json", StringComparison.Ordinal)) Load(file, format, false);
                else Load(file, exact, true);
            }
            foreach (var (key, value) in exact.ToArray())
            {
                var source = DmmTextRules.Surroundings(key);
                var target = DmmTextRules.Surroundings(value);
                if (source.Length != target.Length) continue;
                for (var i = 0; i < source.Length; i++) exact.TryAdd(source[i], target[i]);
            }
        }
        // I18n and master translations are protected from split/format passes.
        // Story assets are not a global generic dictionary.
        var dir = Path.Combine(root, "masterTrans");
        foreach (var file in (Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json") : Array.Empty<string>()).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (ResourceFiles.TryRead(file, path => new DmmMasterTable(path), Warn, out var table))
            {
                foreach (var value in table.TranslatedValues) translated.Add(value);
                masterLoaded?.Invoke(file, table);
            }
        }
        void Warn(string message) { if (!warnings.Contains(message)) warnings.Add(message); }
    }
    public bool IsKnown(string value) => translated.Contains(value) || exact.ContainsKey(value);
    public bool HasKey(string key) => keyed.ContainsKey(key);
    public string Translate(string value) { TryTranslate(value, out var result); return result; }
    public bool TryTranslate(string value, out string result)
    {
        result = value;
        if (exact.TryGetValue(value, out var match)) { result = match; return true; }
        if (translated.Contains(value)) return false;
        var card = value.TrimEnd('+');
        if (card.Length != 0 && exact.TryGetValue(card, out match)) { result = match + value[card.Length..]; return true; }
        if (!value.Contains('{') && DmmTextRules.Numbers.IsMatch(value))
        {
            var values = new List<object>();
            var pattern = DmmTextRules.Numbers.Replace(value, m => { values.Add(m.Value); return "{" + (values.Count - 1) + "}"; });
            if (format.TryGetValue(pattern, out var template))
            {
                var formatted = Format(template, values);
                if (formatted.Length != 0) { result = formatted; return true; }
            }
        }
        var matched = false;
        foreach (var key in DmmTextRules.Fragments(value) ?? Array.Empty<string>())
            if (key.Length != 0 && split.TryGetValue(key, out var text) && text.Length != 0)
            { result = DmmTextRules.ReplaceFirst(result, key, DmmTextRules.NumberComma(text)); matched = true; }
        return matched;
    }
    private static string Format(string template, List<object> values)
    {
        // Misc.cpp handles 1..24 arguments; an invalid format returns the literal template.
        if (values.Count is < 1 or > 24) return template;
        try { return DmmStringFormat.Format(template, values.Select(v => (string)v).ToArray()); }
        catch (FormatException) { return template; }
    }
    public IEnumerable<(string File, string Text)> Missing(string value)
    {
        if (IsKnown(value)) yield break;
        var card = value.TrimEnd('+');
        if (card.Length != 0 && exact.ContainsKey(card)) yield break;
        if (!value.Contains('{') && DmmTextRules.Numbers.IsMatch(value))
        {
            var values = new List<object>();
            var pattern = DmmTextRules.Numbers.Replace(value, m => { values.Add(m.Value); return "{" + (values.Count - 1) + "}"; });
            if (format.TryGetValue(pattern, out var template) && Format(template, values).Length != 0) yield break;
            if (!translated.Contains(pattern) && !DmmTextRules.Pure(pattern)) yield return ("fmt", pattern);
        }
        var fragments = DmmTextRules.Fragments(value);
        if (fragments is { Length: 0 }) yield break;
        foreach (var fragment in fragments ?? Array.Empty<string>())
            if (fragment.Length > 0 && (!split.TryGetValue(fragment, out var text) || text.Length == 0) &&
                !translated.Contains(fragment) && !DmmTextRules.Pure(fragment)) yield return ("split", fragment);
        if (!translated.Contains(value) && !DmmTextRules.Pure(value)) yield return ("orig", value);
    }
}
