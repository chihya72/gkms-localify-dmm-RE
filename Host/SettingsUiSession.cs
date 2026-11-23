using System.Collections.Concurrent;
using System.Text.Json;
using Gkms.Sdk;
using ImGuiNET;

namespace Gkms.Host;

/// <summary>A settings-only plugin instance. Does not construct or start the game entry point.</summary>
public sealed class SettingsUiSession : IDisposable
{
    private readonly Context context;
    private PluginPackageLoader? loader;
    private IPluginSettingsUi? plugin;
    public string? Fault { get; private set; }
    public ISettingsUiContext Services => context;
    private bool disposed;

    public SettingsUiSession(Package package, string language, Func<JsonElement> read,
        Action<JsonElement, bool> save, Func<CancellationToken, Task<string>> reload, Action<string> log)
    {
        if (!Catalog.Supports(package.Manifest.ApiVersion)) throw new InvalidDataException("插件 API 不匹配。");
        context = new(package, language, read, save, reload, log);
        try
        {
            if (string.IsNullOrWhiteSpace(package.Manifest.SettingsUi)) plugin = new SchemaSettingsUi();
            else
            {
                loader = new PluginPackageLoader(package);
                var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package.Directory, package.Manifest.Assembly)));
                var type = assembly.GetType(package.Manifest.SettingsUi, true)!;
                if (!type.IsVisible || type.IsAbstract || type.ContainsGenericParameters ||
                    !typeof(IPluginSettingsUi).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidDataException("settingsUi 必须是公开、无参并实现 IPluginSettingsUi 的类型。");
                plugin = (IPluginSettingsUi)Activator.CreateInstance(type)!;
            }
            plugin.Initialize(context);
        }
        catch { Dispose(); throw; }
    }

    public void OpenConfigUi()
    {
        context.CheckThread();
        if (disposed) throw new ObjectDisposedException(nameof(SettingsUiSession));
        if (Fault != null) return;
        try { context.Open(); }
        catch (Exception error) { Fail(error); }
    }

    /// <summary>Call between ImGui.NewFrame and Render, inside the manager's settings window.</summary>
    public bool DrawFrame()
    {
        context.CheckThread();
        if (disposed) throw new ObjectDisposedException(nameof(SettingsUiSession));
        if (Fault != null) return false;
        if (ImGui.GetCurrentContext() == IntPtr.Zero) throw new InvalidOperationException("绘制需要宿主 ImGui 上下文。");
        try { context.Render(); return true; }
        catch (Exception error) { Fail(error); return false; }
    }
    public bool DrawResourceSettings()
    {
        context.CheckThread();
        if (disposed) throw new ObjectDisposedException(nameof(SettingsUiSession));
        if (Fault != null) return false;
        try { if (plugin is SchemaSettingsUi schema) schema.DrawResourceSettings(); return true; }
        catch (Exception error) { Fail(error); return false; }
    }
    private void Fail(Exception error) { Fault = error.GetBaseException().Message; context.Log("设置界面已停止: " + Fault); }

    public void Dispose()
    {
        context.CheckThread();
        if (disposed) return;
        disposed = true;
        context.Stop();
        try { plugin?.Dispose(); } catch (Exception error) { context.Log("设置界面卸载: " + error.Message); }
        plugin = null;
        loader?.Unload(); loader = null;
    }

    private sealed class Context : ISettingsUiContext, IPluginUiBuilder
    {
        private readonly Package package;
        private readonly Func<JsonElement> read;
        private readonly Action<JsonElement, bool> save;
        private readonly Func<CancellationToken, Task<string>> reload;
        private readonly Action<string> log;
        private readonly CancellationTokenSource stop = new();
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly List<IDisposable> owned = new();
        private readonly ConcurrentQueue<Action> pending = new();
        private Action? draw, open;
        private volatile bool stopped;
        public string Id => package.Manifest.Id;
        public string Name => package.Manifest.DisplayName(Language);
        public string Description => package.Manifest.Description ?? "";
        public string Language { get; }
        public JsonElement Settings { get; private set; }
        public IReadOnlyList<PluginSetting> Fields => package.Manifest.Settings;
        public IReadOnlyList<PluginPreset> Presets => package.Manifest.Presets;
        public IPluginUiBuilder UiBuilder => this;
        public CancellationToken Stopping { get; }
        public bool CanReloadResources => package.Manifest.CanReloadResources;
        public Context(Package package, string language, Func<JsonElement> read, Action<JsonElement, bool> save,
            Func<CancellationToken, Task<string>> reload, Action<string> log)
        {
            (this.package, Language, this.read, this.save, this.reload, this.log) = (package, language, read, save, reload, log);
            Settings = read(); Stopping = stop.Token;
        }
        public void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("设置 UI 操作必须在 UI 线程执行；后台请使用 UiBuilder.Enqueue。");
        }
        private void CheckActive() { CheckThread(); if (stopped) throw new ObjectDisposedException(Id); }
        public event Action Draw { add { CheckActive(); draw += value; } remove { CheckThread(); draw -= value; } }
        public event Action OpenConfigUi { add { CheckActive(); open += value; } remove { CheckThread(); open -= value; } }
        public void Open() { CheckActive(); open?.Invoke(); }
        public void Render()
        {
            CheckActive();
            for (var count = 0; count < 128 && pending.TryDequeue(out var action); count++) action();
            draw?.Invoke();
        }
        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (stopped) throw new ObjectDisposedException(Id);
            pending.Enqueue(action);
        }
        public void SaveSettings(JsonElement values) => Save(values, false);
        public void ReloadSettings() { CheckActive(); Settings = read(); }
        public void ReplaceSettings(JsonElement values) => Save(values, true);
        private void Save(JsonElement values, bool replace)
        {
            CheckActive();
            if (values.ValueKind != JsonValueKind.Object) throw new InvalidDataException("配置必须是 JSON 对象。");
            PluginSettings.Validate(Fields, values);
            save(values.Clone(), replace); Settings = read();
        }
        public Task<string> ReloadResourcesAsync()
        {
            CheckActive();
            if (!CanReloadResources) throw new InvalidOperationException("此插件不提供资源重载。");
            return reload(Stopping);
        }
        public T Own<T>(T resource) where T : IDisposable { CheckActive(); ArgumentNullException.ThrowIfNull(resource); owned.Add(resource); return resource; }
        public void Log(string message) => log(Id + ": " + message);
        public void Stop()
        {
            CheckThread(); stopped = true;
            draw = open = null;
            try { stop.Cancel(); } catch (Exception error) { Log("取消设置工作: " + error.GetBaseException().Message); }
            pending.Clear();
            foreach (var resource in owned.AsEnumerable().Reverse())
                try { resource.Dispose(); } catch (Exception error) { Log("释放设置资源: " + error.Message); }
            owned.Clear(); stop.Dispose();
        }
    }
}
