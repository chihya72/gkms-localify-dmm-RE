using Gkms.Sdk;

namespace Gkms.Plugins.I18n;

public sealed class Plugin : ILocalifyPlugin
{
    private static IReadOnlyDictionary<string, string>? entries;
    public void Start(IPluginContext context)
    {
        entries = Interop.ReadDictionary(Path.Combine(context.DataDirectory, "local-files", "localization.json"));
        context.Patch(context.Require("quaunity-ui.Runtime", "Qua.UI.I18n", "SetValue", "System.String", "System.String"),
            GameMethods.Handler<Plugin>(nameof(Translate)));
        context.Log($"已读取 {entries.Count} 个界面键。");
    }

    public static void Translate(string __0, ref string __1)
    {
        if (__0 != null && entries != null && entries.TryGetValue(__0, out var value)) __1 = value;
    }
    public void Dispose() => entries = null;
}
