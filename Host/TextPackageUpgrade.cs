using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gkms.Host;

internal static class TextPackageUpgrade
{
    private static readonly Dictionary<string, (string Option, string Assembly)> Former = new(StringComparer.Ordinal)
    {
        ["i18n"] = ("interfaceText", "Gkms.I18n.dll"), ["text"] = ("genericText", "Gkms.Text.dll"),
        ["story"] = ("story", "Gkms.Story.dll"), ["font"] = ("font", "Gkms.Font.dll"),
        ["subtitles"] = ("subtitles", "Gkms.Subtitles.dll")
    };

    public static bool IsFormer(Manifest m) => Former.TryGetValue(m.Id, out var part) && m.Assembly == part.Assembly;

    public static bool IsInstalled(IReadOnlyDictionary<string, Package> catalog) =>
        catalog.TryGetValue("localization", out var bundle) && bundle.Manifest.Assembly == "Gkms.Localization.dll";

    public static Profile Convert(Profile profile, IReadOnlyDictionary<string, Package>? catalog)
    {
        if (catalog != null && !IsInstalled(catalog)) return profile;
        bool WasReplaced(string id) => Former.ContainsKey(id) && (catalog == null || !catalog.ContainsKey(id));
        if (!profile.Enabled.Any(WasReplaced)) return profile;
        var options = profile.Settings == null ? new() : new Dictionary<string, JsonElement>(profile.Settings);
        if (!profile.Enabled.Contains("localization"))
        {
            var merged = options.TryGetValue("localization", out var opt) && opt.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(opt.GetRawText())!.AsObject() : new JsonObject();
            foreach (var (id, part) in Former) merged[part.Option] ??= WasReplaced(id) && profile.Enabled.Contains(id);
            options["localization"] = JsonSerializer.SerializeToElement(merged);
        }
        var enabled = profile.Enabled.Select(id => WasReplaced(id) ? "localization" : id).Distinct().ToArray();
        return new(enabled, options);
    }
}
