using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.TextureCapture;

public sealed class Plugin : ILocalifyPlugin
{
    private static readonly HashSet<int> seen = new();
    private static MethodInfo temporary = null!, release = null!, blit = null!, read = null!, apply = null!, encode = null!, destroy = null!, instanceId = null!;
    private static PropertyInfo active = null!;
    private static Type image = null!, rect = null!;
    private static string root = "";
    private static IPluginContext? context;

    public void Start(IPluginContext ctx)
    {
        context = ctx;
        const string core = "UnityEngine.CoreModule", rt = "UnityEngine.RenderTexture", tex = "UnityEngine.Texture2D";
        root = Directory.CreateDirectory(Path.Combine(ctx.DataDirectory, "dump-files", "texture2d")).FullName;
        temporary = ctx.Require(core, rt, "GetTemporary", "System.Int32", "System.Int32");
        release = ctx.Require(core, rt, "ReleaseTemporary", rt);
        active = ctx.RequireType(core, rt).GetProperty("active")!;
        blit = ctx.Require(core, "UnityEngine.Graphics", "Blit", "UnityEngine.Texture", rt);
        image = ctx.RequireType(core, tex);
        rect = ctx.RequireType(core, "UnityEngine.Rect");
        read = ctx.Require(core, tex, "ReadPixels", "UnityEngine.Rect", "System.Int32", "System.Int32", "System.Boolean");
        apply = ctx.Require(core, tex, "Apply", "System.Boolean", "System.Boolean");
        encode = ctx.Require("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion", "EncodeToPNG", tex);
        destroy = ctx.Require(core, "UnityEngine.Object", "Destroy", "UnityEngine.Object");
        instanceId = ctx.Require(core, "UnityEngine.Object", "GetInstanceID");
        ctx.Patch(ctx.Require("UnityEngine.UIModule", "UnityEngine.CanvasRenderer", "SetTexture", "UnityEngine.Texture"),
            GameMethods.Handler<Plugin>(nameof(Capture)), priority: 800);
    }

    public static void Capture(object __0)
    {
        if (__0 == null || !seen.Add((int)instanceId.Invoke(__0, null)!)) return;
        object? buffer = null, cpu = null, previous = null;
        try
        {
            var type = __0.GetType();
            var name = type.GetProperty("name")?.GetValue(__0) as string;
            if (string.IsNullOrWhiteSpace(name)) return;
            var fileName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd(' ', '.');
            if (fileName.Length == 0) return;
            var target = Path.Combine(root, fileName + ".png");
            if (File.Exists(target)) return;
            var width = (int)type.GetProperty("width")!.GetValue(__0)!;
            var height = (int)type.GetProperty("height")!.GetValue(__0)!;
            if (width <= 0 || height <= 0) return;
            previous = active.GetValue(null);
            buffer = temporary.Invoke(null, new object[] { width, height });
            blit.Invoke(null, new[] { __0, buffer });
            active.SetValue(null, buffer);
            cpu = Activator.CreateInstance(image, width, height);
            read.Invoke(cpu, new[] { Activator.CreateInstance(rect, 0f, 0f, (float)width, (float)height), 0, 0, false });
            apply.Invoke(cpu, new object[] { false, false });
            var bytes = encode.Invoke(null, new[] { cpu });
            if (bytes is IEnumerable<byte> png) File.WriteAllBytes(target, png.ToArray());
        }
        catch (Exception e) { context?.Log(e.GetBaseException().Message); }
        finally
        {
            if (buffer != null) { active.SetValue(null, previous); release.Invoke(null, new[] { buffer }); }
            if (cpu != null) destroy.Invoke(null, new[] { cpu });
        }
    }
    public void Dispose() { seen.Clear(); context = null; }
}
