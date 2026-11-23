using System.Reflection;

namespace Gkms.Sdk;

/// <summary>Game-owned values observed before optional plugins change them. Lives with the host,
/// so toggling a plugin does not discard the current scene's unmodified settings.</summary>
public static class LiveGameState
{
    public sealed record Call(object Instance, MethodInfo Method, object[] Arguments, long Sequence = 0);
    private static long sequence;
    private static readonly Dictionary<(object, string), Call> calls = new();
    private static readonly object sync = new();
    [ThreadStatic] private static int replay;
    public static bool IsReplaying => replay != 0;
    public static int TextResourcesRevision { get; set; }
    // Native overlay owns this callback; plugins sample it without accessing ImGui across threads.
    public static Func<bool>? InputCapture { get; set; }
    public static IDisposable Replay() { replay++; return new ReplayScope(); }
    private sealed class ReplayScope : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (!disposed) { replay--; disposed = true; } }
    }
    public static void Record(object instance, MethodInfo method, object[] arguments)
    {
        if (IsReplaying) return;
        lock (sync) calls[(instance, method.Name)] = new(instance, method, arguments.ToArray(), ++sequence);
    }
    public static Call[] Calls(string method) { lock (sync) return calls.Values.Where(c => c.Method.Name == method).ToArray(); }
    public static Call[] Calls() { lock (sync) return calls.Values.OrderBy(c => c.Sequence).ToArray(); }
    public static void Clear()
    {
        lock (sync)
        {
            calls.Clear(); sequence = 0;
        }
    }
    public static object[] FindObjects(IPluginContext context, Type? type, bool assets = false)
    {
        if (type == null) return Array.Empty<object>();
        var find = assets ? context.Find("UnityEngine.CoreModule", "UnityEngine.Resources", "FindObjectsOfTypeAll", "System.Type") : null;
        find ??= context.Find("UnityEngine.CoreModule", "UnityEngine.Object", "FindObjectsOfType", "System.Type", "System.Boolean");
        if (find == null) return Array.Empty<object>();
        var args = find.GetParameters().Length == 1 ? new[] { Interop.NativeTypeObject(type) } : new[] { Interop.NativeTypeObject(type), true };
        return Interop.Items(find.Invoke(null, args)).ToArray();
    }
}
