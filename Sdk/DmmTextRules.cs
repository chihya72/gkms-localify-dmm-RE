using System.Text.RegularExpressions;

namespace Gkms.Sdk;

// Local.cpp GetSplitTagsTranslationFull / SplitByTags and StringParser::parse(false).
// The original format-number and split delimiters deliberately differ.
public static class DmmTextRules
{
    public static readonly Regex Numbers = new(@"[0-9+＋%％.×,，－\-]+", RegexOptions.CultureInvariant);
    private static readonly Regex Tags = new(@"<.*?>(.*?)</.*?>", RegexOptions.CultureInvariant);
    private const string SplitFlags = "0123456789+＋-－%％【】.:：×";
    private const string PureFlags = "0123456789:/ .%,+-x\n";
    public static string Trim(string value) => value.Trim(' ', '\t', '\r', '\n', '\v', '\f');
    public static bool Pure(string value) => value.All(PureFlags.Contains);
    public static string[] Surroundings(string value)
    {
        var pieces = new List<string>();
        var tail = "";
        while (Tags.Match(value) is { Success: true } match)
        {
            if (Pure(match.Groups[1].Value))
            { pieces.Add(value[..match.Index]); tail = value[(match.Index + match.Length)..]; }
            value = value[(match.Index + match.Length)..];
        }
        if (tail.Length != 0) pieces.Add(tail);
        return pieces.ToArray();
    }
    // null means NO_SPLIT, an empty array means NO_SPLIT_AND_EMPTY.
    public static string[]? Fragments(string value)
    {
        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();
        var inTag = false;
        void Flush()
        {
            if (current.Length == 0) return;
            pieces.Add(Trim(current.ToString())); current.Clear();
        }
        foreach (var c in value)
        {
            if (c == '<') inTag = true;
            if (c == '>') { inTag = false; Flush(); continue; }
            if (inTag) { Flush(); continue; }
            if (SplitFlags.Contains(c)) Flush(); else current.Append(c);
        }
        if (pieces.Count == 0)
        {
            if (current.Length == 0) return Array.Empty<string>();
            if (value.Length == 0 || !SplitFlags.Contains(value[0])) return null;
        }
        Flush();
        return pieces.ToArray();
    }
    public static string ReplaceFirst(string text, string old, string value)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + value + text[(at + old.Length)..];
    }
    public static string NumberComma(string text)
    {
        var candidate = ReplaceFirst(text, "，", ",");
        return Pure(candidate) ? candidate : text;
    }
}
