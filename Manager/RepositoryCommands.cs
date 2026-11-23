using Gkms.Host;

namespace Gkms.Manager;

internal static class RepositoryCommands
{
    public static async Task<int?> Run(string root, string[] args)
    {
        if (args is ["export-repository", var packages, var bundle, var index])
        {
            Console.WriteLine($"已生成 {PluginRepositoryPublisher.Export(packages, bundle, index)} 个插件的官方源、索引和安装包。");
            return 0;
        }
        if (args is ["export-repository", var masterPackages, var masterBundle, var legacyIndex, var master])
        {
            Console.WriteLine($"已生成 {PluginRepositoryPublisher.Export(masterPackages, masterBundle, legacyIndex, master)} 个插件的官方源、index.json 和 pluginmaster.json。");
            return 0;
        }
        if (args.Length == 0 || args[0] is not ("sources" or "plugins-online" or "plugins-check" or "plugins-install" or "plugins-update" or "plugins-subscribe")) return null;
        var service = new PluginRepositoryService(root);
        if (args is ["sources"])
        {
            foreach (var source in PluginRepositoryStore.Sources(PluginRepositoryStore.Read(root)))
                Console.WriteLine($"{(source.Enabled ? "[x]" : "[ ]")} {source.Id}  {source.Name}  {(source.Bundled ? "随附官方源" : source.Url)}");
            return 0;
        }
        if (args is ["sources", "add", var name, var url])
        {
            await new PluginRepositoryClient(root).ReadAsync(new("preview", name, PluginRepositoryStore.NormalizeUrl(url)));
            PluginRepositoryStore.Add(root, name, url); Console.WriteLine("已添加订阅源。"); return 0;
        }
        if (args is ["sources", "official", var official])
        {
            var normalized = official == "bundled" ? "" : PluginRepositoryStore.NormalizeUrl(official);
            await new PluginRepositoryClient(root).ReadAsync(new("official", "官方插件", normalized));
            PluginRepositoryStore.SetOfficialUrl(root, normalized); return 0;
        }
        if (args is ["sources", "remove", var removed]) { PluginRepositoryStore.Remove(root, removed); return 0; }
        if (args is ["sources", "enable" or "disable", var sourceId])
        { PluginRepositoryStore.Enable(root, sourceId, args[1] == "enable"); return 0; }
        if (args is ["plugins-online"] or ["plugins-check"] or ["plugins-install", _, _] or ["plugins-subscribe", _, _] or ["plugins-update", _])
        {
            PluginInstallation.Cleanup(root, Console.Error.WriteLine);
            var results = await service.RefreshAsync();
            foreach (var failure in results.Where(r => r.Error != null)) Console.Error.WriteLine(failure.Source.Name + ": " + failure.Error);
            var offers = results.SelectMany(r => r.Plugins).ToArray();
            var installed = Catalog.Discover(Path.Combine(root, "plugins"), Console.Error.WriteLine);
            var updates = PluginRepositoryService.Updates(results, installed, PluginRepositoryStore.Read(root));
            if (args[0] is "plugins-online" or "plugins-check")
            {
                foreach (var offer in args[0] == "plugins-online" ? offers : updates)
                    Console.WriteLine($"{offer.Source.Id}  {offer.Plugin.Id}  {installed.GetValueOrDefault(offer.Plugin.Id)?.Manifest.Version ?? "未安装"} -> {offer.Plugin.Version}  API {offer.Plugin.ApiVersion}  {offer.Plugin.Name}");
                Console.WriteLine($"{updates.Length} 个插件可更新。");
                return results.Any(r => r.Error != null) ? 1 : 0;
            }
            if (args[0] == "plugins-update")
            {
                var selected = updates.Where(o => args[1] == "all" || o.Plugin.Id == args[1]).ToArray();
                foreach (var offer in selected) { await service.InstallAsync(offer, true); Console.WriteLine($"{offer.Plugin.Id}: 已更新至 {offer.Plugin.Version}"); }
                return results.Any(r => r.Error != null) ? 1 : 0;
            }
            var chosen = offers.SingleOrDefault(o => o.Source.Id == args[1] && o.Plugin.Id == args[2]) ?? throw new InvalidOperationException("在启用的来源中找不到该插件。");
            if (args[0] == "plugins-subscribe") { service.Subscribe(chosen); Console.WriteLine("已关联更新来源。"); }
            else { await service.InstallAsync(chosen, false); Console.WriteLine("已安装，默认停用。"); }
            return 0;
        }
        Console.Error.WriteLine("sources [add <名称> <URL> | remove <源ID> | enable/disable <源ID> | official <URL|bundled>] | plugins-online | plugins-check | plugins-install <源ID> <插件ID> | plugins-subscribe <源ID> <插件ID> | plugins-update <插件ID|all>");
        return 2;
    }
}
