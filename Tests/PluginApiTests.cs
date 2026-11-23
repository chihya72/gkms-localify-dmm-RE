using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gkms.Host;
using Gkms.Sdk;

internal static class PluginApiTests
{
    public static void RunIsolated(string work, string packages, string command = "--plugin-api")
    {
        // ALC unloading is cooperative. End the loader test process before deleting its DLLs on Windows.
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(PluginApiTests).Assembly.Location);
        foreach (var value in new[] { command, packages, work }) start.ArgumentList.Add(value);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new Exception("插件 API 测试超时。"); }
        Console.Write(output.GetAwaiter().GetResult());
        Check(process.ExitCode == 0, errors.GetAwaiter().GetResult());
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static void Invalid(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    public static void Run(string work, string packages)
    {
        Builtins(packages);
        Settings();
        PluginLifecycleTests.Run(work);
        ResourceSourceTests.Run(work, packages);
        ThirdParty(Directory.CreateDirectory(Path.Combine(work, "plugin-api")).FullName);
        PluginUiTests.Run(work, packages);
        Console.WriteLine("插件 API 专项通过：14 个内置清单、独立 SDK 示例、生命周期、异常清理、配置校验。");
    }
    private static void Builtins(string directory)
    {
        var log = new List<string>();
        var packages = Catalog.Discover(directory, log.Add);
        Check(log.Count == 0 && packages.Count == 14, "内置插件清单: " + string.Join("; ", log));
        foreach (var package in packages.Values)
        {
            var m = package.Manifest;
            Check(m.ApiVersion == PluginApi.Current && !string.IsNullOrWhiteSpace(m.Author) && !string.IsNullOrWhiteSpace(m.Description), m.Id + ": API 2 元数据");
            PluginSettings.ValidateDefinition(m.Settings, m.Presets);
            var defaults = JsonSerializer.SerializeToElement(m.Settings.ToDictionary(f => f.Key, f => f.Default));
            PluginSettings.Validate(m.Settings, defaults);
            var loader = new InspectionLoader();
            try
            {
                var assembly = loader.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(package.Directory, m.Assembly)));
                Check(!assembly.GetReferencedAssemblies().Any(a => a.Name is "Gkms.Host" or "Gkms.Manager" or "Gkms.BepInEx" or "Gkms.Runtime" or "0Harmony"), m.Id + ": 插件不得依赖框架实现");
                var type = assembly.GetType(m.EntryPoint, true)!;
                Check(typeof(ILocalifyPlugin).IsAssignableFrom(type) && type.IsVisible && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null, m.Id + ": 统一入口");
                Check(m.CanReloadResources == typeof(IResourceReloadable).IsAssignableFrom(type), m.Id + ": 资源重载声明与接口一致");
                if (m.SettingsUi != null) Check(typeof(IPluginSettingsUi).IsAssignableFrom(assembly.GetType(m.SettingsUi, true)), m.Id + ": 统一 ImGui 设置入口");
                Check(m.HasSettingsUi == (m.Settings.Length > 0), m.Id + ": 无参数内置插件不提供空配置页");
                if (m.ResourceUpdater is { } name)
                {
                    var updater = assembly.GetType(name, true)!;
                    Check(typeof(IResourceUpdateService).IsAssignableFrom(updater) && updater.GetConstructor(Type.EmptyTypes) != null, m.Id + ": 资源更新接口");
                }
            }
            finally { loader.Unload(); }
        }
        PluginSettings.Validate(packages["graphics"].Manifest.Settings, Json("""{"reflectionQuality":99,"lodQuality":-1,"volumeIndex":5}"""));
        Check(!packages["ios-login"].Manifest.HasSettingsUi, "iOS 不显示独立配置");
        var physics = packages["physics"].Manifest;
        var preset = physics.Presets.Single(p => p.Name == "Default");
        foreach (var field in physics.Settings.Where(f => preset.Values.TryGetProperty(f.Key, out _)))
            Check(field.Default.GetRawText() == preset.Values.GetProperty(field.Key).GetRawText() ||
                JsonSerializer.Serialize(field.Default) == JsonSerializer.Serialize(preset.Values.GetProperty(field.Key)), "物理默认值与 Default 预设一致: " + field.Key);
    }
    private static void Settings()
    {
        var fields = new[] {
            new PluginSetting("count", "Count", "integer", Json("3")) { Min = 1, Max = 9 },
            new PluginSetting("mode", "Mode", "string", Json("\"a\"")) { Choices = new[] { "a", "b" } },
            new PluginSetting("offset", "Offset", "number-array", Json("[0,0,0]")),
            new PluginSetting("enabled", "Enabled", "boolean", Json("true"))
        };
        PluginSettings.ValidateDefinition(fields, Array.Empty<PluginPreset>());
        PluginSettings.Validate(fields, Json("""{"count":5,"unknown":{"keep":true}}"""));
        PluginSettings.Validate(fields, default);
        foreach (var invalid in new[] { "[]", "{\"count\":0}", "{\"count\":1.5}", "{\"count\":1.0}", "{\"count\":2147483648}", "{\"offset\":[0,0]}", "{\"enabled\":null}", "{\"mode\":\"c\"}" })
            Invalid(() => PluginSettings.Validate(fields, Json(invalid)), "拒绝无效参数: " + invalid);
        Invalid(() => PluginSettings.ValidateDefinition(new[] { fields[0], fields[0] }, Array.Empty<PluginPreset>()), "重复字段");
        Invalid(() => PluginSettings.ValidateDefinition(new[] { fields[0] with { Default = Json("-1") } }, Array.Empty<PluginPreset>()), "无效默认值");
        Invalid(() => PluginSettings.ValidateDefinition(fields, new[] { new PluginPreset("bad", Json("{\"count\":0}")) }), "无效预设");
    }
    private static void ThirdParty(string work)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "example", "net6.0"));
        var root = Directory.CreateDirectory(Path.Combine(work, "plugins")).FullName;
        var package = Directory.CreateDirectory(Path.Combine(root, "greeting")).FullName;
        foreach (var file in Directory.GetFiles(source, "Gkms.ExamplePlugin.*")) File.Copy(file, Path.Combine(package, Path.GetFileName(file)), true);
        var manifestPath = Path.Combine(package, "plugin.json");
        File.Copy(Path.Combine(source, "plugin.json"), manifestPath, true);
        Check(!File.Exists(Path.Combine(package, "Gkms.Sdk.dll")), "第三方包不携带 SDK 副本");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var profile = Path.Combine(work, "profile.json");
        var log = new List<string>();
        var game = new ProbeGame();
        File.WriteAllText(profile, """{"enabled":["example.greeting"],"settings":{"example.greeting":{"message":"external-plugin"}}}""");
        using (var host = new PluginHost(game, log.Add))
        {
            host.Start(root, profile, work); host.Pump();
            Check(host.Active.SequenceEqual(new[] { "example.greeting" }) && log.Any(s => s.Contains("external-plugin")), "外部 SDK 插件可发现、加载和读取配置");
            host.ReloadResources("example.greeting");
            Check(log.Any(s => s.Contains("示例资源重载入口")), "资源重载扩展接口");
            var count = log.Count;
            host.ReloadProfile(); host.Pump();
            Check(log.Count == count, "同一清单与参数重载不得重复启动");
            File.WriteAllText(profile, """{"enabled":["example.greeting"],"settings":{"example.greeting":{"logOnStart":"invalid"}}}""");
            try { host.ReloadProfile(); throw new Exception("无效配置不应成功"); } catch (InvalidOperationException) { }
            Check(host.Active.Contains("example.greeting"), "配置校验失败保留正在运行的插件");
            Check(host.GetStates()["example.greeting"].State == PluginRunState.Failed, "配置失败必须进入统一失败状态");
        }
        manifest["entryPoint"] = "Example.Greeting.ContractProbe";
        manifest["canReloadResources"] = false;
        manifest["settings"] = new JsonArray();
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        foreach (var flags in new[] { "{}", "{\"fail\":true}", "{\"fail\":true,\"cancelThrows\":true,\"resourceThrows\":true,\"disposeThrows\":true}" })
        {
            log.Clear();
            File.WriteAllText(profile, "{\"enabled\":[\"example.greeting\"],\"settings\":{\"example.greeting\":" + flags + "}}");
            using (var host = new PluginHost(game, log.Add))
            {
                host.Start(root, profile, work);
                Check(host.Active.Count == (flags == "{}" ? 1 : 0), "启动失败不能注册为活动插件");
                Check(host.GetStates()["example.greeting"].State == (flags == "{}" ? PluginRunState.Running : PluginRunState.Failed), "启动结果进入统一状态");
                File.WriteAllText(profile, "{\"enabled\":[]}");
                host.ReloadProfile(); host.Pump(); host.Dispose();
            }
            Check(game.Live == 0 && !log.Any(s => s.EndsWith(": queued")), "失败或停用后移除 Hook 并丢弃排队操作");
            Check(log.Count(s => s.EndsWith("owned-released:True")) == 1 && log.Count(s => s.EndsWith("child-released:True")) == 1, "父子作用域取消并恰好释放一次");
            Check(log.Any(s => s.EndsWith("stopped-registration-rejected")) && log.Count(s => s.EndsWith(": disposed")) == 1, "拒绝停用后的注册，插件卸载恰好一次");
            Check(log.FindIndex(s => s.EndsWith("last-owned")) < log.FindIndex(s => s.EndsWith("owned-released:True")), "资源逆序释放");
            Check(log.Any(s => s.Contains(Path.Combine("re", "data", "example.greeting"))), "插件私有存储目录");
        }
        // Both unsupported API levels and missing entry assemblies must leave other plugins available.
        manifest["entryPoint"] = "Example.Greeting.Plugin"; manifest["apiVersion"] = 999;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        File.WriteAllText(profile, "{\"enabled\":[\"example.greeting\"]}");
        using (var host = new PluginHost(game, log.Add)) { host.Start(root, profile, work); Check(host.Active.Count == 0, "未知 API 不加载"); }
        manifest["apiVersion"] = 0;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Check(Catalog.Discover(root, log.Add).Count == 0, "不接受非当前 API");
        manifest["apiVersion"] = PluginApi.Current;
        manifest["assembly"] = "Missing.dll";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        using (var host = new PluginHost(game, log.Add)) { host.Start(root, profile, work); Check(host.Active.Count == 0, "缺少 DLL 按插件隔离失败"); }
        manifest.Remove("author"); manifest.Remove("version"); manifest.Remove("description");
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Check(Catalog.Discover(root, log.Add).Count == 0, "API 2 元数据缺失不能作为合规插件发现");
    }
    private sealed class InspectionLoader() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name == typeof(ILocalifyPlugin).Assembly.GetName().Name ? typeof(ILocalifyPlugin).Assembly : null;
    }
    private sealed class ProbeGame : IGameServices
    {
        public int Live;
        public Type? FindType(string assembly, string fullName) => fullName == "ProbeTarget" ? typeof(ProbeTarget) : null;
        public IDisposable Patch(string owner, MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, int priority)
        { Live++; return new Release(() => Live--); }
    }
    private sealed class ProbeTarget { public static void Ping() { } }
    private sealed record Release(Action Action) : IDisposable { public void Dispose() => Action(); }
}
