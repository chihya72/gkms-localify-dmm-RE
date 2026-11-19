using Gkms.Sdk;

namespace Gkms.Plugins.Localization;

public sealed class KeyedText : ILocalifyPlugin
{
    private static IReadOnlyDictionary<string, string>? entries;
    private static bool textTest;
    public void Start(IPluginContext context)
    {
        textTest = context.Option("textTest", false);
        var file = Path.Combine(context.DataDirectory, "local-files", "localization.json");
        entries = File.Exists(file) ? Interop.ReadDictionary(file) : new Dictionary<string, string>();
        context.Patch(context.Require("quaunity-ui.Runtime", "Qua.UI.I18n", "SetValue", "System.String", "System.String"),
            GameMethods.Handler<KeyedText>(nameof(Translate)));
        context.Log($"已读取 {entries.Count} 个界面键。");
    }

    public static void Translate(string __0, ref string __1)
    {
        if (__0 != null && entries != null && entries.TryGetValue(__0, out var value)) __1 = value;
        else if (textTest && !string.IsNullOrEmpty(__1)) __1 = "[I18]" + __1;
    }
    public void Dispose() => entries = null;
}
