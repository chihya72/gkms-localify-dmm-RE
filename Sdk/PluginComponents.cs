namespace Gkms.Sdk;

// Each component owns its callbacks so a failure can be rolled back independently.
public sealed class PluginComponents : IDisposable
{
    private readonly Stack<ILocalifyPlugin> running = new();
    public int Count => running.Count;

    public void Start(IPluginContext context, ILocalifyPlugin component, string name)
    {
        var scope = ((IPluginScope)context).OpenScope();
        try { component.Start(scope); running.Push(component); }
        catch (Exception error)
        {
            scope.Dispose();
            context.Log($"{name} 加载失败: {error.GetBaseException().Message}");
            try { component.Dispose(); }
            catch (Exception cleanup) { context.Log($"{name} 清理失败: {cleanup.GetBaseException().Message}"); }
        }
    }

    public void Dispose()
    {
        List<Exception>? errors = null;
        while (running.TryPop(out var component))
            try { component.Dispose(); }
            catch (Exception error) { (errors ??= new()).Add(error); }
        if (errors != null) throw new AggregateException(errors);
    }
}
