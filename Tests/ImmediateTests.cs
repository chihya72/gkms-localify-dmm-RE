using System.Text.Json;
using Gkms.Host;
using UnityEngine;

internal static class ImmediateTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string work, string sourcePackages)
    {
        var data = Directory.CreateDirectory(Path.Combine(work, "immediate")).FullName;
        var packages = TestPackages.Copy(sourcePackages, data);
        var resources = Directory.CreateDirectory(TestPackages.Resources(packages, "localization")).FullName;
        var master = Directory.CreateDirectory(Path.Combine(resources, "masterTrans")).FullName;
        var stories = Directory.CreateDirectory(Path.Combine(resources, "resource")).FullName;
        var textures = Directory.CreateDirectory(Path.Combine(TestPackages.Resources(packages, "textures"), "others")).FullName;
        File.WriteAllBytes(Path.Combine(resources, "gakumasassets"), new byte[] { 1 });
        var subtitleFile = Gkms.Plugins.Localization.VoiceSubtitles.ConfigFile(resources);
        Directory.CreateDirectory(Path.GetDirectoryName(subtitleFile)!);
        File.WriteAllText(subtitleFile, """{"subtitles":{"sud_vo_system_a_home_1":"正在播放的字幕"}}""");
        File.WriteAllBytes(Path.Combine(textures, "shared.png"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(textures, "gpu.png"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(textures, "crunched.png"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(textures, "rgba.png"), new byte[] { 1, 2, 3 });
        void Texts(string revision)
        {
            File.WriteAllText(Path.Combine(resources, "localization.json"), JsonSerializer.Serialize(new Dictionary<string, string> {
                ["key"] = "界面" + revision, ["home.mission_pass"] = "通票" + revision, ["other.pass"] = "另一通票" + revision }));
            File.WriteAllText(Path.Combine(resources, "generic.json"), JsonSerializer.Serialize(new Dictionary<string, string> {
                ["日本語"] = "文本" + revision, ["パス"] = "通行证", ["別の一般語"] = "一般文本" }));
            File.WriteAllText(Path.Combine(master, "Card.json"), """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"表名REV"}]}""".Replace("REV", revision));
            File.WriteAllText(Path.Combine(stories, "story.txt"), "[message text=<r\\=物語>剧情" + revision + "</r> name=Name]");
        }
        Texts("一");
        var profile = Path.Combine(data, "re", "profile.json");
        void Profile(bool on, float spring = 1, int quality = 3)
        {
            File.WriteAllText(profile, JsonSerializer.Serialize(new { enabled = on ? new[] { "localization", "graphics", "physics", "textures" } : new[] { "localization" },
                settings = new { physics = new { spring }, graphics = new { quality } } }));
        }
        Profile(false);
        var game = new TestGame { StripReadbackMetadata = true }; var log = new List<string>();
        var shared = new Texture2D(8, 16) { name = "shared", filterMode = 2 };
        var gpu = new Texture2D(16, 8, TextureFormat.DXT5, true, false) { name = "gpu", isReadable = false };
        var crunched = new Texture2D(32, 16, TextureFormat.DXT5Crunched, true, false) { name = "crunched" };
        var rgba = new Texture2D(16, 32, TextureFormat.RGBA32, false, true) { name = "rgba", isReadable = false };
        var late = new Texture2D(32, 64) { name = "late", isReadable = false };
        var originalPixels = shared.Pixels.ToArray(); var originalGpu = gpu.Pixels.ToArray();
        var previousTarget = new RenderTexture(); RenderTexture.active = previousTarget; GL.sRGBWrite = false;
        var first = new CanvasRenderer(); var second = new CanvasRenderer();
        // The material consumer deliberately never goes through CanvasRenderer.SetTexture.
        Texture materialTexture = shared;
        var font = new TMPro.TMP_FontAsset { name = "campusnormal" };
        var originalFace = font.faceInfo;
        var originalWeights = font.fontWeightTable; var originalFallbacks = font.fallbackFontAssetTable;
        var raw = new Tmp { font = font }; var label = new Tmp { font = font }; var story = new Tmp { font = font };
        var pass = new Tmp { font = font }; var otherPass = new Tmp { font = font };
        var slice = new Tmp { font = font }; var chars = new Tmp { font = font };
        var originalMaterial = new Material(font.material) { Outline = .7f }; raw.fontSharedMaterial = originalMaterial;
        originalMaterial.SetFloat("_GradientScale", 4);
        UnityEngine.Object.SceneObjects = new UnityEngine.Object[] { raw, label, story, pass, shared, gpu, crunched, rgba };
        var quality = new Campus.Common.CampusQualityManager();
        var actor = new Campus.Common.CampusActorAnimation(); var dataBones = new Campus.Common.CampusActorAnimationInitializeData();
        var bone = dataBones.swingBreastBones[0]; bone.spring = 7;
        var keyed = new Ui(); var card = new Card { Name = "元の名前" };
        var home = new Campus.OutGame.HomePageGroupPresenter(); var homeActor = new Campus.OutGame.HomeTopActor();
        var voice = new Campus.Common.Sound.CampusSoundPlayer { Cue = "sud_vo_system_a_home_1" };
        var lyrics = new LivePresenter();
        using (var host = new PluginHost(game, log.Add))
        {
            host.Start(packages, profile, data);
            quality.ApplySetting(1, 1234, .8f, 1); quality.SetReflectionQuality(Campus.Common.QualityLevel.Low);
            actor.Setup(null, dataBones); card.Read(); keyed.SetValue("key", "元のラベル");
            new Octo().LoadFromCacheOrDownload("story.txt", (_, _) => { }, null);
            raw.text = "日本語"; label.text = keyed.GetValue("key"); story.text = "物語";
            keyed.SetValue("home.mission_pass", "パス"); pass.text = keyed.GetValue("home.mission_pass");
            first.SetTexture(shared); second.SetTexture(shared);
            home.Current._view.Initialize(); home.OnScreenVisitEnd(); homeActor.OnLoadEnd(); voice.PlayWith(homeActor.Actor, true);
            lyrics.SetLyricsActive(false);
            StrippedGL.Available = false;
            File.WriteAllText(profile, """{"enabled":["textures"]}"""); host.ReloadProfile();
            Check(host.GetWarnings().ContainsKey("textures") && shared.Pixels.SequenceEqual(originalPixels) && gpu.Pixels.SequenceEqual(originalGpu),
                "读回入口不可用时明确报告替换失败，并保留尚未替换的原图");
            StrippedGL.Available = true; host.ReloadResources("textures");
            Check(!host.GetWarnings().ContainsKey("textures") && shared.Pixels.SequenceEqual(new byte[] { 1, 2, 3 }),
                "托管 GL 接口被裁剪时使用原生入口，重载成功清除失败状态");
            Profile(false); host.ReloadProfile();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Profile(true); host.ReloadProfile();
                quality.Update(); quality.Update();
                Check(host.Active.Count == 4 && !log.Any(l => l.Contains("加载失败")), string.Join("\n", log));
                var loadedCount = log.Count(l => l.EndsWith(": 已加载。", StringComparison.Ordinal));
                File.WriteAllText(profile, """{ "enabled": ["localization", "graphics", "physics", "textures"], "settings": { "graphics": { "quality": 3 }, "physics": { "spring": 1.0 } } }""");
                host.ReloadProfile();
                Check(log.Count(l => l.EndsWith(": 已加载。", StringComparison.Ordinal)) == loadedCount,
                    "设置的空白、顺序和等值数值变化不应重启任何插件");
                Check(Resources.LastClone is { activeSelf: true, Destroyed: false } bubble &&
                    ((Campus.ADV.MessageContainer)bubble.GetComponent(typeof(Campus.ADV.MessageContainer))!).Text == "正在播放的字幕",
                    "启动时文本插件正常处理主页与语音事件");
                Check(!lyrics.Visible, "只启用汉化不强制开启 Live 解锁的歌词开关");
                Check(quality.Settings == (3, 3384, .77f, 3) && quality.Reflection == Campus.Common.QualityLevel.Higher, "已有画质管理器启用立即刷新");
                Check(bone.spring == 1 && bone.isDirty, "已有角色物理无需再次 Setup");

                new Octo().LoadFromCacheOrDownload("story.txt", (_, _) => story.text = "剧情一", null);
                Check(raw.text == "文本一" && label.text == "界面一" && card.Name == "表名一" && story.text == "剧情一", "明确来源的界面、主表和剧情在启停时更新");
                Check(pass.text == "通票一", "有实际 I18n 输出或对象键时使用通票译文");
                pass.SetText(keyed.GetValue("home.mission_pass"));
                var passText = keyed.GetValue("home.mission_pass");
                slice.PopulateTextBackingArray("[" + passText + "]", 1, passText.Length);
                chars.SetCharArray(passText.ToCharArray(), 0, passText.Length);
                Check(pass.text == "通票一" && slice.text == "通票一" && chars.text == "通票一", "嵌套 SetText、切片和字符数组不二次翻译界面键译文");
                if (cycle == 2)
                {
                    keyed.SetValue("other.pass", "パス"); otherPass.text = keyed.GetValue("other.pass");
                    pass.text = keyed.GetValue("home.mission_pass");
                    Check(otherPass.text == "另一通票一" && pass.text == "通票一", "同一日文的不同界面键不能串词");
                }
                Check(raw.font == font && font.sourceFontFile is { Destroyed: false } && font.Clears == 1, "内置字体原地更新，汉化反复启停不重新清理图集");
                Check(font.faceInfo.Equals(originalFace) && font.fontWeightTable == originalWeights && font.fallbackFontAssetTable == originalFallbacks,
                    "原地替换保留排版参数和引用表；非真实渲染断言");
                Check(raw.fontSharedMaterial == originalMaterial && raw.fontSharedMaterial?.Outline == .7f && raw.fontSharedMaterial?.mainTexture == font.atlasTexture && raw.fontSharedMaterial.GetFloat("_GradientScale") == 4,
                    "不克隆或改写游戏材质及描边参数");
                Check(first.Current == shared && second.Current == shared && materialTexture == shared && shared.Pixels.SequenceEqual(new byte[] { 1, 2, 3 }) && gpu.Pixels.SequenceEqual(shared.Pixels), "共享引用与非 Canvas 材质原地更新，含 GPU 非可读贴图");
                Check(!gpu.isReadable && RenderTexture.active == previousTarget && !GL.sRGBWrite, "不可读纹理加载 PNG 后仍不可读，读回不污染渲染目标和颜色状态");
                var retired = new Tmp { font = font }; retired.text = "日本語";
                UnityEngine.Object.Destroy(retired);
                Texts("二");
                try { host.ReloadResources("localization"); throw new Exception("文本重载应拒绝"); } catch (InvalidOperationException) { }
                Check(retired.AccessAfterDestroy == 0 && raw.text == "文本一" && card.Name == "表名一", "拒绝文本重载，不访问旧对象或替换启动词典");
                Profile(true, 4, 5); host.ReloadProfile();
                Check(bone.spring == 4 && quality.Settings.Item1 == 5, "配置重载立即应用当前角色与画质");
                File.WriteAllBytes(Path.Combine(textures, "shared.png"), new byte[] { 9 }); host.ReloadResources("textures");
                Check(shared.Pixels.SequenceEqual(new byte[] { 9 }) && first.Current == materialTexture, "共享纹理即时资源重载");
                if (cycle == 0)
                {
                    File.WriteAllBytes(Path.Combine(textures, "late.png"), new byte[] { 1, 2, 3 });
                    UnityEngine.Object.SceneObjects = UnityEngine.Object.SceneObjects.Append(late).ToArray();
                    ImageConversion.FailLoadAfterMutationOnce = ImageConversion.FailRestoreOnce = late;
                    try { host.ReloadResources("textures"); throw new Exception("部分写入与回滚失败不应报告成功"); }
                    catch (AggregateException) { }
                    Check(shared.Pixels.SequenceEqual(new byte[] { 9 }) && log.Any(l => l.Contains("贴图回滚未完成")),
                        "个别回滚失败后继续恢复其它贴图，保留失败对象的原图");
                }
                Profile(false);
                if (cycle == 1) Texture2D.FailRawRestoreOnce = shared;
                if (cycle == 0)
                {
                    ImageConversion.FailRestoreOnce = gpu;
                    try { host.ReloadProfile(); throw new Exception("恢复失败不应报告停用成功"); }
                    catch (InvalidOperationException) { }
                    Check(host.DescribeStatus().Contains("恢复尚未完成") && !host.Active.Contains("textures"), "恢复失败保留备份并向管理器报告");
                }
                host.ReloadProfile();
                Check(!Resources.LastClone!.Destroyed && !lyrics.Visible, "文本插件保持启动状态，不移除字幕气泡");
                Check(retired.AccessAfterDestroy == 0, "停用不访问已销毁的文字组件");
                Check(raw.text == "文本一" && label.text == "界面一" && story.text == "剧情一" && card.Name == "表名一", "其他插件停用不恢复文本插件的译文");
                Check(pass.text == "通票一" && slice.text == "通票一" && chars.text == "通票一" && (cycle != 2 || otherPass.text == "另一通票一"), "文本开关不影响本次运行译文");
                Check(raw.font == font && label.font == font && font.sourceFontFile is { Destroyed: false } && font.Clears == 1, "反复启停汉化不改变框架字体或重建图集");
                Check(raw.fontSharedMaterial == originalMaterial && !originalMaterial.Destroyed && !font.atlasTexture.Destroyed, "恢复原材质且不销毁原图集");
                Check(bone.spring == 7 && quality.Settings == (1, 1234, .8f, 1) && quality.Reflection == Campus.Common.QualityLevel.Low, "停用恢复游戏原参数");
                Check(shared.Pixels.SequenceEqual(originalPixels) && shared.width == 8 && shared.height == 16 && shared.filterMode == 2 &&
                    gpu.Pixels.SequenceEqual(originalGpu) && gpu.width == 16 && gpu.height == 8 && !gpu.isReadable && Texture2D.NonReadableReinitializations == 0,
                    "原对象恢复像素和尺寸，不对不可读贴图调用 Reinitialize；失败后重试仍可恢复");
                Check(crunched.width == 32 && crunched.height == 16 && crunched.Pixels.SequenceEqual(originalGpu) && crunched.isReadable &&
                    rgba.width == 16 && rgba.height == 32 && rgba.Pixels.SequenceEqual(originalGpu) && !rgba.isReadable,
                    "Crunched 与不可读 RGBA 原图通过 PNG 恢复尺寸与像素；原始数据恢复失败自动使用 PNG 备份");
                Check(late.width == 32 && late.height == 64 && late.Pixels.SequenceEqual(originalGpu) && !late.isReadable,
                    "重载部分写入且回滚失败后，停用仍能从保留的备份恢复");
                story.text = "剧情二"; Check(story.text == "剧情二", "停用后无来源的后续剧情文字保持原样");
                Texts("一"); File.WriteAllBytes(Path.Combine(textures, "shared.png"), new byte[] { 1, 2, 3 });
            }
            UnityEngine.Object.SceneObjects = UnityEngine.Object.SceneObjects.Append(otherPass).Distinct().ToArray();
            Profile(true); host.ReloadProfile();
            Check(pass.text == "通票一" && otherPass.text == "另一通票一",
                "存在同源异键后再次热启用，使用对象原键，不退回全局パス通用译文");
            Profile(false); host.ReloadProfile();
        }
        UnityEngine.Object.SceneObjects = Array.Empty<UnityEngine.Object>();
        RenderTexture.active = null;
        Check(game.Live == 0, "关闭宿主释放状态观察与功能 Hook");
        Console.WriteLine("即时启停离线专项通过：文本启动状态固定、内置字体持续有效、画质/角色/贴图热切换与恢复，连续三轮；不代表实机通过。");
    }
}
