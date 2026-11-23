using System.Globalization;
using System.Text;

namespace Gkms.Sdk;

// Misc.cpp passes only matched numeric STRINGS to fmt 11, not numeric arguments.
// Implement its string presentations rather than .NET composite-format semantics.
internal static class DmmStringFormat
{
    public static string Format(string template, string[] values)
    {
        var output = new StringBuilder();
        var next = 0; var indexing = 0;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c is not ('{' or '}')) { output.Append(c); continue; }
            if (i + 1 < template.Length && template[i + 1] == c) { output.Append(c); i++; continue; }
            if (c == '}') throw new FormatException();
            var end = template.IndexOf('}', i + 1);
            if (end < 0) throw new FormatException();
            var field = template[(i + 1)..end]; i = end;
            var colon = field.IndexOf(':');
            var id = colon < 0 ? field : field[..colon];
            int index;
            if (id.Length == 0)
            { if (indexing == 2) throw new FormatException(); indexing = 1; index = next++; }
            else
            {
                if (indexing == 1 || id.Any(ch => ch is < '0' or > '9') || id.Length > 1 && id[0] == '0' ||
                    !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out index)) throw new FormatException();
                indexing = 2;
            }
            if (index >= values.Length) throw new FormatException();
            output.Append(Present(values[index], colon < 0 ? "" : field[(colon + 1)..]));
        }
        return output.ToString();
    }
    private static string Present(string text, string spec)
    {
        var cursor = 0; var fill = " "; var align = '<';
        if (spec.Length != 0)
        {
            var length = char.IsHighSurrogate(spec[0]) && spec.Length > 1 ? 2 : 1;
            if (spec.Length > length && spec[length] is '<' or '>' or '^')
            { fill = spec[..length]; if (fill is "{" or "}") throw new FormatException(); align = spec[length]; cursor = length + 1; }
            else if (spec[0] is '<' or '>' or '^') { align = spec[0]; cursor = 1; }
        }
        var width = 0;
        if (cursor < spec.Length && spec[cursor] == '0') throw new FormatException(); // zero padding is numeric-only.
        var start = cursor;
        while (cursor < spec.Length && spec[cursor] is >= '0' and <= '9') cursor++;
        if (cursor > start && !int.TryParse(spec[start..cursor], out width)) throw new FormatException();
        if (cursor < spec.Length && spec[cursor] == '.')
        {
            start = ++cursor;
            while (cursor < spec.Length && spec[cursor] is >= '0' and <= '9') cursor++;
            if (start == cursor || !int.TryParse(spec[start..cursor], out var precision)) throw new FormatException();
            // Numeric captures are BMP characters, so precision counts code points directly.
            text = text[..Math.Min(precision, text.Length)];
        }
        if (cursor < spec.Length)
        {
            if (spec[cursor] == '?') text = '"' + text + '"';
            else if (spec[cursor] != 's') throw new FormatException();
            cursor++;
        }
        if (cursor != spec.Length) throw new FormatException();
        // fmt's display width for the full-width signs/comma matched by Local.cpp's number regex is two.
        var lengthOnScreen = text.Sum(c => c is '＋' or '％' or '，' or '－' ? 2 : 1);
        var padding = Math.Max(0, width - lengthOnScreen);
        var left = align == '>' ? padding : align == '^' ? padding / 2 : 0;
        return string.Concat(Enumerable.Repeat(fill, left)) + text + string.Concat(Enumerable.Repeat(fill, padding - left));
    }
}
