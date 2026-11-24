using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Gkms.Host;

internal static class ResourceSourceTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string work, string packages) => RunAsync(work, packages).GetAwaiter().GetResult();
    private static async Task RunAsync(string work, string packages)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "resource-sources")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "re"));
        var package = Catalog.Discover(packages, _ => { })["localization"];
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var entry = new StreamWriter(zip.CreateEntry("localization.json").Open())) entry.Write("{}");
        var payload = bytes.ToArray();
        var saved = Path.Combine(root, "saved.zip"); var once = Path.Combine(root, "once.zip");
        File.WriteAllBytes(saved, payload); File.WriteAllBytes(once, payload);
        File.WriteAllText(Path.Combine(root, "re", "profile.json"), JsonSerializer.Serialize(new { settings = new Dictionary<string, object>
            { ["localization"] = new { updateSource = saved }, ["textures"] = new { updateSource = saved } } }));
        var release = await PluginHost.CheckResourcesAsync(package, root, "");
        Check(release.Download == saved, "空白手动来源使用已保存默认来源");
        release = await PluginHost.CheckResourcesAsync(package, root, once);
        Check(release.Download == once && File.ReadAllText(Path.Combine(root, "re", "profile.json")).Contains("saved.zip"), "本次来源覆盖默认但不改写配置");
        var textures = Catalog.Discover(packages, _ => { })["textures"];
        Check((await PluginHost.CheckResourcesAsync(textures, root, "")).Download == saved,
            "贴图原版默认地址可被用户保存的自定义来源覆盖");
        Check((await PluginHost.CheckResourcesAsync(textures, root, once)).Download == once,
            "贴图本次来源同样优先于保存的默认来源");
        try { await PluginHost.CheckResourcesAsync(package, root, root); throw new Exception("错误接受文件夹"); }
        catch (InvalidDataException error) { Check(error.Message.Contains("文件夹"), "文件夹必须得到明确错误"); }
        try { await PluginHost.CheckResourcesAsync(package, root, Path.Combine(root, "missing.zip")); throw new Exception("错误接受不存在文件"); }
        catch (FileNotFoundException) { }

        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var url = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener(); listener.Prefixes.Add(url); listener.Start();
        var serving = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var request = await listener.GetContextAsync();
                    var data = request.Request.Url!.AbsolutePath switch
                    {
                        "/api" => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { tag_name = "vtest", body = "release", assets = new[] { new { name = "GakumasTranslationData.zip", browser_download_url = url + "download" } } })),
                        "/page" => Encoding.UTF8.GetBytes("<html>not a release API</html>"),
                        _ => payload
                    };
                    request.Response.ContentLength64 = data.Length;
                    await request.Response.OutputStream.WriteAsync(data); request.Response.Close();
                }
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { }
        });
        try
        {
            release = await PluginHost.CheckResourcesAsync(package, root, url + "api");
            Check(release.Version == "vtest" && release.Download == url + "download", "发布 API 解析版本及资源附件");
            release = await PluginHost.CheckResourcesAsync(package, root, url + "download");
            Check(release.Version == null && release.Download == url + "download", "无 ZIP 扩展名直链按实际 ZIP 内容识别");
            release = await PluginHost.CheckResourcesAsync(package, root, url + "file.zip?token=test");
            Check(release.Download == url + "file.zip?token=test", "含查询参数的 ZIP 直链保留完整地址");
            try { await PluginHost.CheckResourcesAsync(package, root, url + "page"); throw new Exception("错误接受网页"); }
            catch (InvalidDataException error) { Check(error.Message.Contains("发布 API"), "普通网页应说明支持的 API 格式"); }
        }
        finally { listener.Stop(); await serving; }
        Console.WriteLine("资源来源专项通过：默认/本次优先级、发布 API、ZIP 直链、无扩展名下载、本地 ZIP 与文件夹拒绝。");
    }
}
