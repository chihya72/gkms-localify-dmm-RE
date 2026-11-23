using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gkms.Host;
using Gkms.Plugins.Localization;
using HarmonyLib;

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length == 2 && args[0] == "--manager-ui")
{
    ManagerUiRegressionTests.Run(args[1]);
    return 0;
}
if (args.Length == 3 && args[0] == "--repositories")
{
    PluginRepositoryTests.Run(args[2], args[1]);
    return 0;
}
if (args.Length >= 3 && args[0] == "--window")
{
    WindowMessageTests.Run(args[1], args[2]);
    return 0;
}

if (args[0] == "--native")
{
    NativeBridgeTests.Run(args[1]);
    return 0;
}
if (args[0] == "--overlay")
{
    Directory.CreateDirectory(args[2]);
    OverlayTests.Run(args[2], args[1]);
    return 0;
}

if (args[0] == "--plugin-api")
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    PluginApiTests.Run(args[2], args[1]);
    return 0;
}
if (args[0] == "--audit")
{
    AuditTests.Run(args[2], args[1]);
    return 0;
}
if (args[0] == "--immediate")
{
    ImmediateTests.Run(args[2], args[1]);
    return 0;
}
if (args[0] == "--migration")
{
    MigrationTests.Run(args[2], args[1]);
    return 0;
}
if (args[0] == "--dmm-parity")
{
    DmmParityTests.Run(args[2], args[1]);
    return 0;
}
if (args[0] == "--text-startup")
{
    TextStartupTests.Run(args[2], args[1]); return 0;
}
if (args[0] == "--ios-login")
{
    IosLoginTests.Run(args[2], args[1]);
    return 0;
}

if (args[0] != "--suite")
{
    var suiteWork = Directory.CreateDirectory(Path.Combine(args[1], Guid.NewGuid().ToString("N"))).FullName;
    try
    {
        // Harmony/native generic callbacks may retain test assemblies until process exit on Windows.
        // Own the scratch directory in a parent that never loads those assemblies.
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(TestPackages).Assembly.Location);
        foreach(var value in new[]{"--suite",args[0],suiteWork}.Concat(args.Skip(2))) start.ArgumentList.Add(value);
        using var process=System.Diagnostics.Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync(); var errors=process.StandardError.ReadToEndAsync();
        process.WaitForExit(); Console.Write(output.GetAwaiter().GetResult()); Console.Error.Write(errors.GetAwaiter().GetResult()); return process.ExitCode;
    }
    finally {Directory.Delete(suiteWork,recursive:true);}
}
var distributionPackages = args[1];
var work = args[2];
var packages = TestPackages.Copy(distributionPackages, work);
Verify(work, packages);
VerifyAssets(work, packages);
VerifyVoice(work, packages);
VerifyTextBundle(work, packages);
VerifyPurchase(work, packages);
IosLoginTests.Run(work, packages);
VerifyCameraPhysics(work, packages);
VerifyDisplayOptions(work, packages);
VerifyFrameworkAndCapture(work, packages);
PluginApiTests.RunIsolated(work, packages, "--dmm-parity");
PluginApiTests.RunIsolated(work, packages, "--text-startup");
PluginApiTests.RunIsolated(work, packages, "--migration");
// Harmony's CLR fixture detours retain generic code across collectible test packages.
// Use a fresh fixture process; native bridge lifecycle tests run separately without Harmony.
PluginApiTests.RunIsolated(work, packages, "--audit");
PluginApiTests.RunIsolated(work, packages, "--immediate");
PluginApiTests.RunIsolated(work, packages);
VerifyUpdate(work, packages);
VerifyPanelServices(work, Path.GetDirectoryName(Path.GetFullPath(distributionPackages))!);
PluginInstallationTests.Run(work, distributionPackages);
PluginRepositoryTests.Run(work, distributionPackages);
if (args.Length > 3) VerifyPack(args[3]);
Console.WriteLine("PASS");
return 0;

static void Check(bool condition, string detail)
{
    if (!condition) throw new Exception(detail);
}

static void Verify(string work, string packages)
{
    var local = Directory.CreateDirectory(TestPackages.Resources(packages, "localization")).FullName;
    // Conflicting legacy files must never supply the new package's translations or version.
    var legacy = Directory.CreateDirectory(Path.Combine(work, "local-files")).FullName;
    File.WriteAllText(Path.Combine(legacy, "localization.json"), """{"key":"错误旧译"}""");
    File.WriteAllText(Path.Combine(legacy, "version.txt"), "wrong-legacy-version");
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
    // MasterLocal.cpp BuildUniqueKeyValue uses [index], not the sub-primary kind value.
    Check(card.Name == "译名" && card.Parts[0].Text == "子项" && card.Parts[1].Text == "", "原版主数据复合主键与按位置子表");

    var log = new List<string>();
    var profile = Path.Combine(work, "profile.json");
    var testGame = new TestGame();
    var i18n = new Ui();
    var tmp = new Tmp();
    var octo = new Octo();
    using (var host = new PluginHost(testGame, log.Add))
    {
        host.Start(packages, profile, work);
        Check(host.Active.Count == 0 && testGame.Live > 0, "空配置保留日志和原始场景状态观察");
        host.Dispose(); // A new session is required before changing startup-only plugins.
        File.WriteAllText(profile, """{"enabled":["missing","i18n","frame-rate","text","story","text"],"settings":{"frame-rate":{"fps":"invalid"}}}""");
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
    i18n.SetValue("key", "original"); tmp.SetText("花"); Check(testGame.Live == 0 && i18n.Value == "original" && tmp.Value == "花", $"停用后恢复原方法: hooks={testGame.Live}, I18n={i18n.Value}, TMP={tmp.Value}\n{string.Join("\n", log)}");

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
    File.WriteAllBytes(Path.Combine(TestPackages.Resources(packages, "localization"), "gakumasassets"), new byte[] { 1 });
    var png = Directory.CreateDirectory(Path.Combine(TestPackages.Resources(packages, "textures"), "others")).FullName;
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
        Check(host.Active.SequenceEqual(new[] { "textures" }), string.Join(Environment.NewLine, log));
        text.Awake(); text.Awake();
        canvas.SetTexture(original);
        Check(text.font.sourceFontFile is { Destroyed: false } && text.font.Clears == 1 && TMPro.TMP_Settings.fallbackFontAssets.Count == 0 && canvas.Current == original && original.Pixels.SequenceEqual(new byte[] { 1 }) && canvas.Current?.filterMode == 2,
            string.Join(Environment.NewLine, log));
        var previous = canvas.Current!;
        File.WriteAllBytes(Path.Combine(png, "title.png"), Array.Empty<byte>());
        try { host.ReloadResources("textures"); throw new Exception("accepted invalid texture"); }
        catch (InvalidDataException) { }
        Check(canvas.Current == previous && !previous.Destroyed, "损坏贴图包保留当前有效纹理");
        File.WriteAllBytes(Path.Combine(png, "title.png"), new byte[] { 2 });
        host.ReloadResources("textures");
        Check(!previous.Destroyed && canvas.Current == original && original.Pixels.SequenceEqual(new byte[] { 2 }), "原地热重载，不更换共享纹理引用");
    }
    Check(text.font.sourceFontFile is { Destroyed: false } && TMPro.TMP_Settings.fallbackFontAssets.Count == 0 && canvas.Current == original && original.width == 8 && original.Pixels.SequenceEqual(new byte[] { 137, 80, 78, 71 }), "宿主释放不销毁仍被字体引用的源Font，贴图停用恢复原图");
}

static void VerifyVoice(string work, string packages)
{
    var file = VoiceSubtitles.ConfigFile(TestPackages.Resources(packages, "localization"));
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
    page.Current._view.Initialize(); page.OnScreenVisitEnd(); actor.OnLoadEnd(); voice.PlayWith(actor.Actor, true); tick.Update(); tick.Update();
    var clone = UnityEngine.Resources.LastClone;
    Check(clone != null && clone.activeSelf && clone.name == "HomeVoiceNativeDialogueBubble", string.Join(Environment.NewLine, log));
    var message = (Campus.ADV.MessageContainer)clone!.GetComponent(typeof(Campus.ADV.MessageContainer))!;
    Check(message.Text == "第一行\n第二行" && message._messageText.Complete && clone.transform.sizeDelta == new UnityEngine.Vector2(240,90)
        && clone.transform.anchoredPosition == new UnityEngine.Vector2(125, 20), "原生气泡及跨画布定位");
    var group = (UnityEngine.CanvasGroup)clone.GetComponent(typeof(UnityEngine.CanvasGroup))!;
    Check(group.alpha == .75f && !group.blocksRaycasts && !group.interactable, "气泡跟随页头淡出且允许点击下层");
    File.WriteAllText(file, """{"subtitles":{"sud_vo_system_a_home_1":"运行中更新字幕"}}""");
    try { host.ReloadResources("localization"); throw new Exception("文本重载应拒绝"); } catch (InvalidOperationException) { }
    tick.Update(); tick.Update();
    Check(!clone.Destroyed && message.Text == "第一行\\n第二行".Replace("\\n", "\n"), "运行中更新字幕文件不重建气泡或改变本次字幕");
    voice.Stop(); tick.Update(); Check(!clone.activeSelf, "语音结束后隐藏");
    voice.PlayWith(new object(), true); tick.Update(); Check(!clone.activeSelf, "其他角色的台词不显示");
    voice.Play(); tick.Update(); Check(clone.activeSelf, "复用气泡");
    voice.Cue = "sud_vo_new_home_missing"; voice.Play(); tick.Update(); Check(!clone.activeSelf, "未知语音清理旧文本");
    var screen = new Campus.OutGame.HomeTopScreenPresenter();
    screen.OnOpenOutInactive(); tick.Update(); Check(!clone.activeSelf && !clone.Destroyed, "原版暂离主页隐藏并保留气泡，销毁场景才释放");
    page.OnScreenVisitStart(); voice.Cue = "sud_vo_system_a_home_1"; voice.PlayWith(actor.Actor, true); tick.Update(); tick.Update();
    clone = UnityEngine.Resources.LastClone!;
    Check(clone.activeSelf && !clone.Destroyed, "返回主页时重新绑定同一角色");
    screen.OnDestroy(); tick.Update();
    Check(clone.Destroyed && !UnityEngine.Resources.Template.Destroyed, "退出主页销毁克隆");
    File.WriteAllText(file, """{"subtitles":{"sud_vo_test_home_2":"兼容旧表"}}""");
    Check(SubtitleConfig.Load(file).Lookup("sud_vo_test_home_2") == "兼容旧表", "旧字幕格式");
}

static void VerifyTextBundle(string work, string packages)
{
    var local = TestPackages.Resources(packages, "localization");
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
        card.Read(); new TMPro.TextMeshProUGUI().Awake();
        view.OnScreenVisitEnd(); voice.Play(); new Campus.Common.CampusQualityManager().Update();
        live.SetLyricsActive(false);
        Check(ui.Value == "蒼" && tmp.Value == "歌词译文" && story == "剧情" && card.Name == "译名" &&
              tmp.font.sourceFontFile is { Destroyed: false } && TMPro.TMP_Settings.fallbackFontAssets.Count == 0 && !live.Visible, "文本处理启用，不擅自启用 Live 解锁的歌词显示");
        var clone = UnityEngine.Resources.LastClone;
        var messageType = typeof(Campus.ADV.MessageContainer);
        Check(clone == null || clone.Destroyed || !clone.activeSelf, "尊重原版字幕资源 enabled:false");
        live.SetLyricsActive(false); Check(!live.Visible, "保留游戏原生歌词操作");
    }
    File.WriteAllText(profile, """{"enabled":[]}""");
    using var disabled = new PluginHost(game, log.Add);
    disabled.Start(packages, profile, work);
    card = new Card(); card.Read(); live = new LivePresenter(); live.SetLyricsActive(false);
    ui.SetValue("key", "original"); tmp.SetText("歌の一節"); new TMPro.TextMeshProUGUI().Awake();
    Check(disabled.Active.Count == 0 && game.Live > 0 && ui.Value == "original" && tmp.Value == "歌の一節" &&
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
    void Tick(string? press = null) { if (press != null) UnityEngine.Input.Pressed.Add(press); UnityEngine.Time.frameCount++; brain.PushStateToUnityCamera(default); }
    using (var host = new PluginHost(new TestGame(), log.Add))
    {
        host.Start(packages,profile,work);
        Check(host.Active.Count == 2, string.Join(Environment.NewLine,log));
        actor.LateUpdate(); UnityEngine.Input.Held.Add("e"); Tick(); UnityEngine.Input.Held.Clear();
        Check(!face.activeSelf && !hair.activeSelf && brain.OutputCamera.fieldOfView < 60,"启用第一人称立即隐藏头部，原版 E 键减少视野");
        Tick("f"); actor.LateUpdate(); Check(face.activeSelf && !hair.activeSelf,"切回自由镜头恢复原有头部状态");
        Tick("f"); actor.LateUpdate();
        var data = new Campus.Common.CampusActorAnimationInitializeData(); var rig = new Campus.Common.CampusActorAnimation();
        rig.Setup(null, data); rig.Setup(null,data);
        var bone = data.swingBreastBones[0];
        Check(bone.leftBreast.localScale.x == 1.2f && bone.limitInfo.axisX == new UnityEngine.Vector2Int(40,5) &&
              bone.limitInfo.useLimit == 1, "原版每次实际 Setup 都乘以轴限位系数");
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
        Check(card.ProduceCardSkinId == "grand", "与原版一致：未持有皮肤时仍选择最高演出");
        inventory.Items.Add("small"); Check(card.ProduceCardSkinId == "grand", "低等级持有皮肤不限制主表演出选择");
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
        Check(host.Active.SequenceEqual(new[] { "ios-login" }), string.Join(Environment.NewLine, logs));
        var api = Qua.Network.ApiBase.Instance;
        Check(!Firebase.Platform.PlatformInformation.IsIOS && api.GetPlatformString() == "DMM", "DMM 认证前保持原平台");
        new Campus.Common.Proto.Client.Api.AuthLoginResponse { GameAuthToken = "offline-test" }.Read();
        Check(Firebase.Platform.PlatformInformation.IsIOS && !Firebase.Platform.PlatformInformation.IsAndroid &&
            api.GetPlatformString() == "iOS" && api._platform == "iOS", "认证后请求与平台识别同步切换为 iOS");
        _ = new Campus.Common.Proto.Client.Api.AuthLoginRequest();
        Check(!Firebase.Platform.PlatformInformation.IsIOS && api.GetPlatformString() == "DMM", "重新登录先恢复 DMM");
        new Campus.Common.Proto.Client.Api.AuthLoginResponse().Read();
        Check(!Firebase.Platform.PlatformInformation.IsIOS, "空令牌不视为登录成功");
        new Campus.Common.Proto.Client.Api.AuthLoginResponse { GameAuthToken = "offline-test" }.Read();
        var first = manager.CheckPurchaseAsync(null, default).Result;
        var second = manager.CheckPurchaseAsync(null, default).Result;
        Check(!first.CanPurchase && !second.CanPurchase && !ReferenceEquals(first, second) &&
              manager.Calls == 0, "重复充值各自返回已完成的拒绝结果");
    }
    Check(manager.CheckPurchaseAsync(null, default).Result.CanPurchase && manager.Calls == 1, "移除充值保护后恢复原回调");
    Check(!Firebase.Platform.PlatformInformation.IsIOS && Qua.Network.ApiBase.Instance._platform == "DMM", "停用合并插件时恢复平台");
}

static void VerifyFrameworkAndCapture(string work, string packages)
{
    var data = Directory.CreateDirectory(Path.Combine(work, "capture-fixture")).FullName;
    var root = Directory.CreateDirectory(Path.Combine(data, "re")).FullName;
    var profile = Path.Combine(root, "profile.json");
    File.WriteAllText(profile, """{"enabled":["settings-panel","unity-log"]}""");
    var game = new TestGame();
    var log = new List<string>();
    var key = new Ui();
    var tmp = new Tmp();
    var canvas = new UnityEngine.CanvasRenderer();
    var quality = new Campus.Common.CampusQualityManager();
    var texture = new UnityEngine.Texture2D(8, 8) { name = "atlas" };
    var output = Path.Combine(Catalog.Discover(packages, _ => { })["capture"].Directory, "output");
    var textFile = Path.Combine(output, "observed.jsonl");
    using (var host = new PluginHost(game, log.Add))
    {
        host.Start(packages, profile, data);
        host.Start(packages, profile, data);
        Check(game.Live > 0 && host.Active.Count == 0, "空配置保留日志与原始状态观察；快捷键由原生覆盖层处理");
        UnityEngine.DebugLogHandler.Internal_Log(0, 0, "offline", null);
        UnityEngine.Input.U = true;
        var ctrlReads = UnityEngine.Input.CtrlReads;
        quality.Update();
        Check(log.Count(line => line == "[Error] framework: offline") == 1 && UnityEngine.Input.CtrlReads == ctrlReads, "框架日志保留 Unity 严重等级；不再轮询 Unity 输入或启动外部管理器");

        File.WriteAllText(profile, """{"enabled":["texture-capture","capture"]}""");
        host.Start(packages, profile, data);
        key.SetValue("menu", "原文"); key.SetValue("menu", "原文"); tmp.text = "台词";
        canvas.SetTexture(texture); canvas.SetTexture(texture);
        var png = File.ReadAllBytes(Path.Combine(output, "texture2d", "others", "atlas.png"));
        Check(host.Active.SequenceEqual(new[]{ "capture" }) && png.Take(4).SequenceEqual(new byte[] { 137, 80, 78, 71 }),
              "一个采集开关同时导出文本和贴图");
    }
    UnityEngine.Input.U = true; quality.Update();
    UnityEngine.DebugLogHandler.Internal_Log(0, 0, "after-dispose", null);
    tmp.text = "已卸载"; canvas.SetTexture(new UnityEngine.Texture2D(8, 8) { name = "after" });
    var lines = File.ReadAllLines(textFile);
    using var capture = JsonDocument.Parse("[" + string.Join(",", lines) + "]");
    var values = capture.RootElement.EnumerateArray().Select(row => row.GetProperty("value").GetString()).ToArray();
    Check(game.Live == 0 && !log.Any(line => line.Contains("after-dispose")) && values.SequenceEqual(new[] { "原文", "台词" }) &&
          !File.Exists(Path.Combine(output, "texture2d", "others", "after.png")), "卸载时释放宿主和采集回调");
    UnityEngine.Input.U = false;
}

static void VerifyPanelServices(string work, string distribution)
{
    var data = Directory.CreateDirectory(Path.Combine(work, "manager-fixture", "gakumas-local")).FullName;
    var root = Directory.CreateDirectory(Path.Combine(data, "re")).FullName;
    foreach (var (id, package) in Catalog.Discover(Path.Combine(distribution, "plugins"), _ => {}))
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "plugins", id)).FullName;
        File.Copy(Path.Combine(package.Directory, "plugin.json"), Path.Combine(folder, "plugin.json"));
        if (package.Manifest.ResourceUpdater != null)
            File.Copy(Path.Combine(package.Directory, package.Manifest.Assembly), Path.Combine(folder, package.Manifest.Assembly));
    }
    int Attempt(System.Action action)
    {
        try { action(); return 0; }
        catch (Exception e) when (e is InvalidOperationException or IOException or InvalidDataException or ArgumentException) { return 1; }
    }
    int Select(bool enabled, params string[] ids) => Attempt(() =>
        Gkms.Manager.ProfileEditor.Apply(root, Catalog.Discover(Path.Combine(root, "plugins"), _ => { }),
            ids.ToDictionary(id => id, _ => enabled)));
    int Update(string id, string? source = null) => Attempt(() =>
        PluginHost.UpdateResourcesAsync(Catalog.Discover(Path.Combine(root, "plugins"), _ => { })[id], data, source).GetAwaiter().GetResult());
    int Install(string path) => Attempt(() =>
    {
        using var prepared = Gkms.Manager.PluginInstallation.Prepare(root, path);
        Gkms.Manager.PluginInstallation.Install(prepared);
    });
    int Uninstall(string id) => Attempt(() => Gkms.Manager.PluginInstallation.UninstallAsync(root, id).GetAwaiter().GetResult());
    var pluginDir = Path.Combine(root, "plugins");
    var onlyOld = new Dictionary<string, Package>();
    foreach (var (id, assembly) in new[] { ("i18n", "I18n"), ("master", "Master"), ("lyrics", "Lyrics"),
        ("texture-capture", "TextureCapture"), ("settings-panel", "SettingsPanel"), ("unity-log", "UnityLog"), ("purchase-guard", "PurchaseGuard") })
    {
        var former = Directory.CreateDirectory(Path.Combine(pluginDir, "obsolete-" + id)).FullName;
        var manifest = new Manifest(id, id, Catalog.ApiVersion, $"Gkms.{assembly}.dll", $"Gkms.Plugins.{assembly}.Plugin")
            { Author = "Fixture", Version = "1.0.0", Description = "Retired feature fixture" };
        File.WriteAllText(Path.Combine(former, "plugin.json"), JsonSerializer.Serialize(manifest, Catalog.Json));
        onlyOld.Add(id, new(former, manifest));
    }
    var current = Catalog.Discover(pluginDir, _ => {});
    Check(current.ContainsKey("localization") && current.ContainsKey("ios-login") && !onlyOld.Keys.Any(current.ContainsKey), "覆盖安装时不显示已合并或内置的旧包");
    var fileName = Path.Combine(root, "profile.json");
    File.WriteAllText(fileName, """{"enabled":["texture-capture","settings-panel","capture","unity-log"]}""");
    Check(Catalog.ReadProfile(fileName, current).Enabled.SequenceEqual(new[] { "capture" }), "旧采集和内置功能迁移");
    File.WriteAllText(fileName, """{"enabled":["font"]}""");
    Check(Catalog.ReadProfile(fileName, current).Enabled.Length == 0, "旧字体开关迁移为框架内置，不启用文本翻译");
    foreach (var id in new[] { "localization", "master", "lyrics", "story", "subtitles", "i18n", "text" })
    {
        File.WriteAllText(fileName, JsonSerializer.Serialize(new { enabled = new[] { id } }));
        Check(Catalog.ReadProfile(fileName, current).Enabled.SequenceEqual(new[] { "localization" }), $"从 {id} 迁移到总开关");
    }
    File.WriteAllText(fileName, """{"enabled":["i18n","story","localization","master","lyrics"],"settings":{"localization":{"story":false,"font":false,"textTest":true},"camera":{"speed":5}}}""");
    Check(Catalog.ReadProfile(fileName, onlyOld).Enabled.Contains("i18n"), "仅安装旧包时保留原选择");
    Check(Select(true, "ios-login","unknown") != 0 && Catalog.ReadProfile(fileName).Enabled.SequenceEqual(new[]{"localization"}), "批量选择应全部生效或保留原配置");
    Check(Select(true, "ios-login") == 0 && Select(false, "ios-login") == 0 &&
          Catalog.ReadProfile(fileName).Enabled.SequenceEqual(new[]{"localization"}) &&
          Catalog.ReadProfile(fileName, current).Settings!["localization"].GetProperty("textTest").GetBoolean(), "管理器保留诊断设置");
    using (var saved = JsonDocument.Parse(File.ReadAllText(fileName)))
        Check(saved.RootElement.GetProperty("enabled").GetArrayLength() == 1 &&
              saved.RootElement.GetProperty("settings").GetProperty("localization").EnumerateObject().Single().Name == "textTest",
              "保存配置时合并重复项并移除分项开关");
    Check(Select(false, "localization") == 0 && Catalog.ReadProfile(fileName, current).Enabled.Length == 0, "总开关可一并关闭");
    File.WriteAllText(Path.Combine(data, "localizationConfig.json"),
        """{"enabled":true,"targetFrameRate":120,"replaceFont":false,"dumpRuntimeTexture":true,"enableFreeCamera":true,"loginAsIOS":true,"dbgMode":true,"unlockAllLive":true,"unlockAllLiveCostume":false}""");
    Check(Attempt(() => Gkms.Manager.LegacyProfile.Import(root, current)) == 0, "旧版选项导入");
    var imported = Catalog.ReadProfile(fileName);
    Check(imported.Enabled.Contains("camera") && imported.Enabled.Contains("frame-rate") && imported.Enabled.Contains("ios-login") &&
        imported.Enabled.Contains("localization") && imported.Enabled.Contains("capture") && !imported.Settings!["localization"].TryGetProperty("font", out _) &&
        imported.Settings!["frame-rate"].GetProperty("fps").GetInt32() == 120 &&
        imported.Settings["camera"].GetProperty("speed").GetInt32() == 5 &&
        imported.Settings["live-unlock"].GetProperty("live").GetBoolean() && !imported.Settings["live-unlock"].GetProperty("costumes").GetBoolean() &&
        imported.Settings["capture"].GetProperty("textures").GetBoolean() && !imported.Settings["capture"].GetProperty("text").GetBoolean(), "旧版参数与独立子开关迁移");

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
    Check(Select(false, "localization", "textures") == 0 && Update("localization") == 0 && Update("textures", texZip) == 0,
          "插件未启用也可通过管理器调用各自 DLL 的更新入口");
    var textResources = TestPackages.Resources(pluginDir, "localization");
    var textureResources = TestPackages.Resources(pluginDir, "textures");
    Check(File.ReadAllText(Path.Combine(textResources, "localization.json")).Contains("中文") &&
          File.ReadAllText(Path.Combine(textureResources, "title.png")) == "fixture" &&
          !Directory.Exists(Path.Combine(data,"local-files")) && !Directory.Exists(Path.Combine(data,"texture2d")), "两种资源进入各自插件目录，不创建旧共享目录");
    Check(Update("textures", textZip) != 0 && Update("capture", texZip) != 0 &&
          File.Exists(Path.Combine(textureResources, "title.png")), "错误的包类型或不支持更新的插件不会改写资源");
    var pluginZip = Path.GetFullPath(Path.Combine(distribution, "..", "..", "plugin-packages", "Gkms.FrameRate.zip"));
    Check(Uninstall("frame-rate") == 0 && Install(pluginZip) == 0 && Install(pluginZip) != 0 &&
          Select(true, "frame-rate") == 0 && Uninstall("frame-rate") == 0, "面板服务安装、启用、卸载和重复安装处理");
    Check(!Catalog.Discover(pluginDir, _ => { }).ContainsKey("frame-rate") &&
          Catalog.ReadProfile(fileName).Settings!["frame-rate"].GetProperty("fps").GetInt32() == 120 &&
          File.Exists(Path.Combine(textResources, "localization.json")), "卸载保留设置和翻译资源");
}

static void VerifyUpdate(string work, string packages)
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
        Add("masterTrans/Localization.json", """{"rules":{"primaryKeys":["id"]},"data":[{"id":"nested","name":"主数据翻译"}]}""");
        Add("resource/scene.txt","新版剧情");
        Add("gakumasassets","新版字体包");
        Add("genericTrans/genericTrans/lyrics/home_voice_subtitles.json","""{"subtitles":{"cue":"新版字幕"}}""");
        Add("genericTrans/new.json","""{"new":"新词条"}""");
        Add("genericTrans/genericTrans/lyrics/srt_live_bundle.json", """{"歌詞":"新歌词"}""");
        Add("masterTrans/Card.json", """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"新表名"}]}""");
    }
    data.Position = 0;
    using var zip = new System.IO.Compression.ZipArchive(data);
    var packageDirectory = Catalog.Discover(packages, _ => { })["localization"].Directory;
    var count = Gkms.Resources.PackInstaller.Install(packageDirectory, false, zip, null);
    var installed = Path.Combine(packageDirectory,"resources");
    var card = new Card();
    new MasterTable(Path.Combine(installed, "masterTrans", "Card.json")).Apply(card);
    var dictionary = new TextDictionary(installed);
    Check(count == 8 && card.Name == "新表名" && dictionary.Translate("歌詞") == "新歌词" && File.ReadAllText(Path.Combine(installed,"resource","scene.txt")) == "新版剧情" &&
          File.ReadAllText(Path.Combine(installed,"masterTrans","Localization.json")).Contains("主数据翻译") &&
          File.ReadAllText(Path.Combine(installed,"gakumasassets")) == "新版字体包" &&
          SubtitleConfig.Load(VoiceSubtitles.ConfigFile(installed)).Lookup("cue") == "新版字幕" &&
          dictionary.Translate("new") == "新词条" &&
          File.ReadAllText(Path.Combine(installed, "localization.json")).Contains("更新") &&
          File.Exists(Path.Combine(installed, "generic.json")) &&
          File.Exists(Path.Combine(packageDirectory, "resources.previous", "localization.json")) &&
          File.ReadAllText(Path.Combine(work,"local-files","localization.json")).Contains("错误旧译"), "资源更新保存自定义文件与上一版，不改旧版目录");
    using var ambiguousData=new MemoryStream();
    using(var writer=new System.IO.Compression.ZipArchive(ambiguousData,System.IO.Compression.ZipArchiveMode.Create,true))
        foreach(var name in new[]{"first/localization.json","second/localization.json"})
        {using var entry=new StreamWriter(writer.CreateEntry(name).Open()); entry.Write("{}");}
    ambiguousData.Position=0;
    using(var ambiguous=new System.IO.Compression.ZipArchive(ambiguousData))
    {
        try {Gkms.Resources.PackInstaller.Install(packageDirectory,false,ambiguous,null); throw new Exception("应拒绝两个平级资源根目录");}
        catch(InvalidDataException) { }
    }
    Check(File.ReadAllText(Path.Combine(installed,"localization.json")).Contains("更新"),"歧义资源包保留现有资源");
    using var bad = new MemoryStream();
    using (var writer = new System.IO.Compression.ZipArchive(bad, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        writer.CreateEntry("texture2d/texture_version.txt");
        writer.CreateEntry("texture2d/../outside.png");
    }
    bad.Position = 0;
    using var invalid = new System.IO.Compression.ZipArchive(bad);
    var textureDirectory = Catalog.Discover(packages, _ => { })["textures"].Directory;
    var beforeInvalidPack = File.ReadAllBytes(Path.Combine(textureDirectory, "resources", "others", "title.png"));
    try { Gkms.Resources.PackInstaller.Install(textureDirectory, true, invalid, "v1"); throw new Exception("错误接受了越界资源"); }
    catch (InvalidDataException) { }
    Check(File.ReadAllBytes(Path.Combine(textureDirectory, "resources", "others", "title.png")).SequenceEqual(beforeInvalidPack) &&
          !File.Exists(Path.Combine(textureDirectory, "resources", "texture_version.txt")) &&
          !File.Exists(Path.Combine(textureDirectory, "outside.png")), "损坏资源应保留已安装的贴图且不能写入包外");
}

static void VerifyPack(string root)
{
    var local = root;
    var dictionary = new TextDictionary(local);
    var tables = Directory.GetFiles(Path.Combine(local,"masterTrans"), "*.json");
    foreach (var path in tables) _ = new MasterTable(path);
    var i18n = Gkms.Sdk.Interop.ReadDictionary(Path.Combine(local, "localization.json"));
    Check(dictionary.Count > 0 && i18n.Count > 0 && tables.Length > 0, "真实词库为空");
    var voices = SubtitleConfig.Load(VoiceSubtitles.ConfigFile(local));
    Check(voices.Lines.Count > 0, "真实语音字幕为空");
    Console.WriteLine($"资源包通过，界面键 {i18n.Count}，通用词条 {dictionary.Count}，语音字幕 {voices.Lines.Count}，主数据表 {tables.Length}。");
}

internal static class TestPackages
{
    public static string Copy(string source, string data)
    {
        var target = Path.Combine(data, "re", "plugins");
        foreach (var package in Catalog.Discover(source, _ => { }).Values)
        {
            var directory = Directory.CreateDirectory(Path.Combine(target, Path.GetFileName(package.Directory))).FullName;
            foreach (var file in Directory.EnumerateFiles(package.Directory))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
        return target;
    }
    public static string Resources(string packages, string id) => Path.Combine(Catalog.Discover(packages, _ => { })[id].Directory, "resources");
}

public sealed class LivePresenter
{
    public bool Visible;
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetLyricsActive(bool visible) => Visible = visible;
}
public sealed class Card : Google.Protobuf.IMessage
{
    public Action<Card>? Parse;
    public string Id { get; set; } = "p";
    public int Rank => 1;
    private string name = "";
    public bool FailRestore;
    public bool FailTranslationBefore, FailTranslationAfter;
    public int NameWrites;
    public string Name
    {
        get => name;
        set
        {
            NameWrites++;
            if (FailRestore && value.StartsWith("原")) throw new InvalidOperationException("master restore");
            if (FailTranslationBefore && value.StartsWith("译")) throw new InvalidOperationException("translation before write");
            name = value;
            if (FailTranslationAfter && value.StartsWith("译")) throw new InvalidOperationException("translation after write");
        }
    }
    public List<string>? Lines { get; set; }
    public List<Part> Parts { get; } = new(){new(PartKind.First), new(PartKind.Second)};
    [MethodImpl(MethodImplOptions.NoInlining)] public void InternalMergeFrom() => Parse?.Invoke(this);
}
public enum PartKind { First, Second }
public sealed record Part(PartKind Kind) { public string Text { get; set; } = ""; }
public sealed class Ui
{
    public string Value = "";
    public readonly Dictionary<string, string> Values = new();
    public Action<Ui, string>? Written;
    public bool FailWriteBefore, FailWriteAfter;
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetValue(string key, string value)
    {
        if (FailWriteBefore) throw new InvalidOperationException("I18n before write");
        Value = value; Values[key] = value; Written?.Invoke(this, key);
        if (FailWriteAfter) throw new InvalidOperationException("I18n after write");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public string GetValue(string key) => Values.GetValueOrDefault(key) ?? "";
}
public class Tmp : UnityEngine.Object
{
    public TMPro.TMP_FontAsset font { get; set; } = new();
    public UnityEngine.Material? fontSharedMaterial { get; set; }
    public string Value = "";
    public int AccessAfterDestroy;
    public TMPro.TMP_FontAsset? FontAtWrite;
    public UnityEngine.Font? SourceFontAtWrite;
    public bool FailWriteBefore, FailWriteAfter;
    public Action<Tmp>? Written;
    public string text
    {
        get { if (Destroyed) { AccessAfterDestroy++; throw new NullReferenceException("Destroyed native TMP object"); } return Value; }
        [MethodImpl(MethodImplOptions.NoInlining)] set
        {
            if (FailWriteBefore) throw new InvalidOperationException("fixture before write");
            FontAtWrite = font; SourceFontAtWrite = font.sourceFontFile; Value = value;
            Written?.Invoke(this);
            if (FailWriteAfter) throw new InvalidOperationException("fixture after write");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetText(string value) { FontAtWrite = font; SourceFontAtWrite = font.sourceFontFile; Value = value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetText(string value, bool sync) { FontAtWrite = font; SourceFontAtWrite = font.sourceFontFile; Value = value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void PopulateTextBackingArray(string value, int start, int count) { FontAtWrite = font; SourceFontAtWrite = font.sourceFontFile; Value = value.Substring(start, count); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetCharArray(char[] value, int start, int count) => Value = new string(value, start, count);
}
public sealed class Octo
{
    public int OriginalCalls;
    [MethodImpl(MethodImplOptions.NoInlining)] public void LoadFromCacheOrDownload(string name, Action<string, object?> onComplete, object? progress) { OriginalCalls++; onComplete("orig",null); }
}
public sealed class TestGame : IGameServices, IAdvancedGameServices
{
    public int Live;
    public Action<string, MethodBase>? BeforePatch;
    public bool FailAfterFirstText;
    public bool RejectSharedText;
    public bool RejectQualityTracking;
    public bool StripReadbackMetadata;
    public readonly List<MethodBase> Patched = new();
    private int textPatches;
    public Type? FindType(string assembly, string fullName) => fullName switch
        { "System.Threading.CancellationTokenSource" => typeof(CancellationTokenSource),
          "UnityEngine.GL" when StripReadbackMetadata => new UnityEngine.StrippedGL(),
          "Qua.UI.I18n" => typeof(Ui), "TMPro.TMP_Text" => typeof(Tmp), "Octo.Loader.OctoResourceLoader" => typeof(Octo),
          "Campus.Live.LiveScenePresenter" => typeof(LivePresenter), "Campus.Common.Proto.Client.Master.Card" => typeof(Card),
          _ => typeof(TestGame).Assembly.GetType(fullName) };
    public IDisposable Patch(string owner, MethodInfo method, MethodInfo? prefix, MethodInfo? postfix, int priority)
        => PatchAdvanced(owner, method, prefix, postfix, null, priority);
    public IDisposable PatchAdvanced(string owner, MethodBase method, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? finalizer, int priority)
    {
        BeforePatch?.Invoke(owner, method);
        Patched.Add(method);
        if (FailAfterFirstText && owner != "framework" && method.DeclaringType == typeof(Tmp) && ++textPatches == 2) throw new Exception("模拟游戏更新导致签名不兼容");
        if (RejectSharedText && method.DeclaringType == typeof(Tmp) && method.Name == "SetText")
            throw new NotSupportedException("Shared native code has incompatible IL2CPP signatures: SetText");
        if (RejectQualityTracking && method.Name is "SetReflectionQuality" or "SetLODQuality")
            throw new NotSupportedException("Shared native code has incompatible IL2CPP signatures: quality setter");
        var harmony = new Harmony("tests." + owner);
        harmony.Patch(method, prefix == null ? null : new HarmonyMethod(prefix) { priority = priority },
            postfix == null ? null : new HarmonyMethod(postfix) { priority = priority },
            finalizer: finalizer == null ? null : new HarmonyMethod(finalizer) { priority = priority });
        Live++;
        return new Cleanup(() =>
        {
            // The native bridge removes a registration atomically. Harmony rebuilds after each
            // handler removal; remove state consumers before the prefix that declares __state.
            foreach (var handler in new[] { finalizer, postfix, prefix }.OfType<MethodInfo>()) harmony.Unpatch(method, handler);
            Live--;
        });
    }
    private sealed record Cleanup(Action Callback) : IDisposable { public void Dispose() => Callback(); }
}
