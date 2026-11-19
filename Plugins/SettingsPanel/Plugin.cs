using System.Diagnostics;
using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.SettingsPanel;

public sealed class Plugin : ILocalifyPlugin
{
    private static MethodInfo key = null!, down = null!;
    private static Process? window;
    private static string manager = "";
    private static IPluginContext? session;

    public void Start(IPluginContext ctx)
    {
        manager = Path.Combine(ctx.DataDirectory, "re", "Gkms.Manager.exe");
        if (!File.Exists(manager)) throw new FileNotFoundException("缺少插件管理器", manager);
        session = ctx;
        key = ctx.Require("UnityEngine.InputLegacyModule", "UnityEngine.Input", "GetKey", "System.String");
        down = ctx.Require("UnityEngine.InputLegacyModule", "UnityEngine.Input", "GetKeyDown", "System.String");
        ctx.Patch(ctx.Require("campus-submodule.Runtime", "Campus.Common.CampusQualityManager", "Update"),
            postfix: GameMethods.Handler<Plugin>(nameof(Update)));
    }

    public static void Update()
    {
        if (!(bool)down.Invoke(null, new object[] { "u" })! ||
            !(bool)key.Invoke(null, new object[] { "left ctrl" })! && !(bool)key.Invoke(null, new object[] { "right ctrl" })!) return;
        try
        {
            if (window is { HasExited: false }) return;
            window?.Dispose();
            window = Process.Start(new ProcessStartInfo(manager) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception e) { session?.Log(e.Message); }
    }
    public void Dispose() { window?.Dispose(); window = null; session = null; }
}
