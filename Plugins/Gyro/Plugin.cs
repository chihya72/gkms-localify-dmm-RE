using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Gyro;

public sealed class Plugin : ILocalifyPlugin
{
    private static MethodInfo axis = null!, key = null!, euler = null!;
    private static PropertyInfo frame = null!, dt = null!;
    private static float sensitivity, pitch, yaw;
    private static int last = -1, signX, signY;
    public void Start(IPluginContext ctx)
    {
        sensitivity = ctx.Option("sensitivity", 1f);
        if (!float.IsFinite(sensitivity) || sensitivity <= 0) throw new InvalidDataException("sensitivity 须为正数。");
        signX = ctx.Option("invertX", false) ? -1 : 1;
        signY = ctx.Option("invertY", false) ? -1 : 1;
        const string input = "UnityEngine.InputLegacyModule";
        key = ctx.Require(input, "UnityEngine.Input", "GetKey", "System.String");
        axis = ctx.Require(input, "UnityEngine.Input", "GetAxisRaw", "System.String");
        euler = ctx.Require("UnityEngine.CoreModule", "UnityEngine.Quaternion", "Euler", "System.Single", "System.Single", "System.Single");
        var time = ctx.RequireType("UnityEngine.CoreModule", "UnityEngine.Time");
        frame = time.GetProperty("frameCount")!; dt = time.GetProperty("unscaledDeltaTime")!;
        const string campus = "campus-submodule.Runtime", controller = "Campus.Common.CampusGyroController";
        var rotation = ctx.Require(campus, controller, "get_Rotation");
        ctx.Patch(rotation, GameMethods.Handler<Plugin>(nameof(Attitude)).MakeGenericMethod(rotation.ReturnType));
        var manager = ctx.RequireType(campus, "Campus.Live.LivePanoramaCameraManager");
        var controllerSlot = manager.GetProperty("_gyroController", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (controllerSlot?.CanWrite == true)
        {
            slot = controllerSlot;
            ctx.Patch(ctx.Require(campus, manager.FullName!, "LateUpdate"), GameMethods.Handler<Plugin>(nameof(EnsureController)));
            var ready = ctx.Find(campus, manager.FullName!, "get_IsGyroReady");
            if (ready != null) ctx.Patch(ready, postfix: GameMethods.Handler<Plugin>(nameof(Supported)));
        }
        var photo = ctx.Find(campus, "Campus.Photography.PhotographyActivityController", "Initialize",
            "UnityEngine.Rendering.VolumeProfile", controller, "System.Single");
        if (photo != null) ctx.Patch(photo, GameMethods.Handler<Plugin>(nameof(ProvideController)).MakeGenericMethod(rotation.DeclaringType!));
        var support = ctx.Find("Assembly-CSharp", "Campus.Photography.Photography3DPresenter", "get_IsGyroSupported");
        if (support != null) ctx.Patch(support, postfix: GameMethods.Handler<Plugin>(nameof(Supported)));
        var panorama = ctx.Find("Assembly-CSharp", "Campus.Live.LiveScenePanoramaContentPresenter", "Initialize", "UnityEngine.RectTransform", "System.Boolean");
        if (panorama != null) ctx.Patch(panorama, GameMethods.Handler<Plugin>(nameof(Panorama)));
    }
    private static PropertyInfo? slot;
    private static object CreateController(Type type)
    {
        var instance = Activator.CreateInstance(type)!;
        type.GetProperty("IsGyro")?.SetValue(instance, true);
        return instance;
    }
    public static void EnsureController(object __instance)
    {
        if (slot != null && slot.GetValue(__instance) == null) slot.SetValue(__instance, CreateController(slot.PropertyType));
    }
    public static void ProvideController<T>(ref T __1) where T : class => __1 ??= (T)CreateController(typeof(T));
    public static void Supported(ref bool __result) => __result = true;
    public static void Panorama(ref bool __1) => __1 = true;
    private static int Held(string name) => (bool)key.Invoke(null, new object[] { name })! ? 1 : 0;
    public static bool Attitude<T>(ref T __result) where T : struct
    {
        var now = (int)frame.GetValue(null)!;
        if (now != last)
        {
            last = now;
            var step = (float)dt.GetValue(null)! * 60 * sensitivity;
            yaw += signX * (Held("[6]") - Held("[4]")) * step;
            pitch += signY * (Held("[2]") - Held("[8]")) * step;
            if (Held("mouse 1") != 0)
            {
                yaw += signX * sensitivity * (float)axis.Invoke(null, new object[] { "Mouse X" })!;
                pitch -= signY * sensitivity * (float)axis.Invoke(null, new object[] { "Mouse Y" })!;
            }
            if (Held("[5]") != 0) pitch = yaw = 0;
            pitch = Math.Clamp(pitch, -89f, 89f);
        }
        __result = (T)euler.Invoke(null, new object[] { pitch, yaw, 0f })!;
        return false;
    }
    public void Dispose() { }
}
