using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Gkms.Sdk;

namespace Gkms.Host;

public interface IGameServices
{
    Type? FindType(string assembly, string fullName);
    IDisposable Patch(string owner, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, int priority);
}
public interface IAdvancedGameServices
{
    IDisposable PatchAdvanced(string owner, MethodBase target, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? finalizer, int priority);
}

public sealed class PluginHost : IDisposable
{
    private readonly IGameServices game;
    private readonly Action<string> log;
    private sealed record Running(ILocalifyPlugin Plugin, Scope Scope, PluginPackageLoader Loader, Package Package);
    private readonly List<Running> running = new();
    private readonly Dictionary<string, string> cleanupErrors = new();
    private sealed record PendingCleanup(IPluginCleanup Plugin, Scope Scope, PluginPackageLoader? Loader, DateTime Deadline);
    private readonly Dictionary<string, PendingCleanup> finishing = new();
    private readonly Dictionary<string, string> finishingWarnings = new();
    private readonly PluginComponents framework = new();
    private readonly ConcurrentQueue<Action> pending = new();
    private Scope? frameworkScope;
    private RuntimeControl? runtime;
    private string packages = "", profileFile = "", data = "";
    private bool profileApplied;
    private readonly Dictionary<string, string> restartPending = new();
    private readonly Dictionary<string, string> loadErrors = new(), resourceErrors = new();
    private HashSet<string> knownPlugins = new();
    public IReadOnlyList<string> Active => running.Where(item => !cleanupErrors.ContainsKey(item.Scope.Id)).Select(item => item.Scope.Id).ToArray();

    public PluginHost(IGameServices game, Action<string> log) => (this.game, this.log) = (game, log);

    // Lifecycle operations are called on the Unity thread.
    public void Start(string packages, string profileFile, string data)
    {
        this.packages = Path.GetFullPath(packages);
        this.profileFile = Path.GetFullPath(profileFile);
        this.data = Path.GetFullPath(data);
        if (frameworkScope == null)
        {
            frameworkScope = new Scope("framework", data, default, game, log, pending.Enqueue);
            framework.Start(frameworkScope, new UnityLogForwarder(), "Unity 日志");
            framework.Start(frameworkScope, new LiveStateObserver(), "当前场景原始状态");
            framework.Start(frameworkScope, new BuiltinFont(), "内置补全字体");
        }
        ApplyProfile(false);
    }

    public void EnableRuntimeControl()
    {
        if (runtime != null) return;
        if (frameworkScope == null) throw new InvalidOperationException("宿主尚未启动。");
        runtime = new RuntimeControl(this, data, profileFile, log);
        try { runtime.Start(frameworkScope); }
        catch { runtime.Dispose(); runtime = null; throw; }
    }

    public void Pump()
    {
        for (var count = 0; count < 128 && pending.TryDequeue(out var action); count++)
        {
            try { action(); } catch (Exception error) { log("[Error] "+error.GetBaseException().Message); }
        }
        foreach (var (id, item) in finishing.ToArray())
        {
            try
            {
                if (DateTime.UtcNow >= item.Deadline) throw new TimeoutException("停用后的恢复操作超时。");
                if (item.Plugin.Update()) FinishCleanup(id);
            }
            catch (Exception error)
            {
                FinishCleanup(id);
                finishingWarnings[id] = error.GetBaseException().Message;
                log($"[Error] {id}: 停用后的恢复未完成: {finishingWarnings[id]}");
            }
        }
    }
    internal void Enqueue(Action action) => pending.Enqueue(action);
    public void ReloadProfile() => ApplyProfile(true);
    public void ReloadResources(string id)
    {
        var item = running.SingleOrDefault(p => p.Scope.Id == id && !cleanupErrors.ContainsKey(id))
            ?? throw new InvalidOperationException($"{id} 尚未启用。");
        if (!item.Package.Manifest.CanReloadResources || item.Plugin is not IResourceReloadable reloadable)
            throw new InvalidOperationException($"{id} 不支持主动资源重载；资源生效时机见插件说明。");
        try
        {
            reloadable.ReloadResources();
            resourceErrors.Remove(id);
            log($"{id}: 资源重载完成。");
        }
        catch (Exception error) { resourceErrors[id] = error.GetBaseException().Message; throw; }
    }

    private void ApplyProfile(bool reportFailure)
    {
        // Parse the complete request before touching live callbacks.
        var catalog = Catalog.Discover(packages, message => log("[Warning] " + message));
        var profile = Catalog.ReadProfile(profileFile, catalog);
        var enabled = profile.Enabled.Distinct(StringComparer.Ordinal).ToArray();
        var initial = !profileApplied;
        profileApplied = true;
        knownPlugins = catalog.Keys.Union(enabled).ToHashSet();
        restartPending.Clear();
        loadErrors.Clear();
        var errors = new List<string>();
        foreach (var item in running.Where(p => !enabled.Contains(p.Scope.Id)).Reverse().ToArray())
        {
            if (!item.Package.Manifest.SupportsHotDisable)
            { restartPending[item.Scope.Id] = "停用已保存，下次启动游戏时生效；当前仍在运行。"; continue; }
            if (Stop(item.Plugin, item.Scope, item.Loader, allowCleanup: true))
            { running.Remove(item); resourceErrors.Remove(item.Scope.Id); }
            else errors.Add(item.Scope.Id);
        }
        foreach (var id in enabled)
        {
            if (!catalog.TryGetValue(id, out var package)) { loadErrors[id] = "找不到兼容的插件包。"; log($"[Error] {id}: {loadErrors[id]}"); errors.Add(id); continue; }
            if (!Catalog.Supports(package.Manifest.ApiVersion)) { loadErrors[id] = "SDK 版本不兼容。"; log($"[Error] {id}: {loadErrors[id]}"); errors.Add(id); continue; }
            var options = profile.Settings?.GetValueOrDefault(id) ?? default;
            try { PluginSettings.Validate(package.Manifest.Settings, options); }
            catch (Exception error) { loadErrors[id] = error.GetBaseException().Message; log($"[Error] {id}: {loadErrors[id]}"); errors.Add(id); continue; }
            var previous = running.SingleOrDefault(p => p.Scope.Id == id);
            var samePackage = previous != null && previous.Package.Directory == package.Directory &&
                Equivalent(JsonSerializer.SerializeToElement(previous.Package.Manifest, Catalog.Json), JsonSerializer.SerializeToElement(package.Manifest, Catalog.Json));
            if (samePackage && previous != null)
            {
                JsonObject? effective = null;
                foreach (var field in previous.Package.Manifest.Settings.Where(field => field.RequiresRestart))
                {
                    var hasOld = Options(previous.Scope.Settings).TryGetProperty(field.Key, out var oldValue);
                    var hasNew = Options(options).TryGetProperty(field.Key, out var newValue);
                    if (Equivalent(hasOld ? oldValue : field.Default, hasNew ? newValue : field.Default)) continue;
                    restartPending[id] = "参数已保存，下次启动游戏时生效。";
                    effective ??= JsonNode.Parse(Options(options).GetRawText())!.AsObject();
                    if (hasOld) effective[field.Key] = JsonNode.Parse(oldValue.GetRawText());
                    else effective.Remove(field.Key);
                }
                if (effective != null) options = JsonSerializer.SerializeToElement(effective);
            }
            var sameSettings = previous != null && Equivalent(Options(previous.Scope.Settings), Options(options));
            if (samePackage && sameSettings && !cleanupErrors.ContainsKey(id)) continue;
            if (previous == null && !initial && !package.Manifest.SupportsHotEnable)
            { restartPending[id] = "启用已保存，下次启动游戏时生效；当前未运行。"; continue; }
            if (previous != null)
            {
                var policy = previous.Package.Manifest;
                if (!sameSettings && !policy.SupportsSettingsReload)
                { restartPending[id] = "参数已保存，下次启动游戏时生效。"; continue; }
                if (samePackage && !sameSettings && previous.Plugin is ISettingsReloadable settingsReloadable && !cleanupErrors.ContainsKey(id))
                {
                    try { settingsReloadable.ReloadSettings(options); previous.Scope.Settings = options.ValueKind == JsonValueKind.Undefined ? default : options.Clone(); }
                    catch (Exception error) { loadErrors[id] = error.GetBaseException().Message; errors.Add(id); }
                    continue;
                }
                if (!policy.SupportsHotDisable || !package.Manifest.SupportsHotEnable)
                { restartPending[id] = "插件变更已保存，下次启动游戏时生效。"; continue; }
                if (!Stop(previous.Plugin, previous.Scope, previous.Loader)) { errors.Add(id); continue; }
                running.Remove(previous);
            }
            if (!Load(package, options))
            {
                errors.Add(id);
                if (previous != null && !cleanupErrors.ContainsKey(id) && Load(previous.Package, previous.Scope.Settings)) log($"{id}: 已恢复上一次有效参数。");
            }
        }
        if (reportFailure && errors.Count != 0) throw new InvalidOperationException("部分插件未能应用新配置: " + string.Join(", ", errors));
        static JsonElement Options(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? JsonSerializer.SerializeToElement(new { }) : value;
    }

    private static bool Equivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() &&
                a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var value) && Equivalent(p.Value, value)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equivalent(p.First, p.Second)),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x == y : a.GetDouble() == b.GetDouble(),
            _ => true
        };
    }

    private bool Load(Package package, JsonElement options)
    {
        var id = package.Manifest.Id;
        FinishCleanup(id);
        PluginPackageLoader? loader = null;
        var scope = new Scope(id, data, options, game, log, pending.Enqueue, package.Directory);
        ILocalifyPlugin? plugin = null;
        try
        {
            loader = new PluginPackageLoader(package);
            var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package.Directory, package.Manifest.Assembly)));
            var type = assembly.GetType(package.Manifest.EntryPoint, throwOnError: true)!;
            ValidateEntry(type, typeof(ILocalifyPlugin));
            plugin = Activator.CreateInstance(type) as ILocalifyPlugin ?? throw new InvalidDataException("入口未实现 ILocalifyPlugin。");
            if (package.Manifest.CanReloadResources && plugin is not IResourceReloadable)
                throw new InvalidDataException("声明了 canReloadResources 的入口必须实现 IResourceReloadable。");
            if (package.Manifest.SupportsSettingsReload && (!package.Manifest.SupportsHotEnable || !package.Manifest.SupportsHotDisable) && plugin is not ISettingsReloadable)
                throw new InvalidDataException("不支持热启停但声明可重载参数的入口必须实现 ISettingsReloadable。");
            plugin.Start(scope);
            running.Add(new(plugin, scope, loader, package));
            resourceErrors.Remove(id);
            var warnings = (plugin as IPluginDiagnostics)?.Warnings;
            log(warnings?.Count > 0 ? $"[Warning] {id}: 插件报告问题：{string.Join("；", warnings)}" : $"{id}: 已加载。");
            return true;
        }
        catch (Exception error)
        {
            loadErrors[id] = error.GetBaseException().Message;
            log($"[Error] {id}: {error.GetBaseException().Message}");
            if (!Stop(plugin, scope, loader) && plugin != null && loader != null) running.Add(new(plugin, scope, loader, package));
            return false;
        }
    }

    public string DescribeStatus()
    {
        var details = GetErrors().SelectMany(p => p.Value.Select(w => p.Key + ": " + w)).ToArray();
        var active = "已启用: " + (Active.Count == 0 ? "无" : string.Join(", ", Active));
        if (restartPending.Count != 0) active += "\n待重启: " + string.Join(", ", restartPending.Keys);
        return details.Length == 0 ? active : active + "\n状态说明：\n" + string.Join("\n", details);
    }

    private Dictionary<string, string[]> GetErrors()
    {
        var issues = running.SelectMany(p => ((p.Plugin as IPluginDiagnostics)?.Warnings ?? Array.Empty<string>())
            .Select(message => new KeyValuePair<string, string>(p.Scope.Id, message)))
            .Concat(loadErrors).Concat(resourceErrors)
            .Concat(cleanupErrors.Select(p => new KeyValuePair<string, string>(p.Key, "恢复尚未完成，请重试停用。" + p.Value)))
            .Concat(finishingWarnings)
            .Concat(framework.Warnings.Select(message => new KeyValuePair<string, string>("framework", message)));
        return issues.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.Value).Distinct().ToArray());
    }
    public Dictionary<string, string[]> GetWarnings() => GetErrors().SelectMany(p => p.Value.Select(message => new KeyValuePair<string, string>(p.Key, message)))
        .Concat(restartPending).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.Value).Distinct().ToArray());

    public Dictionary<string, PluginRuntimeStatus> GetStates()
    {
        var failures = GetErrors();
        var active = Active.ToHashSet();
        return knownPlugins.Union(active).Union(failures.Keys).Union(restartPending.Keys).ToDictionary(id => id, id =>
            failures.TryGetValue(id, out var issues) ? new PluginRuntimeStatus(PluginRunState.Failed, active.Contains(id), issues) :
            restartPending.TryGetValue(id, out var restart) ? new(PluginRunState.RestartRequired, active.Contains(id), new[] { restart }) :
            new(active.Contains(id) ? PluginRunState.Running : PluginRunState.Disabled, active.Contains(id), Array.Empty<string>()));
    }

    private static void ValidateEntry(Type type, Type contract)
    {
        if (!type.IsVisible || type.IsAbstract || type.ContainsGenericParameters || !contract.IsAssignableFrom(type) ||
            type.GetConstructor(Type.EmptyTypes) == null)
            throw new InvalidDataException($"{type.FullName}: 入口须为实现 {contract.Name} 的公开非抽象类，并提供公开无参构造函数。");
    }

    private bool Stop(ILocalifyPlugin? plugin, Scope scope, PluginPackageLoader? loader, bool allowCleanup = false)
    {
        try { scope.Dispose(); } catch (Exception error) { log($"[Error] {scope.Id}: {error.Message}"); }
        try { plugin?.Dispose(); }
        catch (Exception error)
        {
            cleanupErrors[scope.Id] = error.GetBaseException().Message;
            log($"[Error] {scope.Id}: 停用恢复未完成，保留插件和原状态供重试: {error.Message}");
            return false;
        }
        cleanupErrors.Remove(scope.Id);
        if (allowCleanup && plugin is IPluginStopCleanup deferred)
        {
            Scope? cleanupScope = null;
            IPluginCleanup? cleanup = null;
            try
            {
                cleanup = deferred.CreateStopCleanup();
                if (cleanup != null)
                {
                    FinishCleanup(scope.Id);
                    cleanupScope = new Scope(scope.Id + ".cleanup", data, scope.Settings, game, log, pending.Enqueue, scope.PackageDirectory);
                    cleanup.Start(cleanupScope);
                    finishing[scope.Id] = new(cleanup, cleanupScope, loader, DateTime.UtcNow.AddSeconds(30));
                    log($"{scope.Id}: 功能已关闭，正在执行停用后的数据刷新。");
                    return true;
                }
            }
            catch (Exception error)
            {
                cleanupScope?.Dispose();
                try { cleanup?.Dispose(); } catch (Exception cleanupError) { log("[Error] "+cleanupError.GetBaseException().Message); }
                finishingWarnings[scope.Id] = error.GetBaseException().Message;
                log($"[Error] {scope.Id}: 停用后的恢复未完成: {finishingWarnings[scope.Id]}");
            }
        }
        loader?.Unload();
        return true;
    }

    private void FinishCleanup(string id)
    {
        finishingWarnings.Remove(id);
        if (!finishing.Remove(id, out var item)) return;
        try { item.Scope.Dispose(); }
        finally
        {
            try { item.Plugin.Dispose(); }
            finally { item.Loader?.Unload(); }
        }
    }

    public void Dispose()
    {
        runtime?.Dispose(); runtime = null;
        foreach (var id in finishing.Keys.ToArray()) FinishCleanup(id);
        finishingWarnings.Clear();
        for (var i = running.Count - 1; i >= 0; i--)
        {
            var item = running[i];
            Stop(item.Plugin, item.Scope, item.Loader);
        }
        running.Clear();
        profileApplied = false; restartPending.Clear(); loadErrors.Clear(); resourceErrors.Clear(); knownPlugins.Clear();
        frameworkScope?.Dispose();
        frameworkScope = null;
        framework.Dispose();
        pending.Clear();
    }

    public static async Task<int> UpdateResourcesAsync(Package package, string dataDirectory, string? source)
    {
        var manifest = package.Manifest;
        if (!Catalog.Supports(manifest.ApiVersion) || string.IsNullOrWhiteSpace(manifest.ResourceUpdater))
            throw new InvalidOperationException($"{manifest.Name} 没有兼容的资源更新入口。");
        var loader = new PluginPackageLoader(package);
        try
        {
            var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package.Directory, manifest.Assembly)));
            var type = assembly.GetType(manifest.ResourceUpdater, throwOnError: true)!;
            ValidateEntry(type, typeof(IResourceUpdater));
            var updater = Activator.CreateInstance(type) as IResourceUpdater ?? throw new InvalidDataException("资源更新入口未实现 IResourceUpdater。");
            return await updater.UpdateAsync(Path.GetFullPath(dataDirectory), source).ConfigureAwait(false);
        }
        finally { loader.Unload(); }
    }

    public static Task<ResourceRelease> CheckResourcesAsync(Package package, string dataDirectory, string? source,
        CancellationToken cancellationToken = default) => WithUpdater(package,
            updater => updater.CheckAsync(Path.GetFullPath(dataDirectory), source, cancellationToken));

    public static Task<int> InstallResourcesAsync(Package package, string dataDirectory, ResourceRelease release,
        IProgress<ResourceProgress>? progress = null, CancellationToken cancellationToken = default) => WithUpdater(package,
            updater => updater.InstallAsync(Path.GetFullPath(dataDirectory), release, progress, cancellationToken));

    private static async Task<T> WithUpdater<T>(Package package, Func<IResourceUpdateService, Task<T>> action)
    {
        if (!Catalog.Supports(package.Manifest.ApiVersion) || string.IsNullOrWhiteSpace(package.Manifest.ResourceUpdater))
            throw new InvalidOperationException("插件没有兼容的资源更新入口。");
        var loader = new PluginPackageLoader(package);
        try
        {
            var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package.Directory, package.Manifest.Assembly)));
            var type = assembly.GetType(package.Manifest.ResourceUpdater, true)!;
            ValidateEntry(type, typeof(IResourceUpdateService));
            var updater = Activator.CreateInstance(type) as IResourceUpdateService
                ?? throw new InvalidDataException("资源更新入口不支持版本检查。");
            return await action(updater).ConfigureAwait(false);
        }
        finally { loader.Unload(); }
    }

    private sealed class Scope : IPluginServices
    {
        private readonly IGameServices game;
        private readonly Action<string> log;
        private readonly List<IDisposable> patches = new();
        private readonly Action<Action> enqueue;
        private readonly CancellationTokenSource stopping = new();
        private Action? remove;
        private volatile bool disposed;
        private readonly int thread = Environment.CurrentManagedThreadId;
        public CancellationToken Stopping { get; }
        public string Id { get; }
        public string PackageDirectory { get; }
        public string StorageDirectory
        {
            get { CheckThread(); return Directory.CreateDirectory(Path.Combine(DataDirectory, "re", "data", Id)).FullName; }
        }
        public string DataDirectory { get; }
        public JsonElement Settings { get; set; }
        public Scope(string id, string data, JsonElement settings, IGameServices game, Action<string> log, Action<Action> enqueue, string? package = null)
        {
            (Id, DataDirectory, Settings, this.game, this.log, this.enqueue) = (id, Path.GetFullPath(data), settings, game, log, enqueue);
            PackageDirectory = Path.GetFullPath(package ?? data);
            Stopping = stopping.Token;
        }
        private void CheckThread()
        {
            if (disposed) throw new ObjectDisposedException(Id);
            if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("请通过 Enqueue 在 Unity 线程注册或清理资源。");
        }
        public T Own<T>(T resource) where T : IDisposable
        {
            CheckThread(); ArgumentNullException.ThrowIfNull(resource); patches.Add(resource); return resource;
        }
        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (disposed) throw new ObjectDisposedException(Id);
            enqueue(() => { if (!disposed) action(); });
        }
        public IPluginScope OpenScope()
        {
            CheckThread();
            var child = new Scope(Id, DataDirectory, Settings, game, log, enqueue, PackageDirectory);
            child.remove = () => patches.Remove(child);
            patches.Add(child);
            return child;
        }
        public Type? FindType(string assembly, string fullName) => game.FindType(assembly, fullName);
        public void Log(string message) => log(PluginLog.WithSource(message, Id));
        public void Patch(MethodInfo target, MethodInfo? prefix = null, MethodInfo? postfix = null, int priority = 400)
        {
            CheckThread(); patches.Add(game.Patch(Id, target, prefix, postfix, priority));
        }
        public void PatchAdvanced(MethodBase target, MethodInfo? prefix = null, MethodInfo? postfix = null,
            MethodInfo? finalizer = null, int priority = 400)
        {
            CheckThread(); patches.Add(game is IAdvancedGameServices advanced
                ? advanced.PatchAdvanced(Id, target, prefix, postfix, finalizer, priority)
                : target is MethodInfo method && finalizer == null ? game.Patch(Id, method, prefix, postfix, priority)
                : throw new NotSupportedException("接入层未提供构造函数与异常回调服务。"));
        }
        public void Dispose()
        {
            if (disposed) return;
            CheckThread();
            disposed = true;
            try { stopping.Cancel(); } catch (Exception error) { Log($"[Error] 取消后台工作失败: {error.GetBaseException().Message}"); }
            foreach (var patch in patches.AsEnumerable().Reverse().ToArray())
            {
                try { patch.Dispose(); } catch (Exception e) { Log($"[Error] 移除回调失败: {e.Message}"); }
            }
            patches.Clear(); remove?.Invoke(); remove = null;
            stopping.Dispose();
        }
    }
}
