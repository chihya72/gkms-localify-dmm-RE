using System.Reflection;
using System.Text.Json;
using Gkms.Sdk;

namespace Gkms.Plugins.Subtitles;

public sealed class Plugin : ILocalifyPlugin
{
    private static Dictionary<string, string> lines = new();
    private static MethodInfo? show;
    private static string? pending;
    private static bool showCue;
    public void Start(IPluginContext context)
    {
        var path = Path.Combine(context.DataDirectory, "local-files", "genericTrans", "genericTrans", "lyrics", "home_voice_subtitles.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("enabled", out var enabled) && !enabled.GetBoolean()) throw new InvalidDataException("字幕资源已禁用。");
        foreach (var entry in root.GetProperty("subtitles").EnumerateArray())
            lines[entry.GetProperty("voiceAssetId").GetString()!] = entry.GetProperty("text").GetString()!;
        showCue = root.TryGetProperty("showCueWhenMissing", out var flag) && flag.GetBoolean();
        show = context.Require("Assembly-CSharp", "Campus.Common.ToastManager", "Show", "System.String");
        context.Patch(context.Require("campus-submodule.Runtime", "Campus.Common.CampusSoundManager", "OnSoundPlayed", "System.String"),
            postfix: GameMethods.Handler<Plugin>(nameof(Heard)));
        context.Patch(context.Require("campus-submodule.Runtime", "Campus.Common.CampusQualityManager", "Update"),
            postfix: GameMethods.Handler<Plugin>(nameof(Display)));
    }
    public static void Heard(string __0)
    {
        if (__0 == null || !__0.StartsWith("sud_vo_", StringComparison.Ordinal) || !__0.Contains("_home_")) return;
        if (lines.TryGetValue(__0, out var line) || showCue) Interlocked.Exchange(ref pending, line ?? __0);
    }
    public static void Display()
    {
        var text = Interlocked.Exchange(ref pending, null);
        if (!string.IsNullOrEmpty(text)) show?.Invoke(null, new object[] { text });
    }
    public void Dispose() { pending = null; lines = new(); show = null; }
}
