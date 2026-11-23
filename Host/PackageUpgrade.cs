using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gkms.Host;

internal static class PackageUpgrade
{
    private static readonly Dictionary<string, string> Former = new(StringComparer.Ordinal)
    {
        ["i18n"] = "Gkms.I18n.dll", ["text"] = "Gkms.Text.dll", ["story"] = "Gkms.Story.dll",
        ["subtitles"] = "Gkms.Subtitles.dll", ["master"] = "Gkms.Master.dll",
        ["lyrics"] = "Gkms.Lyrics.dll"
    };
    private static readonly string[] FormerSwitches = { "interfaceText", "genericText", "story", "font", "subtitles", "master", "lyrics" };

    private static readonly Dictionary<string, string> BuiltIns = new(StringComparer.Ordinal)
    {
        ["settings-panel"] = "Gkms.SettingsPanel.dll", ["unity-log"] = "Gkms.UnityLog.dll", ["font"] = "Gkms.Font.dll"
    };

    private static bool HasPackage(IReadOnlyDictionary<string, Package>? catalog, string id, string assembly) =>
        catalog == null || catalog.TryGetValue(id, out var package) && package.Manifest.Assembly == assembly;

    public static bool IsRetired(Manifest manifest, IReadOnlyDictionary<string, Package> catalog) =>
        BuiltIns.TryGetValue(manifest.Id, out var builtin) && manifest.Assembly == builtin ||
        manifest.Id == "purchase-guard" && manifest.Assembly == "Gkms.PurchaseGuard.dll" && HasPackage(catalog, "ios-login", "Gkms.PurchaseGuard.dll") ||
        manifest.Id == "texture-capture" && manifest.Assembly == "Gkms.TextureCapture.dll" && HasPackage(catalog, "capture", "Gkms.Capture.dll") ||
        Former.TryGetValue(manifest.Id, out var oldText) && manifest.Assembly == oldText && HasPackage(catalog, "localization", "Gkms.Localization.dll");

    public static Profile Convert(Profile profile, IReadOnlyDictionary<string, Package>? catalog)
    {
        var hasText = HasPackage(catalog, "localization", "Gkms.Localization.dll");
        var hasCapture = HasPackage(catalog, "capture", "Gkms.Capture.dll");
        string? Migrate(string id)
        {
            if (catalog?.ContainsKey(id) == true) return id;
            if (id == "purchase-guard" && HasPackage(catalog, "ios-login", "Gkms.PurchaseGuard.dll")) return "ios-login";
            if (id is "unlock-live" or "unlock-costumes" && HasPackage(catalog, "live-unlock", "Gkms.LiveUnlock.dll")) return "live-unlock";
            if (BuiltIns.ContainsKey(id)) return null;
            if (hasCapture && id == "texture-capture") return "capture";
            return hasText && Former.ContainsKey(id) ? "localization" : id;
        }
        var enabled = profile.Enabled.Select(Migrate).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var settings = profile.Settings;
        if (hasText && settings != null && settings.TryGetValue("localization", out var options) && options.ValueKind == JsonValueKind.Object &&
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
