using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Graphics;

public sealed class Plugin : ILocalifyPlugin
{
    private static int quality, pixels, volume, reflection, lod;
    private static float scale;
    private static MethodInfo? setReflection, setLod;
    public void Start(IPluginContext context)
    {
        quality = context.Option("quality", 3);
        pixels = context.Option("maxBufferPixel", 3384);
        volume = context.Option("volumeIndex", 3);
        scale = context.Option("renderScale", 1f);
        reflection = context.Option("reflectionQuality", -1);
        lod = context.Option("lodQuality", -1);
        if (quality is < 1 or > 5 || pixels <= 0 || volume is < 0 or > 4 || !float.IsFinite(scale) || scale <= 0 ||
            reflection is < -1 or > 5 || lod is < -1 or > 5) throw new InvalidDataException("画质参数超出范围。");
        var target = context.Require("campus-submodule.Runtime", "Campus.Common.CampusQualityManager", "ApplySetting",
            "System.Int32", "System.Int32", "System.Single", "System.Int32");
        var methods = target.DeclaringType!.GetMethods(GameMethods.Declared);
        setReflection = methods.SingleOrDefault(m => m.Name == "SetReflectionQuality" && m.GetParameters().Length == 1);
        setLod = methods.SingleOrDefault(m => m.Name == "SetLODQuality" && m.GetParameters().Length == 1);
        context.Patch(target, GameMethods.Handler<Plugin>(nameof(Apply)));
        if (scale > 1)
        {
            var limited = context.Require("vl-unity.Runtime", "VL.VLUtility", "GetLimitedResolution",
                "System.Int32", "System.Int32", "UnityEngine.Vector2", "System.Int32", "System.Single", "System.Boolean");
            context.Patch(limited, GameMethods.Handler<Plugin>(nameof(ScaleResolution)));
        }
    }
    public static void ScaleResolution(ref int __0, ref int __1)
    {
        __0 = (int)Math.Clamp((double)__0 * scale, 1, int.MaxValue);
        __1 = (int)Math.Clamp((double)__1 * scale, 1, int.MaxValue);
    }
    private static void SetLevel(object instance, MethodInfo? method, int index)
    {
        if (index < 0 || method == null) return;
        var type = method.GetParameters()[0].ParameterType;
        if (type.IsEnum && index < Enum.GetValues(type).Length)
            method.Invoke(instance, new[] { Enum.GetValues(type).GetValue(index) });
    }
    public static void Apply(object __instance, ref int __0, ref int __1, ref float __2, ref int __3)
    {
        SetLevel(__instance, setReflection, reflection); SetLevel(__instance, setLod, lod);
        (__0, __1, __2, __3) = (quality, pixels, scale, volume);
    }
    public void Dispose() { setReflection = null; setLod = null; }
}
