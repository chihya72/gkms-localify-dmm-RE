using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Gkms.Bridge;
using Gkms.Host;
using Gkms.Sdk;
using Gkms.Plugins.Camera;

internal static class DmmParityTests
{
    private static void Check(bool value, string source) { if (!value) throw new Exception("DMM source regression: " + source); }
    private static bool Close(float a, float b) => Math.Abs(a - b) < .0001f;
    public static void Run(string work, string sourcePackages)
    {
        var root = Directory.CreateDirectory(Path.Combine(work, "dmm-parity")).FullName;
        var packages = TestPackages.Copy(sourcePackages, root);
        TextRules(TestPackages.Resources(packages, "localization"));
        MessageBoundary(root, packages);
        CardSelection(root, packages);
        DisplayBoundaries(root, packages);
        TextureCandidates(root, packages);
        CameraFormulas();
        IcallCache();
        Console.WriteLine("原版出处回归通过：词典/主表/完整消息边界、卡牌、画质与帧率顺序、拍照与 SP、贴图候选与停用、镜头公式、icall 缓存恢复。此结果不代表实机通过。");
    }
    private static void DisplayBoundaries(string root, string packages)
    {
        // Hook.cpp CampusQualityManager_ApplySetting: FPS -> LOD -> reflection -> original.
        var profile = Path.Combine(root, "display.json");
        File.WriteAllText(profile, """{"enabled":["graphics","frame-rate","auto-photo","schedule-sp"],"settings":{"graphics":{"reflectionQuality":99,"lodQuality":-1},"frame-rate":{"fps":90}}}""");
        var quality = new Campus.Common.CampusQualityManager();
        var previousChoices = Campus.Live.LiveUtility.Choices;
        var log = new List<string>();
        using (var host = new PluginHost(new TestGame(), log.Add))
        {
            host.Start(packages, profile, root);
            Check(host.Active.Count == 4, string.Join("\n", log));
            quality.Trace.Clear(); quality.ApplySetting(1, 10, 1, 0);
            Check(quality.Trace.SequenceEqual(new[] { "fps", "lod", "reflection", "apply" }), "ApplySetting source ordering");
            Check(quality.Lod == Campus.Common.QualityLevel.Highest && quality.Reflection == Campus.Common.QualityLevel.Highest, "size_t comparison clamps both positive and negative out-of-range indices");
            quality.SetLODQuality(Campus.Common.QualityLevel.Low); quality.SetReflectionQuality(Campus.Common.QualityLevel.Middle);
            Check(quality.Lod == Campus.Common.QualityLevel.Low && quality.Reflection == Campus.Common.QualityLevel.Middle, "independent quality setters pass through");
            var limited = VL.VLUtility.GetLimitedResolution(0, -10, default, 100, 1, false);
            Check(limited.x == 0 && limited.y == -10, "GetLimitedResolution does not multiply when renderScale <= 1");
            foreach (var pair in new[] { (1,30), (3,30), (5,20), (10,10), (99,99), (0,0) })
            { Campus.Live.LiveUtility.Limit = pair.Item1; Check(Campus.Live.LiveUtility.GetAutoPhotoLimitCount() == pair.Item2, "AutoPhoto original limit switch"); }
            Campus.Live.LiveUtility.Choices = new[] { 1,3,5,99 };
            Check(Campus.Live.LiveUtility.AutoPhotoCountTypeValues().SequenceEqual(new[] {10,20,30,99}), "AutoPhoto only replaces first three elements");
            var editedChoices = Campus.Live.LiveUtility.Choices;
            var loaded = log.Count(s => s.EndsWith(": 已加载。"));
            File.WriteAllText(profile, """{"enabled":["graphics","frame-rate","auto-photo","schedule-sp"],"settings":{"graphics":{"reflectionQuality":99,"lodQuality":-1},"frame-rate":{"fps":90},"auto-photo":{"counts":[12,24,36]}}}"""); host.ReloadProfile();
            Campus.Live.LiveUtility.Limit = 3;
            Check(editedChoices.SequenceEqual(new[] {12,24,36,99}) && Campus.Live.LiveUtility.GetAutoPhotoLimitCount() == 36 &&
                loaded == log.Count(s => s.EndsWith(": 已加载。")), "AutoPhoto hot settings update existing choices and callbacks without restarting");
            Campus.Live.LiveUtility.Choices = new[] { 1,3 };
            Check(Campus.Live.LiveUtility.AutoPhotoCountTypeValues().SequenceEqual(new[] {1,3}), "AutoPhoto short array remains unchanged");
            Campus.Live.LiveUtility.Choices = null!;
            Check(Campus.Live.LiveUtility.AutoPhotoCountTypeValues() == null, "AutoPhoto null remains null");
            foreach (var value in new[] {0,1,99,-1})
                Check((int)Campus.InGame.ExamExtensions.ExchangeSpToNormal((Campus.Common.Proto.Client.Enums.ProduceStepType)value) == value, "SP hook preserves even unknown enum values");
            File.WriteAllText(profile, """{"enabled":["frame-rate"],"settings":{"frame-rate":{"fps":0}}}"""); host.ReloadProfile();
            quality.TargetFrameRate = 37; UnityEngine.Application.targetFrameRate = 19; quality.ApplySetting(1,10,1,0);
            Check(quality.TargetFrameRate == 37 && UnityEngine.Application.targetFrameRate == 19, "FPS zero follows both original setters");
        }
        Campus.Live.LiveUtility.Choices = previousChoices; Campus.Live.LiveUtility.Limit = 3;
    }
    private static void TextureCandidates(string root, string packages)
    {
        // HookTexture.cpp GetNamedTextureCandidates / ApplyLocalImageToTexture2DFromCandidates.
        var resources = TestPackages.Resources(packages, "textures");
        var image = Directory.CreateDirectory(Path.Combine(resources, "image")).FullName;
        var others = Directory.CreateDirectory(Path.Combine(resources, "others")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(image, "nested")).FullName;
        File.WriteAllBytes(Path.Combine(image, "img_test"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(image, "img_test.png"), new byte[] {42});
        File.WriteAllBytes(Path.Combine(others, "img_test.png"), new byte[] {99});
        File.WriteAllBytes(Path.Combine(others, "sactx_test.png"), new byte[] {99});
        File.WriteAllBytes(Path.Combine(nested, "img_nested.png"), new byte[] {43});
        File.WriteAllBytes(Path.Combine(image, "img_duplicate.png"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(nested, "img_duplicate.png"), new byte[] {45});
        var profile = Path.Combine(root, "textures.json"); File.WriteAllText(profile, """{"enabled":["textures"]}""");
        var texture = new UnityEngine.Texture2D(8,8) {name="img_test", hideFlags=4};
        var other = new UnityEngine.Texture2D(8,8) {name="sactx_test"};
        var recursive = new UnityEngine.Texture2D(8,8) {name="img_nested"};
        var duplicate = new UnityEngine.Texture2D(8,8) {name="img_duplicate"};
        var duplicateWithExtension = new UnityEngine.Texture2D(8,8) {name="img_duplicate.png"};
        var original = texture.Pixels.ToArray(); var canvas = new UnityEngine.CanvasRenderer();
        using (var host = new PluginHost(new TestGame(), _ => {}))
        {
            host.Start(packages, profile, root); Check(host.Active.Contains("textures"), "texture plugin loads");
            canvas.SetTexture(texture); canvas.SetTexture(other); canvas.SetTexture(recursive);
            canvas.SetTexture(duplicate); canvas.SetTexture(duplicateWithExtension);
            Check(duplicate.Pixels.SequenceEqual(new byte[] {45}) && duplicateWithExtension.Pixels.SequenceEqual(new byte[] {45}),
                "all same-name PNG candidates retained: corrupt first file falls through, with or without extension");
            Check(texture.Pixels.SequenceEqual(new byte[] {42}) && texture.hideFlags == 36, "failed direct candidate falls through to PNG and sets DontUnloadUnusedAsset");
            Check(other.width == 8 && recursive.Pixels.SequenceEqual(new byte[] {43}), "category isolation and recursive same-category lookup");
            File.WriteAllBytes(Path.Combine(image, "img_test"), new byte[] {44}); host.ReloadResources("textures");
            Check(texture.Pixels.SequenceEqual(new byte[] {44}), "reloaded direct candidate has original priority");
            Check(duplicate.Pixels.SequenceEqual(new byte[] {45}), "resource reload retains same-name fallback candidates");
            File.WriteAllBytes(Path.Combine(image, "img_duplicate.png"), new byte[] {46}); host.ReloadResources("textures");
            Check(duplicate.Pixels.SequenceEqual(new byte[] {46}) && duplicateWithExtension.Pixels.SequenceEqual(new byte[] {46}),
                "fixed first candidate regains priority on reload");
        }
        Check(texture.Pixels.SequenceEqual(original) && texture.width == 8 && texture.hideFlags == 4, "hot disable restores original pixels and flags on same object");
        Check(duplicate.width == 8 && duplicateWithExtension.width == 8, "same-name replacements also restore original textures on disable");
    }
    private static void TextRules(string root)
    {
        Directory.CreateDirectory(root);
        var generic = Directory.CreateDirectory(Path.Combine(root, "genericTrans")).FullName;
        var master = Directory.CreateDirectory(Path.Combine(root, "masterTrans")).FullName;
        var story = Directory.CreateDirectory(Path.Combine(root, "resource")).FullName;
        File.WriteAllText(Path.Combine(root, "localization.json"), """{"pass":"通票","protected":"次回 2"}""");
        File.WriteAllText(Path.Combine(root, "generic.json"), """{"パス":"通行证","通票":"精确覆盖","カード":"卡","回数<color=red>1</color>回":"次数<color=red>1</color>次","[__split__]lost":"[__split__]失"}""");
        File.WriteAllText(Path.Combine(root, "generic.split.json"), """{"[__split__]次回":"[__split__]下次","[__split__]報酬":"[__split__]奖励"}""");
        File.WriteAllText(Path.Combine(generic, "nested.split.json"), """{"単語":"词语"}""");
        File.WriteAllText(Path.Combine(generic, "number.fmt.json"), """{"第{0}回":"第{0}次","章{0}":"字{5}","値<color={0}>{1}</color>":"值<color={0}>{1}</color>","A{0}B{1}":"x{} y{}","C{0}":"{{{0:*>5.2s}}}","D{0}":"{0:03d}","E{0}F{1}":"{} {1}","G{0}":"{0:?}","H{0}":"{0:^6s}"}""");
        File.WriteAllText(Path.Combine(story, "story.txt"), "[message text=<r\\=劇中専用>仅限剧情</r>]");
        File.WriteAllText(Path.Combine(master, "Card.json"), """{"rules":{"primaryKeys":["id"]},"data":[{"id":"p","name":"次回 3"}]}""");
        File.WriteAllText(Path.Combine(generic, "srt_live_regression.json"), JsonSerializer.Serialize(new Dictionary<string,string> {
            ["歌詞\r\n"] = "歌詞\n歌词\r\n", ["二行\r\n次行\r\n"] = "二行\n次行\n翻译\r\n" }));
        var dictionary = new DmmTextDictionary(root);
        Check(dictionary.Translate("歌詞\r\n") == "歌詞\n歌词\r\n" && dictionary.Translate("歌詞") == "歌詞" &&
            dictionary.Translate("二行\n次行") == "二行\n次行", "DMM matches the exact SRT key without injecting newline aliases");
        // Local.cpp GetGenericText: exact -> translated -> trailing '+' -> number format -> split.
        Check(dictionary.Translate("通票") == "精确覆盖", "GetGenericText exact lookup precedes the translated set");
        Check(dictionary.Translate("次回 2") == "次回 2" && dictionary.Translate("次回 3") == "次回 3", "I18n/master values enter translatedText");
        Check(dictionary.Translate("カード++") == "卡++", "ASCII trailing card upgrade suffix");
        Check(dictionary.Translate("A12B34") == "x12 y34" && dictionary.Translate("C1234") == "{***12}" &&
            dictionary.Translate("D12") == "{0:03d}" && dictionary.Translate("E12F34") == "{} {1}" &&
            dictionary.Translate("G12") == "\"12\"" && dictionary.Translate("H＋1") == " ＋1  ",
            "Misc.cpp fmt string arguments: automatic indices, escaped braces, fill/precision, invalid numeric/mixed indices, debug and wide signs");
        Check(dictionary.Translate("次回") == "次回" && dictionary.Translate("2次回") == "2下次", "GetSplitTagsTranslationFull NO_SPLIT boundary");
        Check(dictionary.Translate("次回,報酬") == "次回,報酬" && dictionary.Translate("2次回,報酬") == "2次回,報酬", "comma is not a split delimiter");
        Check(dictionary.Translate("<b>パス</b>") == "<b>パス</b>", "split fragments never fall back to genericText");
        Check(dictionary.Translate("単語") == "词语" && dictionary.Translate("2単語") == "2词语", "LoadData recursive .split.json also loads into exact");
        Check(dictionary.Translate("<b>lost</b>") == "<b>lost</b>", "LoadData clears split entries after generic.json");
        Check(dictionary.Translate("回数") == "次数", "ProcessGenericTextLabels surrounding numeric tag text");
        Check(dictionary.Translate("値<color=2>4</color>") == "值<color=2>4</color>", "StringParser parse(false) tokenizes numbers inside tags");
        Check(dictionary.Translate("章3") == "字{5}", "Misc::stringFormatString preserves an invalid template literally");
        Check(dictionary.Translate("劇中専用") == "劇中専用", "Octo script contents do not become global dictionary entries");
        Check(dictionary.Missing("未知").SequenceEqual(new[] { ("orig", "未知") }), "plain unknown text goes to generic_orig");
        Check(!dictionary.Missing("第12回").Any() && dictionary.Missing("2未知").Contains(("split", "未知")), "capture uses the original translation hit/miss order");
        Check(TextureNames.Category("img_banner") == "image" && TextureNames.Category("sactx-a") == "atlas" && TextureNames.Category("atlas") == "others", "HookTexture category prefixes");
        Check(TextureNames.Portable("sactx-a-128x128-hash-ui-home-1234abcd.png") == "sactx-a-128x128-ui-home" && TextureNames.Portable("sactx-a-wide-hash-ui-1234abcd") == null, "GetPortableSactxTextureName removes only defined segments");
        Check(TextureNames.DumpName("a|b. ") == "a_b._" && TextureNames.DumpName(null) == "texture", "SanitizeDumpPathPart and unnamed texture fallback");
    }
    private static void MessageBoundary(string root, string packages)
    {
        var file = Path.Combine(TestPackages.Resources(packages, "localization"), "masterTrans", "Card.json");
        File.WriteAllText(file, """{"rules":{"primaryKeys":["id","rank","parts.kind"]},"data":[{"id":"p","rank":1,"name":"首译","parts":[{"kind":"PartKind_Second","text":"首项"}]},{"id":"p","rank":1,"name":"后译","parts":[{"kind":"PartKind_First","text":"后项"}]}]}""");
        var profile = Path.Combine(root, "profile.json");
        File.WriteAllText(profile, """{"enabled":["localization","ios-login"]}""");
        var log = new List<string>(); var card = new Card { Name = "原文" };
        var late = new Campus.Common.Proto.Client.Master.LateMaster();
        using (var host = new PluginHost(new TestGame(), log.Add))
        {
            host.Start(packages, profile, root);
            late.Read();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!, "LateMaster.json"), """{"rules":{"primaryKeys":["id"]},"data":[{"id":"late","name":"下载后热启用译文"}]}""");
            File.WriteAllText(profile, """{"enabled":["localization","ios-login"]}"""); host.ReloadProfile();
            Check(late.Name == "后来下载资源的主表原文", "new resource files do not retroactively translate parsed objects");
            Check(host.Active.Count == 2 && !log.Any(l => l.Contains("主数据翻译 加载失败")), string.Join("\n", log));
            card.InternalMergeFrom();
            Check(card.Name == "原文", "Hook.cpp does not translate partial InternalMergeFrom calls");
            card.Read();
            Check(card.Name == "首译" && card.Parts[0].Text == "首项" && card.Parts[1].Text == "", "MasterLocal emplace first field wins, child arrays use index");
            card.Parse = c => c.Name = "新服务器原文"; card.Read();
            Check(card.Name == "首译", "complete message is translated after real parsing");
            var auth = new Campus.Common.Proto.Client.Api.AuthLoginResponse { GameAuthToken = "offline-fixture" };
            auth.InternalMergeFrom();
            Check(!Firebase.Platform.PlatformInformation.IsIOS, "partial auth read cannot establish DMM authentication");
            auth.Read(); Check(Firebase.Platform.PlatformInformation.IsIOS, "complete AuthLoginResponse activates iOS");
            new Campus.Common.Proto.Client.Api.AuthLoginResponse().Read();
            Check(!Firebase.Platform.PlatformInformation.IsIOS, "empty auth response clears the authenticated flag");
        }
        Check(card.Name == "首译" && card.Parts[0].Text == "首项" && late.Name == "后来下载资源的主表原文", "shutdown removes hooks without restoring translated game data");
    }
    private static void CardSelection(string root, string packages)
    {
        var table = Campus.Common.Master.MasterManager.ProduceCardSkinMaster;
        var saved = table.Items; table.Items = new() { null! }; table.Reads = 0;
        var profile = Path.Combine(root, "profile.json"); File.WriteAllText(profile, """{"enabled":["card-cut-in"]}""");
        var card = new Campus.InGame.Card.ExamCardData();
        using (var host = new PluginHost(new TestGame(), _ => { }))
        {
            host.Start(packages, profile, root);
            Check(card.ProduceCardSkinId == "" && card.ProduceCardSkinId == "" && table.Reads == 2, "Hook.cpp all-null master retries rather than marking the cache ready");
            table.Items = new() { new("first", "card", 3), new("last", "card", 3), new("zero", "card", 0) };
            Check(card.ProduceCardSkinId == "first", "strict greater-than retains first maximum cut-in");
            card.Selection = "manual"; Check(card.ProduceCardSkinId == "manual", "manual skin selection wins");
        }
        table.Items = saved;
    }
    private static void CameraFormulas()
    {
        var orbit = CameraMath.Orbit(new(1,2,3), new(90,1,2));
        Check(Close(orbit.X,3) && Close(orbit.Y,3) && Close(orbit.Z,3), "camera.cpp CalcPositionFromLookAt world orbit");
        var first = CameraMath.FirstPerson(new(1,2,3), Vector3.UnitZ, new(.5f,.064f,.25f));
        Check(Close(first.X,1.5f) && Close(first.Y,2.064f) && Close(first.Z,2.75f), "CalcFirstPersonPosition cross product signs and world height");
        Check(CameraMath.FirstPerson(new(1,2,3), Vector3.UnitY, new(9,1,9)) == new Vector3(1,3,3), "vertical forward uses height-only fallback");
        Check(!CameraMath.Look(Vector3.Zero,out _) && !CameraMath.Look(Vector3.UnitY,out _) && CameraMath.Look(-Vector3.UnitZ,out var q) && Close(Math.Abs(q.Y),1), "TryLookRotationQuat degenerate and backwards directions");
        var height = new CameraMath.Height();
        Check(Close(height.Apply(10,true,true),10) && Close(height.Apply(20,true,true),10.625f) && Close(height.Apply(20,false,true),11.171875f), "CheckNewY pre-push comparison and post-push average");
        Check(Close(height.Apply(float.NaN,true,true),11.171875f) && Close(height.Apply(27,false,false),27), "CheckNewY finite guard and instant mode");
    }
    internal static void IcallCache()
    {
        // Hook.cpp GyroIcall::Patch: mov rax,[rip+disp]; test rax,rax.
        // This private allocation contains a scanner fixture, never game memory.
        var buffer = Marshal.AllocHGlobal(256);
        IcallBoolean original = () => 0, replacement = () => 1;
        var originalPointer = Marshal.GetFunctionPointerForDelegate(original);
        var cache = buffer + 128;
        try
        {
            Marshal.Copy(new byte[256],0,buffer,256);
            Marshal.Copy(new byte[] {0x48,0x8b,0x05,121,0,0,0,0x48,0x85,0xc0},0,buffer,10);
            Marshal.WriteIntPtr(cache, originalPointer);
            Check(NativeIcallCache.Locate(buffer) == cache, "RIP-relative icall cache location");
            IntPtr thunk;
            using (NativeIcallCache.Replace(buffer, originalPointer, replacement))
            {
                thunk = Marshal.ReadIntPtr(cache);
                Check(Marshal.GetDelegateForFunctionPointer<IcallBoolean>(thunk)() == 1, "SDK callback bool ABI through permanent bridge thunk");
                try { using var duplicate = NativeIcallCache.Replace(buffer,originalPointer,replacement); throw new Exception("duplicate cache lease accepted"); }
                catch (InvalidOperationException) { }
            }
            Check(Marshal.ReadIntPtr(cache) == originalPointer && Marshal.GetDelegateForFunctionPointer<IcallBoolean>(thunk)() == 0, "dispose restores cache and in-flight thunk falls back to original");
            Marshal.WriteByte(buffer, 0, 0x90);
            try { NativeIcallCache.Locate(buffer); throw new Exception("invalid code pattern accepted"); }
            catch (NotSupportedException) { }
        }
        finally { Marshal.FreeHGlobal(buffer); GC.KeepAlive(original); GC.KeepAlive(replacement); }
    }
}
