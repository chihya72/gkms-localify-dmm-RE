using System.Runtime.CompilerServices;

namespace Il2CppSystem { public sealed record Type(System.Type Managed); }
namespace Il2CppInterop.Runtime
{
    public static class Il2CppType { public static Il2CppSystem.Type From(System.Type t) => new(t); }
}

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = "";
        public static implicit operator bool(Object? obj) => obj != null;
        public static void Destroy(Object value) { }
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
    public class Texture2D : Texture { public Texture2D(int width, int height) { } }
    public static class ImageConversion
    {
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
