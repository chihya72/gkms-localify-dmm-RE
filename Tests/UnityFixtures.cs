using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public static class Application
    {
        private static int rate = 30;
        public static int targetFrameRate
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get => rate;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set => rate = value;
        }
    }
    public partial class Object
    {
        public string name { get; set; } = "";
        public int hideFlags { get; set; }
        public bool Destroyed { get; private set; }
        public static implicit operator bool(Object? obj) => obj != null && !obj.Destroyed;
        public int GetInstanceID() => RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value) => value.Destroyed = true;
    }
    public class Font : Object { }
    public class AssetBundle : Object
    {
        private static bool loaded;
        public static bool ReuseSourceFont;
        public static int Loads;
        private static Font? sharedFont;
        public static AssetBundle? LoadFromMemory(byte[] data)
        {
            if (loaded) throw new IOException("another bundle with the same CAB is already loaded");
            if (data.Length == 0) return null;
            loaded = true; Loads++; return new();
        }
        public T LoadAsset<T>(string name) where T : Object, new() => new();
        public Object LoadAsset(string name, System.Type type) => ReuseSourceFont && type == typeof(Font)
            ? sharedFont ??= new Font() : (Object)Activator.CreateInstance(type)!;
        public void Unload(bool destroy) => loaded = false;
    }
    public class Material : Object
    {
        private readonly Dictionary<string, float> properties = new() { ["_GradientScale"] = 9, ["_TextureWidth"] = 512, ["_TextureHeight"] = 1024 };
        public Texture? mainTexture { get; set; }
        public float Outline { get; set; }
        public Material() { }
        public Material(Material source) { mainTexture = source.mainTexture; Outline = source.Outline; properties = new(source.properties); }
        public bool HasProperty(string name) => properties.ContainsKey(name);
        public float GetFloat(string name) => properties[name];
        public void SetFloat(string name, float value) => properties[name] = value;
    }
    public class Texture : Object { public int filterMode { get; set; } public int wrapMode { get; set; } }
    public enum TextureFormat { RGBA32, DXT5, DXT5Crunched }
    public enum RenderTextureFormat { ARGB32 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public enum GraphicsFormat { R8G8B8A8_UNorm, R8G8B8A8_SRGB }
    public static class GL { public static bool sRGBWrite { get; set; } }
    public sealed unsafe class StrippedGL : System.Reflection.TypeDelegator, Gkms.Sdk.INativeInternalCalls
    {
        public static bool Available = true;
        public StrippedGL() : base(typeof(GL)) { }
        protected override System.Reflection.PropertyInfo? GetPropertyImpl(string name, System.Reflection.BindingFlags flags,
            System.Reflection.Binder? binder, Type? returnType, Type[]? types, System.Reflection.ParameterModifier[]? modifiers)
            => name == "sRGBWrite" ? null : base.GetPropertyImpl(name, flags, binder, returnType, types, modifiers);
        public IntPtr ResolveInternalCall(string signature) => !Available ? IntPtr.Zero : signature switch
        {
            "UnityEngine.GL::get_sRGBWrite()" => (IntPtr)(delegate* unmanaged[Cdecl]<byte>)&Get,
            "UnityEngine.GL::set_sRGBWrite(System.Boolean)" => (IntPtr)(delegate* unmanaged[Cdecl]<byte, void>)&Set,
            _ => IntPtr.Zero
        };
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte Get() => GL.sRGBWrite ? (byte)1 : (byte)0;
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Set(byte value) => GL.sRGBWrite = value != 0;
    }
    public class Texture2D : Texture
    {
        public int width { get; private set; }
        public int height { get; private set; }
        public byte[] Pixels = new byte[] { 137, 80, 78, 71 };
        public TextureFormat format { get; private set; } = TextureFormat.RGBA32;
        public int mipmapCount { get; private set; } = 1;
        public bool isReadable { get; set; } = true;
        private readonly bool srgb = true;
        public GraphicsFormat graphicsFormat => srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
        public Texture2D(int width, int height) => (this.width, this.height) = (width, height);
        public Texture2D(int width, int height, TextureFormat format, bool mipmaps, bool linear) : this(width, height)
        { this.format = format; mipmapCount = mipmaps ? 4 : 1; srgb = !linear; }
        public bool Reinitialize(int width, int height, TextureFormat format, bool mipmaps)
        { if (!isReadable) { NonReadableReinitializations++; return false; } if (format == TextureFormat.DXT5Crunched) return false; this.width = width; this.height = height; this.format = format; mipmapCount = mipmaps ? 4 : 1; Pixels = Array.Empty<byte>(); return true; }
        public static int NonReadableReinitializations;
        public static Texture2D? FailRawRestoreOnce;
        public void DecodeImage(int width, int height, byte[] bytes, bool mark)
        {
            this.width = width; this.height = height; Pixels = bytes.ToArray();
            if (format != TextureFormat.DXT5) format = TextureFormat.RGBA32;
            // Match imported Unity textures: false does not re-enable Read/Write.
            if (mark) isReadable = false;
        }
        public byte[] GetRawTextureData() => isReadable ? Pixels.ToArray() : throw new InvalidOperationException("not readable");
        public void LoadRawTextureData(byte[] bytes)
        {
            if (this == FailRawRestoreOnce) { FailRawRestoreOnce = null; throw new IOException("raw restore failed after Reinitialize"); }
            Pixels = bytes.ToArray();
        }
        public void ReadPixels(Rect rectangle, int x, int y, bool mipmaps) => Pixels = RenderTexture.active!.Pixels.ToArray();
        public void Apply(bool mipmaps, bool unreadable) { if (unreadable) isReadable = false; }
    }
    public class RenderTexture : Texture
    {
        public static RenderTexture? active { get; set; }
        public byte[] Pixels = Array.Empty<byte>();
        public static RenderTexture GetTemporary(int width, int height) => new();
        public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite) => new();
        public static void ReleaseTemporary(RenderTexture texture) { }
    }
    public static class Graphics
    {
        public static void Blit(Texture texture, RenderTexture target) => target.Pixels = ((Texture2D)texture).Pixels.ToArray();
        public static void CopyTexture(Texture source, Texture target) => ((Texture2D)target).Pixels = ((Texture2D)source).Pixels.ToArray();
    }
    public static class DebugLogHandler
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Internal_Log(int level, int options, string message, Object? context) { }
    }
    public static class ImageConversion
    {
        public static bool FailNextEncode;
        public static Texture2D? FailRestoreOnce;
        public static Texture2D? FailLoadAfterMutationOnce;
        public static object EncodeToPNG(Texture2D texture)
        {
            if (FailNextEncode) { FailNextEncode = false; throw new IOException("temporary readback failure"); }
            // Like NativeArray, this implements non-generic IEnumerable, not IEnumerable<byte>.
            if (!texture.isReadable) throw new InvalidOperationException("not readable");
            return new System.Collections.ArrayList(new byte[] { 137,80,78,71,71,75,77,83 }
                .Concat(BitConverter.GetBytes(texture.width)).Concat(BitConverter.GetBytes(texture.height)).Concat(texture.Pixels).ToArray());
        }
        public static bool LoadImage(Texture2D texture, byte[] bytes, bool mark)
        {
            if (bytes.Length == 0) return false;
            var encoded = bytes.Length >= 16 && bytes.Take(8).SequenceEqual(new byte[] { 137,80,78,71,71,75,77,83 });
            if (encoded && texture == FailRestoreOnce) { FailRestoreOnce = null; return false; }
            texture.DecodeImage(encoded ? BitConverter.ToInt32(bytes, 8) : 2, encoded ? BitConverter.ToInt32(bytes, 12) : 2,
                encoded ? bytes[16..] : bytes, mark);
            if (texture == FailLoadAfterMutationOnce) { FailLoadAfterMutationOnce = null; throw new IOException("decode failed after changing pixels"); }
            return true;
        }
        public static bool LoadImage(Texture2D texture, ReadOnlySpan<byte> bytes, bool mark) => throw new Exception("错误的 PNG 重载");
    }
    public class CanvasRenderer
    {
        public Texture? Current;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetTexture(Texture value) => Current = value;
    }
}

namespace UnityEngine.TextCore
{
    public struct FaceInfo
    {
        public int pointSize;
        public float scale, lineHeight, ascentLine, descentLine, baseline, capLine, meanLine;
    }
}
namespace UnityEngine.TextCore.LowLevel { public enum GlyphRenderMode { SDFAA = 4165, SDF = 4134 } }
namespace TMPro
{
    public enum AtlasPopulationMode { Static, Dynamic }
    public class TMP_FontAsset : UnityEngine.Object
    {
        public UnityEngine.Texture2D atlasTexture { get; } = new(512, 1024);
        public UnityEngine.Material material { get; }
        public TMP_FontAsset() => material = new() { mainTexture = atlasTexture };
        public UnityEngine.Font? sourceFontFile { get; set; }
        public HashSet<char> Glyphs { get; } = new("日本語原文".ToCharArray());
        public int atlasPopulationMode { get; set; }
        public bool isMultiAtlasTexturesEnabled { get; set; }
        public int atlasWidth { get; set; } = 512;
        public int atlasHeight { get; set; } = 1024;
        public UnityEngine.TextCore.FaceInfo faceInfo { get; set; } = new() {
            pointSize = 64, scale = 1, lineHeight = 64, ascentLine = 55, descentLine = -9, capLine = 48, meanLine = 32 };
        public int atlasPadding { get; set; } = 5;
        public UnityEngine.TextCore.LowLevel.GlyphRenderMode atlasRenderMode { get; set; } = UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDF;
        public float normalStyle { get; set; }
        public float boldStyle { get; set; } = .75f;
        public float normalSpacingOffset { get; set; }
        public float boldSpacing { get; set; } = 7;
        public byte italicStyle { get; set; } = 35;
        public byte tabSize { get; set; } = 10;
        public List<TMP_FontAsset> fallbackFontAssetTable { get; set; } = new();
        public TMP_FontAsset[] fontWeightTable { get; set; } = Array.Empty<TMP_FontAsset>();
        public int SamplingPointSize;
        public int atlasTextureCount { get; set; } = 1;
        public int Clears;
        public static int FailCreateAfter = -1;
        public void ClearFontAssetData(bool textures) { Clears++; Glyphs.Clear(); }
        public static TMP_FontAsset CreateFontAsset(UnityEngine.Font font)
        {
            if (font == null || font.Destroyed) throw new ArgumentNullException(nameof(font));
            if (FailCreateAfter == 0) { FailCreateAfter = -1; throw new IOException("simulated dynamic font allocation failure"); }
            if (FailCreateAfter > 0) FailCreateAfter--;
            return new() { sourceFontFile = font, atlasPopulationMode = 1,
                faceInfo = new() {pointSize=90, scale=1, lineHeight=150, ascentLine=100, descentLine=-35}, SamplingPointSize=90 };
        }
        public static TMP_FontAsset CreateFontAsset(UnityEngine.Font font, int pointSize, int padding,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode renderMode, int width, int height, AtlasPopulationMode mode, bool multi)
        {
            var asset = CreateFontAsset(font);
            asset.SamplingPointSize = pointSize; asset.atlasPadding = padding; asset.atlasRenderMode = renderMode;
            asset.atlasWidth = width; asset.atlasHeight = height; asset.isMultiAtlasTexturesEnabled = multi;
            return asset;
        }
    }
    public static class TMPro_EventManager { public static int Changes; public static void ON_FONT_PROPERTY_CHANGED(bool changed, UnityEngine.Object asset) => Changes++; }
    public static class TMP_Settings { public static List<TMP_FontAsset> fallbackFontAssets { get; } = new(); }
    public class TextMeshProUGUI : global::Tmp
    {
        public int Rebuilds;
        public string TextAtAwake = "";
        public TMP_FontAsset? FontAtAwake;
        public bool FailMesh;
        public void ForceMeshUpdate(bool inactive, bool reparse)
        { if (FailMesh) throw new InvalidOperationException("fixture mesh refresh"); Rebuilds++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Awake() { TextAtAwake = Value; FontAtAwake = font; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnPreRenderCanvas() { }
    }
}
