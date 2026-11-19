using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Gkms.Sdk;

namespace Gkms.Plugins.Window;

public sealed class Plugin : ILocalifyPlugin
{
    private static string orientation = "";
    private static bool resize;
    private static IntPtr handle;
    private static int addedStyle;
    private const int Style = -16, ResizeStyle = 0x00040000 | 0x00010000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetStyle(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetStyle(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);

    public void Start(IPluginContext context)
    {
        orientation = context.Option("orientation", "game");
        resize = context.Option("resizable", true);
        if (orientation is not ("game" or "portrait" or "landscape")) throw new InvalidDataException("orientation 应为 game、portrait 或 landscape。");
        if (orientation != "game")
        {
            var method = context.RequireType("campus-submodule.Runtime", "Campus.Common.ScreenOrientationControllerBase")
                .GetMethods(GameMethods.Declared).Single(m => m.Name == "InternalSetOrientationAsync" && m.GetParameters().Length == 3);
            context.Patch(method, GameMethods.Handler<Plugin>(nameof(Orient)).MakeGenericMethod(method.GetParameters()[0].ParameterType));
        }
        if (!resize) return;
        context.Patch(context.Require("campus-submodule.Runtime", "Campus.Common.CampusQualityManager", "Update"),
            postfix: GameMethods.Handler<Plugin>(nameof(ApplyWindow)));
        var nudge = context.Find("Assembly-CSharp", "Campus.Common.StandAloneWindow.AspectRatioHandler", "NudgeWindow");
        if (nudge != null) context.Patch(nudge, GameMethods.Handler<Plugin>(nameof(SkipNudge)));
    }
    public static void Orient<T>(ref T __0) where T : struct, Enum =>
        __0 = (T)Enum.ToObject(typeof(T), orientation == "portrait" ? 2 : 3);
    public static bool SkipNudge() => false;
    public static void ApplyWindow()
    {
        if (handle != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        var window = process.MainWindowHandle;
        if (window == IntPtr.Zero) return;
        var original = GetStyle(window, Style);
        if (original == 0) return;
        addedStyle = ResizeStyle & ~original;
        if (addedStyle != 0 && SetStyle(window, Style, original | addedStyle) == 0) return;
        handle = window;
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }
    public void Dispose()
    {
        if (handle == IntPtr.Zero || addedStyle == 0) return;
        SetStyle(handle, Style, GetStyle(handle, Style) & ~addedStyle);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
        handle = IntPtr.Zero;
    }
}
