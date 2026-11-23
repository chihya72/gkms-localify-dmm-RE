using System.Reflection;
using System.Text.Json;
using Gkms.Host;

internal static class PluginLifecycleTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string work)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "lifecycle")).FullName;
        var packages = Directory.CreateDirectory(Path.Combine(root, "plugins")).FullName;
        var package = Directory.CreateDirectory(Path.Combine(packages, "example")).FullName;
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "example", "net6.0"));
        foreach (var file in Directory.GetFiles(source, "Gkms.ExamplePlugin.*")) File.Copy(file, Path.Combine(package, Path.GetFileName(file)), true);
        var original = Catalog.ReadManifest(Path.Combine(source, "plugin.json")) with { EntryPoint = "Example.Greeting.ReloadProbe" };
        var manifestFile = Path.Combine(package, "plugin.json");
        var profileFile = Path.Combine(root, "profile.json");
        const string id = "example.greeting";
        var logs = new List<string>();
        void Manifest(Manifest manifest) => File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, Catalog.Json));
        void Profile(bool enabled, string message = "initial") => File.WriteAllText(profileFile,
            JsonSerializer.Serialize(new { enabled = enabled ? new[] { id } : Array.Empty<string>(), settings = new Dictionary<string, object> { [id] = new { message } } }));

        // Install a startup-only package after the host has already started.
        Profile(false);
        using (var host = new PluginHost(new NoGame(), logs.Add))
        {
            host.Start(packages, profileFile, root);
            Manifest(original with { RequiresRestart = true }); Profile(true); host.ReloadProfile();
            Check(!host.Active.Contains(id) && host.GetStates()[id].State == PluginRunState.RestartRequired, "运行中新增插件也必须遵守禁止热启用声明");
            Profile(false); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.Disabled, "撤销待启用变更应清除待重启状态");
        }
        Profile(true);
        using (var host = new PluginHost(new NoGame(), logs.Add))
        {
            host.Start(packages, profileFile, root);
            Check(host.GetStates()[id].State == PluginRunState.Running, "需重启插件在新会话正常启动");
            host.ReloadResources(id);
            Check(logs.Any(s => s.Contains("示例资源重载入口")), "资源重载能力独立于热启停限制");
            Profile(true, "deferred"); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.RestartRequired && !logs.Any(s => s.Contains("参数已重载: deferred")), "禁止热更新的参数保持本次状态");
            Profile(false); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.RestartRequired && host.Active.Contains(id), "禁止热停用的插件延后关闭");
        }

        Manifest(original with { CanHotEnable = true, CanHotDisable = false, CanReloadSettings = true }); Profile(true);
        using (var host = new PluginHost(new NoGame(), logs.Add))
        {
            host.Start(packages, profileFile, root);
            Profile(true, "in-place"); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.Running && logs.Any(s => s.Contains("参数已重载: in-place")), "禁止热停用的插件通过独立接口更新参数");
            Profile(true, "reject");
            try { host.ReloadProfile(); throw new Exception("应拒绝参数"); } catch (InvalidOperationException) { }
            Check(host.GetStates()[id].State == PluginRunState.Failed && host.Active.Contains(id), "参数更新失败可见且保留旧实例");
            Profile(true, "recovered"); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.Running, "重试成功清除旧失败状态");
            Profile(false); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.RestartRequired, "独立参数接口不能绕过热停用限制");
        }

        Manifest(original); Profile(false);
        using (var host = new PluginHost(new NoGame(), logs.Add))
        {
            host.Start(packages, profileFile, root); Profile(true); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.Running, "普通插件支持热启用");
            Profile(true, "resource-failure"); host.ReloadProfile();
            try { host.ReloadResources(id); throw new Exception("应报告资源错误"); } catch (IOException) { }
            Check(host.GetStates()[id].State == PluginRunState.Failed, "资源重载失败进入失败状态");
            Manifest(original with { Version = "9.9.9" }); Profile(true, "new-instance"); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.Running, "成功替换实例清除旧资源错误");
            Profile(false); host.ReloadProfile();
            Check(host.GetStates()[id].State == PluginRunState.Disabled && !host.Active.Contains(id), "普通插件支持热停用");
        }
        Console.WriteLine("生命周期专项通过：运行中安装、独立热启停/参数/资源能力、失败与恢复状态。");
    }
    private sealed class NoGame : IGameServices
    {
        public Type? FindType(string assembly, string fullName) => null;
        public IDisposable Patch(string owner, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, int priority) => throw new NotSupportedException();
    }
}
