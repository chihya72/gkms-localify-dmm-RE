using Gkms.Sdk;

namespace Gkms.Host;

internal sealed class UnityLogForwarder : ILocalifyPlugin
{
    private static IPluginContext? session;
    [ThreadStatic] private static bool writing;
    public void Start(IPluginContext context)
    {
        session = context;
        var method = context.RequireType("UnityEngine.CoreModule", "UnityEngine.DebugLogHandler")
            .GetMethods(GameMethods.Declared).Single(m => m.Name == "Internal_Log" && m.GetParameters() is [_, _, { ParameterType: var t }, _] && t == typeof(string));
        context.Patch(method, GameMethods.Handler<UnityLogForwarder>(nameof(Observe)));
    }
    public static void Observe(object __0, string __2)
    {
        if (writing) return;
        writing = true;
        // Unity LogType: Error=0, Assert=1, Warning=2, Log=3, Exception=4.
        var level = Convert.ToInt32(__0) switch { 0 or 1 or 4 => PluginLogLevel.Error, 2 => PluginLogLevel.Warning, _ => PluginLogLevel.Info };
        try { session?.Log(level, __2); } finally { writing = false; }
    }
    public void Dispose() => session = null;
}
