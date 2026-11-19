using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gkms.Host;
using Gkms.Plugins.Localization;
using HarmonyLib;

var packages = args[0];
var work = Path.Combine(args[1], Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    Verify(work, packages);
    VerifyAssets(work, packages);
    VerifyVoice(work, packages);
    VerifyTextBundle(work, packages);
    VerifyPurchase(work, packages);
    VerifyCameraPhysics(work, packages);
    VerifyDisplayOptions(work, packages);
    VerifyFrameworkAndCapture(work, packages);
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
        Check(host.Active.Count == 0 && testGame.Live == 1, "空配置仍转发 Unity 日志");
        File.WriteAllText(profile, """{"enabled":["missing","i18n","frame-rate","text","story","text"],"settings":{"frame-rate":{"fps":-4}}}""");
        host.Start(packages, profile, work);
        Check(host.Active.SequenceEqual(new[]{"localization"}), string.Join(Environment.NewLine, log));
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
    i18n.SetValue("key", "original");
    Check(retry.Active.SequenceEqual(new[]{"localization"}) && tmp.Value == "花" && i18n.Value == "蒼", "通用文本启动失败后保留界面翻译");
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

static void VerifyVoice(string work, string packages)
{
    var file = VoiceSubtitles.ConfigFile(Path.Combine(work, "local-files"));
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, """{"fontSize":28,"minBubbleWidth":240,"bubbleWidth":400,"leftOffset":25,"textPadding":[18,14],"subtitles":[{"voiceAssetId":"sud_vo_system_a_home_1","text":"第一行\n第二行"}]}""");
    var profile = Path.Combine(work, "profile.json");
    File.WriteAllText(profile, """{"enabled":["localization"]}""");
    var log = new List<string>();
    using var host = new PluginHost(new TestGame(), log.Add);
    host.Start(packages, profile, work);
    var page = new Campus.OutGame.HomePageGroupPresenter();
    var actor = new Campus.OutGame.HomeTopActor();
    var voice = new Campus.Common.Sound.CampusSoundPlayer { Cue = "sud_vo_system_a_home_1" };
    var tick = new Campus.Common.CampusQualityManager();
    voice.Play(); tick.Update();
    Check(UnityEngine.Resources.LastClone == null, "离开主页时不创建气泡");
    page.Current._view.Initialize(); page.OnScreenVisitEnd(); actor.OnLoadEnd(); voice.PlayWith(actor.Actor, true); tick.Update();
    var clone = UnityEngine.Resources.LastClone;
    Check(clone != null && clone.activeSelf && clone.name == "HomeVoiceNativeDialogueBubble", string.Join(Environment.NewLine, log));
    var message = (Campus.ADV.MessageContainer)clone!.GetComponent(new Il2CppSystem.Type(typeof(Campus.ADV.MessageContainer)))!;
    Check(message.Text == "第一行\n第二行" && message._messageText.Complete && clone.transform.sizeDelta == new UnityEngine.Vector2(240,90)
        && clone.transform.anchoredPosition == new UnityEngine.Vector2(125, 20), "原生气泡及跨画布定位");
    var group = (UnityEngine.CanvasGroup)clone.GetComponent(new Il2CppSystem.Type(typeof(UnityEngine.CanvasGroup)))!;
    Check(group.alpha == .75f && !group.blocksRaycasts && !group.interactable, "气泡跟随页头淡出且允许点击下层");
    voice.Stop(); tick.Update(); Check(!clone.activeSelf, "语音结束后隐藏");
    voice.PlayWith(new object(), true); tick.Update(); Check(!clone.activeSelf, "其他角色的台词不显示");
    voice.Play(); tick.Update(); Check(clone.activeSelf, "复用气泡");
    voice.Cue = "sud_vo_new_home_missing"; voice.Play(); tick.Update(); Check(!clone.activeSelf, "未知语音清理旧文本");
    var screen = new Campus.OutGame.HomeTopScreenPresenter();
    screen.OnOpenOutInactive(); tick.Update(); Check(clone.Destroyed, "关闭主页时释放气泡");
    page.OnScreenVisitStart(); voice.Cue = "sud_vo_system_a_home_1"; voice.PlayWith(actor.Actor, true); tick.Update();
    clone = UnityEngine.Resources.LastClone!;
    Check(clone.activeSelf && !clone.Destroyed, "返回主页时重新绑定同一角色");
    screen.OnDestroy(); tick.Update();
    Check(clone.Destroyed && !UnityEngine.Resources.Template.Destroyed, "退出主页销毁克隆");
    File.WriteAllText(file, """{"subtitles":{"sud_vo_test_home_2":"兼容旧表"}}""");
    Check(SubtitleConfig.Load(file).Lookup("sud_vo_test_home_2") == "兼容旧表", "旧字幕格式");
}

static void VerifyTextBundle(string work, string packages)
{
    var local = Path.Combine(work, "local-files");
    var master = Directory.CreateDirectory(Path.Combine(local, "masterTrans")).FullName;
    File.Copy(Path.Combine(local, "Card.json"), Path.Combine(master, "Card.json"));
    var subtitleFile = VoiceSubtitles.ConfigFile(local);
    File.WriteAllText(subtitleFile, """{"enabled":false,"subtitles":{"sud_vo_bundle_home_one":"主页汉化"}}""");
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(subtitleFile)!, "srt_live_bundle.json"), """{"歌の一節":"歌词译文"}""");
    var profile = Path.Combine(work, "profile.json");
    File.WriteAllText(profile, """{"enabled":["master"],"settings":{"localization":{"interfaceText":false,"genericText":false,"story":false,"font":false,"subtitles":false}}}""");
    var game = new TestGame();
    var log = new List<string>();
    var live = new LivePresenter();
    var ui = new Ui();
    var tmp = new Tmp();
    var octo = new Octo();
    var card = new Card();
    var view = new Campus.OutGame.HomePageGroupPresenter();
    var voice = new Campus.Common.Sound.CampusSoundPlayer { Cue = "sud_vo_bundle_home_one" };
    using (var host = new PluginHost(game, log.Add))
    {
        host.Start(packages, profile, work);
        Check(host.Active.SequenceEqual(new[] { "localization" }), string.Join(Environment.NewLine, log));
        ui.SetValue("key", "original"); tmp.SetText("歌の一節");
        string? story = null;
        octo.LoadFromCacheOrDownload("scene.txt", (text, _) => story = text, null);
        card.InternalMergeFrom(); new TMPro.TextMeshProUGUI().Awake();
        view.OnScreenVisitEnd(); voice.Play(); new Campus.Common.CampusQualityManager().Update();
        live.SetLyricsActive(false);
        Check(ui.Value == "蒼" && tmp.Value == "歌词译文" && story == "剧情" && card.Name == "译名" &&
              TMPro.TMP_Settings.fallbackFontAssets.Count == 1 && live.Visible, "一个开关加载所有文本处理");
        var clone = UnityEngine.Resources.LastClone;
        var messageType = new Il2CppSystem.Type(typeof(Campus.ADV.MessageContainer));
        Check(clone is { activeSelf: true, Destroyed: false } && ((Campus.ADV.MessageContainer)clone.GetComponent(messageType)!).Text == "主页汉化",
              "旧资源的分项开关不能关闭气泡");
        live.SetLyricsActive(false); Check(!live.Visible, "保留游戏原生歌词操作");
    }
    File.WriteAllText(profile, """{"enabled":[]}""");
    using var disabled = new PluginHost(game, log.Add);
    disabled.Start(packages, profile, work);
    card = new Card(); card.InternalMergeFrom(); live = new LivePresenter(); live.SetLyricsActive(false);
    ui.SetValue("key", "original"); tmp.SetText("歌の一節"); new TMPro.TextMeshProUGUI().Awake();
    Check(disabled.Active.Count == 0 && game.Live == 1 && ui.Value == "original" && tmp.Value == "歌の一節" &&
          card.Name == "" && !live.Visible && TMPro.TMP_Settings.fallbackFontAssets.Count == 0 &&
          UnityEngine.Resources.LastClone!.Destroyed, "关闭一个插件后移除全部回调和资源");
}

static void VerifyCameraPhysics(string work, string packages)
{
    var profile = Path.Combine(work,"profile.json");
    File.WriteAllText(profile, """{"enabled":["camera","physics"],"settings":{"camera":{"mode":"first-person"},"physics":{"useScale":true,"scale":1.2,"axisX":[2,0.5]}}}""");
    var log = new List<string>();
    var face = new UnityEngine.GameObject(); var hair = new UnityEngine.GameObject();
    hair.SetActive(false);
    var actor = new Campus.Common.CampusActorController();
    actor._rootBody.parent!.Children["Root_Face/VLSkinningRenderer"] = face.transform;
    actor._rootBody.parent.Children["Root_Hair"] = hair.transform;
    var brain = new Cinemachine.CinemachineBrain();
    void Toggle() { UnityEngine.Input.F8 = true; UnityEngine.Time.frameCount++; brain.PushStateToUnityCamera(null); }
    using (var host = new PluginHost(new TestGame(), log.Add))
    {
        host.Start(packages,profile,work);
        Check(host.Active.Count == 2, string.Join(Environment.NewLine,log));
        UnityEngine.Input.Scroll = 1;
        Toggle(); actor.LateUpdate(); Check(!face.activeSelf && !hair.activeSelf && brain.OutputCamera.fieldOfView == 50,"第一人称隐藏脸和头发及调整视野");
        Toggle(); Check(face.activeSelf && !hair.activeSelf && brain.OutputCamera.fieldOfView == 60,"离开第一人称恢复视野和模型");
        Toggle(); actor.LateUpdate();
        var data = new Campus.Common.CampusActorAnimationInitializeData(); var rig = new Campus.Common.CampusActorAnimation();
        rig.Setup(null, data); rig.Setup(null,data);
        var bone = data.swingBreastBones[0];
        Check(bone.leftBreast.localScale.x == 1.2f && bone.limitInfo.axisX == new UnityEngine.Vector2Int(20,10) &&
              bone.limitInfo.useLimit == 1, "骨骼缩放和轴限位重复初始化");
    }
    Check(face.activeSelf && !hair.activeSelf,"卸载恢复第一人称模型");
}

static void VerifyDisplayOptions(string work, string packages)
{
    var profile = Path.Combine(work, "profile.json");
    File.WriteAllText(profile, """{"enabled":["localization","schedule-sp","graphics","card-cut-in"],"settings":{"localization":{"textTest":true},"graphics":{"renderScale":1.5}}}""");
    var log = new List<string>();
    using (var host = new PluginHost(new TestGame(), log.Add))
    {
        host.Start(packages, profile, work);
        Check(host.Active.Count == 4, string.Join(Environment.NewLine, log));
        var card = new Campus.InGame.Card.ExamCardData();
        var inventory = Campus.Common.User.UserDataManager.UserProduceCardSkinList;
        Check(card.ProduceCardSkinId == "", "未持有皮肤时保留原结果");
        inventory.Items.Add("small"); Check(card.ProduceCardSkinId == "small", "仅使用已持有的皮肤演出");
        inventory.Items.Add("grand"); Check(card.ProduceCardSkinId == "grand", "持有多款时取最高演出");
        card.Selection = "chosen"; Check(card.ProduceCardSkinId == "chosen", "保留手动选用的皮肤");
        inventory.Items.Clear();
        var keyed = new Ui(); keyed.SetValue("missing", "未译");
        var tmp = new Tmp(); tmp.SetText("未译");
        Check(keyed.Value == "[I18]未译" && tmp.Value == "[T1]未译", "翻译测试标记");
        tmp.SetText("花"); Check(tmp.Value == "花朵","已翻译文本保留原样");
        Check(Campus.InGame.ExamExtensions.ExchangeSpToNormal(Campus.Common.Proto.Client.Enums.ProduceStepType.Sp) ==
              Campus.Common.Proto.Client.Enums.ProduceStepType.Sp, "日程保留 SP 标记");
        var size = VL.VLUtility.GetLimitedResolution(800, 600, default, 3000, 1, true);
        Check(size.x == 1200 && size.y == 900, "超采样渲染尺寸");
    }
    Check(Campus.InGame.ExamExtensions.ExchangeSpToNormal(Campus.Common.Proto.Client.Enums.ProduceStepType.Sp) ==
          Campus.Common.Proto.Client.Enums.ProduceStepType.Normal, "关闭后恢复原始日程显示");
}

static void VerifyPurchase(string work, string packages)
{
    var manager = new Campus.Common.Purchase.InAppPurchaseManager();
    var profile = Path.Combine(work, "profile.json");
    File.WriteAllText(profile, """{"enabled":["purchase-guard"]}""");
    var logs = new List<string>();
    using (var host = new PluginHost(new TestGame(), logs.Add))
    {
        host.Start(packages, profile, work);
        Check(host.Active.SequenceEqual(new[] { "purchase-guard" }), string.Join(Environment.NewLine, logs));
        var first = manager.CheckPurchaseAsync(null, default).Result;
        var second = manager.CheckPurchaseAsync(null, default).Result;
        Check(!first.CanPurchase && !second.CanPurchase && !ReferenceEquals(first, second) &&
              manager.Calls == 0, "重复充值各自返回已完成的拒绝结果");
    }
    Check(manager.CheckPurchaseAsync(null, default).Result.CanPurchase && manager.Calls == 1, "移除充值保护后恢复原回调");
}

static void VerifyFrameworkAndCapture(string work, string packages)
{
    var data = Directory.CreateDirectory(Path.Combine(work, "capture-fixture")).FullName;
    var root = Directory.CreateDirectory(Path.Combine(data, "re")).FullName;
    // Only the U-key path is exercised. No GUI process is launched by this fixture.
    File.WriteAllBytes(Path.Combine(root, "Gkms.Manager.exe"), Array.Empty<byte>());
    var profile = Path.Combine(root, "profile.json");
    File.WriteAllText(profile, """{"enabled":["settings-panel","unity-log"]}""");
    var game = new TestGame();
    var log = new List<string>();
    var key = new Ui();
    var tmp = new Tmp();
    var canvas = new UnityEngine.CanvasRenderer();
    var quality = new Campus.Common.CampusQualityManager();
    var texture = new UnityEngine.Texture2D(8, 8) { name = "atlas" };
    var textFile = Path.Combine(data, "dump-files", "observed.jsonl");
    using (var host = new PluginHost(game, log.Add))
    {
        host.Start(packages, profile, data);
        host.Start(packages, profile, data);
        Check(game.Live == 2 && host.Active.Count == 0, "不启用插件也只注册一次两项框架服务");
        UnityEngine.DebugLogHandler.Internal_Log(0, 0, "offline", null);
        UnityEngine.Input.U = true;
        quality.Update();
        Check(log.Count(line => line == "framework: offline") == 1 && UnityEngine.Input.CtrlReads == 2, "框架日志和 Ctrl+U 输入回调");

        File.WriteAllText(profile, """{"enabled":["texture-capture","capture"]}""");
        host.Start(packages, profile, data);
        key.SetValue("menu", "原文"); key.SetValue("menu", "原文"); tmp.text = "台词";
        canvas.SetTexture(texture); canvas.SetTexture(texture);
        var png = File.ReadAllBytes(Path.Combine(data, "dump-files", "texture2d", "atlas.png"));
        Check(host.Active.SequenceEqual(new[]{ "capture" }) && png.SequenceEqual(new byte[] { 137, 80, 78, 71 }),
              "一个采集开关同时导出文本和贴图");
    }
    UnityEngine.Input.U = true; quality.Update();
    UnityEngine.DebugLogHandler.Internal_Log(0, 0, "after-dispose", null);
    tmp.text = "已卸载"; canvas.SetTexture(new UnityEngine.Texture2D(8, 8) { name = "after" });
    var lines = File.ReadAllLines(textFile);
    using var capture = JsonDocument.Parse("[" + string.Join(",", lines) + "]");
    var values = capture.RootElement.EnumerateArray().Select(row => row.GetProperty("value").GetString()).ToArray();
    Check(game.Live == 0 && !log.Any(line => line.Contains("after-dispose")) && values.SequenceEqual(new[] { "原文", "台词" }) &&
          !File.Exists(Path.Combine(data, "dump-files", "texture2d", "after.png")), "卸载时释放宿主和采集回调");
    UnityEngine.Input.U = false;
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
        if (package.Manifest.ResourceUpdater != null)
            File.Copy(Path.Combine(package.Directory, package.Manifest.Assembly), Path.Combine(folder, package.Manifest.Assembly));
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
    var pluginDir = Path.Combine(root, "plugins");
    var onlyOld = new Dictionary<string, Package>();
    foreach (var (id, assembly) in new[] { ("i18n", "I18n"), ("master", "Master"), ("lyrics", "Lyrics"),
        ("texture-capture", "TextureCapture"), ("settings-panel", "SettingsPanel"), ("unity-log", "UnityLog") })
    {
        var former = Directory.CreateDirectory(Path.Combine(pluginDir, "obsolete-" + id)).FullName;
        var manifest = new Manifest(id, id, 1, $"Gkms.{assembly}.dll", $"Gkms.Plugins.{assembly}.Plugin");
        File.WriteAllText(Path.Combine(former, "plugin.json"), JsonSerializer.Serialize(manifest, Catalog.Json));
        onlyOld.Add(id, new(former, manifest));
    }
    var current = Catalog.Discover(pluginDir, _ => {});
    Check(current.ContainsKey("localization") && !onlyOld.Keys.Any(current.ContainsKey), "覆盖安装时只显示一个文本汉化包");
    var fileName = Path.Combine(root, "profile.json");
    File.WriteAllText(fileName, """{"enabled":["texture-capture","settings-panel","capture","unity-log"]}""");
    Check(Catalog.ReadProfile(fileName, current).Enabled.SequenceEqual(new[] { "capture" }), "旧采集和内置功能迁移");
    foreach (var id in new[] { "localization", "master", "lyrics", "font", "story", "subtitles", "i18n", "text" })
    {
        File.WriteAllText(fileName, JsonSerializer.Serialize(new { enabled = new[] { id } }));
        Check(Catalog.ReadProfile(fileName, current).Enabled.SequenceEqual(new[] { "localization" }), $"从 {id} 迁移到总开关");
    }
    File.WriteAllText(fileName, """{"enabled":["i18n","story","localization","master","lyrics"],"settings":{"localization":{"story":false,"font":false,"textTest":true},"camera":{"speed":5}}}""");
    Check(Catalog.ReadProfile(fileName, onlyOld).Enabled.Contains("i18n"), "仅安装旧包时保留原选择");
    Check(Run("enable","purchase-guard","unknown") != 0 && Catalog.ReadProfile(fileName).Enabled.SequenceEqual(new[]{"localization"}), "批量选择应全部生效或保留原配置");
    Check(Run("enable","purchase-guard") == 0 && Run("disable","purchase-guard") == 0 && Run("list") == 0 &&
          Catalog.ReadProfile(fileName).Enabled.SequenceEqual(new[]{"localization"}) &&
          Catalog.ReadProfile(fileName, current).Settings!["localization"].GetProperty("textTest").GetBoolean(), "管理器保留诊断设置");
    using (var saved = JsonDocument.Parse(File.ReadAllText(fileName)))
        Check(saved.RootElement.GetProperty("enabled").GetArrayLength() == 1 &&
              saved.RootElement.GetProperty("settings").GetProperty("localization").EnumerateObject().Single().Name == "textTest",
              "保存配置时合并重复项并移除分项开关");
    Check(Run("disable", "localization") == 0 && Catalog.ReadProfile(fileName, current).Enabled.Length == 0, "总开关可一并关闭");
    File.WriteAllText(Path.Combine(data, "localizationConfig.json"),
        """{"enabled":true,"targetFrameRate":120,"replaceFont":false,"dumpRuntimeTexture":true,"enableFreeCamera":true,"loginAsIOS":true}""");
    Check(Run("import-legacy") == 0, "旧版选项导入");
    var imported = Catalog.ReadProfile(fileName);
    Check(imported.Enabled.Contains("camera") && imported.Enabled.Contains("frame-rate") && imported.Enabled.Contains("purchase-guard") &&
        imported.Enabled.Contains("localization") && imported.Enabled.Contains("capture") && !imported.Settings!["localization"].TryGetProperty("font", out _) &&
        imported.Settings!["frame-rate"].GetProperty("fps").GetInt32() == 120 &&
        imported.Settings["camera"].GetProperty("speed").GetInt32() == 5, "旧版参数迁移");

    static string MakeArchive(string root, string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(root, name + ".zip");
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
            writer.Write(entry.Content);
        }
        return path;
    }
    var textZip = MakeArchive(root, "text", ("local-files/localization.json", """{"ui":"中文"}"""));
    var texZip = MakeArchive(root, "textures", ("texture2d/texture_version.txt", "v1"), ("texture2d/title.png", "fixture"));
    File.WriteAllText(Path.Combine(data, "config.json"), JsonSerializer.Serialize(new { useAPIAssetsURL = textZip, useAPITextureAssetsURL = texZip }));
    Check(Run("disable", "localization", "textures") == 0 && Run("update", "localization") == 0 && Run("update", "textures", texZip) == 0,
          "插件未启用也可通过管理器调用各自 DLL 的更新入口");
    Check(File.ReadAllText(Path.Combine(data, "local-files", "localization.json")).Contains("中文") &&
          File.ReadAllText(Path.Combine(data, "texture2d", "title.png")) == "fixture", "两种资源进入各自目录");
    Check(Run("update", "textures", textZip) != 0 && Run("update", "capture", texZip) != 0 &&
          File.Exists(Path.Combine(data, "texture2d", "title.png")), "错误的包类型或不支持更新的插件不会改写资源");
}

static void VerifyUpdate(string work)
{
    using var release = JsonDocument.Parse("""{"assets":[{"name":"texture2d.zip","browser_download_url":"https://example.com/textures"},{"name":"GakumasTranslationData.zip","browser_download_url":"https://example.com/text"}]}""");
    Check(Gkms.Resources.PackInstaller.SelectReleaseZip(release.RootElement, false).AbsolutePath == "/text" &&
          Gkms.Resources.PackInstaller.SelectReleaseZip(release.RootElement, true).AbsolutePath == "/textures", "同一发布页的文本与贴图 ZIP 分别选择");
    using var data = new MemoryStream();
    using (var writer = new System.IO.Compression.ZipArchive(data, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        void Add(string path, string content)
        {
            using var entry = new StreamWriter(writer.CreateEntry("release/local-files/" + path).Open());
            entry.Write(content);
        }
        Add("localization.json", """{"key":"更新"}""");
        Add("resource/scene.txt","新版剧情");
        Add("gakumasassets","新版字体包");
        Add("genericTrans/genericTrans/lyrics/home_voice_subtitles.json","""{"subtitles":{"cue":"新版字幕"}}""");
        Add("genericTrans/new.json","""{"new":"新词条"}""");
        Add("genericTrans/genericTrans/lyrics/srt_live_bundle.json", """{"歌詞":"新歌词"}""");
        Add("masterTrans/Card.json", """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"新表名"}]}""");
    }
    data.Position = 0;
    using var zip = new System.IO.Compression.ZipArchive(data);
    var count = Gkms.Resources.PackInstaller.Install(work, false, zip, null);
    var installed = Path.Combine(work,"local-files");
    var card = new Card();
    new MasterTable(Path.Combine(installed, "masterTrans", "Card.json")).Apply(card);
    var dictionary = new TextDictionary(installed);
    Check(count == 7 && card.Name == "新表名" && dictionary.Translate("歌詞") == "新歌词" && File.ReadAllText(Path.Combine(installed,"resource","scene.txt")) == "新版剧情" &&
          File.ReadAllText(Path.Combine(installed,"gakumasassets")) == "新版字体包" &&
          SubtitleConfig.Load(VoiceSubtitles.ConfigFile(installed)).Lookup("cue") == "新版字幕" &&
          dictionary.Translate("new") == "新词条" &&
          File.ReadAllText(Path.Combine(work, "local-files", "localization.json")).Contains("更新") &&
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
    var voices = SubtitleConfig.Load(VoiceSubtitles.ConfigFile(local));
    Check(voices.Lines.Count > 0, "真实语音字幕为空");
    Console.WriteLine($"资源包通过，界面键 {i18n.Count}，通用词条 {dictionary.Count}，语音字幕 {voices.Lines.Count}，主数据表 {tables.Length}。");
}

public sealed class LivePresenter
{
    public bool Visible;
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetLyricsActive(bool visible) => Visible = visible;
}
public sealed class Card
{
    public string Id => "p";
    public int Rank => 1;
    public string Name { get; set; } = "";
    public List<Part> Parts { get; } = new(){new(PartKind.First), new(PartKind.Second)};
    [MethodImpl(MethodImplOptions.NoInlining)] public void InternalMergeFrom() { }
}
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
    public string text { get => Value; [MethodImpl(MethodImplOptions.NoInlining)] set => Value = value; }
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
          "Campus.Live.LiveScenePresenter" => typeof(LivePresenter), "Campus.Common.Proto.Client.Master.Card" => typeof(Card),
          _ => typeof(TestGame).Assembly.GetType(fullName) };
    public IDisposable Patch(string owner, MethodInfo method, MethodInfo? prefix, MethodInfo? postfix, int priority)
    {
        if (FailAfterFirstText && method.DeclaringType == typeof(Tmp) && ++textPatches == 2) throw new Exception("模拟游戏更新导致签名不兼容");
        var harmony = new Harmony("tests." + owner);
        harmony.Patch(method, prefix == null ? null : new HarmonyMethod(prefix) { priority = priority },
            postfix == null ? null : new HarmonyMethod(postfix) { priority = priority });
        Live++;
        return new Cleanup(() => { harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id); Live--; });
    }
    private sealed record Cleanup(Action Callback) : IDisposable { public void Dispose() => Callback(); }
}
