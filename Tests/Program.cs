using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gkms.Host;
using Gkms.Plugins.Text;
using Gkms.Plugins.Master;
using HarmonyLib;

var packages = args[0];
var work = Path.Combine(args[1], Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    Verify(work, packages);
    VerifyAssets(work, packages);
    VerifyUpdate(work);
    VerifyManager(work, Path.GetDirectoryName(Path.GetFullPath(packages))!);
    if (args.Length > 2) VerifyPack(args[2]);
    Console.WriteLine("PASS");
    return 0;
}
finally { Directory.Delete(work, recursive: true); }

static void Check(bool condition, string detail)
{
    if (!condition) throw new Exception(detail);
}

static void Verify(string work, string packages)
{
    var local = Directory.CreateDirectory(Path.Combine(work, "local-files")).FullName;
    var generic = Directory.CreateDirectory(Path.Combine(local, "genericTrans")).FullName;
    var resources = Directory.CreateDirectory(Path.Combine(local, "resource")).FullName;
    File.WriteAllText(Path.Combine(local,"localization.json"), """{"key":"蒼"}""");
    File.WriteAllText(Path.Combine(local,"generic.json"), """{"花":"花朵"}""");
    File.WriteAllText(Path.Combine(generic,"a.fmt.json"), """{"第{0}回":"第{0}次"}""");
    File.WriteAllText(Path.Combine(generic,"b.split.json"), """{"[__split__]次回":"[__split__]下次"}""");
    File.WriteAllText(Path.Combine(resources,"scene.txt"), "剧情");
    var dict = new TextDictionary(local);
    Check(dict.Translate("花++") == "花朵++" && dict.Translate("第35回") == "第35次" &&
          dict.Translate("<b>次回</b>") == "<b>下次</b>" && dict.Translate("unknown") == "unknown", "通用词典");
    var masterFile = Path.Combine(local, "Card.json");
    File.WriteAllText(masterFile, """{"rules":{"primaryKeys":["id","rank","parts.kind"]},"data":[{"id":"p","rank":1,"name":"译名","parts":[{"kind":"PartKind_Second","text":"子项"}]}]}""");
    var card = new Card();
    new MasterTable(masterFile).Apply(card);
    Check(card.Name == "译名" && card.Parts[0].Text == "" && card.Parts[1].Text == "子项", "主数据复合主键与子表");

    var log = new List<string>();
    var profile = Path.Combine(work, "profile.json");
    var testGame = new TestGame();
    var i18n = new Ui();
    var tmp = new Tmp();
    var octo = new Octo();
    using (var host = new PluginHost(testGame, log.Add))
    {
        host.Start(packages, profile, work);
        Check(host.Active.Count == 0 && testGame.Live == 0, "空配置");
        File.WriteAllText(profile, """{"enabled":["missing","i18n","frame-rate","text","story","text"],"settings":{"frame-rate":{"fps":-4}}}""");
        host.Start(packages, profile, work);
        Check(host.Active.SequenceEqual(new[]{"i18n","text","story"}), string.Join(Environment.NewLine, log));
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Gkms.Textures"), "未选中的 DLL 已被加载");
        i18n.SetValue("key", "original");
        tmp.SetText("第12回");
        string? result = null;
        octo.LoadFromCacheOrDownload("scene.txt", (text, error) => result = text, null);
        Check(i18n.Value == "蒼" && tmp.Value == "第12次" && result == "剧情" && octo.OriginalCalls == 0, "运行入口");
        octo.LoadFromCacheOrDownload(@"..\scene.txt", (text, error) => result = text, null);
        Check(octo.OriginalCalls == 1, "资源目录边界");
        var count = testGame.Live;
        host.Start(packages, profile, work);
        Check(testGame.Live == count, "重复启动");
    }
    i18n.SetValue("key", "original"); tmp.SetText("花"); Check(testGame.Live == 0 && i18n.Value == "original" && tmp.Value == "花", "停用后恢复原方法");

    // A plugin fails after its first patch, then another plugin still starts.
    testGame.FailAfterFirstText = true;
    File.WriteAllText(profile, """{"enabled":["text","i18n"]}""");
    using var retry = new PluginHost(testGame, log.Add);
    retry.Start(packages, profile, work);
    tmp.SetText("花");
    Check(retry.Active.SequenceEqual(new[]{"i18n"}) && tmp.Value == "花" && testGame.Live == 1, "启动失败回滚");
}

static void VerifyAssets(string work, string packages)
{
    File.WriteAllBytes(Path.Combine(work, "local-files", "gakumasassets"), new byte[] { 1 });
    var png = Directory.CreateDirectory(Path.Combine(work, "texture2d")).FullName;
    File.WriteAllBytes(Path.Combine(png, "title.png"), new byte[] { 1 });
    var original = new UnityEngine.Texture2D(8, 8) { name = "title", filterMode = 2 };
    var canvas = new UnityEngine.CanvasRenderer();
    var text = new TMPro.TextMeshProUGUI();
    var profile = Path.Combine(work, "profile.json");
    File.WriteAllText(profile, """{"enabled":["font","textures"]}""");
    var log = new List<string>();
    using (var host = new PluginHost(new TestGame(), log.Add))
    {
        host.Start(packages, profile, work);
        Check(host.Active.Count == 2, string.Join(Environment.NewLine, log));
        text.Awake(); text.Awake();
        canvas.SetTexture(original);
        Check(TMPro.TMP_Settings.fallbackFontAssets.Count == 1 && canvas.Current != original && canvas.Current?.filterMode == 2,
            string.Join(Environment.NewLine, log));
    }
    canvas.SetTexture(original);
    Check(TMPro.TMP_Settings.fallbackFontAssets.Count == 0 && canvas.Current == original, "字体和贴图停用后应恢复");
}

static void VerifyManager(string work, string distribution)
{
    var data = Directory.CreateDirectory(Path.Combine(work, "manager-fixture", "gakumas-local")).FullName;
    var root = Directory.CreateDirectory(Path.Combine(data, "re")).FullName;
    foreach (var file in Directory.EnumerateFiles(distribution, "Gkms.*"))
        File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
    foreach (var (id, package) in Catalog.Discover(Path.Combine(distribution, "plugins"), _ => {}))
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "plugins", id)).FullName;
        File.Copy(Path.Combine(package.Directory, "plugin.json"), Path.Combine(folder, "plugin.json"));
    }
    int Run(params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"Gkms.Manager.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    }
    var fileName = Path.Combine(root, "profile.json");
    File.WriteAllText(fileName, """{"enabled":[],"settings":{"camera":{"speed":5}}}""");
    Check(Run("enable","story","unknown") != 0 && Catalog.ReadProfile(fileName).Enabled.Length == 0, "批量选择应全部生效或保留原配置");
    Check(Run("enable","story","i18n") == 0 && Run("disable","story") == 0 && Run("list") == 0 &&
          Catalog.ReadProfile(fileName).Enabled.SequenceEqual(new[]{"i18n"}), "管理器选择");
    File.WriteAllText(Path.Combine(data, "localizationConfig.json"),
        """{"enabled":true,"targetFrameRate":120,"replaceFont":false,"enableFreeCamera":true}""");
    Check(Run("import-legacy") == 0, "旧版选项导入");
    var imported = Catalog.ReadProfile(fileName);
    Check(imported.Enabled.Contains("camera") && imported.Enabled.Contains("frame-rate") && !imported.Enabled.Contains("font") &&
        imported.Settings!["frame-rate"].GetProperty("fps").GetInt32() == 120 &&
        imported.Settings["camera"].GetProperty("speed").GetInt32() == 5, "旧版参数迁移");
}

static void VerifyUpdate(string work)
{
    using var data = new MemoryStream();
    using (var writer = new System.IO.Compression.ZipArchive(data, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        using var json = new StreamWriter(writer.CreateEntry("release/local-files/localization.json").Open());
        json.Write("""{"key":"更新"}""");
    }
    data.Position = 0;
    using var zip = new System.IO.Compression.ZipArchive(data);
    var count = Gkms.Resources.PackInstaller.Install(work, false, zip, null);
    Check(count == 1 && File.ReadAllText(Path.Combine(work, "local-files", "localization.json")).Contains("更新") &&
          File.Exists(Path.Combine(work, "local-files", "generic.json")) &&
          File.Exists(Path.Combine(work, "re", "local-files.previous", "localization.json")), "资源更新保存自定义文件与上一版");
    using var bad = new MemoryStream();
    using (var writer = new System.IO.Compression.ZipArchive(bad, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        writer.CreateEntry("texture2d/texture_version.txt");
        writer.CreateEntry("texture2d/../outside.png");
    }
    bad.Position = 0;
    using var invalid = new System.IO.Compression.ZipArchive(bad);
    try { Gkms.Resources.PackInstaller.Install(work, true, invalid, "v1"); throw new Exception("错误接受了越界资源"); }
    catch (InvalidDataException) { }
    Check(File.ReadAllBytes(Path.Combine(work, "texture2d", "title.png")).SequenceEqual(new byte[] { 1 }) &&
          !File.Exists(Path.Combine(work, "texture2d", "texture_version.txt")) &&
          !File.Exists(Path.Combine(work, "re", "outside.png")), "损坏资源应保留已安装的贴图且不能写入包外");
}

static void VerifyPack(string root)
{
    var local = Path.Combine(root, "local-files");
    var dictionary = new TextDictionary(local);
    var tables = Directory.GetFiles(Path.Combine(local,"masterTrans"), "*.json");
    foreach (var path in tables) _ = new MasterTable(path);
    var i18n = Gkms.Sdk.Interop.ReadDictionary(Path.Combine(local, "localization.json"));
    Check(dictionary.Count > 0 && i18n.Count > 0 && tables.Length > 0, "真实词库为空");
    Console.WriteLine($"资源包通过，界面键 {i18n.Count}，通用词条 {dictionary.Count}，主数据表 {tables.Length}。");
}

public sealed class Card { public string Id => "p"; public int Rank => 1; public string Name { get; set; } = ""; public List<Part> Parts { get; } = new(){new(PartKind.First),new(PartKind.Second)}; }
public enum PartKind { First, Second }
public sealed record Part(PartKind Kind) { public string Text { get; set; } = ""; }
public sealed class Ui
{
    public string Value = "";
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetValue(string key, string value) => Value = value;
}
public sealed class Tmp
{
    public string Value = "";
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetText(string value) => Value = value;
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetText(string value, bool sync) => Value = value;
}
public sealed class Octo
{
    public int OriginalCalls;
    [MethodImpl(MethodImplOptions.NoInlining)] public void LoadFromCacheOrDownload(string name, Action<string, object?> onComplete, object? progress) { OriginalCalls++; onComplete("orig",null); }
}
public sealed class TestGame : IGameServices
{
    public int Live;
    public bool FailAfterFirstText;
    private int textPatches;
    public Type? FindType(string assembly, string fullName) => fullName switch
        { "Qua.UI.I18n" => typeof(Ui), "TMPro.TMP_Text" => typeof(Tmp), "Octo.Loader.OctoResourceLoader" => typeof(Octo),
          _ => typeof(TestGame).Assembly.GetType(fullName) };
    public IDisposable Patch(string owner, MethodInfo method, MethodInfo? prefix, MethodInfo? postfix, int priority)
    {
        if (FailAfterFirstText && owner == "text" && ++textPatches == 2) throw new Exception("模拟游戏更新导致签名不兼容");
        var harmony = new Harmony("tests." + owner);
        harmony.Patch(method, prefix == null ? null : new HarmonyMethod(prefix) { priority = priority },
            postfix == null ? null : new HarmonyMethod(postfix) { priority = priority });
        Live++;
        return new Cleanup(() => { harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id); Live--; });
    }
    private sealed record Cleanup(Action Callback) : IDisposable { public void Dispose() => Callback(); }
}
