using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Gkms.Host;
using Gkms.Sdk;
using Campus.Common.Master;
using Campus.Common.User;

internal static class MigrationTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string work, string packages)
    {

        var data = Directory.CreateDirectory(Path.Combine(work, "migration")).FullName;
        packages = TestPackages.Copy(packages, data);
        Unlock(data, packages);
        Reload(data, packages);
        FontWithSharedText(data, packages);
        Inputs(data, packages);
        Capture(data, packages);
        Update(data, packages).GetAwaiter().GetResult();
        Console.WriteLine("迁移专项通过：解锁、热重载、字体、镜头/陀螺仪、窗口、采集、更新。");
    }
    private static void FontWithSharedText(string data,string packages)
    {
        var profile=Path.Combine(data,"re","profile.json");
        File.WriteAllText(profile,"""{"enabled":["localization"]}""");
        var log=new List<string>(); var game=new TestGame();
        var nativeFont=new TMPro.TMP_FontAsset {name="campusnormal"};
        var face = nativeFont.faceInfo; var material = nativeFont.material;
        var weights = nativeFont.fontWeightTable; var fallbacks = nativeFont.fallbackFontAssetTable;
        var notifications = TMPro.TMPro_EventManager.Changes;
        var dictionaryPath = Path.Combine(TestPackages.Resources(packages, "localization"), "generic.json");
        var dictionary = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(dictionaryPath))!.AsObject();
        dictionary["マニー"] = "金币"; dictionary["段落"] = "中间"; dictionary["中间"] = "最终"; dictionary["同文"] = "同文";
        File.WriteAllText(dictionaryPath, dictionary.ToJsonString());
        UnityEngine.AssetBundle.ReuseSourceFont = false;
        using(var host=new PluginHost(game,log.Add))
        {
            host.Start(packages,profile,data);
            var label = new TMPro.TextMeshProUGUI {font=nativeFont}; label.Awake();
            var source = nativeFont.sourceFontFile;
            Check(host.Active.Count == 1 && label.font == nativeFont && source is { Destroyed: false } &&
                nativeFont.Clears == 1 && TMPro.TMPro_EventManager.Changes == notifications + 1 &&
                TMPro.TMP_Settings.fallbackFontAssets.Count == 0, "启动时汉化与内置字体工作，原地清表一次并广播，不添加全局回退资产");
            Check(nativeFont.faceInfo.Equals(face) && nativeFont.material == material &&
                nativeFont.fontWeightTable == weights && nativeFont.fallbackFontAssetTable == fallbacks,
                "保留原FaceInfo、材质、字重和回退引用，不克隆资产");
            nativeFont.Glyphs.Add('中'); label.Awake();
            Check(nativeFont.Clears == 1 && nativeFont.Glyphs.Contains('中'), "重复入口不清除已有动态图集");
            var loads = UnityEngine.AssetBundle.Loads;
            File.WriteAllText(profile,"""{"enabled":["localization"]}"""); host.ReloadProfile();
            Check(host.Active.SequenceEqual(new[]{"localization"}) && !log.Any(message=>message.Contains("加载失败")),string.Join("\n",log));
            try { host.ReloadResources("localization"); throw new Exception("文本重载应拒绝"); } catch (InvalidOperationException) { }
            Check(nativeFont.sourceFontFile == source && nativeFont.Clears == 1 && UnityEngine.AssetBundle.Loads == loads,
                "汉化启用及资源重载不重新加载字体、不清理图集");
            var text = new Tmp(); text.PopulateTextBackingArray("word",0,4);
            Check(text.Value == "新词", "切片入口翻译");
            text.SetText("word"); Check(text.text == "新词", "单参数SetText翻译");
            text.SetText("word", true); Check(text.text == "新词", "双参数SetText翻译");
            var matched = new Tmp(); matched.SetText("マニー", false);
            Check(matched.Value == "金币" && matched.SourceFontAtWrite == source && matched.font.Clears == 1,
                "命中翻译时原地替换发生在原SetText之前");
            var unmatched = new Tmp(); unmatched.SetText("没有词条");
            Check(unmatched.SourceFontAtWrite == null && unmatched.font.sourceFontFile == source,
                "未命中时先原SetText再原地替换");
            var identity = new Tmp(); identity.SetText("同文");
            Check(identity.SourceFontAtWrite == source, "原文等于译文也走前置字体更新");
            var chars = new Tmp(); chars.SetCharArray("段落".ToCharArray(),0,2);
            Check(chars.Value == "中间" && chars.SourceFontAtWrite == source, "字符数组不重复翻译，保留前置字体更新");
            var awake = new TMPro.TextMeshProUGUI {Value="段落"}; awake.Awake();
            Check(awake.TextAtAwake == "最终" && awake.font.sourceFontFile == source && awake.Rebuilds == 0,
                "Awake按原版通过正常setter，不强制提前生成网格");
            Check(!game.Patched.Any(method => method.Name is "OnPreRenderCanvas" or "OnPreRenderObject"),
                "不在渲染期添加翻译/字体补偿");
            foreach (var name in new[] { "CampusAlphanumeric SDF", "CampusAlphanumericSemiBold" })
            {
                var numeric = new TMPro.TextMeshProUGUI {font = new() {name=name}};
                numeric.Awake(); numeric.SetText("123");
                Check(numeric.font.Clears == 0 && numeric.font.sourceFontFile == null, "数字专用字体始终保留");
            }
            // No CreateFontAsset call is allowed, even across translation reload.
            TMPro.TMP_FontAsset.FailCreateAfter = 0;
            try { try { host.ReloadResources("localization"); throw new Exception("文本重载应拒绝"); } catch (InvalidOperationException) { } Check(TMPro.TMP_FontAsset.FailCreateAfter == 0, "不创建独立字体资产"); }
            finally { TMPro.TMP_FontAsset.FailCreateAfter = -1; }
            UnityEngine.Object.Destroy(source!);
            label.SetText("没有词条");
            Check(nativeFont.sourceFontFile is {Destroyed:false} renewed && renewed != source &&
                nativeFont.Clears == 1 && nativeFont.Glyphs.Contains('中') && UnityEngine.AssetBundle.Loads == loads,
                "场景回收源Font后从同一内置包取回，不清理仍有效的字形、不重载资源");
            source = nativeFont.sourceFontFile;
            File.WriteAllText(profile,"""{"enabled":[]}"""); host.ReloadProfile();
            Check(label.font == nativeFont && nativeFont.sourceFontFile == source && nativeFont.Clears == 1,
                "汉化停用不恢复或销毁字体");
            var later = new Tmp(); later.SetText("マニー", true);
            Check(later.Value == "金币" && later.font.sourceFontFile == source, "关闭汉化的请求在下次启动生效");
            try { host.ReloadResources("font"); throw new Exception("字体不能有资源重载入口"); }
            catch (InvalidOperationException) { }
        }
        Check(game.Live==0,"框架退出释放回调");
    }
    private static void Unlock(string data, string packages)
    {
        var file = Path.Combine(data, "profile.json");
        File.WriteAllText(file, """{"enabled":["live-unlock"]}""");
        MasterManager.IdolCardSkinMaster.Items.Add(new("i_card-hski-1", "skin", "head"));
        MasterManager.IdolCardSkinMaster.Items.Add(new("i_card-hski-2", "skin", "head"));
        MasterManager.IdolCardSkinMaster.Items.Add(new("i_card-other-1", "other", "other-head"));
        MasterManager.CostumePhotoGroupMaster.Items.Add(new(new[] { "photo" }));
        MasterManager.CostumeMaster.Items.AddRange(new[]
        {
            new Costume("skin", "head", CostumeColorGroupId: "color"),
            new Costume("variant", "variant-head", "default-head", "color"),
            new Costume("photo", "photo-head"), new Costume("public", "public-head", ViewStartTime: 1_600_000_000_000),
            new Costume("future", "future-head", CostumeColorGroupId: "color", ViewStartTime: DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds()),
            new Costume("hidden", "hidden-head")
        });
        var bodies = UserDataManager.Bodies = new(); var heads = UserDataManager.Heads = new();
        var skins = new UserIdolCardSkinCollection(); var log = new List<string>();
        using (var host = new PluginHost(new TestGame(), log.Add))
        {
            host.Start(packages, file, data);
            Check(host.Active.SequenceEqual(new[] { "live-unlock" }), string.Join("\n", log));
            Check(skins.Exists("music-test") && skins.Exists("i_card-skin-test") && !skins.Exists("other"), "Live 所有权限定 ID 范围");
            Check(!new Campus.OutGame.MeishiBasePIdolTabPageModel().Owned, "名片构造保留真实持有状态");
            Campus.OutGame.MeishiBasePIdolTabPageModel.Throw = true;
            try { _ = new Campus.OutGame.MeishiBasePIdolTabPageModel(); throw new Exception("expected fixture failure"); }
            catch (InvalidOperationException) { }
            finally { Campus.OutGame.MeishiBasePIdolTabPageModel.Throw = false; }
            Check(skins.Exists("music-test"), "名片构造抛异常后恢复解锁作用域");
            Check(bodies.GetAll().Count == 0, "空账号集合没有模板时不伪造无效记录");
            bodies.Add(new() { CostumeId = "owned" }); heads.Add(new() { CostumeHeadId = "owned-head" });
            Check(!UserDataManager._userCostumeList.Exists("skin") && !UserDataManager._userCostumeHeadList.Exists("head"), "原版不在集合 getter 上提前写入");
            _ = bodies.GetAll(); _ = heads.GetAll();
            Check(bodies.Exists("skin") && heads.Exists("head"), "原版 GetAll 入口补齐服装和头饰");
            var count = bodies.GetAll().Count;
            Check(count == 6 && bodies.FindBy(_ => true).Count() == count && !bodies.Exists("future") && !bodies.Exists("hidden") &&
                heads.Exists("default-head") && heads.Exists("public-head"), "去重、配色/拍照/公开服装和未来资源过滤");
            var unrelated = new UserRows<UserCostume>(); unrelated.Add(new() { CostumeId = "safe" });
            Check(unrelated.GetAll().Count == 1, "共享泛型方法不污染其他集合");
            var photo = new Campus.Photography.PhotographyCostumeSettingData();
            Check(photo.GetCostumes("hski").Count == 1 && photo.GetCostumeHeads("hski").Count == 1 &&
                photo.GetCostumeHeads("hski").Count == 1 && !new Campus.Photography.PhotographyCostumeSettingListItemModel().IsDisabled,
                "摄影条目解锁、角色过滤和去重");
            var thumbnail = new Campus.OutGame.PictureBook.PictureBookLiveThumbnailView();
            thumbnail.SetReleaseDataAsync(new(), "hski", false, false, false, default);
            thumbnail.SetUnReleaseDataAsync(new(), false, "hski", default);
            Check(thumbnail.Unlocked && thumbnail.Skin && thumbnail.Exempt, "Live 缩略图参数解锁");
            bodies.Add(new() { CostumeId = "skin" }); // A server response legitimately replaces the temporary row.
            File.WriteAllText(file, """{"enabled":["live-unlock"],"settings":{"live-unlock":{"live":false,"costumes":true}}}""");
            host.ReloadProfile();
            Check(!skins.Exists("music-test") && !new Campus.Photography.PhotographyCostumeSettingListItemModel().IsDisabled,
                "Live 与服装子开关可以分别启停");
        }
        Check(bodies.Count == 2 && bodies.Exists("owned") && bodies.Exists("skin") && heads.Count == 1 &&
            !skins.Exists("music-test") && new Campus.Photography.PhotographyCostumeSettingListItemModel().IsDisabled,
            "停用只撤销临时记录，保留后续真实拥有的数据");
    }

    private static void Reload(string data, string packages)
    {
        var local = Directory.CreateDirectory(TestPackages.Resources(packages, "localization")).FullName;
        var root = Directory.CreateDirectory(Path.Combine(data, "re")).FullName;
        var profile = Path.Combine(root, "profile.json");
        var keyed = Path.Combine(local, "localization.json");
        var generic = Path.Combine(local, "generic.json");
        File.WriteAllText(keyed, """{"key":"旧译"}"""); File.WriteAllText(generic, """{"word":"旧词"}""");
        File.WriteAllBytes(Path.Combine(local, "gakumasassets"), new byte[] {1});
        File.WriteAllText(profile, """{"enabled":["localization","gyro"],"settings":{"gyro":{"sensitivity":2}}}""");
        var log = new List<string>(); var game = new TestGame(); var ui = new Ui(); var tmp = new Tmp();
        var nativeFont = new TMPro.TMP_FontAsset { name = "campusnormal" };
        var originalFont = nativeFont.sourceFontFile;
        var text = new TMPro.TextMeshProUGUI { font = nativeFont };
        using (var host = new PluginHost(game, log.Add))
        {
            host.Start(packages, profile, data); host.EnableRuntimeControl();
            Check(host.Active.Count == 2, string.Join("\n", log));
            text.Awake(); text.Awake();
            Check(text.font == nativeFont && text.font.sourceFontFile != null && text.font.atlasPopulationMode == 1 &&
                text.font.atlasWidth == 4096 && text.font.atlasHeight == 4096 &&
                text.font.isMultiAtlasTexturesEnabled && nativeFont.Clears == 1, "框架原地替换，仅首次清理原字形表");
            var numeric = new TMPro.TextMeshProUGUI { font = new() {name = "CampusAlphanumeric SDF"} };
            numeric.Awake(); Check(numeric.font.Clears == 0, "保留数字专用字体");
            var patches = game.Live;
            File.WriteAllText(keyed, """{"key":"新译"}"""); File.WriteAllText(generic, """{"word":"新词"}""");
            var reply = Request(root, "reload-resources", host, "localization");
            ui.SetValue("key", "original"); tmp.SetText("word");
            Check(!reply.Success && reply.Message.Contains("不支持主动资源重载") && ui.Value == "旧译" && tmp.Value == "旧词" && game.Live == patches && TMPro.TMP_Settings.fallbackFontAssets.Count == 0,
                "拒绝文本重载，本次词典与回调保持不变");
            File.WriteAllText(generic, "{"); File.WriteAllText(keyed, """{"key":"损坏包中的译文"}""");
            reply = Request(root, "reload-resources", host, "localization"); ui.SetValue("key", "original");
            Check(!reply.Success && ui.Value == "旧译", "运行中损坏的文件不改变本次词典");
            File.WriteAllText(generic, """{"word":"新词"}""");
            File.WriteAllBytes(Path.Combine(local, "gakumasassets"), Array.Empty<byte>());
            var source = text.font;
            reply = Request(root, "reload-resources", host, "localization");
            Check(!reply.Success && text.font == source && !source.Destroyed && nativeFont.Clears == 1 && TMPro.TMP_Settings.fallbackFontAssets.Count == 0,
                "插件目录的损坏字体包不影响文本重载或框架内置字体");
            File.WriteAllBytes(Path.Combine(local, "gakumasassets"), new byte[] {1});
            File.WriteAllText(profile, """{"enabled":["localization","gyro"],"settings":{"gyro":{"sensitivity":-1}}}""");
            reply = Request(root, "reload-profile", host);
            Check(!reply.Success && host.Active.Contains("gyro") && game.Live == patches, "无效新参数恢复上一版插件和回调");
            File.WriteAllText(profile, "{"); reply = Request(root, "reload-profile", host);
            Check(!reply.Success && host.Active.Count == 2, "损坏配置不卸载现有插件");
            File.WriteAllText(profile, """{"enabled":[]}"""); reply = Request(root, "reload-profile", host);
            Check(reply.Success && host.Active.SequenceEqual(new[] { "localization" }) && text.font == nativeFont && nativeFont.Clears == 1 && nativeFont.sourceFontFile != originalFont && nativeFont.atlasWidth == 4096 &&
                TMPro.TMP_Settings.fallbackFontAssets.Count == 0, "停用所有插件后框架字体继续有效");
            Check(Request(root, "status", host).Active.SequenceEqual(new[] { "localization" }), "运行时状态来自宿主");
            File.WriteAllText(profile, """{"enabled":["ios-login"]}""");
            File.SetLastWriteTimeUtc(profile, DateTime.UtcNow.AddSeconds(2));
            new Campus.Common.CampusQualityManager().Update();
            Check(host.Active.OrderBy(x => x).SequenceEqual(new[] { "ios-login", "localization" }), "框架自动检测配置文件变化并在 Unity 更新中启用插件");
        }
        Check(game.Live == 0 && !File.Exists(Path.Combine(root, "runtime.json")), "宿主卸载清理运行时入口");
    }
    private static RuntimeReply Request(string root, string command, PluginHost host, string? id = null)
    {
        var request = RuntimeClient.SendAsync(root, command, id);
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!request.IsCompleted && DateTime.UtcNow < until) { host.Pump(); Thread.Sleep(5); }
        return request.GetAwaiter().GetResult() ?? throw new Exception("runtime pipe missing");
    }

    private static void Inputs(string data, string packages)
    {
        var profile = Path.Combine(data, "profile.json"); var log = new List<string>();
        File.WriteAllText(profile, """{"enabled":["gyro","camera"],"settings":{"camera":{"hideHead":false}}}""");
        var panorama = new Campus.Live.LivePanoramaCameraManager();
        var controller = new Campus.Common.CampusGyroController();
        using (var host = new PluginHost(new TestGame(), log.Add))
        {
            host.Start(packages, profile, data); Check(host.Active.Count == 2, string.Join("\n", log));
            panorama.LateUpdate(); Check(panorama._gyroController?._initialized == true && panorama.IsGyroReady, "全景自动补齐控制器");
            var photo = new Campus.Photography.PhotographyActivityController(); photo.Initialize(new(), controller, 89);
            Check(controller._initialized && controller._gyro?.gyroSupported == true && !controller._isGyro && new Campus.Photography.Photography3DPresenter().IsGyroSupported, "原版初始化 VLGyro，不改写控制器 IsGyro 开关");
            UnityEngine.Input.Held.Add("[7]"); UnityEngine.Time.frameCount++; var roll = controller.Rotation;
            Check(roll.z > 0 && roll.x == 0 && roll.y == 0 && controller.Rotation == roll, "7 产生滚转且一帧只采样一次");
            LiveGameState.InputCapture = () => true;
            UnityEngine.Time.frameCount++; Check(controller.Rotation == roll, "面板占用输入时不采样异步陀螺仪按键");
            LiveGameState.InputCapture = null;
            UnityEngine.Input.Held.Clear(); UnityEngine.Input.Held.Add("[4]"); UnityEngine.Time.frameCount++;
            Check(controller.Rotation == roll, "不占用游戏小键盘 4/6/2/8");
            UnityEngine.Input.Held.Clear(); controller.ResetRotation();
            Check(Math.Abs(controller.Rotation.w - 1) < .0001f, "控制器重置设定相对基准");
            UnityEngine.Input.Held.Add("mouse 1"); UnityEngine.Input.mousePosition = default;
            UnityEngine.Time.frameCount++; var beforeDrag = controller.Rotation;
            UnityEngine.Input.mousePosition = new(20, 10, 0);
            UnityEngine.Time.frameCount++; var rotated = controller.Rotation;
            Check(rotated.x != 0 && rotated.y != 0, "鼠标控制俯仰和偏航");
            UnityEngine.Input.Held.Clear(); UnityEngine.Input.Held.Add("[5]"); UnityEngine.Time.frameCount++;
            Check(Math.Abs(controller.Rotation.z + roll.z) < .0001f, "5 只归中模拟传感器，保留控制器相对基准"); controller.UpdateRotation();
            Check(controller.NativeCalls > 0, "模拟输入仍经过游戏控制器更新"); UnityEngine.Input.Held.Clear();

            var actor = new Campus.Common.CampusActorController();
            var brain = new Cinemachine.CinemachineBrain();
            void Tick(string? press = null) { if (press != null) UnityEngine.Input.Pressed.Add(press); UnityEngine.Time.frameCount++; actor.LateUpdate(); brain.PushStateToUnityCamera(default); }
            Tick(); UnityEngine.Input.Held.Add("w"); Tick(); UnityEngine.Input.Held.Clear();
            Check(brain.OutputCamera.transform.position.z < 1.3f, "原版自由镜头 W 向负 Z 移动，启用后无需 F8");
            Tick("f"); actor.LateUpdate(); Tick(); var y = brain.OutputCamera.transform.position.y;
            actor._rootBody.position = new(0,10,0); actor.LateUpdate(); Tick();
            Check(brain.OutputCamera.transform.position.y < 10, "原版默认跟随高度没有额外 0.25 偏移");
            Tick("v"); Check(brain.OutputCamera.transform.position.y == 10, "V 切换跟随即时高度");
            Tick("f"); actor._rootBody.rotation = UnityEngine.Quaternion.Euler(10,20,30); actor.LateUpdate(); Tick();
            UnityEngine.Rendering.RenderPipelineManager.EndCameraRendering(default, brain.OutputCamera);
            Check(brain.OutputCamera.nearClipPlane == .001f, "第一人称应用近裁剪");
            var pointer = brain.OutputCamera.transform.m_CachedPtr; var result = default(UnityEngine.Quaternion);
            UnityEngine.Transform.set_rotation_Injected(pointer, ref result);
            Check(result == actor._rootBody.rotation, "独立旋转入口在第一人称允许角色滚转");
            Tick("v"); UnityEngine.Transform.set_rotation_Injected(pointer, ref result);
            Check(result != actor._rootBody.rotation, "V 切换后独立旋转入口改为朝向注视点");
            Tick("f");
            UnityEngine.Rendering.RenderPipelineManager.EndCameraRendering(default, brain.OutputCamera);
            Check(brain.OutputCamera.nearClipPlane == .3f, "回到自由模式立即恢复近裁剪，无须停用插件");
        }
        Check(!controller._initialized && !controller._isGyro && panorama._gyroController == null, "停用恢复控制器并撤销注入");
        UnityEngine.Input.Held.Clear(); UnityEngine.Input.Pressed.Clear(); UnityEngine.Input.MouseX = UnityEngine.Input.MouseY = 0;
        File.WriteAllText(profile, """{"enabled":["window"]}""");
        var window = new Campus.Common.StandAloneWindow.WindowManager(); var handle = new Campus.Common.StandAloneWindow.WindowHandle();
        using (var host = new PluginHost(new TestGame(), log.Add))
        {
            host.Start(packages, profile, data); Check(host.Active.Contains("window"), string.Join("\n", log));
            // Do not tick ApplyWindow: all verification uses fake Screen/Window methods and leaves native windows alone.
            UnityEngine.Screen.SetResolution(1440, 900, false); window.ApplyOrientationSettings(0);
            UnityEngine.Screen.SetResolution(900, 1440, false); window.ApplyOrientationSettings(1); window.ApplyOrientationSettings(1);
            Check(UnityEngine.Screen.width == 900 && UnityEngine.Screen.height == 1440 && window.Calls == 0, "原版每次切换后记住该次输入尺寸，下次使用该值");
            Check(handle.SetWindowLong(-4, new IntPtr(5)) == IntPtr.Zero && handle.Calls == 0 && handle.SetWindowLong(-16, new IntPtr(6)) == new IntPtr(6), "只拦截窗口过程恢复");
            File.WriteAllText(profile, """{"enabled":["window"],"settings":{"window":{"resizable":false}}}"""); host.ReloadProfile();
            var calls = handle.Calls;
            Check(handle.SetWindowLong(-4, new IntPtr(7)) == new IntPtr(7) && handle.Calls == calls + 1,
                "关闭自由缩放后，按 DMM 原版透传游戏窗口过程设置");
            window.ApplyOrientationSettings(0);
            Check(window.Calls == 1, "关闭自由缩放后透传游戏方向尺寸处理");
            File.WriteAllText(profile, """{"enabled":["window"],"settings":{"window":{"resizable":true}}}"""); host.ReloadProfile();
            calls = handle.Calls;
            Check(handle.SetWindowLong(-4, new IntPtr(8)) == IntPtr.Zero && handle.Calls == calls,
                "重新开启自由缩放后恢复原版窗口过程拦截");
        }
        window.ApplyOrientationSettings(0); Check(window.Calls == 2, "关闭窗口插件恢复游戏处理");
    }

    private static void Capture(string data, string packages)
    {
        var profile = Path.Combine(data, "profile.json"); File.WriteAllText(profile, """{"enabled":["capture"]}""");
        using (var host = new PluginHost(new TestGame(), _ => { }))
        {
            host.Start(packages, profile, data); Check(host.Active.Contains("capture"), "采集插件启动");
            new Ui().SetValue("button", "原始按钮");
            var tmp = new Tmp(); tmp.PopulateTextBackingArray("前切片后", 1, 2); tmp.SetCharArray("字符集".ToCharArray(), 0, 3);
            new TMPro.TextMeshProUGUI {text = "初始文本"}.Awake();
            new UnityEngine.UI.Text().text = "传统文本"; new UnityEngine.UIElements.TextField().value = "字段文本";
            tmp.SetText("第12回<b>完成</b>"); tmp.SetText("1234"); tmp.SetText("word");
            UnityEngine.Input.Pressed.Add("[+]"); new Campus.Common.CampusQualityManager().Update(); tmp.SetText("分组文本");
        }
        var root = Path.Combine(Catalog.Discover(packages, _ => { })["capture"].Directory, "output");
        var generic = Interop.ReadDictionary(Path.Combine(root, "generic.json"));
        var originals = Interop.ReadDictionary(Path.Combine(root, "generic_orig.json"));
        Check(new[] { "切片", "字符集", "初始文本", "传统文本", "字段文本" }.All(originals.ContainsKey) &&
            new[] { "[__split__]第", "[__split__]回" }.All(generic.ContainsKey) &&
            !originals.ContainsKey("1234") && !originals.ContainsKey("word") && !generic.ContainsKey("切片"), "原版采集分类：完整原文仅写 orig，分段写 generic，并过滤已知文本和纯数字");
        Check(Interop.ReadDictionary(Path.Combine(root, "generic.fmt.json")).ContainsKey("第{0}回<b>完成</b>") &&
            Interop.ReadDictionary(Path.Combine(root, "generic_orig_1.json")).ContainsKey("分组文本") &&
            Interop.ReadDictionary(Path.Combine(root, "localization.json"))["button"] == "原始按钮", "分类词典与分组导出");
    }

    private static async Task Update(string data, string packages)
    {
        using var contents = new MemoryStream();
        using (var archive = new ZipArchive(contents, ZipArchiveMode.Create, true))
        { using var writer = new StreamWriter(archive.CreateEntry("local-files/localization.json").Open()); writer.Write("{\"key\":\"网络更新\"}"); }
        var zip = contents.ToArray();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var first = await reader.ReadLineAsync(); while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    var payload = first!.Contains("/pack.zip") ? zip : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    { tag_name = "offline-v2", body = "fixture release notes", assets = new[] { new {name = "GakumasTranslationData.zip", browser_download_url = url + "/pack.zip"} } }));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(payload);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            var package = Catalog.Discover(packages, _ => { })["localization"];
            var release = await PluginHost.CheckResourcesAsync(package, data, url + "/release");
            Check(release.Version == "offline-v2" && release.Description == "fixture release notes" && !release.IsCurrent, "读取更新版本和发布说明");
            var progress = new ProgressRecorder();
            Check(await PluginHost.InstallResourcesAsync(package, data, release, progress) == 1 && progress.Value?.Received == zip.Length && progress.Value.Total == zip.Length,
                "流式下载报告进度并安装");
            Check((await PluginHost.CheckResourcesAsync(package, data, url + "/release")).IsCurrent, "文本版本号随资源安装后正确比较");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await PluginHost.InstallResourcesAsync(package, data, release, cancellationToken: cancelled.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { }
            Check(File.ReadAllText(Path.Combine(package.Directory, "resources", "localization.json")).Contains("网络更新") &&
                !File.Exists(Path.Combine(package.Directory, "resources.edit")), "取消更新保留当前资源并释放写锁");
            var log = new List<string>();
            using var panel = new Gkms.Manager.ResourceUpdatePanel(package,Path.Combine(data,"re"),log.Add);
            panel.Check(url+"/release"); await panel.Completion; panel.ProcessUpdates();
            panel.Install(); await panel.Completion; panel.ProcessUpdates();
            Check(!panel.Busy && panel.Status.Contains("1 个资源文件已安装") &&
                log.Any(line=>line.Contains("资源安装成功")), "最后下载进度不能覆盖安装结果，成功写日志");
            var invalidZip = Path.Combine(data,"invalid-resources.zip");
            using (var invalid = ZipFile.Open(invalidZip,ZipArchiveMode.Create)) invalid.CreateEntry("unrelated.txt");
            panel.Check(invalidZip); await panel.Completion; panel.ProcessUpdates();
            panel.Install(); await panel.Completion; panel.ProcessUpdates();
            Check(!panel.Busy && panel.Status.Contains("localization.json") && log.Any(line=>line.Contains("资源操作失败")),
                "下载或选择完成后校验失败必须显示错误并记日志");
        }
        finally { stop.Cancel(); await server; listener.Stop(); }
    }
    private sealed class ProgressRecorder : IProgress<ResourceProgress>
    { public ResourceProgress? Value; public void Report(ResourceProgress value) => Value = value; }
}
