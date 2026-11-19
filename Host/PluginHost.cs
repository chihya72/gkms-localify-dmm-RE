using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Gkms.Sdk;

namespace Gkms.Host;

public interface IGameServices
{
    Type? FindType(string assembly, string fullName);
    IDisposable Patch(string owner, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, int priority);
}

public sealed class PluginHost : IDisposable
{
    private readonly IGameServices game;
    private readonly Action<string> log;
    private readonly List<(ILocalifyPlugin Plugin, Scope Scope, PackageLoader Loader)> running = new();
    public IReadOnlyList<string> Active => running.Select(item => item.Scope.Id).ToArray();

    public PluginHost(IGameServices game, Action<string> log) => (this.game, this.log) = (game, log);

    // Lifecycle operations are called on the Unity thread.
    public void Start(string packages, string profileFile, string data)
    {
        var profile = Catalog.ReadProfile(profileFile);
        var catalog = Catalog.Discover(packages, log);
        foreach (var id in profile.Enabled.Distinct(StringComparer.Ordinal))
        {
            if (running.Any(item => item.Scope.Id == id)) continue;
            if (!catalog.TryGetValue(id, out var package)) { log($"{id}: 找不到插件包。"); continue; }
            if (package.Manifest.ApiVersion != Catalog.ApiVersion) { log($"{id}: SDK 版本不兼容。"); continue; }
            var loader = new PackageLoader(package.Directory);
            var options = profile.Settings?.GetValueOrDefault(id) ?? default;
            var scope = new Scope(id, data, options, game, log);
            ILocalifyPlugin? plugin = null;
            try
            {
                var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package.Directory, package.Manifest.Assembly)));
                var type = assembly.GetType(package.Manifest.EntryPoint, throwOnError: true)!;
                plugin = Activator.CreateInstance(type) as ILocalifyPlugin ?? throw new InvalidDataException("入口未实现 ILocalifyPlugin。");
                plugin.Start(scope);
                running.Add((plugin, scope, loader));
                log($"{id}: 已加载。");
            }
            catch (Exception error)
            {
                log($"{id}: {error.GetBaseException().Message}");
                Stop(plugin, scope, loader);
            }
        }
    }

    private void Stop(ILocalifyPlugin? plugin, Scope scope, PackageLoader loader)
    {
        try { scope.Dispose(); } catch (Exception error) { log($"{scope.Id}: {error.Message}"); }
        try { plugin?.Dispose(); } catch (Exception error) { log($"{scope.Id}: {error.Message}"); }
        loader.Unload();
    }

    public void Dispose()
    {
        for (var i = running.Count - 1; i >= 0; i--)
        {
            var item = running[i];
            Stop(item.Plugin, item.Scope, item.Loader);
        }
        running.Clear();
    }

    private sealed class PackageLoader : AssemblyLoadContext
    {
        private readonly string directory;
        public PackageLoader(string directory) : base(isCollectible: true) => this.directory = Path.GetFullPath(directory);
        protected override Assembly? Load(AssemblyName name)
        {
            if (AssemblyName.ReferenceMatchesDefinition(name, typeof(ILocalifyPlugin).Assembly.GetName()))
                return typeof(ILocalifyPlugin).Assembly;
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private sealed class Scope : IPluginContext, IDisposable
    {
        private readonly IGameServices game;
        private readonly Action<string> log;
        private readonly Stack<IDisposable> patches = new();
        public string Id { get; }
        public string DataDirectory { get; }
        public JsonElement Settings { get; }
        public Scope(string id, string data, JsonElement settings, IGameServices game, Action<string> log) =>
            (Id, DataDirectory, Settings, this.game, this.log) = (id, Path.GetFullPath(data), settings, game, log);
        public Type? FindType(string assembly, string fullName) => game.FindType(assembly, fullName);
        public void Log(string message) => log($"{Id}: {message}");
        public void Patch(MethodInfo target, MethodInfo? prefix = null, MethodInfo? postfix = null, int priority = 400) =>
            patches.Push(game.Patch(Id, target, prefix, postfix, priority));
        public void Dispose()
        {
            while (patches.TryPop(out var patch))
            {
                try { patch.Dispose(); } catch (Exception e) { Log($"移除回调失败: {e.Message}"); }
            }
        }
    }
}
