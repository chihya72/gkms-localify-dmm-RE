using System.Text.Json;
using Gkms.Sdk;

namespace Gkms.Plugins.Capture;

public sealed class Plugin : ILocalifyPlugin
{
    private static readonly object sync = new();
    private static readonly HashSet<(string, string)> seen = new();
    private static StreamWriter? writer;
    private static IPluginContext? session;
    private static bool failed;
    public void Start(IPluginContext context)
    {
        var root = Directory.CreateDirectory(Path.Combine(context.DataDirectory, "dump-files")).FullName;
        writer = File.AppendText(Path.Combine(root, "observed.jsonl"));
        writer.AutoFlush = true;
        session = context;
        context.Patch(context.Require("quaunity-ui.Runtime", "Qua.UI.I18n", "SetValue", "System.String", "System.String"),
            GameMethods.Handler<Plugin>(nameof(KeyValue)), priority: 800);
        context.Patch(context.Require("Unity.TextMeshPro", "TMPro.TMP_Text", "set_text", "System.String"),
            GameMethods.Handler<Plugin>(nameof(Text)), priority: 800);
    }
    public static void KeyValue(string __0, string __1) => Record("i18n", __0, __1);
    public static void Text(string __0) => Record("text", __0, __0);
    private static void Record(string kind, string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (sync)
        {
            if (failed || writer == null || !seen.Add((kind, key))) return;
            try { writer.WriteLine(JsonSerializer.Serialize(new { kind, key, value })); }
            catch (IOException error) { failed = true; session?.Log(error.Message); }
        }
    }
    public void Dispose() { lock (sync) { writer?.Dispose(); writer = null; session = null; seen.Clear(); } }
}
