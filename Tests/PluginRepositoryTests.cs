using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Gkms.Host;
using Gkms.Manager;

internal static class PluginRepositoryTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint attributes, uint disposition, uint flags, nint template);
    public static void Run(string work, string packages) => RunAsync(work, packages).GetAwaiter().GetResult();
    public static async Task VerifyPublishedAsync(string work)
    {
        var root = Path.Combine(work, "published-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var service = new PluginRepositoryService(root);
        var result = (await service.RefreshAsync()).Single();
        Check(result.Error == null, "official HTTPS index: " + result.Error);
        Check(result.Source.Url == PluginRepositoryStore.DefaultOfficialUrl && result.Plugins.Length == 14,
            "built-in source discovers 14 released official plugins without configuration");
        foreach (var offer in result.Plugins)
        {
            var package = await service.InstallAsync(offer, false);
            Check(package.Manifest.Id == offer.Plugin.Id && package.Manifest.Version == offer.Plugin.Version &&
                File.Exists(Path.Combine(package.Directory, package.Manifest.Assembly)), "published package installation: " + offer.Plugin.Id);
            Check(PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), offer.Plugin.Id, result.Source), "official subscription ownership");
            Console.WriteLine($"Published install PASS: {offer.Plugin.Id} {offer.Plugin.Version}");
        }
        Check(Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.Length == 0, "downloaded plugins remain disabled");
        Console.WriteLine("Published repository PASS: default HTTPS source downloaded and installed all 14 packages. Artifacts: " + root);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Repositories: " + message); }
    private static async Task Fails(Func<Task> action, string message)
    {
        try { await action(); } catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException or OperationCanceledException or HttpRequestException or AggregateException) { return; }
        throw new Exception("Repositories accepted: " + message);
    }
    private static async Task RunAsync(string work, string packages)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "repositories")).FullName;
        Check(PluginRepositoryStore.Sources(PluginRepositoryStore.Read(root)).Single() is { Enabled: true, Bundled: false } online &&
            online.Url == PluginRepositoryStore.DefaultOfficialUrl, "fresh installations use the official online catalog");
        PluginRepositoryStore.WriteAtomic(Path.Combine(root, "plugin-repositories.json"), new { officialEnabled = false });
        Check(PluginRepositoryStore.Read(root) is { OfficialEnabled: false } missing && missing.OfficialUrl == PluginRepositoryStore.DefaultOfficialUrl,
            "older settings without an address inherit the default and preserve disabled state");
        PluginRepositoryStore.SetOfficialUrl(root, "https://example.com/custom.json");
        Check(PluginRepositoryStore.Read(root).OfficialUrl == "https://example.com/custom.json", "custom official address is preserved");
        PluginRepositoryStore.SetOfficialUrl(root, "");
        PluginRepositoryStore.Enable(root, "official", true);
        Check(PluginRepositoryStore.Sources(PluginRepositoryStore.Read(root)).Single().Bundled, "explicit offline selection survives settings round trip");
        var withoutBundle = await new PluginRepositoryClient(root).RefreshAsync();
        Check(withoutBundle.Single().Plugins.Length == 0 && withoutBundle.Single().Error!.Contains("官方离线仓库"), "standalone framework reports how to install the separately distributed official bundle");
        var moving = Directory.CreateDirectory(Path.Combine(root, "rename-source")).FullName;
        using (var held = CreateFileW(moving, 0, 3, 0, 3, 0x02000000, 0))
        {
            Check(!held.IsInvalid, "hold directory without delete sharing");
            var release = Task.Run(async () => { await Task.Delay(100); held.Dispose(); });
            PluginInstallation.MoveDirectory(moving, Path.Combine(root, "rename-destination"));
            await release;
            Check(Directory.Exists(Path.Combine(root, "rename-destination")), "bounded rename retries recover after directory handle release");
        }
        var staged = Path.GetDirectoryName(Path.GetFullPath(packages))!;
        var bundle = Path.Combine(staged, PluginRepositoryClient.BundleName);
        Check(File.Exists(bundle), "build includes official repository bundle");
        File.Copy(bundle, Path.Combine(root, PluginRepositoryClient.BundleName));
        var client = new PluginRepositoryClient(root);
        var official = (await client.RefreshAsync()).Single();
        Check(official.Error == null && official.Plugins.Length == 14, "bundled official catalog contains all 14 plugins");
        using (var archive = ZipFile.OpenRead(bundle))
        {
            using var stream = archive.GetEntry("pluginmaster.json")!.Open();
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            var masterPlugins = PluginRepositoryFormat.Parse(bytes.ToArray());
            Check(masterPlugins.Select(p => p.Id).SequenceEqual(official.Plugins.Select(o => o.Plugin.Id)), "flat and wrapped catalogs publish the same 14 IDs");
            Check(masterPlugins.All(p => !string.IsNullOrWhiteSpace(p.Punchline) && !string.IsNullOrWhiteSpace(p.Category) && p.Tags.Length > 0), "all official plugins have a summary, category and searchable tags");
            Check(masterPlugins.Single(p => p.Id == "localization") is { RequiresRestart: true, HasResourceUpdater: true, HasSettingsUi: true }, "text plugin publishes restart and resource capabilities");
            Check(masterPlugins.Single(p => p.Id == "frame-rate").Matches("FPS") && masterPlugins.Single(p => p.Id == "frame-rate").Matches("Frame rate"), "tag and translated name searches are available");
        }
        var service = new PluginRepositoryService(root);
        foreach (var candidate in official.Plugins.Where(o => o.Plugin.Id != "frame-rate"))
        {
            var package = await service.InstallAsync(candidate, false);
            Check(package.Manifest.Id == candidate.Plugin.Id && package.Manifest.Version == candidate.Plugin.Version &&
                File.Exists(Path.Combine(package.Directory, package.Manifest.Assembly)), "official package matches its catalog: " + candidate.Plugin.Id);
            await PluginInstallation.UninstallAsync(root, candidate.Plugin.Id);
        }
        var frameOffer = official.Plugins.Single(o => o.Plugin.Id == "frame-rate");
        var installed = await service.InstallAsync(frameOffer, false);
        Check(PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), "frame-rate", official.Source), "official installs subscribe automatically");
        Check(!Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.Contains("frame-rate"), "fresh install remains disabled");
        await PluginInstallation.UninstallAsync(root, "frame-rate");
        PluginRepositoryStore.Enable(root, "official", false);

        string[] versions = { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.1.0", "1.10.0", "2.0.0" };
        for (int i = 1; i < versions.Length; i++) Check(PluginVersion.Parse(versions[i - 1]).CompareTo(PluginVersion.Parse(versions[i])) < 0, "SemVer ordering");
        Check(PluginVersion.Parse("1.2.3+build.9").CompareTo(PluginVersion.Parse("1.2.3+build.10")) == 0, "build metadata does not cause updates");
        foreach (var bad in new[] { "01.2.3", "1.2", "1.2.3-beta.01", "1.2.3.4" })
            await Fails(() => { PluginVersion.Parse(bad); return Task.CompletedTask; }, "bad SemVer");
        foreach (var bad in new[] { "file:///C:/test", "http://example.com/index.json", "https://user:password@example.com/a", "https://example.com/a#fragment" })
            await Fails(() => { PluginRepositoryStore.NormalizeUrl(bad); return Task.CompletedTask; }, "invalid transport");

        var original = Catalog.Discover(packages, _ => { })["frame-rate"];
        var manifest = original.Manifest with { Id = "repository-test", Name = "Repository fixture", Version = "1.0.0" };
        byte[] MakeZip(string version)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using (var entry = zip.CreateEntry("plugin.json").Open()) entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest with { Version = version }, Catalog.Json));
                using (var entry = zip.CreateEntry(manifest.Assembly).Open()) entry.Write(File.ReadAllBytes(Path.Combine(original.Directory, manifest.Assembly)));
                using (var entry = zip.CreateEntry("resources/default.txt").Open()) entry.Write(Encoding.UTF8.GetBytes("package defaults"));
            }
            return bytes.ToArray();
        }
        var metadata = new RepositoryPlugin(manifest.Id, manifest.Name, "1.0.0", 2, manifest.Author!, manifest.Description!, "plugin.zip") { Assembly = manifest.Assembly };
        using var server = new LocalRepository();
        void Publish(string version, string? payloadVersion = null, bool flat = false)
        {
            var plugin = metadata with { Version = version, UpdateUrl = flat ? "update.zip" : null, Tags = new[] { "fixture", "FPS" } };
            server.Set("/repo/index.json", flat
                ? JsonSerializer.SerializeToUtf8Bytes(new[] { PluginMasterEntry.FromPlugin(plugin) }, PluginRepositoryFormat.MasterJson)
                : JsonSerializer.SerializeToUtf8Bytes(new RepositoryIndex(1, "fixture", new[] { plugin }), Catalog.Json));
            server.Set("/repo/plugin.zip", MakeZip(flat ? "1.0.0" : payloadVersion ?? version));
            server.Set("/repo/update.zip", MakeZip(payloadVersion ?? version));
        }
        Publish("1.0.0", flat: true);
        PluginRepositoryStore.Add(root, "Third party", server.Url + "repo/index.json");
        var source = PluginRepositoryStore.Read(root).Sources.Single();
        await Fails(() => { PluginRepositoryStore.Add(root, "duplicate", source.Url); return Task.CompletedTask; }, "duplicate source URL");
        var offer = (await service.RefreshAsync()).Single().Plugins.Single();
        Check(offer.Source.Id == source.Id, "real HTTP index and relative ZIP URL");
        Check(offer.Plugin.Matches("fps"), "flat catalog tags survive HTTP parsing");
        installed = await service.InstallAsync(offer, false);
        var resources = Path.Combine(installed.Directory, "resources");
        File.WriteAllText(Path.Combine(resources, "local.txt"), "用户资源");
        File.WriteAllText(Path.Combine(resources, "default.txt"), "用户修改默认资源");
        var output = Directory.CreateDirectory(Path.Combine(installed.Directory, "output")).FullName;
        File.WriteAllText(Path.Combine(output, "capture.txt"), "用户采集");
        var privateData = Directory.CreateDirectory(Path.Combine(root, "data", manifest.Id)).FullName;
        File.WriteAllText(Path.Combine(privateData, "keep.txt"), "private");
        File.WriteAllText(Path.Combine(root, "profile.json"), """{"enabled":["repository-test","other"],"settings":{"repository-test":{"fps":144,"custom":"keep"},"other":{"value":5}},"custom":true}""");
        Publish("1.1.0", flat: true);
        var results = await service.RefreshAsync();
        var upgrades = PluginRepositoryService.Updates(results, Catalog.Discover(Path.Combine(root, "plugins"), _ => { }), PluginRepositoryStore.Read(root));
        Check(upgrades.Length == 1, "unified update discovery");
        var old = installed;
        using (var occupied = new FileStream(Path.Combine(old.Directory, manifest.Assembly), FileMode.Open, FileAccess.Read, FileShare.Read))
            installed = await service.InstallAsync(upgrades.Single(), true);
        Check(installed.Directory != old.Directory && installed.Manifest.Version == "1.1.0", "updates use a new directory while old DLL is locked");
        Check(installed.Manifest.Version == "1.1.0", "updates use DownloadLinkUpdate while installation URL still serves 1.0.0");
        Check(File.ReadAllText(Path.Combine(installed.Directory, "resources", "local.txt")) == "用户资源" && File.ReadAllText(Path.Combine(installed.Directory, "output", "capture.txt")) == "用户采集", "update retains independent resources and capture");
        var profile = Catalog.ReadProfile(Path.Combine(root, "profile.json"));
        Check(profile.Enabled.Contains(manifest.Id) && profile.Enabled.Contains("other") && profile.Settings![manifest.Id].GetProperty("fps").GetInt32() == 144 && File.Exists(Path.Combine(privateData, "keep.txt")), "enabled state, settings and private data retained");
        PluginInstallation.Cleanup(root);
        Check(!Directory.Exists(old.Directory), "deferred old DLL cleanup");

        Publish("1.2.0", "1.1.0");
        offer = (await service.RefreshAsync()).Single().Plugins.Single();
        await Fails(async () => await service.InstallAsync(offer, true), "index/package version mismatch");
        Check(Catalog.Discover(Path.Combine(root, "plugins"), _ => { })[manifest.Id].Manifest.Version == "1.1.0", "bad download cannot alter installed plugin");
        await Fails(async () => { using var prepared = await client.DownloadAsync(offer with { Plugin = offer.Plugin with { ApiVersion = 99 } }, null, default); }, "incompatible API");
        using (var canceled = new CancellationTokenSource())
        {
            var progress = new CancelProgress(canceled);
            await Fails(async () => { using var prepared = await client.DownloadAsync(offer, progress, canceled.Token); }, "cancel during download");
        }
        Check(!Directory.EnumerateFiles(root, ".plugin-download-*").Any() && !Directory.EnumerateDirectories(root, ".plugin-install-*").Any(), "cancellation and mismatch clean temporary files");
        PluginRepositoryStore.Add(root, "Foreign", server.Url + "foreign.json");
        server.Set("/foreign.json", JsonSerializer.SerializeToUtf8Bytes(new RepositoryIndex(1, "foreign", new[] { metadata with { Version = "99.0.0", DownloadUrl = "repo/plugin.zip" } }), Catalog.Json));
        results = await service.RefreshAsync();
        upgrades = PluginRepositoryService.Updates(results, Catalog.Discover(Path.Combine(root, "plugins"), _ => { }), PluginRepositoryStore.Read(root));
        Check(upgrades.Length == 1 && upgrades[0].Source.Id == source.Id, "foreign source cannot take over an installed ID");
        await Fails(async () => await service.InstallAsync(results.Single(r => r.Source.Id != source.Id).Plugins.Single(), true), "foreign takeover");
        server.Set("/foreign.json", Encoding.UTF8.GetBytes("invalid JSON"));
        results = await service.RefreshAsync();
        Check(results.Count(r => r.Error != null) == 1 && results.Single(r => r.Source.Id == source.Id).Plugins.Length == 1, "one bad source does not hide good sources");
        server.Set("/foreign.json", Encoding.UTF8.GetBytes("""[{"InternalName":"foreign","Name":"FFXIV plugin","AssemblyVersion":"1.0.0.0","DalamudApiLevel":11}]"""));
        Check((await service.RefreshAsync()).Single(r => r.Source.Id != source.Id).Error!.Contains("GkmsApiLevel"), "Dalamud packages report a specific framework incompatibility");
        server.Set("/foreign.json", JsonSerializer.SerializeToUtf8Bytes(new[] { PluginMasterEntry.FromPlugin(metadata) with { DownloadLinkUpdate = "http://example.com/update.zip" } }, PluginRepositoryFormat.MasterJson));
        Check((await service.RefreshAsync()).Single(r => r.Source.Id != source.Id).Error != null, "unsafe update links rejected before installation");
        server.Set("/foreign.json", JsonSerializer.SerializeToUtf8Bytes(new[] { PluginMasterEntry.FromPlugin(metadata), PluginMasterEntry.FromPlugin(metadata) }, PluginRepositoryFormat.MasterJson));
        Check((await service.RefreshAsync()).Single(r => r.Source.Id != source.Id).Error != null, "duplicate flat catalog IDs rejected");
        server.Set("/foreign.json", JsonSerializer.SerializeToUtf8Bytes(new RepositoryIndex(1, "duplicate", new[] { metadata, metadata }), Catalog.Json));
        Check((await service.RefreshAsync()).Single(r => r.Source.Id != source.Id).Error != null, "duplicate index IDs rejected");
        PluginRepositoryStore.Enable(root, source.Id, false);
        await Fails(async () => await service.InstallAsync(offer, true), "disabled source rejects stale UI action");
        PluginRepositoryStore.Enable(root, source.Id, true);

        Publish("1.2.0");
        offer = (await service.RefreshAsync()).Single(r => r.Source.Id == source.Id).Plugins.Single();
        File.WriteAllText(Path.Combine(root, "runtime.json"), JsonSerializer.Serialize(new { processId = Environment.ProcessId }));
        var host = ReplySequence(new(true, "stopped", Array.Empty<string>()), new(false, "new plugin failed", Array.Empty<string>()), new(true, "stopped new", Array.Empty<string>()), new(true, "old restored", new[] { manifest.Id }));
        await Fails(async () => await service.InstallAsync(offer, true), "runtime activation failure rolls back");
        await host;
        File.Delete(Path.Combine(root, "runtime.json"));
        installed = Catalog.Discover(Path.Combine(root, "plugins"), _ => { })[manifest.Id];
        Check(installed.Manifest.Version == "1.1.0" && Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.Contains(manifest.Id) && File.Exists(Path.Combine(installed.Directory, "resources", "local.txt")) && !File.Exists(Path.Combine(root, "plugin-update.json")), "rollback restores old version, resources, state and clears journal");

        using (var prepared = await client.DownloadAsync(offer, null, default))
        {
            var stage = prepared.Stage!;
            var destination = Path.Combine(root, "plugins", "interrupted-new");
            var journal = new PluginUpgrade.Journal(manifest.Id, Path.GetFileName(installed.Directory), Path.GetFileName(destination), Path.GetFileName(stage), true, new[] { "resources", "output" });
            PluginRepositoryStore.WriteAtomic(Path.Combine(root, "plugin-update.json"), journal);
            ProfileEditor.Apply(root, Catalog.Discover(Path.Combine(root, "plugins"), _ => { }), new Dictionary<string, bool> { [manifest.Id] = false });
            File.Move(Path.Combine(stage, "plugin.json"), Path.Combine(stage, ".pending.json"));
            Directory.Move(stage, destination); prepared.Stage = null;
            Directory.Delete(Path.Combine(destination, "resources"), true);
            Directory.Move(Path.Combine(installed.Directory, "resources"), Path.Combine(destination, "resources"));
            File.Move(Path.Combine(installed.Directory, "plugin.json"), Path.Combine(installed.Directory, ".uninstalled.json"));
            PluginInstallation.Cleanup(root);
            Check(Catalog.Discover(Path.Combine(root, "plugins"), _ => { })[manifest.Id].Manifest.Version == "1.1.0" && File.Exists(Path.Combine(installed.Directory, "resources", "local.txt")) && File.Exists(Path.Combine(installed.Directory, "output", "capture.txt")) && !Directory.Exists(destination), "interrupted partial resource move recovers before cleanup");
        }
        await service.InstallAsync(offer, true);
        await PluginInstallation.UninstallAsync(root, manifest.Id);
        await service.InstallAsync(offer, false);
        installed = Catalog.Discover(Path.Combine(root, "plugins"), _ => { })[manifest.Id];
        Check(File.Exists(Path.Combine(installed.Directory, "resources", "local.txt")) && File.ReadAllText(Path.Combine(installed.Directory, "resources", "default.txt")) == "用户修改默认资源" && !Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled.Contains(manifest.Id), "uninstall/reinstall restores resources including edits to package defaults and leaves plugin disabled");
        PluginRepositoryStore.Remove(root, source.Id);
        Check(Catalog.Discover(Path.Combine(root, "plugins"), _ => { }).ContainsKey(manifest.Id), "removing a source does not uninstall plugins");
        PluginRepositoryStore.Add(root, "Re-added", source.Url);
        var readded = PluginRepositoryStore.Read(root).Sources.Single(s => s.Url == source.Url);
        Check(PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), manifest.Id, readded), "re-add same URL retains ownership");
        var localOffer = (await service.RefreshAsync()).Single(r => r.Source.Id == readded.Id).Plugins.Single();
        await PluginInstallation.UninstallAsync(root, manifest.Id);
        using (var prepared = await client.DownloadAsync(localOffer, null, default)) PluginInstallation.Install(prepared);
        Check(!PluginRepositoryStore.Read(root).Origins.ContainsKey(manifest.Id), "manual ZIP installation clears previous repository ownership");
        service.Subscribe(localOffer);
        Check(PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), manifest.Id, readded), "manual plugin can explicitly subscribe to its source");
        Console.WriteLine("Plugin repositories PASS: all 14 official packages, pluginmaster/index catalogs, metadata search, separate install/update URLs, HTTP download, SemVer, cancellation, ownership, locked DLLs, resource preservation, runtime rollback and crash recovery.");
    }
    private sealed record CancelProgress(CancellationTokenSource Source) : IProgress<PluginTransferProgress>
    { public void Report(PluginTransferProgress value) { if (value.Received > 0) Source.Cancel(); } }
    private static async Task ReplySequence(params RuntimeReply[] replies)
    {
        foreach (var reply in replies)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var pipe = new NamedPipeServerStream("gkms-localify-" + Environment.ProcessId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            Check((await reader.ReadLineAsync().WaitAsync(timeout.Token))!.Contains("reload-profile"), "update requests runtime reload");
            await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
        }
    }
    private sealed class LocalRepository : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> files = new();
        private readonly Task serving;
        public string Url { get; }
        public LocalRepository()
        {
            var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            Url = $"http://127.0.0.1:{number}/"; listener.Prefixes.Add(Url); listener.Start();
            serving = Task.Run(async () =>
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var request = await listener.GetContextAsync();
                        try
                        {
                            if (files.TryGetValue(request.Request.Url!.AbsolutePath, out var bytes))
                            { request.Response.ContentLength64 = bytes.Length; await request.Response.OutputStream.WriteAsync(bytes); }
                            else request.Response.StatusCode = 404;
                        }
                        finally { request.Response.Close(); }
                    }
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { }
            });
        }
        public void Set(string path, byte[] bytes) => files[path] = bytes;
        public void Dispose() { listener.Close(); serving.GetAwaiter().GetResult(); }
    }
}
