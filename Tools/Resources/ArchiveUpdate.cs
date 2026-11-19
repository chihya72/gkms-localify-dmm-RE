using System.IO.Compression;
using System.Text.Json;

namespace Gkms.Resources;

internal static class ArchiveUpdate
{
    public static async Task<int> RunAsync(string dataDirectory, string? source, bool textures, string configKey, string defaultUrl)
    {
        var root = Path.Combine(dataDirectory, "re");
        var config = Path.Combine(dataDirectory, "config.json");
        if (string.IsNullOrWhiteSpace(source) && File.Exists(config))
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(config));
            if (settings.RootElement.TryGetProperty(configKey, out var value)) source = value.GetString();
        }
        var file = string.IsNullOrWhiteSpace(source) ? defaultUrl : source.Trim();
        using var guard = new FileStream(Path.Combine(root, "resources.edit"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        string? version = null;
        string? download = null;
        try
        {
            if (Uri.TryCreate(file, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("GkmsLocalifyRE/0.1");
                if (!uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var release = JsonDocument.Parse(await client.GetStringAsync(uri).ConfigureAwait(false));
                    version = release.RootElement.GetProperty("tag_name").GetString();
                    uri = PackInstaller.SelectReleaseZip(release.RootElement, textures);
                }
                download = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
                using var stream = await client.GetStreamAsync(uri).ConfigureAwait(false);
                await using (var dest = File.Create(download)) await stream.CopyToAsync(dest).ConfigureAwait(false);
                file = download;
            }
            using var zip = ZipFile.OpenRead(file);
            return PackInstaller.Install(dataDirectory, textures, zip, version);
        }
        finally { if (download != null) File.Delete(download); }
    }
}
