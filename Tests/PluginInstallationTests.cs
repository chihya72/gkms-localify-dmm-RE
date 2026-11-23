using System.IO.Compression;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Gkms.Host;
using Gkms.Manager;

internal static class PluginInstallationTests
{
    public static void Run(string work, string packages)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "installation")).FullName;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            using var held = PluginInstallation.AcquireLock(root);
            ExpectFailure(() => { using var conflicting = PluginInstallation.AcquireLock(root); }, "持有锁时拒绝并发修改，释放后可立即再次获取");
        }
        var source = Catalog.Discover(packages, _ => { })["frame-rate"];
        var manifest = source.Manifest with { Id = "installer-test", Name = "Installer fixture" };
        var dll = File.ReadAllBytes(Path.Combine(source.Directory, source.Manifest.Assembly));
        string MakeZip(string name, Manifest? metadata = null, string prefix = "", params (string Name, byte[] Data)[] extra)
        {
            var path = Path.Combine(work, name + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            void Add(string entry, byte[] bytes)
            {
                using var stream = archive.CreateEntry(prefix + entry).Open();
                stream.Write(bytes);
            }
            Add("plugin.json", JsonSerializer.SerializeToUtf8Bytes(metadata ?? manifest, Catalog.Json));
            Add(manifest.Assembly, dll);
            foreach (var item in extra) Add(item.Name, item.Data);
            return path;
        }
        Package Install(string zip)
        {
            using var prepared = PluginInstallation.Prepare(root, zip);
            return PluginInstallation.Install(prepared);
        }
        var zip = MakeZip("install-valid", prefix: "wrapper/", extra: new[] { ("private/data.txt", Encoding.UTF8.GetBytes("payload")) });
        File.WriteAllText(Path.Combine(root, "profile.json"), """{"enabled":["installer-test","missing-other"],"settings":{"installer-test":{"fps":144,"unknown":"keep"},"missing-other":{"x":1}},"custom":true}""");
        var privateData = Directory.CreateDirectory(Path.Combine(root, "data", manifest.Id)).FullName;
        File.WriteAllText(Path.Combine(privateData, "keep.txt"), "private");
        var installed = Install(zip);
        Check(File.ReadAllText(Path.Combine(installed.Directory, "private", "data.txt")) == "payload" &&
            Catalog.Discover(Path.Combine(root, "plugins"), _ => { }).ContainsKey(manifest.Id), "安装完整插件包并由宿主发现");
        Check(Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.SequenceEqual(new[] { "missing-other" }), "安装默认停用，保留其他选择");
        var before = File.ReadAllText(Path.Combine(root, "profile.json"));
        ExpectFailure(() => Install(zip), "重复 ID 必须拒绝覆盖");
        Check(File.ReadAllText(Path.Combine(root, "profile.json")) == before, "拒绝重复包不能修改配置");

        var catalog = Catalog.Discover(Path.Combine(root, "plugins"), _ => { });
        var retainedResources = Directory.CreateDirectory(Path.Combine(installed.Directory, "resources")).FullName;
        File.WriteAllText(Path.Combine(retainedResources, "localization.json"), "保留译文");
        var retainedOutput = Directory.CreateDirectory(Path.Combine(installed.Directory, "output")).FullName;
        File.WriteAllText(Path.Combine(retainedOutput, "observed.jsonl"), "保留采集");
        using (var updating = new FileStream(Path.Combine(installed.Directory,"resources.edit"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
        {
            ExpectFailure(() => PluginInstallation.UninstallAsync(root,manifest.Id).GetAwaiter().GetResult(), "资源更新期间不能删除插件");
            Check(File.Exists(Path.Combine(installed.Directory,"plugin.json")), "资源事务冲突保留插件清单");
        }
        File.Delete(Path.Combine(installed.Directory,"resources.edit"));
        ProfileEditor.Apply(root, catalog, new Dictionary<string, bool> { [manifest.Id] = true });
        using (var occupied = new FileStream(Path.Combine(installed.Directory, manifest.Assembly), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!PluginInstallation.UninstallAsync(root, manifest.Id).GetAwaiter().GetResult(), "Windows 占用 DLL 时延后清理");
            Check(!Catalog.Discover(Path.Combine(root, "plugins"), _ => { }).ContainsKey(manifest.Id) &&
                !Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.Contains(manifest.Id), "已卸载包不能被发现或自动启用");
            Check(PluginInstallation.Cleanup(root) == 1, "占用文件重试仍显示等待清理");
            Check(File.ReadAllText(Path.Combine(retainedResources,"localization.json"))=="保留译文", "卸载与清理保留插件自己的资源");
            ExpectFailure(() => ProfileEditor.Apply(root, catalog, new Dictionary<string, bool> { [manifest.Id] = true }), "旧管理器快照不能启用已卸载的包");
            var reinstalled = Install(zip);
            Check(reinstalled.Directory != installed.Directory, "重新安装不复用旧 DLL 路径");
            Check(File.ReadAllText(Path.Combine(reinstalled.Directory,"resources","localization.json"))=="保留译文", "重装自动接回独立资源");
            Check(File.ReadAllText(Path.Combine(reinstalled.Directory,"output","observed.jsonl"))=="保留采集", "卸载、清理和重装保留独立采集输出");
            Check(PluginInstallation.Cleanup(root) == 1 && File.Exists(Path.Combine(reinstalled.Directory, "plugin.json")), "清理不能误删新安装");
            Check(PluginInstallation.UninstallAsync(root, manifest.Id).GetAwaiter().GetResult(), "正常卸载立即删除文件");
            Check(File.Exists(Path.Combine(reinstalled.Directory,"resources","localization.json")), "无占用卸载仍保留独立资源");
        }
        Check(PluginInstallation.Cleanup(root) == 0 && !Directory.Exists(installed.Directory), "释放文件后自动清理残留");
        Check(File.Exists(Path.Combine(privateData, "keep.txt")) &&
            Catalog.ReadProfile(Path.Combine(root, "profile.json")).Settings![manifest.Id].GetProperty("fps").GetInt32() == 144 &&
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "profile.json"))).RootElement.GetProperty("custom").GetBoolean(), "卸载保留配置及私有数据");

        foreach (var path in new[] { "../escape.txt", "C:/escape.txt", "/absolute.txt", "data:stream", "sub/../../escape", "CON.txt", "name. ", ".uninstalled.json", "ImGui.NET.dll", "Gkms.Sdk.dll", "PLUGIN.JSON" })
        {
            var bad = MakeZip("bad-" + Guid.NewGuid().ToString("N"), extra: new[] { (path, new byte[] { 1 }) });
            ExpectFailure(() => { using var candidate = PluginInstallation.Prepare(root, bad); }, "拒绝危险路径/框架文件/重复清单 " + path);
        }
        foreach (var invalid in new[] { manifest with { ApiVersion = 1 }, manifest with { Author = null }, manifest with { Assembly = "missing.dll" } })
        {
            var bad = MakeZip("bad-manifest-" + Guid.NewGuid().ToString("N"), invalid);
            ExpectFailure(() => { using var candidate = PluginInstallation.Prepare(root, bad); }, "拒绝错误 API、元数据和缺失程序集");
        }
        var linkZip = MakeZip("bad-symlink");
        using (var archive = ZipFile.Open(linkZip, ZipArchiveMode.Update))
            archive.CreateEntry("symlink").ExternalAttributes = unchecked((int)0xa1ff0000);
        ExpectFailure(() => { using var candidate = PluginInstallation.Prepare(root, linkZip); }, "拒绝 ZIP 符号链接");
        Check(!File.Exists(Path.Combine(root, "escape.txt")) && !Directory.EnumerateDirectories(root, ".plugin-install-*").Any(), "失败或取消安装不留下可发现的半包和暂存目录");
        using (var canceled = PluginInstallation.Prepare(root, zip)) { }
        Check(!Catalog.Discover(Path.Combine(root, "plugins"), _ => { }).ContainsKey(manifest.Id), "取消预览不安装插件");

        installed = Install(zip);
        File.WriteAllText(Path.Combine(root, "runtime.json"), JsonSerializer.Serialize(new { processId = Environment.ProcessId }));
        ExpectFailure(() => PluginInstallation.UninstallAsync(root, manifest.Id).GetAwaiter().GetResult(), "宿主存活但无响应时保留文件");
        Check(File.Exists(Path.Combine(installed.Directory, "plugin.json")), "连接失败不能假装完成卸载");
        async Task SimulatedHost(RuntimeReply reply)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var pipe = new NamedPipeServerStream("gkms-localify-" + Environment.ProcessId, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            var request = JsonSerializer.Deserialize<RuntimeRequest>((await reader.ReadLineAsync().WaitAsync(timeout.Token))!, Catalog.Json)!;
            Check(request.Command == "reload-profile", "卸载必须请求宿主应用停用配置");
            Check(!Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.Contains(manifest.Id), "请求卸载前先持久保存停用选择");
            await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
        }
        foreach (var reply in new[] { new RuntimeReply(false, "fixture failure", Array.Empty<string>()), new RuntimeReply(true, "still active", new[] { manifest.Id }) })
        {
            var host = SimulatedHost(reply);
            ExpectFailure(() => PluginInstallation.UninstallAsync(root, manifest.Id).GetAwaiter().GetResult(), "宿主失败或插件仍活动时拒绝删除");
            host.GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(installed.Directory, "plugin.json")), "宿主拒绝卸载时保留安装包");
        }
        var success = SimulatedHost(new(true, "stopped", Array.Empty<string>()));
        Check(PluginInstallation.UninstallAsync(root, manifest.Id).GetAwaiter().GetResult(), "收到真实命名管道确认后卸载");
        success.GetAwaiter().GetResult();
        File.Delete(Path.Combine(root, "runtime.json"));

        var archiveRoot = Path.GetFullPath(Path.Combine(packages, "..", "..", "..", "plugin-packages"));
        var builtins = Directory.GetFiles(archiveRoot, "*.zip");
        Check(builtins.Length == 14, "14 个内置插件均生成独立安装包");
        foreach (var builtin in builtins) Install(builtin);
        Check(Catalog.Discover(Path.Combine(root, "plugins"), _ => { }).Count == 14, "全部内置安装包遵循同一安装规则");
        Console.WriteLine("插件安装、卸载、占用清理、重新安装、ZIP 校验及运行中确认通过。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or BadImageFormatException) { return; }
        throw new Exception(message);
    }
}
