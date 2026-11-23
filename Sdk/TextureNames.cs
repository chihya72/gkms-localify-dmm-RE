namespace Gkms.Sdk;

// HookTexture.cpp: category selection and portable sactx names are independent.
public static class TextureNames
{
    public static string Category(string name)
    {
        var leaf = Path.GetFileName(name);
        return leaf.StartsWith("img", StringComparison.OrdinalIgnoreCase) ? "image" :
            leaf.StartsWith("sactx", StringComparison.OrdinalIgnoreCase) ? "atlas" : "others";
    }
    public static string? Portable(string name)
    {
        name = Path.GetFileName(name);
        if (name.EndsWith(".png", StringComparison.Ordinal)) name = name[..^4];
        var parts = name.Split('-');
        if (parts.Length < 5 || parts[0] != "sactx" || !parts[2].Contains('x')) return null;
        var end = parts.Length;
        if (parts[^1].Length == 8 && parts[^1].All(Uri.IsHexDigit)) end--;
        return end <= 4 ? null : string.Join("-", parts.Take(3).Concat(parts.Skip(4).Take(end - 4)));
    }
    public static string DumpName(string? name)
    {
        if (string.IsNullOrEmpty(name)) name = "texture";
        if (name is "." or "..") return "_";
        var value = name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray();
        // Preserve the original sanitizer: it replaces the last trailing character.
        if (value[^1] is '.' or ' ') value[^1] = '_';
        return new string(value);
    }
}
