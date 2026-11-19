using System.Runtime.CompilerServices;

namespace Il2CppSystem { public sealed record Type(System.Type Managed); }
namespace Il2CppInterop.Runtime
{
    public static class Il2CppType { public static Il2CppSystem.Type From(System.Type t) => new(t); }
}

namespace UnityEngine
{
    public partial class Object
    {
        public string name { get; set; } = "";
        public bool Destroyed { get; private set; }
        public static implicit operator bool(Object? obj) => obj != null && !obj.Destroyed;
        public int GetInstanceID() => RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value) => value.Destroyed = true;
    }
    public class Font : Object { }
    public class AssetBundle : Object
    {
        public static AssetBundle LoadFromMemory(byte[] data) => new();
        public T LoadAsset<T>(string name) where T : Object, new() => new();
        public Object LoadAsset(string name, Il2CppSystem.Type type) => (Object)Activator.CreateInstance(type.Managed)!;
        public void Unload(bool destroy) { }
    }
    public class Texture : Object { public int filterMode { get; set; } public int wrapMode { get; set; } }
    public class Texture2D : Texture
    {
        public int width { get; }
        public int height { get; }
        public Texture2D(int width, int height) => (this.width, this.height) = (width, height);
        public void ReadPixels(Rect rectangle, int x, int y, bool mipmaps) { }
        public void Apply(bool mipmaps, bool unreadable) { }
    }
    public class RenderTexture : Texture
    {
        public static RenderTexture? active { get; set; }
        public static RenderTexture GetTemporary(int width, int height) => new();
        public static void ReleaseTemporary(RenderTexture texture) { }
    }
    public static class Graphics { public static void Blit(Texture texture, RenderTexture target) { } }
    public static class DebugLogHandler
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Internal_Log(int level, int options, string message, Object? context) { }
    }
    public static class ImageConversion
    {
        public static byte[] EncodeToPNG(Texture2D texture) => new byte[] { 137, 80, 78, 71 };
        public static bool LoadImage(Texture2D texture, byte[] bytes, bool mark) => bytes.Length > 0;
        public static bool LoadImage(Texture2D texture, ReadOnlySpan<byte> bytes, bool mark) => throw new Exception("错误的 PNG 重载");
    }
    public class CanvasRenderer
    {
        public Texture? Current;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetTexture(Texture value) => Current = value;
    }
}

namespace TMPro
{
    public class TMP_FontAsset : UnityEngine.Object
    {
        public static TMP_FontAsset CreateFontAsset(UnityEngine.Font font) => new();
    }
    public static class TMP_Settings { public static List<TMP_FontAsset> fallbackFontAssets { get; } = new(); }
    public class TextMeshProUGUI
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Awake() { }
    }
}
