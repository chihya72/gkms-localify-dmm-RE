using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Host;

/// <summary>Mandatory, session-long, in-place DMM font replacement. No resource-reload interface.</summary>
internal sealed class BuiltinFont : ILocalifyPlugin, IPluginDiagnostics
{
    private const string ResourceName = "Gkms.Host.BuiltinFont.bundle";
    private static BuiltinFont? current;
    private readonly HashSet<string> warnings = new();
    // Like DMM, key by native address AND Unity instance ID, without keeping every
    // game font alive through a GC handle. Values remember the source Font ID.
    private readonly Dictionary<(IntPtr Pointer, int Id), int> updated = new();
    private IPluginContext context = null!;
    private MethodInfo loadBundle = null!, loadAsset = null!, unload = null!, alive = null!, instanceId = null!, changed = null!;
    private object? bundle, source, nativeFontType;
    private byte[] bytes = Array.Empty<byte>();
    private bool applying;
    public IReadOnlyList<string> Warnings => warnings.ToArray();

    public void Start(IPluginContext context)
    {
        this.context = context;
        const string ab = "UnityEngine.AssetBundleModule", unity = "UnityEngine.CoreModule", tmp = "Unity.TextMeshPro";
        loadBundle = context.RequireType(ab, "UnityEngine.AssetBundle").GetMethods(GameMethods.Declared).Single(m =>
            m.Name == "LoadFromMemory" && m.GetParameters() is [{ ParameterType: var buffer }] && Interop.IsByteArray(buffer));
        loadAsset = context.Require(ab, "UnityEngine.AssetBundle", "LoadAsset", "System.String", "System.Type");
        unload = context.Require(ab, "UnityEngine.AssetBundle", "Unload", "System.Boolean");
        alive = context.Require(unity, "UnityEngine.Object", "op_Implicit", "UnityEngine.Object");
        instanceId = context.Require(unity, "UnityEngine.Object", "GetInstanceID");
        changed = context.Require(tmp, "TMPro.TMPro_EventManager", "ON_FONT_PROPERTY_CHANGED", "System.Boolean", "UnityEngine.Object");
        nativeFontType = Interop.NativeTypeObject(context.RequireType("UnityEngine.TextRenderingModule", "UnityEngine.Font"));
        using (var stream = typeof(BuiltinFont).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException("框架缺少内置补全字体。", ResourceName))
        using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); bytes = buffer.ToArray(); }
        EnsureSource();
        current = this;
        GameTextFont.ApplyFont = Apply;
        void Bind(MethodInfo? target, bool before = false)
        {
            if (target == null) return;
            // The translator retains DMM's matched-before / unmatched-after timing.
            // These host hooks cover every corresponding entry when it is absent.
            context.Patch(target, before ? GameMethods.Handler<BuiltinFont>(nameof(WithoutTranslator)) : null,
                before ? null : GameMethods.Handler<BuiltinFont>(nameof(WithoutTranslator)), 0);
        }
        Bind(context.Find(tmp, "TMPro.TMP_Text", "set_text", "System.String"));
        Bind(context.Find(tmp, "TMPro.TMP_Text", "SetText", "System.String"));
        Bind(context.Find(tmp, "TMPro.TMP_Text", "SetText", "System.String", "System.Boolean"));
        Bind(context.Find(tmp, "TMPro.TMP_Text", "PopulateTextBackingArray", "System.String", "System.Int32", "System.Int32"));
        Bind(context.FindType(tmp, "TMPro.TMP_Text")?.GetMethods(GameMethods.Declared).SingleOrDefault(m =>
            m.Name == "SetCharArray" && m.GetParameters() is [{ ParameterType: var array }, { ParameterType: var a }, { ParameterType: var b }]
            && (array.IsArray && array.GetElementType() == typeof(char) || array.IsGenericType && array.GetGenericArguments().SequenceEqual(new[] { typeof(char) }))
            && a == typeof(int) && b == typeof(int)));
        Bind(context.Find(tmp, "TMPro.TextMeshProUGUI", "Awake"), true);
        var find = context.Find(unity, "UnityEngine.Object", "FindObjectsOfType", "System.Type", "System.Boolean");
        var textType = context.RequireType(tmp, "TMPro.TMP_Text");
        if (find != null)
            foreach (var text in Interop.Items(find.Invoke(null, new[] { Interop.NativeTypeObject(textType), true })).ToArray()) Apply(text);
        context.Log("内置字体已启用：补全版 31723 字符，按原 DMM 原地替换；保留 CampusAlphanumeric 数字字体；不随汉化启停，不支持字体热重载。");
    }

    public static void WithoutTranslator(object __instance)
    { if (!GameTextFont.TranslationHooksActive) current?.Apply(__instance); }

    private bool Alive(object? value) => value != null && alive.Invoke(null, new[] { value }) is true;
    private int Id(object value) => (int)instanceId.Invoke(value, null)!;
    private void EnsureSource()
    {
        if (Alive(source)) return;
        if (!Alive(bundle)) bundle = loadBundle.Invoke(null,
            new[] { Interop.WrapBytes(loadBundle.GetParameters()[0].ParameterType, bytes) })
            ?? throw new IOException("内置字体包加载失败。");
        source = loadAsset.Invoke(bundle, new[] { "assets/fonts/gkamszhfontmix.otf", nativeFontType });
        if (!Alive(source)) throw new IOException("内置字体包缺少有效的补全字体。");
        context.Log("已取得框架内置补全字体（固定资源，不读取插件字体文件）。");
    }

    private void Apply(object text)
    {
        if (applying) return;
        try
        {
            if (!Alive(text)) return;
            var asset = Interop.Get(text, "font");
            if (!Alive(asset)) return;
            var name = Interop.Get(asset!, "name")?.ToString() ?? "";
            if (name.Contains("campusalphanumeric", StringComparison.OrdinalIgnoreCase)) return;
            EnsureSource();
            var key = (Interop.PointerOf(asset!), Id(asset!));
            var sourceId = Id(source!);
            var isNew = !updated.TryGetValue(key, out var previousSource);
            applying = true;
            try
            {
                if (isNew || previousSource != sourceId)
                {
                    Interop.Set(asset!, "sourceFontFile", source);
                    updated[key] = sourceId;
                }
                if (isNew)
                {
                    // Preserve FaceInfo, sampling, weights, fallbacks, materials and
                    // every consumer's original asset reference, exactly as DMM.
                    Interop.Set(asset!, "atlasPopulationMode", 1);
                    Interop.Set(asset!, "isMultiAtlasTexturesEnabled", true);
                    if (Convert.ToInt32(Interop.Get(asset!, "atlasWidth")) < 4096)
                    { Interop.Set(asset!, "atlasWidth", 4096); Interop.Set(asset!, "atlasHeight", 4096); }
                    Interop.Call(asset!, "ClearFontAssetData", false);
                    changed.Invoke(null, new object[] { true, asset! });
                }
                if (Convert.ToInt32(Interop.Get(asset!, "atlasTextureCount")) > 1)
                    Warn("字体图集已溢出第 0 页，游戏艺术字渲染可能缺字。");
                Interop.Set(text, "font", asset);
            }
            catch { if (isNew) updated.Remove(key); throw; }
            finally { applying = false; }
        }
        catch (Exception error) { Warn("内置字体替换失败: " + error.GetBaseException().Message); }
    }
    private void Warn(string message) { if (warnings.Add(message)) context.Log(message); }

    public void Dispose()
    {
        // Only framework shutdown reaches here. Never restore game fonts or
        // destroy a source Font still referenced by their in-place assets.
        if (ReferenceEquals(current, this)) { current = null; GameTextFont.ApplyFont = null; }
        if (bundle != null && Alive(bundle)) unload.Invoke(bundle, new object[] { false });
        bundle = source = null;
        bytes = Array.Empty<byte>(); updated.Clear(); warnings.Clear();
    }
}
