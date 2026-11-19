using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Gkms.Host;

namespace Gkms.Bridge;

[BepInPlugin("gkms.localify.re", "Gakumas Localify RE", "0.1.0")]
[BepInProcess("gakumas.exe")]
public sealed class Entry : BasePlugin, IGameServices
{
    private PluginHost? host;

    public override void Load()
    {
        var data = Path.Combine(Paths.GameRootPath, "gakumas-local");
        var root = Path.Combine(data, "re");
        host = new PluginHost(this, message => Log.LogInfo(message));
        host.Start(Path.Combine(root, "plugins"), Path.Combine(root, "profile.json"), data);
    }

    public override bool Unload()
    {
        host?.Dispose();
        host = null;
        return true;
    }

    public Type? FindType(string assembly, string fullName)
    {
        Assembly interop;
        try { interop = Assembly.Load(new AssemblyName(assembly)); }
        catch (FileNotFoundException) { return null; }
        // Il2CppInterop can prefix assemblies/types that overlap .NET namespaces.
        return interop.GetType(fullName) ?? interop.GetType("Il2Cpp" + fullName);
    }

    public IDisposable Patch(string owner, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, int priority)
    {
        var harmony = new Harmony("gkms.localify.re." + owner);
        try
        {
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(prefix) { priority = priority },
                postfix == null ? null : new HarmonyMethod(postfix) { priority = priority });
            return new Registration(harmony, target, prefix, postfix);
        }
        catch
        {
            new Registration(harmony, target, prefix, postfix).Dispose();
            throw;
        }
    }

    private sealed record Registration(Harmony Harmony, MethodInfo Target, MethodInfo? Prefix, MethodInfo? Postfix) : IDisposable
    {
        public void Dispose()
        {
            if (Prefix != null) Harmony.Unpatch(Target, Prefix);
            if (Postfix != null) Harmony.Unpatch(Target, Postfix);
        }
    }
}
