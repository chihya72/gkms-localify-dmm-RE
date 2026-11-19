using System.Globalization;
using System.Text.RegularExpressions;
using Gkms.Sdk;

namespace Gkms.Plugins.Text;

public sealed class TextDictionary
{
    private const string Marker = "[__split__]";
    private static readonly Regex Numbers = new(@"[0-9+＋%％.×,，－\-]+", RegexOptions.CultureInvariant);
    private static readonly Regex Fragments = new(@"<[^>]*>|[^<>0-9+＋%％.×,，－\-]+", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, string> exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> split = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> format = new(StringComparer.Ordinal);
    private readonly HashSet<string> translated = new(StringComparer.Ordinal);
    public int Count => exact.Count + split.Count + format.Count;

    public TextDictionary(string root)
    {
        var dir = Path.Combine(root, "genericTrans");
        var files = new[] { Path.Combine(root, "generic.json"), Path.Combine(root, "generic.split.json") }
            .Concat(Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal) : Array.Empty<string>());
        foreach (var file in files.Where(File.Exists))
        {
            if (Path.GetFileName(file).Equals("home_voice_subtitles.json", StringComparison.OrdinalIgnoreCase)) continue;
            var map = file.EndsWith(".fmt.json", StringComparison.OrdinalIgnoreCase) ? format :
                file.EndsWith(".split.json", StringComparison.OrdinalIgnoreCase) ? split : exact;
            foreach (var (key, value) in Interop.ReadDictionary(file))
            {
                if (key.StartsWith(Marker, StringComparison.Ordinal) && value.StartsWith(Marker, StringComparison.Ordinal))
                    split[key[Marker.Length..]] = value[Marker.Length..];
                else map[key] = value;
                translated.Add(value);
            }
        }
    }

    public string Translate(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (exact.TryGetValue(value, out var match)) return match;
        if (translated.Contains(value)) return value;
        var card = value.TrimEnd('+');
        if (card.Length != value.Length && exact.TryGetValue(card, out match)) return match + value[card.Length..];
        if (!value.Contains('{') && Numbers.IsMatch(value))
        {
            var values = new List<object>();
            var pattern = Numbers.Replace(value, m => { values.Add(m.Value); return "{" + (values.Count - 1) + "}"; });
            if (format.TryGetValue(pattern, out var template))
            {
                try { return string.Format(CultureInfo.InvariantCulture, template, values.ToArray()); }
                catch (FormatException) { /* A bad row must leave the original UI text usable. */ }
            }
        }
        return Fragments.Replace(value, m =>
        {
            if (m.Value.StartsWith('<')) return m.Value;
            var key = m.Value.Trim();
            if (key.Length == 0 || (!split.TryGetValue(key, out var text) && !exact.TryGetValue(key, out text))) return m.Value;
            var start = m.Value.IndexOf(key, StringComparison.Ordinal);
            return m.Value[..start] + text + m.Value[(start + key.Length)..];
        });
    }
}
