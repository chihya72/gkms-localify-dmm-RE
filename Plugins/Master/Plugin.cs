using System.Collections.Concurrent;
using Gkms.Sdk;

namespace Gkms.Plugins.Master;

public sealed class Plugin : ILocalifyPlugin
{
    private static readonly ConcurrentDictionary<Type, MasterTable> tables = new();
    private static readonly ConcurrentDictionary<Type, byte> reported = new();
    private static IPluginContext? session;
    public void Start(IPluginContext context)
    {
        session = context;
        var root = Path.Combine(context.DataDirectory, "local-files", "masterTrans");
        foreach (var file in Directory.EnumerateFiles(root, "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            var type = context.FindType("Assembly-CSharp", "Campus.Common.Proto.Client.Master." + Path.GetFileNameWithoutExtension(file));
            if (type == null) continue;
            var methods = type.GetMethods(GameMethods.Declared);
            var target = methods.FirstOrDefault(m => m.Name.EndsWith("InternalMergeFrom", StringComparison.Ordinal)) ??
                methods.FirstOrDefault(m => m.Name == "MergeFrom" && m.GetParameters() is [{ ParameterType.Name: "CodedInputStream" }]);
            if (target == null) continue;
            try
            {
                var table = new MasterTable(file);
                context.Patch(target, postfix: GameMethods.Handler<Plugin>(nameof(Translate)));
                tables[type] = table;
            }
            catch (Exception e) { context.Log($"{Path.GetFileName(file)}: {e.GetBaseException().Message}"); }
        }
        if (tables.Count == 0) throw new InvalidDataException("没有可用的主数据表。");
        context.Log($"已接入 {tables.Count} 张主数据表。");
    }
    public static void Translate(object __instance)
    {
        var type = __instance.GetType();
        if (!tables.TryGetValue(type, out var table)) return;
        try { table.Apply(__instance); }
        catch (Exception error)
        {
            if (reported.TryAdd(type, 0)) session?.Log($"{type.Name}: {error.GetBaseException().Message}");
        }
    }
    public void Dispose() { tables.Clear(); reported.Clear(); session = null; }
}
