using Gkms.Sdk;

namespace Gkms.Plugins.UnityLog;

public sealed class Plugin : ILocalifyPlugin
{
    private static IPluginContext? session;
    [ThreadStatic] private static bool writing;
    public void Start(IPluginContext context)
    {
        session = context;
        var method = context.RequireType("UnityEngine.CoreModule", "UnityEngine.DebugLogHandler")
            .GetMethods(GameMethods.Declared).Single(m => m.Name == "Internal_Log" && m.GetParameters() is [_, _, { ParameterType: var t }, _] && t == typeof(string));
        context.Patch(method, GameMethods.Handler<Plugin>(nameof(Observe)));
    }
    public static void Observe(string __2)
    {
        if (writing) return;
        writing = true;
        try { session?.Log(__2); } finally { writing = false; }
    }
    public void Dispose() => session = null;
}
