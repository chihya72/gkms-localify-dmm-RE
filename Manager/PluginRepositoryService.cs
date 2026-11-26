using Gkms.Host;

namespace Gkms.Manager;

internal sealed class PluginRepositoryService
{
    private readonly string root;
    private readonly PluginRepositoryClient client;
    public PluginRepositoryService(string root) { this.root = Path.GetFullPath(root); client = new(this.root); }
    public Task<RepositoryResult[]> RefreshAsync(CancellationToken token = default) => client.RefreshAsync(token);
    public static PluginOffer[] Updates(IEnumerable<RepositoryResult> results, IReadOnlyDictionary<string, Package> installed, RepositorySettings settings) =>
        results.SelectMany(r => r.Plugins).Where(o => installed.TryGetValue(o.Plugin.Id, out var current) &&
            PluginRepositoryStore.Owns(settings, o.Plugin.Id, o.Source) &&
            PluginVersion.Parse(o.Plugin.Version).CompareTo(PluginVersion.Parse(current.Manifest.Version!)) > 0).ToArray();
    private void CheckSource(PluginSource source) => PluginRepositoryStore.RequireSource(root, source);
    public void Subscribe(PluginOffer offer)
    {
        CheckSource(offer.Source);
        var installed = Catalog.Discover(Path.Combine(root, "plugins"), _ => { });
        if (!installed.TryGetValue(offer.Plugin.Id, out var package)) throw new InvalidOperationException("插件尚未安装。");
        var state = PluginRepositoryStore.Read(root);
        if (state.Origins.ContainsKey(offer.Plugin.Id) && !PluginRepositoryStore.Owns(state, offer.Plugin.Id, offer.Source))
            throw new InvalidOperationException("插件属于其他源，请先卸载再从新源安装。");
        if (offer.Plugin.Assembly != null && offer.Plugin.Assembly != package.Manifest.Assembly)
            throw new InvalidOperationException("源中的插件程序集名称与本地插件不符。");
        PluginRepositoryStore.Bind(root, offer.Plugin.Id, offer.Source);
    }
    public async Task<Package> InstallAsync(PluginOffer offer, bool update,
        IProgress<PluginTransferProgress>? progress = null, CancellationToken token = default)
    {
        CheckSource(offer.Source);
        var installed = Catalog.Discover(Path.Combine(root, "plugins"), _ => { });
        if (update && (!installed.ContainsKey(offer.Plugin.Id) || !PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), offer.Plugin.Id, offer.Source)))
            throw new InvalidOperationException("只能从该插件已订阅的来源更新。");
        if (!update && installed.ContainsKey(offer.Plugin.Id)) throw new InvalidOperationException("插件已经安装。");
        if (update)
        {
            progress?.Report(new("重新读取插件源：" + offer.Source.Name));
            var latest = await client.ReadAsync(offer.Source, token).ConfigureAwait(false);
            offer = latest.Plugins.SingleOrDefault(candidate => candidate.Plugin.Id == offer.Plugin.Id)
                ?? throw new InvalidOperationException("订阅源中已找不到此插件，请重新检查列表。");
            token.ThrowIfCancellationRequested();
            CheckSource(offer.Source);
            var current = Catalog.Discover(Path.Combine(root, "plugins"), _ => { });
            if (!current.TryGetValue(offer.Plugin.Id, out var package) ||
                !PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), offer.Plugin.Id, offer.Source))
                throw new InvalidOperationException("插件安装状态或订阅来源已改变，请重新检查更新。");
            if (PluginVersion.Parse(offer.Plugin.Version).CompareTo(PluginVersion.Parse(package.Manifest.Version!)) <= 0)
            {
                progress?.Report(new("已安装版本不低于订阅源版本，无需更新"));
                return package;
            }
        }
        using var prepared = await client.DownloadAsync(offer, progress, token, update).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        CheckSource(offer.Source);
        progress?.Report(new(update ? "应用插件更新，保留配置和资源" : "安装插件", CanCancel: false));
        var result = update ? await PluginUpgrade.ApplyAsync(prepared, offer.Source).ConfigureAwait(false) : PluginInstallation.Install(prepared, offer.Source);
        progress?.Report(new(update ? "插件已更新" : "插件已安装，默认停用", CanCancel: false));
        return result;
    }
    public async Task<string[]> UpdateAllAsync(IEnumerable<PluginOffer> candidates, IProgress<PluginTransferProgress>? progress = null,
        CancellationToken token = default)
    {
        var messages = new List<string>();
        foreach (var offer in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                progress?.Report(new("准备更新 " + offer.Plugin.Name));
                var installed = await InstallAsync(offer, true, progress, token).ConfigureAwait(false);
                messages.Add($"{installed.Manifest.Name}: 当前版本 {installed.Manifest.Version}。");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { messages.Add($"{offer.Plugin.Name}: 更新失败：{error.Message}"); }
        }
        return messages.ToArray();
    }
}
