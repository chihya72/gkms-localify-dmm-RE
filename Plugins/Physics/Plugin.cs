using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Physics;

public sealed class Plugin : ILocalifyPlugin
{
    private static readonly Dictionary<string, object> values = new();
    private static PropertyInfo bones = null!;
    public void Start(IPluginContext context)
    {
        foreach (var (name, fallback) in new Dictionary<string, float> { ["damping"]=.33f, ["stiffness"]=.08f,
                     ["spring"]=.12f, ["pendulum"]=.55f, ["pendulumRange"]=.2f, ["average"]=.8f, ["rootWeight"]=1f })
        {
            var value = context.Option(name, fallback);
            if (!float.IsFinite(value)) throw new InvalidDataException(name);
            values[name] = value;
        }
        values["useArmCorrection"] = context.Option("useArmCorrection", true);
        var target = context.RequireType("campus-submodule.Runtime", "Campus.Common.CampusActorAnimation")
            .GetMethods(GameMethods.Declared).Single(m => m.Name == "Setup" && m.GetParameters() is [_, { ParameterType.Name: "CampusActorAnimationInitializeData" }]);
        bones = target.GetParameters()[1].ParameterType.GetProperty("swingBreastBones") ?? throw new MissingMemberException("swingBreastBones");
        context.Patch(target, GameMethods.Handler<Plugin>(nameof(Apply)));
    }

    public static void Apply(object __1)
    {
        if (__1 == null || bones.GetValue(__1) is not { } list) return;
        var type = list.GetType();
        var count = (int)type.GetProperty("Count")!.GetValue(list)!;
        var index = type.GetProperty("Item", new[] { typeof(int) })!;
        for (var i = 0; i < count; i++)
        {
            if (index.GetValue(list, new object[] { i }) is not { } bone) continue;
            foreach (var (key, value) in values) bone.GetType().GetProperty(key)?.SetValue(bone, value);
        }
    }
    public void Dispose() => values.Clear();
}
