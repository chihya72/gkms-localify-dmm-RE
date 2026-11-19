using Gkms.Sdk;

namespace Gkms.Plugins.AutoPhoto;

public sealed class Plugin : ILocalifyPlugin
{
    private static int[] counts = Array.Empty<int>();
    public void Start(IPluginContext context)
    {
        counts = context.Option("counts", new[] { 10, 20, 30 });
        if (counts.Length != 3 || counts.Any(x => x <= 0)) throw new InvalidDataException("counts 需要三个正整数。");
        var limit = context.Require("Assembly-CSharp", "Campus.Live.LiveUtility", "GetAutoPhotoLimitCount");
        context.Patch(limit, postfix: GameMethods.Handler<Plugin>(nameof(Limit)));
        var options = context.Find("Assembly-CSharp", "Campus.Live.LiveUtility", "AutoPhotoCountTypeValues");
        if (options != null) context.Patch(options, postfix: GameMethods.Handler<Plugin>(nameof(Options)));
    }
    public static void Options(object __result)
    {
        if (__result is int[] arr && arr.Length >= 3) { counts.CopyTo(arr, 0); return; }
        if (__result == null) return;
        var type = __result.GetType();
        if (Convert.ToInt32(type.GetProperty("Length")?.GetValue(__result)) < 3) return;
        var index = type.GetProperty("Item");
        for (var i = 0; i < counts.Length; i++) index?.SetValue(__result, counts[i], new object[] { i });
    }
    public static void Limit(ref int __result) => __result = __result switch
        { 1 or 3 => counts[2], 5 => counts[1], 10 => counts[0], _ => __result };
    public void Dispose() { }
}
