using System.IO.Compression;
using System.Text.Json;

namespace Gkms.Resources;

public static class PackInstaller
{
    // The distribution marker identifies the payload, including release ZIPs with a wrapper folder.
    public static int Install(string data, bool textures, ZipArchive archive, string? release)
    {
        var name = textures ? "texture2d" : "local-files";
        var marker = textures ? "texture_version.txt" : "localization.json";
        var markers = archive.Entries.Where(e => e.Name.Equals(marker, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (markers.Length != 1) throw new InvalidDataException($"资源包须包含唯一的 {marker}。");
        var prefix = markers[0].FullName[..^markers[0].Name.Length];
        var target = Path.Combine(data, name);
        var backup = Path.Combine(data, "re", name + ".previous");
        var stage = Path.Combine(data, "re", name + "." + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target);
        Directory.CreateDirectory(stage);
        try
        {
            var count = 0;
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length != 0))
            {
                var relative = entry.FullName[prefix.Length..];
                var parts = relative.Split('/', '\\');
                if (parts.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                    p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new InvalidDataException($"无效的资源路径 {relative}");
                if (textures && !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    !relative.Equals(marker, StringComparison.OrdinalIgnoreCase)) continue;
                var dest = Path.Combine(stage, Path.Combine(parts));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest);
                count++;
            }
            var markerPath = Path.Combine(stage, marker);
            if (textures)
            {
                var actual = File.ReadAllText(markerPath).Trim();
                if (actual.Length == 0 || release != null && actual != release || count < 2)
                    throw new InvalidDataException("贴图版本或 PNG 内容与发布包不符。");
            }
            else
            {
                using var dict = JsonDocument.Parse(File.ReadAllText(markerPath));
                if (dict.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException(marker);
                // Translation ZIPs often omit the separately distributed font bundle and user overrides.
                if (Directory.Exists(target)) CopyMissing(target, stage);
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(stage, target); }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            return count;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    private static void CopyMissing(string source, string dest)
    {
        foreach (var item in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"资源目录包含链接，请先单独处理 {item.FullName}");
            var path = Path.Combine(dest, item.Name);
            if (item is DirectoryInfo dir) { Directory.CreateDirectory(path); CopyMissing(dir.FullName, path); }
            else if (!File.Exists(path)) File.Copy(item.FullName, path);
        }
    }
}
