using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;
using Gkms.Host;
using Gkms.Sdk;

internal static class TextStartupTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("启动时文本翻译: " + message); }
    private static void RejectReload(PluginHost host)
    {
        try { host.ReloadResources("localization"); }
        catch (InvalidOperationException error) when (error.Message.Contains("不支持主动资源重载") || error.Message.Contains("尚未启用")) { return; }
        throw new Exception("文本资源重载必须被明确拒绝");
    }
    public static void Run(string work, string sourcePackages)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "text-startup")).FullName;
        var packages = TestPackages.Copy(sourcePackages, root);
        var resources = Directory.CreateDirectory(TestPackages.Resources(packages, "localization")).FullName;
        var master = Directory.CreateDirectory(Path.Combine(resources, "masterTrans")).FullName;
        var stories = Directory.CreateDirectory(Path.Combine(resources, "resource")).FullName;
        var split = Directory.CreateDirectory(Path.Combine(resources, "genericTrans")).FullName;
        var profile = Path.Combine(root, "profile.json");
        void Profile(bool text, bool gyro = false, bool diagnose = false) => File.WriteAllText(profile,
            JsonSerializer.Serialize(new { enabled = (text ? new[] { "localization" } : Array.Empty<string>()).Concat(gyro ? new[] { "gyro" } : Array.Empty<string>()),
                settings = new { localization = new { textTest = diagnose } } }));
        void Resources(string version)
        {
            File.WriteAllText(Path.Combine(resources, "localization.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["home.mission_pass"] = "通票" + version, ["other"] = "另一个" + version }));
            File.WriteAllText(Path.Combine(resources, "generic.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["パス"] = "通行证", ["原文"] = "普通" + version, ["段落"] = "中间", ["中间"] = "最终", ["同文"] = "同文" }));
            File.WriteAllText(Path.Combine(master, "Card.json"), """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"主表VERSION"}]}""".Replace("VERSION", version));
            File.WriteAllText(Path.Combine(stories, "story.txt"), "剧情" + version);
        }
        // A broken file must not disable healthy files at startup.
        File.WriteAllText(Path.Combine(split, "broken.json"), "{");
        File.WriteAllText(Path.Combine(master, "broken.json"), "{");
        Resources("一"); Profile(true);
        var unusedStory = Path.Combine(stories, "unused-story.txt");
        File.WriteAllText(unusedStory, "本次不请求的文件");
        using var unreadableUnusedStory = new FileStream(unusedStory, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var logs = new List<string>(); var game = new TestGame();
        var existing = new Tmp { Value = "原文" };
        UnityEngine.Object.SceneObjects = new UnityEngine.Object[] { existing };
        using (var host = new PluginHost(game, logs.Add))
        {
            host.Start(packages, profile, root);
            Check(host.Active.Contains("localization"), string.Join("\n", logs));
            Check(!logs.Any(v => v.Contains("unused-story.txt")), "启动不读取本次未请求的剧情文件");
            Check(existing.text == "原文", "不再扫描或改写启动前的控件");
            Check(host.GetWarnings().Values.SelectMany(v => v).Any(v => v.Contains("broken.json")), "坏文件逐个报告\n" + string.Join("\n", logs));
            var ui = new Ui(); ui.SetValue("home.mission_pass", "パス"); ui.SetValue("other", "パス");
            var text = new Tmp(); text.text = ui.GetValue("home.mission_pass");
            Check(text.text == "通票一" && ui.GetValue("other") == "另一个一", "按键选择不同界面译文");
            var generic = new Tmp(); generic.SetText("パス"); Check(generic.text == "通行证", "无键输入只走正常通用翻译");
            var card = new Card { Name = "原始字段" }; card.Read();
            card.Parse = c => { Check(c.Name == "主表一", "游戏解析看到当前值，不先恢复原文"); c.Name = "服务器新值"; };
            card.Read(); Check(card.Name == "主表一", "游戏解析完成后应用主表");
            card.Parse = c => { c.Name = "部分写入"; throw new InvalidOperationException("parse"); };
            try { card.Read(); } catch (InvalidOperationException e) when (e.Message == "parse") { }
            Check(card.Name == "部分写入", "解析失败不翻译也不恢复旧字段"); card.Parse = null;
            var nested = new Tmp(); var ca = new Tmp();
            ca.Written = _ => nested.SetText("段落"); ca.SetCharArray("段落".ToCharArray(), 0, 2);
            Check(ca.text == "中间" && nested.text == "中间", "字符数组只绕过自身 setter，不绕过嵌套其他入口");
            ca.Written = self => { self.Written = null; self.text = "中间"; }; ca.SetCharArray("段落".ToCharArray(), 0, 2);
            Check(ca.text == "最终", "同对象嵌套 setter 仍执行正常翻译");
            ca.FailWriteBefore = true;
            try { ca.SetCharArray("段落".ToCharArray(), 0, 2); } catch (Exception e) when (e.GetBaseException() is InvalidOperationException) { }
            ca.FailWriteBefore = false; ca.text = "中间"; Check(ca.text == "最终", "异常后清理一次性绕过");
            var awake = new TMPro.TextMeshProUGUI { Value = "段落" }; awake.Awake(); Check(awake.text == "最终", "Awake 经正常 setter 再替换");
            var octo = new Octo(); string story = "";
            octo.LoadFromCacheOrDownload("story.txt", (t, _) => story = t, null); Check(story == "剧情一", "剧情按资源路径替换");
            StoryBytes(stories, octo);
            Resources("二"); File.WriteAllText(Path.Combine(stories, "late.txt"), "新增剧情");
            File.WriteAllText(Path.Combine(resources, "localization.json"), "{}");
            RejectReload(host); Check(text.text == "通票一", "拒绝重载不回写当前显示");
            for (var i = 0; i < 3; i++)
            {
                Profile(false, gyro: true, diagnose: true); host.ReloadProfile();
                Check(host.Active.Contains("localization") && host.Active.Contains("gyro"), "文本停用延后，其他插件立即启用");
                Check(host.DescribeStatus().Contains("待重启") && !host.DescribeStatus().Contains("下次启动"), "待重启只显示状态，不重复解释生效时机");
                var unmodified = new Tmp(); unmodified.SetText("未译");
                Check(unmodified.text == "未译", "运行中保存诊断参数也不会改变本次翻译行为");
                ui.SetValue("home.mission_pass", "パス"); text.SetText(ui.GetValue("home.mission_pass"), true); card.Read();
                generic.PopulateTextBackingArray("原文", 0, 2);
                Check(text.text == "通票一" && card.Name == "主表一" && generic.text == "普通一", "页面重新赋值仍使用本次启动的词典");
                var page = new Tmp(); page.text = ui.GetValue("home.mission_pass"); Check(page.text == "通票一", "新页面仍使用同一专用译文");
                octo.LoadFromCacheOrDownload("story.txt", (t, _) => story = t, null); Check(story == "剧情二", "后续请求读取更新后的剧情文件");
                octo.LoadFromCacheOrDownload("late.txt", (t, _) => story = t, null); Check(story == "新增剧情", "本次启动后新增文件在后续请求生效");
                Profile(true); host.ReloadProfile(); Check(!host.Active.Contains("gyro"), "其他插件立即停用");
            }
            var transient = Path.Combine(stories, "transient.txt");
            var originalCalls = octo.OriginalCalls;
            var callbackCalls = 0;
            void Request(string name) => octo.LoadFromCacheOrDownload(name, (t, _) => { story = t; callbackCalls++; }, null);
            Request("transient.txt"); Check(story == "orig" && callbackCalls == 1 && octo.OriginalCalls == originalCalls + 1, "缺文件只调用原加载一次");
            File.WriteAllText(transient, "恢复可读");
            using (var held = new FileStream(transient, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Request("transient.txt");
                Check(story == "orig" && callbackCalls == 2 && octo.OriginalCalls == originalCalls + 2 &&
                    host.GetWarnings().Values.SelectMany(v => v).Any(v => v.Contains("transient.txt")), "读取失败逐文件报告并回退原加载");
                Request("story.txt"); Check(story == "剧情二" && callbackCalls == 3, "一个文件失败不阻断其他剧情");
            }
            Request("transient.txt");
            Check(story == "恢复可读" && callbackCalls == 4 && octo.OriginalCalls == originalCalls + 2 &&
                !host.GetWarnings().Values.SelectMany(v => v).Any(v => v.Contains("transient.txt")), "后续请求重新尝试读取并清除该文件旧错误");
            File.WriteAllText(transient, ""); Request("transient.txt");
            Check(story == "" && callbackCalls == 5 && octo.OriginalCalls == originalCalls + 2, "空文件仍按原版回调空文本");
            File.Delete(transient); Request("transient.txt");
            Check(story == "orig" && callbackCalls == 6 && octo.OriginalCalls == originalCalls + 3, "删除文件后不继续使用旧缓存");
            Profile(false); host.ReloadProfile();
        }
        Check(game.Live == 0, "关闭离线宿主释放全部回调");
        using (var host = new PluginHost(new TestGame(), logs.Add))
        {
            host.Start(packages, profile, root);
            Check(!host.Active.Contains("localization"), "下次启动采用保存的关闭状态");
            Resources("二"); Profile(true, true); host.ReloadProfile();
            var ui = new Ui(); ui.SetValue("home.mission_pass", "パス"); var text = new Tmp(); text.text = "原文";
            Check(!host.Active.Contains("localization") && host.Active.Contains("gyro") && ui.Value == "パス" && text.text == "原文", "关闭状态下运行中请求开启同样延后");
            RejectReload(host);
        }
        Profile(true, true, true);
        using (var host = new PluginHost(new TestGame(), logs.Add))
        {
            host.Start(packages, profile, root);
            var ui = new Ui(); ui.SetValue("home.mission_pass", "パス"); var card = new Card(); card.Read();
            var text = new Tmp(); text.text = "原文"; string story = "";
            new Octo().LoadFromCacheOrDownload("late.txt", (t, _) => story = t, null);
            Check(ui.Value == "通票二" && card.Name == "主表二" && text.text == "普通二" && story == "新增剧情", "再次启动同时采用新开关和全部新词典");
            ui.SetValue("missing", ""); Check(ui.Value == "[I18]", "I18n 空串诊断标记");
            text.text = "未译"; Check(text.text == "[TT]未译", "setter 诊断标记");
            text.SetText("未译"); Check(text.text == "[T1]未译", "单参数 SetText 诊断标记");
            text.SetText("未译", true); Check(text.text == "[TS]未译", "双参数 SetText 诊断标记");
            text.SetCharArray("未译".ToCharArray(), 0, 2); Check(text.text == "[CA]未译", "字符数组不叠加 setter 诊断标记");
            text.PopulateTextBackingArray("未译", 0, 2); Check(text.text == "[TP]未译", "完整切片诊断标记");
            var legacy = new UnityEngine.UI.Text(); legacy.text = "未译"; Check(legacy.text == "[UI]未译", "旧 UI 诊断标记");
            var field = new UnityEngine.UIElements.TextField(); field.value = "未译"; Check(field.value == "未译", "原 DMM TextField 不加诊断标记");
        }
        UnityEngine.Object.SceneObjects = Array.Empty<UnityEngine.Object>();
        ResourceLoading(work, sourcePackages);
        Console.WriteLine("文本翻译离线回归通过：开关/启动词典延后、剧情按请求读取及失败重试、正常入口、嵌套 setter、其他插件热切换；不代表实机通过。");
    }

    private static void ResourceLoading(string work, string sourcePackages)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "text-loading")).FullName;
        var packages = TestPackages.Copy(sourcePackages, root);
        var resources = Directory.CreateDirectory(TestPackages.Resources(packages, "localization")).FullName;
        var master = Directory.CreateDirectory(Path.Combine(resources, "masterTrans")).FullName;
        var generic = Directory.CreateDirectory(Path.Combine(resources, "genericTrans")).FullName;
        var i18nFile = Path.Combine(resources, "localization.json");
        var masterFile = Path.Combine(master, "Card.json");
        var profile = Path.Combine(root, "profile.json");
        File.WriteAllText(profile, """{"enabled":["localization"]}""");
        File.WriteAllText(i18nFile, """{"key":"第2回"}""");
        File.WriteAllText(masterFile, """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"第3回"}]}""");
        File.WriteAllText(Path.Combine(generic, "number.fmt.json"), """{"第{0}回":"错误改写{0}"}""");
        File.WriteAllText(Path.Combine(resources, "generic.json"), """{"原文":"通用译文"}""");
        var game = new TestGame(); var logs = new List<string>();
        FileStream? heldI18n = null, heldMaster = null;
        game.BeforePatch = (owner, method) =>
        {
            if (owner == "framework" || method.DeclaringType != typeof(Ui) || method.Name != "SetValue") return;
            game.BeforePatch = null;
            // Files change between feature registration. Actual writers and generic
            // protection must still share the same successful startup reads.
            File.WriteAllText(i18nFile, """{"key":"第4回"}""");
            File.WriteAllText(masterFile, """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"第5回"}]}""");
            heldI18n = new(i18nFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            heldMaster = new(masterFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        };
        try
        {
            using var host = new PluginHost(game, logs.Add);
            host.Start(packages, profile, root);
            Check(heldI18n != null && heldMaster != null, "共享资源回归确实在组件注册期间修改并锁定文件");
            var ui = new Ui(); ui.SetValue("key", "日文"); var card = new Card(); card.Read();
            var text = new Tmp(); text.text = ui.Value;
            Check(text.text == "第2回", "I18n 实际值与通用保护来自同一次读取");
            text.text = card.Name; Check(text.text == "第3回", "主表实际值与通用保护来自同一次解析");
            text.text = "第6回"; Check(text.text == "错误改写6", "保护未扩大到未加载的新值或相同格式");
            Check(!logs.Any(l => l.Contains("资源文件读取失败")), "已加载文件不会在后续组件启动中重新打开");
        }
        finally { heldI18n?.Dispose(); heldMaster?.Dispose(); }

        File.Delete(i18nFile);
        using (var host = new PluginHost(new TestGame(), logs.Add))
        {
            host.Start(packages, profile, root);
            var text = new Tmp(); text.text = "原文";
            Check(text.text == "原文", "缺 localization.json 时按 DMM 不加载通用词典");
            var card = new Card(); card.Read(); Check(card.Name == "第5回", "缺 localization.json 不阻断主数据");
            var stories = Path.Combine(resources, "resource");
            Check(!Directory.Exists(stories), "本次启动时剧情根目录确实不存在");
            var octo = new Octo(); string story = "";
            octo.LoadFromCacheOrDownload("late.txt", (s, _) => story = s, null);
            Check(story == "orig" && octo.OriginalCalls == 1, "缺剧情目录时仍走游戏加载");
            Directory.CreateDirectory(stories); File.WriteAllText(Path.Combine(stories, "late.txt"), "目录后来新增");
            octo.LoadFromCacheOrDownload("late.txt", (s, _) => story = s, null);
            Check(story == "目录后来新增" && octo.OriginalCalls == 1, "启动后新建整个剧情目录也能在请求时生效");
            File.WriteAllText(i18nFile, "{}"); text.text = "原文";
            Check(text.text == "原文", "补齐启动词典仍需下次启动，剧情例外不扩大");
        }
        using (var host = new PluginHost(new TestGame(), logs.Add))
        {
            host.Start(packages, profile, root);
            var text = new Tmp(); text.text = "原文";
            Check(text.text == "通用译文", "下次启动存在 localization.json 后恢复通用加载");
        }
    }

    private static void StoryBytes(string stories, Octo octo)
    {
        var cases = new Dictionary<string, byte[]>
        {
            ["utf8"] = Encoding.UTF8.GetBytes("剧情\r\n第二行\n第三行\r末尾\r\r\n尾\r"),
            ["bom"] = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("剧情\r\n正文")).ToArray(),
            ["utf16"] = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("UTF16 剧情")).ToArray(),
            ["ctrlz"] = Encoding.UTF8.GetBytes("正文\u001a不可见"),
            ["nul"] = Encoding.UTF8.GetBytes("正文\0不可见")
        };
        foreach (var (name, bytes) in cases)
        {
            var file = Path.Combine(stories, name + ".txt"); File.WriteAllBytes(file, bytes);
            // Independent Windows CRT text reader, matching std::ifstream's mode.
            var handle = OpenText(file, "rt"); Check(handle != IntPtr.Zero, "CRT 对照文件打开");
            var read = new List<byte>();
            try { int value; while ((value = GetChar(handle)) >= 0) read.Add((byte)value); }
            finally { Check(CloseText(handle) == 0, "CRT 对照文件关闭"); }
            var nul = read.IndexOf(0); if (nul >= 0) read.RemoveRange(nul, read.Count - nul);
            var expected = Encoding.UTF8.GetString(read.ToArray());
            string actual = ""; var calls = 0; var originals = octo.OriginalCalls;
            octo.LoadFromCacheOrDownload(name + ".txt", (s, _) => { actual = s; calls++; }, null);
            Check(actual == expected && calls == 1 && octo.OriginalCalls == originals, "剧情字节读取对齐 Windows CRT: " + name);
            if (name == "bom") Check(actual.StartsWith('\ufeff'), "原版保留 UTF-8 BOM");
            if (name == "utf16") Check(actual != "UTF16 剧情", "不再自动将 UTF-16 当作支持的剧情编码");
        }
    }
    [DllImport("ucrtbase.dll", EntryPoint = "_wfopen", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr OpenText(string path, string mode);
    [DllImport("ucrtbase.dll", EntryPoint = "fgetc", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetChar(IntPtr file);
    [DllImport("ucrtbase.dll", EntryPoint = "fclose", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CloseText(IntPtr file);
}
