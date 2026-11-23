using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gkms.Host;

namespace Gkms.Manager;

internal sealed record PluginSource(string Id, string Name, string Url, bool Enabled = true)
{
    public bool Official => Id == "official";
    public bool Bundled => Official && Url.Length == 0;
}
internal sealed record PluginOrigin(string SourceId, string SourceUrl);
internal sealed record RepositorySettings
{
    public string OfficialUrl { get; init; } = "";
    public bool OfficialEnabled { get; init; } = true;
    public PluginSource[] Sources { get; init; } = Array.Empty<PluginSource>();
    public Dictionary<string, PluginOrigin> Origins { get; init; } = new();
}
internal sealed record RepositoryPlugin(string Id, string Name, string Version, int ApiVersion,
    string Author, string Description, string DownloadUrl)
{
    public string? Assembly { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? Punchline { get; init; }
    public string? Category { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string? RepositoryUrl { get; init; }
    public string? UpdateUrl { get; init; }
    public bool RequiresRestart { get; init; }
    public bool HasSettingsUi { get; init; }
    public bool HasResourceUpdater { get; init; }
    public Dictionary<string, string>? Names { get; init; }
    public string DisplayName(string language) => Names?.GetValueOrDefault(language) ?? Name;
    public bool Matches(string query) => string.IsNullOrWhiteSpace(query) ||
        string.Join(" ", new[] { Id, Name, Author, Description, Punchline, Category }
            .Concat(Names?.Values ?? Enumerable.Empty<string>()).Concat(Tags))
            .Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}
internal sealed record RepositoryIndex(int SchemaVersion, string Name, RepositoryPlugin[] Plugins);
internal sealed record PluginOffer(PluginSource Source, RepositoryPlugin Plugin);
internal sealed record RepositoryResult(PluginSource Source, PluginOffer[] Plugins, string? Error = null);
internal sealed record PluginTransferProgress(string Message, long Received = 0, long? Total = null, bool CanCancel = true);

// Dalamud-style distribution metadata, with an explicit Gkms API to avoid treating
// FFXIV assemblies as executable Gkms packages. Package manifests remain unchanged.
internal sealed record PluginMasterEntry(string InternalName, string Name, string AssemblyVersion, int GkmsApiLevel,
    string Author, string Description, string DownloadLinkInstall)
{
    public string? DownloadLinkUpdate { get; init; }
    public string? Assembly { get; init; }
    public string? Punchline { get; init; }
    public string? Category { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string? RepoUrl { get; init; }
    public string? Changelog { get; init; }
    public Dictionary<string, string>? Names { get; init; }
    public bool RequiresRestart { get; init; }
    public bool HasSettingsUi { get; init; }
    public bool HasResourceUpdater { get; init; }
    public RepositoryPlugin ToPlugin() => new(InternalName, Name, AssemblyVersion, GkmsApiLevel, Author, Description, DownloadLinkInstall)
    {
        Assembly = Assembly, UpdateUrl = DownloadLinkUpdate, Punchline = Punchline, Category = Category, Tags = Tags,
        RepositoryUrl = RepoUrl, ReleaseNotes = Changelog, Names = Names, RequiresRestart = RequiresRestart,
        HasSettingsUi = HasSettingsUi, HasResourceUpdater = HasResourceUpdater
    };
    public static PluginMasterEntry FromPlugin(RepositoryPlugin p) => new(p.Id, p.Name, p.Version, p.ApiVersion, p.Author, p.Description, p.DownloadUrl)
    {
        Assembly = p.Assembly, DownloadLinkUpdate = p.UpdateUrl ?? p.DownloadUrl, Punchline = p.Punchline,
        Category = p.Category, Tags = p.Tags, RepoUrl = p.RepositoryUrl, Changelog = p.ReleaseNotes, Names = p.Names,
        RequiresRestart = p.RequiresRestart, HasSettingsUi = p.HasSettingsUi, HasResourceUpdater = p.HasResourceUpdater
    };
}

internal static class PluginRepositoryFormat
{
    public static readonly JsonSerializerOptions MasterJson = new(Catalog.Json) { PropertyNamingPolicy = null };
    public static RepositoryPlugin[] Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var entries = JsonSerializer.Deserialize<PluginMasterEntry[]>(bytes, Catalog.Json)!;
            if (entries.Any(p => p == null || p.GkmsApiLevel < 1))
                throw new InvalidDataException("pluginmaster.json 必须声明 GkmsApiLevel；卫月插件不能在 Gkms 中运行。");
            return entries.Select(p => p.ToPlugin()).ToArray();
        }
        var index = JsonSerializer.Deserialize<RepositoryIndex>(bytes, Catalog.Json);
        if (index is not { SchemaVersion: 1, Plugins: not null } || string.IsNullOrWhiteSpace(index.Name))
            throw new InvalidDataException("需要 Gkms pluginmaster.json 数组，或 schemaVersion=1 的 index.json。");
        return index.Plugins;
    }
}

internal sealed class PluginVersion : IComparable<PluginVersion>
{
    private static readonly Regex Pattern = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");
    private readonly BigInteger[] numbers;
    private readonly string[] prerelease;
    private PluginVersion(BigInteger[] numbers, string[] prerelease) => (this.numbers, this.prerelease) = (numbers, prerelease);
    public static PluginVersion Parse(string value)
    {
        if (value == null || value.Length > 128) throw new InvalidDataException("插件版本无效。");
        var match = Pattern.Match(value);
        if (!match.Success) throw new InvalidDataException("插件版本不是 SemVer: " + value);
        var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : Array.Empty<string>();
        if (pre.Any(s => s.All(char.IsDigit) && s.Length > 1 && s[0] == '0')) throw new InvalidDataException("预发布数字不能有前导零。");
        return new(Enumerable.Range(1, 3).Select(i => BigInteger.Parse(match.Groups[i].Value)).ToArray(), pre);
    }
    public int CompareTo(PluginVersion? other)
    {
        if (other == null) return 1;
        for (var i = 0; i < 3; i++) { var order = numbers[i].CompareTo(other.numbers[i]); if (order != 0) return order; }
        if (prerelease.Length == 0 || other.prerelease.Length == 0)
            return prerelease.Length == other.prerelease.Length ? 0 : prerelease.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            var a = prerelease[i]; var b = other.prerelease[i];
            var an = a.All(char.IsDigit); var bn = b.All(char.IsDigit);
            var order = an && bn ? BigInteger.Parse(a).CompareTo(BigInteger.Parse(b)) : an != bn ? an ? -1 : 1 : string.CompareOrdinal(a, b);
            if (order != 0) return order;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }
}

internal static class PluginRepositoryStore
{
    private const string SettingsFile = "plugin-repositories.json";
    public static RepositorySettings Read(string root)
    {
        var file = Path.Combine(root, SettingsFile);
        var value = File.Exists(file) ? JsonSerializer.Deserialize<RepositorySettings>(File.ReadAllText(file), Catalog.Json)
            ?? throw new InvalidDataException("插件源配置为空。") : Defaults(root);
        if (value.Sources == null || value.Sources.Length > 64 || value.Origins == null || value.Origins.Values.Any(o => o == null) || value.OfficialUrl == null ||
            value.Sources.Any(s => s == null || s.Id == "official" || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name) || s.Url == null) ||
            value.Sources.Select(s => s.Id).Distinct().Count() != value.Sources.Length)
            throw new InvalidDataException("插件源配置无效。");
        if (value.OfficialUrl.Length != 0) NormalizeUrl(value.OfficialUrl);
        foreach (var source in value.Sources) NormalizeUrl(source.Url);
        return value;
    }
    private static RepositorySettings Defaults(string root)
    {
        var state = new RepositorySettings();
        var file = Path.Combine(root, PluginRepositoryClient.BundleName);
        if (!File.Exists(file)) return state;
        using var zip = ZipFile.OpenRead(file);
        var entry = zip.GetEntry("index.json");
        if (entry == null || entry.Length > 4 * 1024 * 1024) return state;
        using var input = entry.Open();
        var index = JsonSerializer.Deserialize<RepositoryIndex>(input, Catalog.Json);
        var installed = Catalog.Discover(Path.Combine(root, "plugins"), _ => { });
        foreach (var plugin in index?.Plugins ?? Array.Empty<RepositoryPlugin>())
            if (installed.TryGetValue(plugin.Id, out var package) && package.Manifest.Assembly == plugin.Assembly &&
                package.Manifest.Author == plugin.Author && Path.GetFileName(package.Directory) == Path.GetFileNameWithoutExtension(plugin.Assembly))
                state.Origins[plugin.Id] = new("official", "");
        return state;
    }
    public static PluginSource[] Sources(RepositorySettings settings) =>
        new[] { new PluginSource("official", "官方插件", settings.OfficialUrl, settings.OfficialEnabled) }.Concat(settings.Sources).ToArray();
    public static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidDataException("插件源和下载地址须为 HTTPS；本机测试可使用 HTTP。地址不能包含账号密码或片段。");
        return uri.AbsoluteUri;
    }
    public static void Change(string root, Func<RepositorySettings, RepositorySettings> edit)
    {
        using var operation = PluginInstallation.AcquireLock(root);
        WriteAtomic(Path.Combine(root, SettingsFile), edit(Read(root)));
    }
    public static void Add(string root, string name, string url)
    {
        url = NormalizeUrl(url);
        Change(root, state =>
        {
            if (state.Sources.Length >= 64) throw new InvalidOperationException("最多添加 64 个第三方源。");
            if (Sources(state).Any(s => s.Url.Length != 0 && NormalizeUrl(s.Url) == url)) throw new InvalidOperationException("已经订阅这个插件源。");
            return state with { Sources = state.Sources.Append(new PluginSource(Guid.NewGuid().ToString("N"), string.IsNullOrWhiteSpace(name) ? new Uri(url).Host : name.Trim(), url)).ToArray() };
        });
    }
    public static void Enable(string root, string id, bool enabled) => Change(root, state => id == "official"
        ? state with { OfficialEnabled = enabled }
        : state with { Sources = state.Sources.Select(s => s.Id == id ? s with { Enabled = enabled } : s).ToArray() });
    public static void Remove(string root, string id)
    {
        if (id == "official") throw new InvalidOperationException("官方源可停用，不能移除。");
        Change(root, state => state with { Sources = state.Sources.Where(s => s.Id != id).ToArray() });
    }
    public static void SetOfficialUrl(string root, string url) =>
        Change(root, state => state with { OfficialUrl = string.IsNullOrWhiteSpace(url) ? "" : NormalizeUrl(url) });
    public static bool Owns(RepositorySettings state, string id, PluginSource source) =>
        state.Origins.TryGetValue(id, out var origin) && (source.Official ? origin.SourceId == "official"
            : origin.SourceId != "official" && origin.SourceUrl == source.Url);
    public static void RequireSource(string root, PluginSource source)
    {
        if (!Sources(Read(root)).Any(s => s.Enabled && s.Id == source.Id && s.Url == source.Url))
            throw new InvalidOperationException("插件源已停用或修改，请重新检查列表。");
    }
    internal static void SetOriginLocked(string root, string id, PluginSource? source)
    {
        var state = Read(root);
        var origins = new Dictionary<string, PluginOrigin>(state.Origins);
        if (source == null) origins.Remove(id); else origins[id] = new(source.Id, source.Url);
        WriteAtomic(Path.Combine(root, SettingsFile), state with { Origins = origins });
    }
    public static void Bind(string root, string id, PluginSource source) => Change(root, state =>
    {
        var origins = new Dictionary<string, PluginOrigin>(state.Origins) { [id] = new(source.Id, source.Url) };
        return state with { Origins = origins };
    });
    public static void WriteAtomic<T>(string file, T value)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, Catalog.Json)); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal sealed class PluginRepositoryClient
{
    public const string BundleName = "official-plugins.zip";
    private const long MaxIndex = 4 * 1024 * 1024, MaxDownload = 256L * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string root;
    public PluginRepositoryClient(string root) => this.root = Path.GetFullPath(root);
    public async Task<RepositoryResult[]> RefreshAsync(CancellationToken token = default)
    {
        var sources = PluginRepositoryStore.Sources(PluginRepositoryStore.Read(root)).Where(s => s.Enabled).ToArray();
        return await Task.WhenAll(sources.Select(async source =>
        {
            try { return await ReadAsync(source, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { return new RepositoryResult(source, Array.Empty<PluginOffer>(), error.GetBaseException().Message); }
        })).ConfigureAwait(false);
    }
    public async Task<RepositoryResult> ReadAsync(PluginSource source, CancellationToken token = default)
    {
        byte[] bytes;
        if (source.Bundled)
        {
            if (!File.Exists(Path.Combine(root, BundleName)))
                throw new InvalidDataException("尚未安装官方离线仓库。请配置官方在线索引，或将官方插件仓库生成的 official-plugins.zip 放入 re 目录。");
            using var archive = ZipFile.OpenRead(Path.Combine(root, BundleName));
            var entry = archive.GetEntry("index.json") ?? throw new InvalidDataException("随附官方源缺少 index.json。");
            using var input = entry.Open();
            using var output = new MemoryStream();
            await CopyAsync(input, output, MaxIndex, entry.Length, null, token).ConfigureAwait(false);
            bytes = output.ToArray();
        }
        else
        {
            using var response = await Http.GetAsync(PluginRepositoryStore.NormalizeUrl(source.Url), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            PluginRepositoryStore.NormalizeUrl(response.RequestMessage!.RequestUri!.AbsoluteUri);
            using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            await CopyAsync(input, output, MaxIndex, response.Content.Headers.ContentLength, null, token).ConfigureAwait(false);
            bytes = output.ToArray();
        }
        var plugins = PluginRepositoryFormat.Parse(bytes);
        if (plugins.Length > 10000) throw new InvalidDataException("插件索引最多包含 10000 个条目。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.Id) || !Regex.IsMatch(plugin.Id, "^[a-z](?:[a-z0-9.-]*[a-z0-9-])?$") ||
                !ids.Add(plugin.Id) || string.IsNullOrWhiteSpace(plugin.Name) || string.IsNullOrWhiteSpace(plugin.Author) ||
                string.IsNullOrWhiteSpace(plugin.Description) || string.IsNullOrWhiteSpace(plugin.DownloadUrl) || plugin.ApiVersion < 1 ||
                plugin.Tags == null || plugin.Tags.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("插件索引存在无效或重复条目。");
            PluginVersion.Parse(plugin.Version);
            _ = DownloadLocation(new(source, plugin));
            _ = DownloadLocation(new(source, plugin), update: true);
            if (plugin.RepositoryUrl != null) PluginRepositoryStore.NormalizeUrl(plugin.RepositoryUrl);
        }
        return new(source, plugins.Select(p => new PluginOffer(source, p)).ToArray());
    }
    private static string DownloadLocation(PluginOffer offer, bool update = false)
    {
        var name = update ? offer.Plugin.UpdateUrl ?? offer.Plugin.DownloadUrl : offer.Plugin.DownloadUrl;
        if (!offer.Source.Bundled)
            return PluginRepositoryStore.NormalizeUrl(new Uri(new Uri(offer.Source.Url), name).AbsoluteUri);
        if (Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("随附源中的插件包名称无效。");
        return name;
    }
    public async Task<PluginInstallation.Prepared> DownloadAsync(PluginOffer offer, IProgress<PluginTransferProgress>? progress, CancellationToken token, bool update = false)
    {
        if (!Catalog.Supports(offer.Plugin.ApiVersion)) throw new InvalidDataException($"需要 API {offer.Plugin.ApiVersion}，当前宿主 API {Catalog.ApiVersion}。");
        var path = Path.Combine(root, ".plugin-download-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var location = DownloadLocation(offer, update);
                if (offer.Source.Bundled)
                {
                    using var archive = ZipFile.OpenRead(Path.Combine(root, BundleName));
                    var entry = archive.GetEntry(location) ?? throw new InvalidDataException("随附源找不到插件包: " + location);
                    using var input = entry.Open();
                    await CopyAsync(input, target, MaxDownload, entry.Length, progress, token).ConfigureAwait(false);
                }
                else
                {
                    using var response = await Http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    PluginRepositoryStore.NormalizeUrl(response.RequestMessage!.RequestUri!.AbsoluteUri);
                    using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    await CopyAsync(input, target, MaxDownload, response.Content.Headers.ContentLength, progress, token).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new("检查插件包"));
            var prepared = PluginInstallation.Prepare(root, path);
            var manifest = prepared.Manifest;
            if (manifest.Id != offer.Plugin.Id || manifest.Version != offer.Plugin.Version || manifest.ApiVersion != offer.Plugin.ApiVersion ||
                offer.Plugin.Assembly != null && manifest.Assembly != offer.Plugin.Assembly)
            { prepared.Dispose(); throw new InvalidDataException("下载包的 ID、版本或 API 与订阅索引不一致。"); }
            return prepared;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static async Task CopyAsync(Stream input, Stream output, long limit, long? total,
        IProgress<PluginTransferProgress>? progress, CancellationToken token)
    {
        if (total > limit) throw new InvalidDataException("插件索引或下载包过大。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var buffer = new byte[81920]; long received = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
            if (count == 0) break;
            received += count;
            if (received > limit) throw new InvalidDataException("插件索引或下载包超过大小限制。");
            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
            progress?.Report(new("下载插件", received, total));
        }
    }
}

internal static class PluginRepositoryPublisher
{
    public static int Export(string packages, string bundleFile, string indexFile, string? masterFile = null)
    {
        var files = Directory.GetFiles(packages, "*.zip").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var plugins = new List<RepositoryPlugin>();
        foreach (var file in files)
        {
            using var archive = ZipFile.OpenRead(file);
            var entries = archive.Entries.Where(e => e.FullName == "plugin.json").ToArray();
            if (entries.Length != 1) throw new InvalidDataException("发布包必须有根目录 plugin.json: " + file);
            using var reader = new StreamReader(entries[0].Open());
            var manifest = Catalog.ParseManifest(reader.ReadToEnd());
            PluginVersion.Parse(manifest.Version!);
            plugins.Add(new(manifest.Id, manifest.Name, manifest.Version!, manifest.ApiVersion, manifest.Author!, manifest.Description!, Path.GetFileName(file))
            {
                Assembly = manifest.Assembly, Names = manifest.Names, Punchline = manifest.Punchline,
                Category = manifest.Category, Tags = manifest.Tags, RepositoryUrl = manifest.RepositoryUrl,
                RequiresRestart = !manifest.SupportsHotEnable || !manifest.SupportsHotDisable,
                HasSettingsUi = manifest.HasSettingsUi, HasResourceUpdater = manifest.ResourceUpdater != null
            });
        }
        if (plugins.Select(p => p.Id).Distinct().Count() != plugins.Count) throw new InvalidDataException("发布包包含重复插件 ID。");
        var index = new RepositoryIndex(1, "Gakumas Localify RE 官方插件", plugins.ToArray());
        PluginRepositoryStore.WriteAtomic(indexFile, index);
        var master = JsonSerializer.Serialize(plugins.Select(PluginMasterEntry.FromPlugin).ToArray(), PluginRepositoryFormat.MasterJson);
        File.WriteAllText(masterFile ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(indexFile))!, "pluginmaster.json"), master);
        using var output = File.Create(bundleFile);
        using var bundle = new ZipArchive(output, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(bundle.CreateEntry("index.json").Open()))
            writer.Write(JsonSerializer.Serialize(index, Catalog.Json));
        using (var writer = new StreamWriter(bundle.CreateEntry("pluginmaster.json").Open())) writer.Write(master);
        foreach (var file in files) bundle.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
        return plugins.Count;
    }
}
