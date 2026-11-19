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
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new PluginPicker(root));
            return 0;
        }
        var catalog = Catalog.Discover(Path.Combine(root, "plugins"), Console.Error.WriteLine);
        if (args is ["import-legacy"])
        {
            LegacyProfile.Import(root, catalog);
            Console.WriteLine("已从 localizationConfig.json 导入选择和参数。");
            return 0;
        }
        if (args[0] is not ("list" or "enable" or "disable") ||
            (args[0] == "list" ? args.Length != 1 : args.Length < 2))
        {
            Console.Error.WriteLine("Gkms.Manager list | enable <id> ... | disable <id> ... | import-legacy");
            return 2;
        }
        if (args[0] == "list")
        {
            var enabled = Catalog.ReadProfile(Path.Combine(root, "profile.json")).Enabled;
            foreach (var (id, p) in catalog.OrderBy(p => p.Key))
                Console.WriteLine($"{(enabled.Contains(id) ? "[x]" : "[ ]")} {id,-20} {p.Manifest.Name} (API {p.Manifest.ApiVersion})");
            foreach (var id in enabled.Except(catalog.Keys)) Console.WriteLine($"[?] {id}  插件包缺失");
            return 0;
        }
        ProfileEditor.Apply(root, catalog, args.Skip(1).Distinct().ToDictionary(id => id, _ => args[0] == "enable"));
        Console.WriteLine("已保存，下一次加载宿主时生效。");
        return 0;
    }
}
