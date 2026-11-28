using Gkms.Sdk;
using ImGuiNET;

namespace Example.Greeting;

public sealed class ReloadProbe : ILocalifyPlugin, IResourceReloadable, ISettingsReloadable
{
    private IPluginContext context = null!;
    private string message = "";
    public void Start(IPluginContext context) { this.context = context; message = context.Option("message", "initial"); }
    public void ReloadSettings(System.Text.Json.JsonElement settings)
    {
        var next = settings.GetProperty("message").GetString()!;
        if (next == "reject") throw new InvalidDataException("测试参数拒绝应用。");
        message = next; context.Log("参数已重载: " + next + ";startup=" + settings.Option("startup", false));
    }
    public void ReloadResources()
    {
        if (message == "resource-failure") throw new IOException("测试资源读取失败。");
        context.Log("示例资源重载入口。");
    }
    public void Dispose() { }
}

public sealed class ProbeSettingsUi : IPluginSettingsUi
{
    private ISettingsUiContext context = null!;
    private bool open;
    public void Initialize(ISettingsUiContext context)
    {
        this.context = context;
        context.Log("ui-init");
        context.Own(new UiRelease(() => context.Log("ui-owned:" + context.Stopping.IsCancellationRequested)));
        context.UiBuilder.OpenConfigUi += Open;
        context.UiBuilder.Draw += Draw;
        if (Flag("throwInit")) throw new InvalidOperationException("ui-init-failure");
    }
    private bool Flag(string name) => context.Settings.ValueKind == System.Text.Json.JsonValueKind.Object &&
        context.Settings.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;
    private void Open() { open = true; context.Log("ui-open"); }
    private void Draw()
    {
        if (!open) throw new Exception("OpenConfigUi was not raised");
        ImGui.TextUnformatted("Independent plugin UI");
        if (ImGui.Button("Save from ImGui")) context.SaveSettings(new { message = "edited-by-imgui", logOnStart = false });
        var position = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        context.Log(FormattableString.Invariant($"button:{position.X},{position.Y}"));
        if (Flag("throwDraw")) { ImGui.Begin("unbalanced-plugin-window"); throw new InvalidOperationException("ui-draw-failure"); }
    }
    public void Dispose()
    {
        if (context == null) return;
        context.UiBuilder.Draw -= Draw; context.UiBuilder.OpenConfigUi -= Open;
        context.Log("ui-disposed");
        if (Flag("throwDispose")) throw new InvalidOperationException("ui-dispose-failure");
    }
    private sealed record UiRelease(Action Callback) : IDisposable { public void Dispose() => Callback(); }
}

// Integration-test entry, deliberately separate from the example's normal manifest entry.
public sealed class ContractProbe : ILocalifyPlugin
{
    private IPluginServices? services;
    private bool disposeThrows;
    public void Start(IPluginContext context)
    {
        var s = services = context.Services();
        disposeThrows = context.Option("disposeThrows", false);
        s.Log("identity=" + s.Id + ";package=" + Path.GetFileName(s.PackageDirectory));
        s.Log("storage=" + s.StorageDirectory);
        s.Own(new Release(() => s.Log("owned-released:" + s.Stopping.IsCancellationRequested)));
        var child = ((IPluginServices)s.OpenScope());
        child.Own(new Release(() => s.Log("child-released:" + child.Stopping.IsCancellationRequested)));
        s.Own(s.Stopping.Register(() =>
        {
            s.Log("cancellation");
            if (context.Option("cancelThrows", false)) throw new InvalidOperationException("cancel failure");
        }));
        s.Own(new Release(() =>
        {
            s.Log("last-owned");
            if (context.Option("resourceThrows", false)) throw new InvalidOperationException("resource failure");
        }));
        var method = context.Find("fixture", "ProbeTarget", "Ping")!;
        s.Patch(method, postfix: GameMethods.Handler<ContractProbe>(nameof(Ping)));
        s.Enqueue(() => s.Log("queued"));
        var rejected = Task.Run(() =>
        {
            try { s.OpenScope(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        if (!rejected) throw new Exception("worker registration was accepted");
        if (context.Option("fail", false)) throw new InvalidOperationException("startup failure");
    }
    public static void Ping() { }
    public void Dispose()
    {
        if (services is not { } s) return;
        services = null;
        try { s.Patch(s.Find("fixture", "ProbeTarget", "Ping")!, postfix: GameMethods.Handler<ContractProbe>(nameof(Ping))); }
        catch (ObjectDisposedException) { s.Log("stopped-registration-rejected"); }
        s.Log("disposed");
        if (disposeThrows) throw new InvalidOperationException("dispose failure");
    }
    private sealed class Release(Action action) : IDisposable
    {
        private Action? release = action;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
