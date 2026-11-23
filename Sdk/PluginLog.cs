namespace Gkms.Sdk;

public enum PluginLogLevel { Debug, Info, Warning, Error, Critical }

// Optional level overload: existing API 2 plugins can keep calling Log(string).
public static class PluginLog
{
    public static void Log(this IPluginContext context, PluginLogLevel level, string message) =>
        context.Log($"[{level}] {message}");

    public static PluginLogLevel? ReadLevel(string message)
    {
        foreach (var level in Enum.GetValues<PluginLogLevel>())
            if (message.StartsWith($"[{level}] ", StringComparison.Ordinal)) return level;
        return null;
    }

    public static string WithSource(string message, string source)
    {
        var level = ReadLevel(message);
        return level.HasValue ? $"[{level}] {source}: {message[(level.ToString()!.Length + 3)..]}" : $"{source}: {message}";
    }

    public static string Normalize(string message) => ReadLevel(message).HasValue ? message : "[Info] " + message;
}
