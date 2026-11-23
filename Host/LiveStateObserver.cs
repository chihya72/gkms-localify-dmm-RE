using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Host;

/// <summary>Read-only observation starts even when every feature is disabled.</summary>
internal sealed class LiveStateObserver : ILocalifyPlugin
{
    public void Start(IPluginContext context)
    {
        LiveGameState.Clear();
        void Watch(MethodInfo? method, string callback, bool postfix = false)
        {
            if (method == null) return;
            try
            {
                var handler = GameMethods.Handler<LiveStateObserver>(callback);
                context.Patch(method, postfix ? null : handler, postfix ? handler : null, postfix ? -1000 : 1000);
            }
            catch (NotSupportedException e) { context.Log(PluginLogLevel.Warning, $"状态观察 {method.Name}: {e.Message}"); }
        }
        var quality = context.FindType("campus-submodule.Runtime", "Campus.Common.CampusQualityManager");
        foreach (var method in quality?.GetMethods(GameMethods.Declared).Where(m => m.Name is "ApplySetting" or "SetReflectionQuality" or "SetLODQuality") ?? Array.Empty<MethodInfo>())
            Watch(method, nameof(Setting));
        var actor = context.FindType("campus-submodule.Runtime", "Campus.Common.CampusActorAnimation");
        Watch(actor?.GetMethods(GameMethods.Declared).SingleOrDefault(m => m.Name == "Setup" && m.GetParameters() is [_, { ParameterType.Name: "CampusActorAnimationInitializeData" }]), nameof(Setting));
        Watch(context.Find("Assembly-CSharp", "Campus.Live.LiveScenePresenter", "SetLyricsActive", "System.Boolean"), nameof(Setting));
    }
    public static void Setting(object __instance, object[] __args, MethodBase __originalMethod) =>
        LiveGameState.Record(__instance, (MethodInfo)__originalMethod, __args);
    public void Dispose() { LiveGameState.Clear(); }
}
