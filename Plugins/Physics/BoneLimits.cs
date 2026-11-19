using System.Reflection;
using System.Runtime.CompilerServices;
using Gkms.Sdk;

namespace Gkms.Plugins.Physics;

internal sealed class BoneLimits
{
    private readonly bool scaleEnabled, limitEnabled;
    private readonly float scale;
    private readonly float[][] factors;
    private readonly ConditionalWeakTable<object, int[][]> originals = new();
    private static readonly string[] axes = { "axisX", "axisY", "axisZ" };
    public BoneLimits(IPluginContext ctx)
    {
        scaleEnabled = ctx.Option("useScale", false);
        scale = ctx.Option("scale", 1f);
        limitEnabled = ctx.Option("useLimit", true);
        factors = axes.Select(axis => ctx.Option(axis, new[] { 1f, 1f })).ToArray();
        if (!float.IsFinite(scale) || scale <= 0 || factors.Any(pair => pair.Length != 2 || pair.Any(v => !float.IsFinite(v))))
            throw new InvalidDataException("骨骼缩放和每轴的两个限位系数须有效。");
    }

    public void Apply(object bone)
    {
        var type = bone.GetType();
        if (scaleEnabled)
            foreach (var name in new[] { "leftBreast", "rightBreast", "leftBreastEnd", "rightBreastEnd" })
            {
                var transform = type.GetProperty(name)?.GetValue(bone);
                if (transform?.GetType().GetProperty("localScale") is { } prop)
                    prop.SetValue(transform, Activator.CreateInstance(prop.PropertyType, scale, scale, scale));
            }
        var limit = type.GetProperty("limitInfo")?.GetValue(bone);
        if (limit == null) return;
        limit.GetType().GetProperty("useLimit")?.SetValue(limit, limitEnabled ? 1 : 0);
        if (!limitEnabled || factors.All(p => p[0] == 1 && p[1] == 1)) return;
        var properties = axes.Select(a => limit.GetType().GetProperty(a) ?? throw new MissingMemberException(a)).ToArray();
        var original = originals.GetValue(limit, obj => properties.Select(p =>
        {
            var value = p.GetValue(obj)!;
            return new[] { (int)p.PropertyType.GetProperty("x")!.GetValue(value)!, (int)p.PropertyType.GetProperty("y")!.GetValue(value)! };
        }).ToArray());
        for (var i = 0; i < properties.Length; i++)
        {
            var x = (int)(original[i][0] * factors[i][0]); var y = (int)(original[i][1] * factors[i][1]);
            properties[i].SetValue(limit, Activator.CreateInstance(properties[i].PropertyType, x, y));
        }
    }
}
