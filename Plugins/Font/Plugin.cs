using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Font;

public sealed class Plugin : ILocalifyPlugin
{
    private static MethodInfo loadBundle = null!, loadAsset = null!, create = null!, unload = null!, destroy = null!, alive = null!;
    private static PropertyInfo fallback = null!;
    private static string path = "";
    private static object? bundle, fontAsset, nativeFontType;
    private static IPluginContext? context;
    private static bool failed;

    public void Start(IPluginContext session)
    {
        context = session;
        path = Path.Combine(session.DataDirectory, "local-files", "gakumasassets");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到旧版字体 AssetBundle", path);
        const string ab = "UnityEngine.AssetBundleModule", type = "UnityEngine.AssetBundle";
        loadBundle = session.RequireType(ab, type).GetMethods(GameMethods.Declared).Single(m =>
            m.Name == "LoadFromMemory" && m.GetParameters() is [{ ParameterType: var buffer }] && Interop.IsByteArray(buffer));
        loadAsset = session.Require(ab, type, "LoadAsset", "System.String", "Il2CppSystem.Type");
        var typeFrom = session.Require("Il2CppInterop.Runtime", "Il2CppInterop.Runtime.Il2CppType", "From", "System.Type");
        var unityFont = session.RequireType("UnityEngine.TextRenderingModule", "UnityEngine.Font");
        nativeFontType = typeFrom.Invoke(null, new object[] { unityFont });
        unload = session.Require(ab, type, "Unload", "System.Boolean");
        destroy = session.Require("UnityEngine.CoreModule", "UnityEngine.Object", "Destroy", "UnityEngine.Object");
        alive = session.Require("UnityEngine.CoreModule", "UnityEngine.Object", "op_Implicit", "UnityEngine.Object");
        create = session.Require("Unity.TextMeshPro", "TMPro.TMP_FontAsset", "CreateFontAsset", "UnityEngine.Font");
        fallback = session.RequireType("Unity.TextMeshPro", "TMPro.TMP_Settings").GetProperty("fallbackFontAssets")
            ?? throw new MissingMemberException("TMP_Settings.fallbackFontAssets");
        session.Patch(session.Require("Unity.TextMeshPro", "TMPro.TextMeshProUGUI", "Awake"),
            GameMethods.Handler<Plugin>(nameof(EnsureFont)));
    }

    public static void EnsureFont()
    {
        if (failed) return;
        try
        {
            var list = fallback.GetValue(null);
            if (list == null) return;
            if (fontAsset == null || !(bool)alive.Invoke(null, new[] { fontAsset })!)
            {
                bundle ??= loadBundle.Invoke(null, new[] { Interop.WrapBytes(loadBundle.GetParameters()[0].ParameterType, File.ReadAllBytes(path)) })
                    ?? throw new IOException("字体包加载失败。");
                var asset = loadAsset.Invoke(bundle, new[] { "assets/fonts/gkamszhfontmix.otf", nativeFontType })
                    ?? throw new IOException("字体包内缺少 gkamszhfontmix.otf。");
                var fontType = create.GetParameters()[0].ParameterType;
                if (!fontType.IsInstanceOfType(asset))
                {
                    var cast = asset.GetType().GetMethods().Single(m => m.Name == "Cast" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                    asset = cast.MakeGenericMethod(fontType).Invoke(asset, null)!;
                }
                fontAsset = create.Invoke(null, new[] { asset }) ?? throw new IOException("TMP 字体创建失败。");
            }
            var type = list.GetType();
            if (!(bool)type.GetMethod("Contains")!.Invoke(list, new[] { fontAsset })!)
                type.GetMethod("Add")!.Invoke(list, new[] { fontAsset });
        }
        catch (Exception e) { failed = true; context?.Log(e.GetBaseException().Message); }
    }

    public void Dispose()
    {
        if (fontAsset != null)
        {
            var list = fallback.GetValue(null);
            list?.GetType().GetMethod("Remove")?.Invoke(list, new[] { fontAsset });
            destroy.Invoke(null, new[] { fontAsset });
        }
        if (bundle != null) unload.Invoke(bundle, new object[] { false });
        fontAsset = null; bundle = null; context = null;
    }
}
