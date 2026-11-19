using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gkms.Host;

internal static class TextPackageUpgrade
{
    private static readonly Dictionary<string, string> Former = new(StringComparer.Ordinal)
    {
        ["i18n"] = "Gkms.I18n.dll", ["text"] = "Gkms.Text.dll", ["story"] = "Gkms.Story.dll",
        ["font"] = "Gkms.Font.dll", ["subtitles"] = "Gkms.Subtitles.dll", ["master"] = "Gkms.Master.dll",
        ["lyrics"] = "Gkms.Lyrics.dll"
    };
    private static readonly string[] FormerSwitches = { "interfaceText", "genericText", "story", "font", "subtitles", "master", "lyrics" };

    public static bool IsFormer(Manifest m) => Former.TryGetValue(m.Id, out var assembly) && m.Assembly == assembly;

    public static bool IsInstalled(IReadOnlyDictionary<string, Package> catalog) =>
        catalog.TryGetValue("localization", out var bundle) && bundle.Manifest.Assembly == "Gkms.Localization.dll";

    public static Profile Convert(Profile profile, IReadOnlyDictionary<string, Package>? catalog)
    {
        if (catalog != null && !IsInstalled(catalog)) return profile;
        bool WasReplaced(string id) => Former.ContainsKey(id) && (catalog == null || !catalog.ContainsKey(id));
        var enabled = profile.Enabled.Select(id => WasReplaced(id) ? "localization" : id).Distinct().ToArray();
        var settings = profile.Settings;
        if (settings != null && settings.TryGetValue("localization", out var options) && options.ValueKind == JsonValueKind.Object &&
            FormerSwitches.Any(key => options.TryGetProperty(key, out _)))
        {
            var remaining = JsonNode.Parse(options.GetRawText())!.AsObject();
            foreach (var key in FormerSwitches) remaining.Remove(key);
            settings = new Dictionary<string, JsonElement>(settings);
            if (remaining.Count == 0) settings.Remove("localization");
            else settings["localization"] = JsonSerializer.SerializeToElement(remaining);
        }
        return new(enabled, settings);
    }
}
