using System.IO.Compression;
using System.Text.Json;
using Gkms.Resources;

if (args.Length != 2 || args[0] is not ("text" or "textures"))
{
    Console.Error.WriteLine("Gkms.Resources <text|textures> <发布 API 地址、ZIP 地址或本地 ZIP>");
    return 2;
}
var data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
try
{
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GkmsLocalifyRE/0.1");
    using var guard = new FileStream(Path.Combine(AppContext.BaseDirectory, "resources.edit"), FileMode.OpenOrCreate,
        FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    var file = args[1];
    string? version = null;
    string? download = null;
    try
    {
        if (Uri.TryCreate(file, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (!uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var release = JsonDocument.Parse(await client.GetStringAsync(uri));
                version = release.RootElement.GetProperty("tag_name").GetString();
                uri = PackInstaller.SelectReleaseZip(release.RootElement, args[0] == "textures");
            }
            download = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N") + ".zip");
            using (var stream = await client.GetStreamAsync(uri))
            await using (var dest = File.Create(download)) await stream.CopyToAsync(dest);
            file = download;
        }
        using var zip = ZipFile.OpenRead(file);
        var count = PackInstaller.Install(data, args[0] == "textures", zip, version);
        Console.WriteLine($"已安装 {count} 个资源文件。");
        return 0;
    }
    finally { if (download != null) File.Delete(download); }
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
