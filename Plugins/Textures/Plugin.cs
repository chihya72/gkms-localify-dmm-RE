using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Textures;

public sealed class Plugin : ILocalifyPlugin
{
    private static readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, object> loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> invalid = new(StringComparer.OrdinalIgnoreCase);
    private static Type texture = null!;
    private static MethodInfo loadImage = null!, destroy = null!, alive = null!;
    private static IPluginContext? context;
    public void Start(IPluginContext session)
    {
        context = session;
        var dir = Path.Combine(session.DataDirectory, "texture2d");
        foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories)
                     .OrderBy(p => Path.GetRelativePath(dir, p).Length).ThenBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            files.TryAdd(name, file);
            if (Portable(name) is { } portable) files.TryAdd(portable, file);
        }
        texture = session.RequireType("UnityEngine.CoreModule", "UnityEngine.Texture2D");
        var conversions = session.RequireType("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion");
        loadImage = conversions.GetMethods(GameMethods.Declared).Single(m => m.Name == "LoadImage" &&
            m.GetParameters() is [{ ParameterType: var t }, { ParameterType: var buffer }, { ParameterType: var b }] &&
            t == texture && Interop.IsByteArray(buffer) && b == typeof(bool));
        destroy = session.Require("UnityEngine.CoreModule", "UnityEngine.Object", "Destroy", "UnityEngine.Object");
        alive = session.Require("UnityEngine.CoreModule", "UnityEngine.Object", "op_Implicit", "UnityEngine.Object");
        var target = session.Require("UnityEngine.UIModule", "UnityEngine.CanvasRenderer", "SetTexture", "UnityEngine.Texture");
        session.Patch(target, GameMethods.Handler<Plugin>(nameof(Replace)).MakeGenericMethod(target.GetParameters()[0].ParameterType));
        session.Log($"已索引 {files.Values.Distinct().Count()} 张贴图。");
    }

    public static string? Portable(string name)
    {
        var parts = name.Split('-');
        if (parts.Length < 5 || parts[0] != "sactx" || !parts[2].Contains('x')) return null;
        var end = parts.Length;
        if (parts[^1].Length == 8 && parts[^1].All(Uri.IsHexDigit)) end--;
        return end <= 4 ? null : string.Join("-", parts.Take(3).Concat(parts.Skip(4).Take(end - 4)));
    }

    public static void Replace<T>(ref T __0) where T : class
    {
        if (__0 == null) return;
        var name = __0.GetType().GetProperty("name")?.GetValue(__0) as string;
        if (name == null || !files.TryGetValue(name.Replace('|', '_'), out var file) &&
            !files.TryGetValue(Portable(name) ?? "", out file) || invalid.Contains(file)) return;
        object? created = null;
        try
        {
            if (loaded.TryGetValue(file, out var cached) && (bool)alive.Invoke(null, new[] { cached })!)
            { __0 = (T)cached; return; }
            created = Activator.CreateInstance(texture, 2, 2)!;
            var bytes = File.ReadAllBytes(file);
            var buffer = loadImage.GetParameters()[1].ParameterType;
            var data = Interop.WrapBytes(buffer, bytes);
            if (!(bool)loadImage.Invoke(null, new[] { created, data, false })!) throw new InvalidDataException(file);
            foreach (var key in new[] { "filterMode", "wrapMode" })
            {
                var prop = texture.GetProperty(key);
                if (prop != null) prop.SetValue(created, prop.GetValue(__0));
            }
            loaded[file] = created;
            __0 = (T)created;
        }
        catch (Exception e)
        {
            if (created != null) destroy.Invoke(null, new[] { created });
            invalid.Add(file);
            context?.Log($"{file}: {e.GetBaseException().Message}");
        }
    }
    public void Dispose()
    {
        foreach (var item in loaded.Values) destroy.Invoke(null, new[] { item });
        loaded.Clear(); files.Clear(); invalid.Clear(); context = null;
    }
}
