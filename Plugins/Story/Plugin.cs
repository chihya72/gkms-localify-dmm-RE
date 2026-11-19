using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Story;

public sealed class Plugin : ILocalifyPlugin
{
    private static string? root;
    private static MethodInfo? invoke;
    public void Start(IPluginContext context)
    {
        root = Path.Combine(context.DataDirectory, "local-files", "resource");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var loader = context.RequireType("Octo", "Octo.Loader.OctoResourceLoader");
        var method = loader.GetMethods(GameMethods.Declared).Single(m => m.Name == "LoadFromCacheOrDownload" &&
            m.GetParameters() is [{ ParameterType: var name }, { ParameterType: var callback }, _] &&
            name == typeof(string) && callback.IsGenericType &&
            callback.GetGenericArguments()[0].FullName is "System.String" or "Il2CppSystem.String");
        invoke = method.GetParameters()[1].ParameterType.GetMethod("Invoke") ?? throw new MissingMethodException("Octo callback");
        context.Patch(method, GameMethods.Handler<Plugin>(nameof(Replace)));
    }

    public static bool Replace(string __0, object __1)
    {
        if (root == null || invoke == null || __1 == null) return true;
        var file = Interop.LocalFile(root, __0);
        if (file == null || !File.Exists(file)) return true;
        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { return true; }
        var parameters = invoke.GetParameters();
        var success = parameters[1].ParameterType.IsValueType ? Activator.CreateInstance(parameters[1].ParameterType) : null;
        invoke.Invoke(__1, new[] { Interop.ConvertString(parameters[0].ParameterType, text), success });
        return false;
    }
    public void Dispose() { root = null; invoke = null; }
}
