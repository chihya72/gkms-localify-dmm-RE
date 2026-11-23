using System.Runtime.CompilerServices;
using System.Text.Json;
using Gkms.Host;
using Gkms.Plugins.Localization;

internal static class AuditTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string work, string sourcePackages)
    {
        var data = Directory.CreateDirectory(Path.Combine(work, "audit")).FullName;
        var packages = TestPackages.Copy(sourcePackages, data);
        var root = Directory.CreateDirectory(TestPackages.Resources(packages, "localization")).FullName;
        var master = Directory.CreateDirectory(Path.Combine(root, "masterTrans")).FullName;
        File.WriteAllText(Path.Combine(root, "localization.json"), """{"key":"旧界面"}""");
        File.WriteAllText(Path.Combine(root, "generic.json"), """{"word":"旧文本","回数<color=red>1</color>回":"次数<color=red>1</color>次"}""");
        File.WriteAllText(Path.Combine(root, "generic.split.json"), """{"[__split__]次回":"[__split__]下次"}""");
        File.WriteAllBytes(Path.Combine(root, "gakumasassets"), new byte[] { 1 });
        var table = Path.Combine(master, "Card.json");
        File.WriteAllText(table, """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"旧表名"}]}""");
        var dictionary = new TextDictionary(root);
        Check(dictionary.Translate("【次回】：次回") == "【下次】：下次" && dictionary.Translate("回数") == "次数",
            "原版括号、冒号分割和带数字标签的短语提取");
        var profile = Path.Combine(data, "re", "profile.json");
        var ids = Catalog.Discover(packages, _ => { }).Keys.OrderBy(x => x).ToArray();
        Check(ids.Length == 14, "全插件核对需要实际的 14 个插件包");
        File.WriteAllText(profile, JsonSerializer.Serialize(new { enabled = ids, settings = new { physics = new { useScale = true, scale = 1.2, axisX = new[] { 2, .5 } } } }));
        var log = new List<string>(); var game = new TestGame();
        var quality = new Campus.Common.CampusQualityManager();
        var rig = new Campus.Common.CampusActorAnimation(); var bones = new Campus.Common.CampusActorAnimationInitializeData();
        var bone = bones.swingBreastBones[0]; bone.spring = 2;
        var keyed = new Ui(); var text = new Tmp(); var card = new Card { Name = "original" };
        var initializedBeforePlugin = new Ui();
        var existingText = new Tmp { text = "word", font = new TMPro.TMP_FontAsset { name = "campusnormal" } };
        var existingFont = existingText.font.sourceFontFile;
        UnityEngine.Object.SceneObjects = new UnityEngine.Object[] { existingText };
        var photoChoices = Campus.Live.LiveUtility.Choices;
        UnityEngine.Application.targetFrameRate = 30;
        using (var host = new PluginHost(game, log.Add))
        {
            host.Start(packages, profile, data);
            initializedBeforePlugin.SetValue("key", "预先缓存的日文");
            File.WriteAllText(profile, JsonSerializer.Serialize(new { enabled = ids, settings = new { physics = new { useScale = true, scale = 1.2, axisX = new[] { 2, .5 } } } }));
            host.Start(packages, profile, data);
            Check(host.Active.Count == 14 && !log.Any(l => l.Contains("加载失败")), string.Join("\n", log));
            Check(initializedBeforePlugin.GetValue("key") == "旧界面", "热启用接管已经初始化的界面词典读取");
            Check(existingText.text == "word" && existingText.font.sourceFontFile != existingFont, "不扫描翻译已有文本；框架内置字体独立替换");
            Check(UnityEngine.Application.targetFrameRate == 60, "帧率热启用立即应用");
            quality.Update(); Check(quality.TargetFrameRate == 60, "下一帧同步游戏帧率管理器");
            quality.TargetFrameRate = 45;
            quality.ApplySetting(1, 1000, 1, 0);
            Check(quality.Settings == (3, 3384, .77f, 3) && quality.Reflection == Campus.Common.QualityLevel.Higher &&
                quality.Lod == Campus.Common.QualityLevel.Higher, "原版画质默认参数和非连续枚举值");
            rig.Setup(null, bones); rig.Setup(null, bones);
            Check(bone.spring == 1 && bone.pendulum == .055f && bone.average == .2f && bone.rootWeight == .5f &&
                bone.limitInfo.axisX == new UnityEngine.Vector2Int(40, 5), "原版物理默认值及每次 Setup 的限位乘法");
            Check(Campus.Live.LiveUtility.AutoPhotoCountTypeValues().SequenceEqual(new[] { 10, 20, 30 }) &&
                Campus.Live.LiveUtility.GetAutoPhotoLimitCount() == 30, "自动拍照选项与上限");
            var canvas = new UnityEngine.CanvasRenderer(); var texture = new UnityEngine.Texture2D(4, 4) { name = "retry" };
            UnityEngine.ImageConversion.FailNextEncode = true; canvas.SetTexture(texture); canvas.SetTexture(texture);
            var output = Path.Combine(Catalog.Discover(packages, _ => { })["capture"].Directory, "output");
            Check(!Directory.Exists(Path.Combine(data, "dump-files")) && File.ReadAllBytes(Path.Combine(output, "texture2d", "others", "retry.png")).Take(4).SequenceEqual(new byte[] { 137, 80, 78, 71 }),
                "非泛型字节数组采集及临时读取失败后重试");
            keyed.SetValue("key", "original"); text.SetCharArray("word".ToCharArray(), 0, 4); card.Read();
            Check(keyed.Value == "旧界面" && text.Value == "旧文本" && card.Name == "旧表名", "三类文本入口生效");
            File.WriteAllText(Path.Combine(root, "localization.json"), """{"key":"新界面"}""");
            File.WriteAllText(Path.Combine(root, "generic.json"), """{"word":"新文本"}""");
            File.WriteAllText(table, """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"新表名"}]}""");
            try { host.ReloadResources("localization"); throw new Exception("文本重载应拒绝"); } catch (InvalidOperationException) { }
            Check(keyed.Value == "旧界面" && text.Value == "旧文本" && card.Name == "旧表名", "本次会话词典保持不变");
            var lyrics = new LivePresenter(); lyrics.SetLyricsActive(false); Check(lyrics.Visible, "首次显示歌词");
            lyrics.SetLyricsActive(false); Check(!lyrics.Visible, "后续允许用户隐藏歌词");
        }
        Check(game.Live == 0 && quality.TargetFrameRate == 45 && UnityEngine.Application.targetFrameRate == 45,
            "全部停用释放 Hook 并恢复游戏请求帧率");
        Check(quality.Settings == (1, 1000, 1f, 0) && quality.Reflection == Campus.Common.QualityLevel.Lowest &&
            quality.Lod == Campus.Common.QualityLevel.Lowest && bone.spring == 2 && bone.leftBreast.localScale == default &&
            bone.limitInfo.axisX == new UnityEngine.Vector2Int(10, 20) && bone.limitInfo.useLimit == 0,
            "停用恢复画质、物理数值、缩放和轴限位");
        Check(photoChoices.SequenceEqual(new[] { 1, 3, 5 }) && keyed.Value == "旧界面" && text.Value == "旧文本" && card.Name == "旧表名",
            "退出时恢复拍照数组，不回写翻译对象");
        Check(initializedBeforePlugin.GetValue("key") == "旧界面", "退出不恢复 I18n 词典");
        Check(existingText.text == "word" && existingText.font.sourceFontFile is { Destroyed: false } && existingText.font.Clears == 1, "停用恢复文本，内置字体保持原地替换状态");
        UnityEngine.Object.SceneObjects = Array.Empty<UnityEngine.Object>();
        File.WriteAllText(profile, """{"enabled":["localization"]}""");
        using (var degraded = new PluginHost(new TestGame { FailAfterFirstText = true }, log.Add))
        {
            degraded.Start(packages, profile, data);
            Check(degraded.Active.Contains("localization") && degraded.DescribeStatus().Contains("通用文本"),
                "部分失败保留可用功能并在管理器状态中报告组件名");
        }
        File.WriteAllText(profile, """{"enabled":["capture"],"settings":{"capture":{"text":true,"textures":false}}}""");
        using (var capture = new PluginHost(new TestGame { RejectSharedText = true }, log.Add))
        {
            capture.Start(packages, profile, data); new Tmp().text = "共享入口后仍采集";
            new UnityEngine.CanvasRenderer().SetTexture(new UnityEngine.Texture2D(4, 4) { name = "text-only" });
            Check(!File.Exists(Path.Combine(Catalog.Discover(packages, _ => { })["capture"].Directory, "output", "texture2d", "text-only.png")),
                "只启用文本采集时不进行 GPU 贴图导出");
            Check(!capture.DescribeStatus().Contains("加载失败") && log.Any(l => l.Contains("跳过不兼容采集入口")),
                "共享 SetText 拒绝不撤销其他采集入口");
        }
        File.WriteAllText(profile, """{"enabled":["graphics"]}""");
        using (var graphics = new PluginHost(new TestGame { RejectQualityTracking = true }, log.Add))
        {
            graphics.Start(packages, profile, data); quality.ApplySetting(1, 1000, 1, 0);
            Check(graphics.Active.Contains("graphics") && quality.Settings.Scale == .77f && graphics.DescribeStatus().Contains("原值跟踪入口不兼容"),
                "可选画质跟踪不兼容时保留画质设置并公开状态");
        }
        Console.WriteLine("Audit PASS: all 14 plugins together, defaults, native-shaped capture, hot reload, disable restoration, partial failure reporting.");
    }
}

namespace Campus.Live
{
    public static class LiveUtility
    {
        public static int[] Choices = { 1, 3, 5 };
        public static int Limit = 3;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int[] AutoPhotoCountTypeValues() => Choices;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int GetAutoPhotoLimitCount() => Limit;
    }
}
