using Gkms.Host;
using Gkms.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }

    private static int Run(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var repositoryCommand = RepositoryCommands.Run(root, args).GetAwaiter().GetResult();
        if (repositoryCommand.HasValue) return repositoryCommand.Value;
        PluginInstallation.Cleanup(root, Console.Error.WriteLine);
        if (args.Length == 0)
        {
            var opened = RuntimeClient.SendAsync(root, "open-panel").GetAwaiter().GetResult();
            Console.WriteLine(opened?.Success == true ? "已打开游戏内 Dear ImGui 面板。" : "游戏内面板未连接。请在游戏中按 Ctrl+U；离线配置可使用 list / enable / disable 命令。");
            return opened?.Success == true ? 0 : 1;
        }
        var catalog = Catalog.Discover(Path.Combine(root, "plugins"), Console.Error.WriteLine);
        if (args is ["install", _])
        {
            using var prepared = PluginInstallation.Prepare(root, args[1]);
            var installed = PluginInstallation.Install(prepared);
            Console.WriteLine($"已安装 {installed.Manifest.Id} {installed.Manifest.Version}，默认停用。使用 enable 启用。");
            return 0;
        }
        if (args is ["uninstall", _])
        {
            var removed = PluginInstallation.UninstallAsync(root, args[1]).GetAwaiter().GetResult();
            Console.WriteLine(removed ? "已卸载，设置和资源已保留。" : "已卸载，文件占用中；关闭游戏后重开管理器自动清理。");
            return 0;
        }
        if (args is ["reload"] or ["status"] or ["reload-resources"] or ["reload-resources", _])
        {
            var reply = RuntimeClient.SendAsync(root, args[0] == "reload" ? "reload-profile" : args[0], args.ElementAtOrDefault(1)).GetAwaiter().GetResult();
            Console.WriteLine(reply == null ? "游戏宿主未连接。" : reply.Message + " " + string.Join(", ", reply.Active));
            return reply?.Success == true ? 0 : 1;
        }
        if (args is ["check-update", _] or ["check-update", _, _])
        {
            if (!catalog.TryGetValue(args[1], out var package)) throw new InvalidDataException($"找不到插件 {args[1]}");
            var release = PluginHost.CheckResourcesAsync(package, Path.Combine(root, ".."), args.ElementAtOrDefault(2)).GetAwaiter().GetResult();
            Console.WriteLine($"{release.InstalledVersion ?? "—"} → {release.Version ?? "ZIP"} {(release.IsCurrent ? "已是当前版本" : "")}\n{release.Description}");
            return 0;
        }
        if (args is ["import-legacy"])
        {
            LegacyProfile.Import(root, catalog);
            Console.WriteLine("已从 localizationConfig.json 导入选择和参数。");
            return 0;
        }
        if (args is ["update", _] or ["update", _, _])
        {
            if (!catalog.TryGetValue(args[1], out var package)) throw new InvalidDataException($"找不到插件 {args[1]}");
            var count = PluginHost.UpdateResourcesAsync(package, Path.Combine(root, ".."), args.ElementAtOrDefault(2)).GetAwaiter().GetResult();
            Console.WriteLine($"{package.Manifest.Name} 已更新 {count} 个资源文件。");
            return 0;
        }
        if (args[0] is not ("list" or "enable" or "disable") ||
            (args[0] == "list" ? args.Length != 1 : args.Length < 2))
        {
            Console.Error.WriteLine("Gkms.Manager list | install <插件.zip> | uninstall <id> | enable <id> ... | disable <id> ... | update <id> [来源] | check-update <id> [来源] | reload | reload-resources [id] | status | import-legacy");
            return 2;
        }
        if (args[0] == "list")
        {
            var enabled = Catalog.ReadProfile(Path.Combine(root, "profile.json"), catalog).Enabled;
            foreach (var (id, p) in catalog.OrderBy(p => p.Key))
                Console.WriteLine($"{(enabled.Contains(id) ? "[x]" : "[ ]")} {id,-20} {p.Manifest.Name} (API {p.Manifest.ApiVersion})");
            foreach (var id in enabled.Except(catalog.Keys)) Console.WriteLine($"[?] {id}  插件包缺失");
            return 0;
        }
        ProfileEditor.Apply(root, catalog, args.Skip(1).Distinct().ToDictionary(id => id, _ => args[0] == "enable"));
        var applied = RuntimeClient.SendAsync(root, "reload-profile").GetAwaiter().GetResult();
        Console.WriteLine(applied == null ? "已保存，宿主未连接，下次加载生效。" : applied.Message);
        return applied?.Success == false ? 1 : 0;
    }
}
