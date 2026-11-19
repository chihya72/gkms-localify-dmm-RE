using Gkms.Sdk;

namespace Gkms.Plugins.FrameRate;

public sealed class Plugin : ILocalifyPlugin
{
    private static int fps;
    public void Start(IPluginContext context)
    {
        fps = context.Option("fps", 60);
        if (fps is < 1 or > 1000) throw new InvalidDataException("fps 应在 1 到 1000 之间。");
        context.Patch(context.Require("campus-submodule.Runtime", "Campus.Common.CampusQualityManager", "set_TargetFrameRate", "System.Single"),
            GameMethods.Handler<Plugin>(nameof(GameRate)));
        var unity = context.Find("UnityEngine.CoreModule", "UnityEngine.Application", "set_targetFrameRate", "System.Int32");
        if (unity != null) context.Patch(unity, GameMethods.Handler<Plugin>(nameof(UnityRate)));
    }
    public static void GameRate(ref float __0) => __0 = fps;
    public static void UnityRate(ref int __0) => __0 = fps;
    public void Dispose() { }
}
