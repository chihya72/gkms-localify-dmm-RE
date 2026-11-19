using System.Runtime.CompilerServices;
using Gkms.Sdk;

namespace Gkms.Plugins.Localization;

internal sealed class LiveLyrics : ILocalifyPlugin
{
    private static ConditionalWeakTable<object, object> initialized = new();
    public void Start(IPluginContext context) => context.Patch(
        context.Require("Assembly-CSharp", "Campus.Live.LiveScenePresenter", "SetLyricsActive", "System.Boolean"),
        GameMethods.Handler<LiveLyrics>(nameof(ShowInitially)));
    public static void ShowInitially(object __instance, ref bool __0)
    {
        if (initialized.TryGetValue(__instance, out _)) return;
        initialized.Add(__instance, new object());
        __0 = true;
    }
    public void Dispose() => initialized = new();
}
