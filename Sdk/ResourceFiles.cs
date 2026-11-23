using System.Text.Json;

namespace Gkms.Sdk;

/// <summary>Isolate failures to the resource file being read, as in DMM's loaders.</summary>
public static class ResourceFiles
{
    public static bool TryRead<T>(string file, Func<string, T> read, Action<string> report, out T value)
    {
        value = default!;
        if (!File.Exists(file)) return false;
        try { value = read(file); return true; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            report($"资源文件读取失败，已跳过 {file}: {error.GetBaseException().Message}");
            return false;
        }
    }
}
