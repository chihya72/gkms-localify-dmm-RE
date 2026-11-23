namespace Gkms.Sdk;

// Each component owns its callbacks so a failure can be rolled back independently.
public sealed class PluginComponents : IDisposable
{
    private readonly Stack<(IPluginScope Scope, ILocalifyPlugin Plugin)> running = new();
    private readonly Stack<(IPluginScope Scope, ILocalifyPlugin Plugin)> cleanupPending = new();
    public int Count => running.Count;
    private readonly Dictionary<string, string> failures = new();
    public IReadOnlyList<string> Failures => failures.Select(p => p.Key + ": " + p.Value).ToArray();
    public IReadOnlyList<string> Warnings => Failures.Concat(running.SelectMany(p =>
        (p.Plugin as IPluginDiagnostics)?.Warnings ?? Array.Empty<string>())).ToArray();

    public void Start(IPluginContext context, ILocalifyPlugin component, string name)
    {
        if (cleanupPending.Count != 0) throw new InvalidOperationException("先前组件的恢复尚未完成，请先重试停用。 ");
        var scope = ((IPluginScope)context).OpenScope();
        try { component.Start(scope); running.Push((scope, component)); failures.Remove(name); }
        catch (Exception error)
        {
            failures[name] = error.GetBaseException().Message;
            var failed = false;
            try { scope.Dispose(); }
            catch (Exception cleanup) { failed = true; context.Log($"{name} 回调清理失败: {cleanup.GetBaseException().Message}"); }
            context.Log($"{name} 加载失败: {error.GetBaseException().Message}");
            try { component.Dispose(); }
            catch (Exception cleanup) { failed = true; context.Log($"{name} 清理失败: {cleanup.GetBaseException().Message}"); }
            if (failed)
            {
                cleanupPending.Push((scope, component));
                throw new InvalidOperationException($"{name} 启动失败且恢复未完成，必须先重试清理。", error);
            }
        }
    }

    public void Dispose()
    {
        List<Exception>? errors = null;
        var retry = new List<(IPluginScope Scope, ILocalifyPlugin Plugin)>();
        while (cleanupPending.TryPop(out var component) || running.TryPop(out component))
        {
            var failed = false;
            try { component.Scope.Dispose(); }
            catch (Exception error) { failed = true; (errors ??= new()).Add(error); }
            try { component.Plugin.Dispose(); }
            catch (Exception error) { failed = true; (errors ??= new()).Add(error); }
            if (failed) retry.Add(component);
        }
        // The host retains a plugin whose cleanup failed. Keep its failed children
        // too, so the next disable really retries restoration before unloading.
        for (var i = retry.Count - 1; i >= 0; i--) cleanupPending.Push(retry[i]);
        failures.Clear();
        if (errors != null) throw new AggregateException(errors);
    }
}
