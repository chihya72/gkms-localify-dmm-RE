using System.Collections.Concurrent;
using System.Numerics;
using Gkms.Host;
using ImGuiNET;

namespace Gkms.Manager;

internal sealed class PluginRepositoryPanel : IDisposable
{
    private static readonly PluginSource LocalSource = new("##local", "本地插件", "");
    internal static PluginOffer[] LocalOffers(IReadOnlyDictionary<string, Package> installed, RepositorySettings settings) =>
        installed.Values.Where(p => !settings.Origins.ContainsKey(p.Manifest.Id)).Select(p =>
        {
            var m = p.Manifest;
            return new PluginOffer(LocalSource, new RepositoryPlugin(m.Id, m.Name, m.Version ?? "", m.ApiVersion,
                m.Author ?? "", m.Description ?? "", "")
            {
                Names = m.Names, Punchline = m.Punchline, Category = m.Category, Tags = m.Tags,
                RepositoryUrl = m.RepositoryUrl, RequiresRestart = m.RequiresRestart,
                HasSettingsUi = m.HasSettingsUi, HasResourceUpdater = m.ResourceUpdater != null
            });
        }).ToArray();
    private static string SourceLabel(PluginSource source) => source == LocalSource ? "本地" : source.Official ? "官方" : "第三方";
    private readonly string root;
    private readonly PluginRepositoryService service;
    private readonly Action beforeChange, changed;
    private readonly Action<string> log;
    private readonly ConcurrentQueue<Action> completed = new();
    private RepositorySettings settings = new();
    private IReadOnlyDictionary<string, Package> installed = new Dictionary<string, Package>();
    private RepositoryResult[] results = Array.Empty<RepositoryResult>();
    private CancellationTokenSource? cancellation;
    private PluginTransferProgress? progress;
    private string search = "", selection = "", sourceFilter = "", categoryFilter = "", message = "", sourceName = "", sourceUrl = "", officialUrl = "";
    private int filter;
    private bool initialized, disposed, confirm;
    private PluginOffer[]? pendingOffers;
    private string? pendingUninstall;
    private bool pendingUpdate;
    public bool Busy { get; private set; }
    public bool Changing { get; private set; }
    public void SyncLocal() { if (!Busy) ReloadLocal(); }

    public PluginRepositoryPanel(string root, Action beforeChange, Action changed, Action<string> log)
    { this.root = root; this.beforeChange = beforeChange; this.changed = changed; this.log = log; service = new(root); ReloadLocal(); }
    private void ReloadLocal()
    {
        settings = PluginRepositoryStore.Read(root);
        installed = Catalog.Discover(Path.Combine(root, "plugins"), log);
        officialUrl = settings.OfficialUrl;
    }
    public void Process()
    {
        while (completed.TryDequeue(out var action)) if (!disposed) action();
    }
    private void Run(Func<CancellationToken, Task<Action>> work, bool mutation = false)
    {
        if (Busy || disposed) return;
        if (mutation) beforeChange();
        Busy = true; Changing = mutation; progress = null; cancellation = new();
        var owner = cancellation;
        _ = Task.Run(async () =>
        {
            try { var apply = await work(owner.Token).ConfigureAwait(false); completed.Enqueue(apply); }
            catch (OperationCanceledException) when (owner.IsCancellationRequested)
            { completed.Enqueue(() => message = "已取消；已完成的安装和更新予以保留。"); }
            catch (Exception error)
            {
                log("[Error] "+error);
                completed.Enqueue(() => message = error.Message + (error.InnerException == null ? "" : "\n" + error.GetBaseException().Message));
            }
            finally
            {
                owner.Dispose();
                completed.Enqueue(() =>
                {
                    Busy = false; Changing = false; cancellation = null;
                    try { ReloadLocal(); if (mutation) changed(); } catch (Exception error) { message = error.Message; }
                });
            }
        });
    }
    private void Refresh()
    {
        initialized = true;
        Run(async token =>
        {
            var next = await service.RefreshAsync(token).ConfigureAwait(false);
            return () => { results = next; ReloadLocal(); message = $"已检查 {next.Length} 个启用的插件源。"; };
        });
    }
    private IProgress<PluginTransferProgress> Progress() => new Transfer(value => completed.Enqueue(() => progress = value));
    private sealed record Transfer(Action<PluginTransferProgress> ReportValue) : IProgress<PluginTransferProgress>
    { public void Report(PluginTransferProgress value) => ReportValue(value); }

    public void DrawLibrary()
    {
        if (!initialized && !Busy) Refresh();
        ImGui.BeginDisabled(Busy);
        if (ImGui.Button("检查所有插件更新")) Refresh();
        var updates = PluginRepositoryService.Updates(results, installed, settings);
        ImGui.SameLine();
        ImGui.BeginDisabled(!updates.Any(o => Catalog.Supports(o.Plugin.ApiVersion)));
        if (ImGui.Button($"更新全部 ({updates.Length})")) { pendingOffers = updates; pendingUpdate = true; confirm = true; }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##repository-search", "搜索名称、作者、说明或标签", ref search, 256);
        ImGui.SetNextItemWidth(125); ImGui.Combo("##repository-filter", ref filter, new[] { "全部插件", "未安装", "已安装", "可更新" }, 4);
        ImGui.SameLine(); ImGui.SetNextItemWidth(-1);
        var sources = PluginRepositoryStore.Sources(settings).Append(LocalSource).ToArray();
        if (ImGui.BeginCombo("##repository-source", sources.FirstOrDefault(s => s.Id == sourceFilter)?.Name ?? "全部来源"))
        {
            if (ImGui.Selectable("全部来源", sourceFilter.Length == 0)) sourceFilter = "";
            foreach (var source in sources) if (ImGui.Selectable(source.Name + "##" + source.Id, sourceFilter == source.Id)) sourceFilter = source.Id;
            ImGui.EndCombo();
        }
        var offers = results.SelectMany(r => r.Plugins).Concat(LocalOffers(installed, settings)).ToArray();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##repository-category", categoryFilter.Length == 0 ? "全部分类" : categoryFilter))
        {
            if (ImGui.Selectable("全部分类", categoryFilter.Length == 0)) categoryFilter = "";
            foreach (var category in offers.Select(o => o.Plugin.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c, StringComparer.Ordinal))
                if (ImGui.Selectable(category!, categoryFilter == category)) categoryFilter = category!;
            ImGui.EndCombo();
        }
        var rows = offers.Where(o =>
            (sourceFilter.Length == 0 || sourceFilter == o.Source.Id) &&
            (categoryFilter.Length == 0 || categoryFilter == o.Plugin.Category) && o.Plugin.Matches(search) &&
            (filter == 0 || filter == 1 && !installed.ContainsKey(o.Plugin.Id) || filter == 2 && installed.ContainsKey(o.Plugin.Id) ||
             filter == 3 && updates.Contains(o))).OrderBy(o => o.Plugin.DisplayName(UiText.Language), StringComparer.Ordinal).ToArray();
        var left = Math.Clamp(ImGui.GetContentRegionAvail().X * .37f, 170, 330);
        ImGui.BeginChild("repository-list", new Vector2(left, -100), true);
        foreach (var offer in rows)
        {
            var key = offer.Source.Id + "/" + offer.Plugin.Id;
            var position = ImGui.GetCursorScreenPos();
            var lineHeight = ImGui.GetTextLineHeight();
            var lineGap = ImGui.GetStyle().ItemSpacing.Y;
            if (ImGui.Selectable("##" + key, selection == key, ImGuiSelectableFlags.None, new Vector2(0, lineHeight * 2 + lineGap))) selection = key;
            var draw = ImGui.GetWindowDrawList();
            draw.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), offer.Plugin.DisplayName(UiText.Language));
            var metadata = SourceLabel(offer.Source) + " · " + offer.Plugin.Version + " · ";
            var secondLine = position + new Vector2(0, lineHeight + lineGap);
            draw.AddText(secondLine, ImGui.GetColorU32(ImGuiCol.TextDisabled), metadata);
            var isInstalled = installed.ContainsKey(offer.Plugin.Id);
            var installedText = isInstalled ? "已安装" : "未安装";
            if (updates.Contains(offer)) installedText += " · 可更新";
            draw.AddText(secondLine + new Vector2(ImGui.CalcTextSize(metadata).X, 0),
                ImGui.GetColorU32(isInstalled ? new Vector4(.4f,.85f,.6f,1) : new Vector4(.65f,.65f,.65f,1)), installedText);
        }
        if (rows.Length == 0) Gkms.Sdk.PluginUiText.Wrapped(Busy ? "正在读取插件源…" : "没有符合条件的插件。可在「订阅源」添加来源，或查看下方错误。");
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("repository-detail", new Vector2(0, -100), true);
        var selected = rows.FirstOrDefault(o => o.Source.Id + "/" + o.Plugin.Id == selection);
        if (selected != null) DrawDetails(selected);
        else Gkms.Sdk.PluginUiText.Wrapped("选择插件查看版本、来源和说明，然后下载安装。插件资源更新仍在各插件自己的设置页中。");
        ImGui.EndChild();
        foreach (var error in results.Where(r => r.Error != null))
            Gkms.Sdk.PluginUiText.Wrapped(error.Source.Name + ": " + error.Error);
        DrawProgress(); DrawConfirmation();
    }
    private void DrawDetails(PluginOffer offer)
    {
        var plugin = offer.Plugin;
        ImGui.TextUnformatted(plugin.DisplayName(UiText.Language)); ImGui.TextDisabled(plugin.Id);
        Gkms.Sdk.PluginUiText.Wrapped("作者: " + plugin.Author);
        if (!string.IsNullOrWhiteSpace(plugin.Punchline)) Gkms.Sdk.PluginUiText.Wrapped(plugin.Punchline);
        if (!string.IsNullOrWhiteSpace(plugin.Category)) Gkms.Sdk.PluginUiText.Wrapped("分类: " + plugin.Category);
        if (plugin.Tags.Length > 0) Gkms.Sdk.PluginUiText.Wrapped("标签: " + string.Join(" · ", plugin.Tags));
        Gkms.Sdk.PluginUiText.Wrapped("来源: " + SourceLabel(offer.Source) + " · " + offer.Source.Name);
        Gkms.Sdk.PluginUiText.Wrapped(offer.Source == LocalSource ? "通过本地安装包安装，尚未关联订阅源。" : offer.Source.Bundled ? "从独立官方插件仓库安装的离线目录；也可配置官方在线索引。" : offer.Source.Url);
        installed.TryGetValue(plugin.Id, out var current);
        Gkms.Sdk.PluginUiText.Wrapped(offer.Source == LocalSource ? $"已安装: {plugin.Version}" : $"已安装: {current?.Manifest.Version ?? "—"}    来源版本: {plugin.Version}");
        ImGui.TextUnformatted($"插件 API: {plugin.ApiVersion}    宿主 API: {Catalog.ApiVersion}");
        if (plugin.RequiresRestart) Gkms.Sdk.PluginUiText.Wrapped("此插件的启停需要重新启动游戏；更新前请关闭游戏，通过管理器命令行更新。");
        if (plugin.HasResourceUpdater) Gkms.Sdk.PluginUiText.Wrapped("提供独立资源更新；安装插件后在其资源更新页下载资源。");
        if (plugin.HasSettingsUi) Gkms.Sdk.PluginUiText.Wrapped("提供插件配置页。");
        if (!string.IsNullOrWhiteSpace(plugin.RepositoryUrl))
        {
            Gkms.Sdk.PluginUiText.Wrapped("项目: " + plugin.RepositoryUrl);
            if (ImGui.SmallButton("复制项目地址")) ImGui.SetClipboardText(plugin.RepositoryUrl);
        }
        ImGui.Separator(); Gkms.Sdk.PluginUiText.Wrapped(plugin.Description);
        if (!string.IsNullOrWhiteSpace(plugin.ReleaseNotes)) { ImGui.Separator(); Gkms.Sdk.PluginUiText.Wrapped("更新说明\n" + plugin.ReleaseNotes); }
        ImGui.Separator(); ImGui.BeginDisabled(Busy);
        var compatible = Catalog.Supports(plugin.ApiVersion);
        if (!compatible) Gkms.Sdk.PluginUiText.Wrapped("此版本需要其他版本的宿主，暂不能安装或更新。");
        if (current == null)
        {
            ImGui.BeginDisabled(!compatible);
            if (ImGui.Button("下载安装")) { pendingOffers = new[] { offer }; pendingUpdate = false; confirm = true; }
            ImGui.EndDisabled();
        }
        else
        {
            if (offer.Source == LocalSource)
                Gkms.Sdk.PluginUiText.Wrapped("在「已安装」页启用或配置此插件。如需自动更新，可在对应订阅源的同名插件详情中关联来源。");
            else if (PluginRepositoryStore.Owns(settings, plugin.Id, offer.Source))
            {
                var newer = PluginVersion.Parse(plugin.Version).CompareTo(PluginVersion.Parse(current.Manifest.Version!)) > 0;
                ImGui.BeginDisabled(!compatible || !newer);
                if (ImGui.Button(newer ? "更新插件" : "当前无需更新")) { pendingOffers = new[] { offer }; pendingUpdate = true; confirm = true; }
                ImGui.EndDisabled();
            }
            else if (!settings.Origins.ContainsKey(plugin.Id))
            {
                Gkms.Sdk.PluginUiText.Wrapped("这个插件来自本地安装，尚未关联更新源。");
                if (ImGui.Button("订阅此来源"))
                    Run(_ => { service.Subscribe(offer); return Task.FromResult<Action>(() => message = "已关联更新来源。"); });
            }
            else Gkms.Sdk.PluginUiText.Wrapped("此插件已订阅其他来源，统一更新会遵循原来的来源。");
            if (ImGui.Button("卸载插件")) { pendingUninstall = plugin.Id; pendingOffers = null; confirm = true; }
        }
        ImGui.EndDisabled();
    }
    public void DrawSources()
    {
        if (!initialized && !Busy) Refresh();
        ImGui.BeginDisabled(Busy);
        var official = settings.OfficialEnabled;
        if (ImGui.Checkbox("启用官方插件源", ref official)) ChangeSource(() => PluginRepositoryStore.Enable(root, "official", official));
        Gkms.Sdk.PluginUiText.Wrapped("已内置官方在线订阅源。离线使用时，将 official-plugins.zip 放入 re 目录并清空地址后保存。");
        if (ImGui.Button("恢复默认官方源"))
            ChangeSource(() => PluginRepositoryStore.SetOfficialUrl(root, PluginRepositoryStore.DefaultOfficialUrl));
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##official-source", "官方 pluginmaster.json 或 index.json 地址；留空使用随附源", ref officialUrl, 2048);
        if (ImGui.Button("保存官方地址"))
        {
            var url = officialUrl;
            Run(async token =>
            {
                var source = new PluginSource("official", "官方插件", string.IsNullOrWhiteSpace(url) ? "" : PluginRepositoryStore.NormalizeUrl(url));
                await new PluginRepositoryClient(root).ReadAsync(source, token).ConfigureAwait(false);
                PluginRepositoryStore.SetOfficialUrl(root, source.Url);
                var next = await service.RefreshAsync(token).ConfigureAwait(false);
                return () => { results = next; message = "官方插件源已保存。"; };
            });
        }
        ImGui.Separator(); ImGui.TextUnformatted("第三方插件源");
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##source-name", "来源名称", ref sourceName, 128);
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##source-url", "插件索引的 HTTPS 地址", ref sourceUrl, 2048);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(sourceUrl));
        if (ImGui.Button("添加订阅源"))
        {
            var name = sourceName; var url = sourceUrl;
            Run(async token =>
            {
                var source = new PluginSource("preview", name, PluginRepositoryStore.NormalizeUrl(url));
                await new PluginRepositoryClient(root).ReadAsync(source, token).ConfigureAwait(false);
                PluginRepositoryStore.Add(root, name, url);
                var next = await service.RefreshAsync(token).ConfigureAwait(false);
                return () => { results = next; sourceName = sourceUrl = ""; message = "已添加插件源。"; };
            });
        }
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("刷新全部来源")) Refresh();
        ImGui.Separator();
        foreach (var source in settings.Sources)
        {
            ImGui.PushID(source.Id);
            var enabled = source.Enabled;
            if (ImGui.Checkbox(source.Name, ref enabled)) ChangeSource(() => PluginRepositoryStore.Enable(root, source.Id, enabled));
            ImGui.SameLine();
            if (ImGui.Button("移除")) ChangeSource(() => PluginRepositoryStore.Remove(root, source.Id));
            Gkms.Sdk.PluginUiText.Wrapped(source.Url);
            var outcome = results.FirstOrDefault(r => r.Source.Id == source.Id);
            if (outcome?.Error != null) ImGui.TextColored(new Vector4(1, .65f, .3f, 1), outcome.Error);
            else if (outcome != null) ImGui.TextDisabled($"{outcome.Plugins.Length} 个插件");
            ImGui.PopID(); ImGui.Separator();
        }
        Gkms.Sdk.PluginUiText.Wrapped("停用或移除订阅源不会卸载已经安装的插件，但会停止检查该来源的更新。");
        ImGui.EndDisabled();
        DrawProgress();
    }
    private void ChangeSource(Action change) => Run(async token =>
    {
        change();
        var next = await service.RefreshAsync(token).ConfigureAwait(false);
        return () => { results = next; message = "订阅源已更新。"; };
    });
    private void DrawProgress()
    {
        if (Busy)
        {
            Gkms.Sdk.PluginUiText.Wrapped(progress?.Message ?? "正在读取插件源…");
            if (progress is { Received: > 0 } value)
                ImGui.ProgressBar(value.Total is > 0 ? (float)value.Received / value.Total.Value : 0,
                    new Vector2(-1, 20), $"{value.Received / 1024} / {(value.Total.HasValue ? (value.Total / 1024).ToString() : "?")} KiB");
            ImGui.BeginDisabled(progress is { CanCancel: false });
            if (ImGui.Button("取消当前操作")) { try { cancellation?.Cancel(); } catch (ObjectDisposedException) { } }
            ImGui.EndDisabled();
        }
        else Gkms.Sdk.PluginUiText.Wrapped(message);
    }
    private void DrawConfirmation()
    {
        const string title = "确认插件操作###repository-operation";
        if (confirm) { confirm = false; ImGui.OpenPopup(title); }
        var open = true;
        ImGuiAppearance.PrepareModal();
        if (ImGui.BeginPopupModal(title, ref open, ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoMove))
        {
            ImGuiAppearance.PlaceModal();
            if (pendingUninstall != null) Gkms.Sdk.PluginUiText.Wrapped("将停用并卸载 " + pendingUninstall + "。该插件的配置和数据（含资源文件）会保留，重新安装后可继续使用。");
            else if (pendingOffers != null)
            {
                Gkms.Sdk.PluginUiText.Wrapped(pendingUpdate ? "更新以下插件，保留配置、资源和原来的启用状态：" : "安装以下插件，安装后默认停用：");
                ImGui.BeginChild("operation-plugins",new Vector2(0,Math.Min(220*ImGui.GetFontSize()/18,ImGui.GetIO().DisplaySize.Y*.3f)));
                foreach (var offer in pendingOffers)
                    Gkms.Sdk.PluginUiText.Wrapped($"{offer.Plugin.DisplayName(UiText.Language)}  {installed.GetValueOrDefault(offer.Plugin.Id)?.Manifest.Version ?? "未安装"} → {offer.Plugin.Version}\n{(offer.Source.Official ? "官方" : "第三方")} · {offer.Source.Name}");
                ImGui.EndChild();
            }
            if (ImGui.Button("确认"))
            {
                var remove = pendingUninstall; var offers = pendingOffers; var update = pendingUpdate;
                log("plugin-repository: " + (remove != null ? "卸载 " + remove : (update ? "更新 " : "安装 ") + string.Join(", ", offers?.Select(o => o.Plugin.Id + " " + o.Plugin.Version + " (" + o.Source.Name + ")") ?? Array.Empty<string>())));
                pendingUninstall = null; pendingOffers = null; ImGui.CloseCurrentPopup();
                Run(async token =>
                {
                    string status;
                    if (remove != null) { var deleted = await PluginInstallation.UninstallAsync(root, remove).ConfigureAwait(false); status = deleted ? "已卸载，配置和资源已保留。" : "已卸载，占用文件将在下次启动清理。"; }
                    else if (offers == null) return () => { };
                    else if (update) status = string.Join("\n", await service.UpdateAllAsync(offers, Progress(), token).ConfigureAwait(false));
                    else { await service.InstallAsync(offers[0], false, Progress(), token).ConfigureAwait(false); status = "已安装。请在「已安装」页启用插件。"; }
                    log("plugin-repository: " + status);
                    return () => message = status;
                }, mutation: true);
            }
            ImGui.SameLine();
            if (ImGui.Button("取消")) { pendingOffers = null; pendingUninstall = null; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!open) { pendingOffers = null; pendingUninstall = null; }
    }
    public void Dispose()
    {
        disposed = true;
        try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
