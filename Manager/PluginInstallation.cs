using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gkms.Host;

namespace Gkms.Manager;

// Installation inspects metadata and PE headers only; plugin code runs only when enabled/opened.
internal static class PluginInstallation
{
    private const string RemovedManifest = ".uninstalled.json";
    internal static readonly string[] ResourceFolders = { "resources", "resources.previous", "output" };
    private const long MaxExpandedBytes = 512L * 1024 * 1024;

    public static FileStream AcquireLock(string root)
    {
        RejectLink(root);
        // The handle owns the lock; the empty file may persist. Deleting it on close can
        // leave Windows briefly returning ACCESS_DENIED to the next operation.
        return new FileStream(Path.Combine(root, "packages.edit"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 1, FileOptions.None);
    }

    public sealed class Prepared : IDisposable
    {
        internal string? Stage { get; set; }
        internal string Root { get; }
        public Manifest Manifest { get; }
        internal Prepared(string root, string stage, Manifest manifest) => (Root, Stage, Manifest) = (root, stage, manifest);
        public void Dispose()
        {
            if (Stage == null) return;
            try { DeleteTree(Stage); }
            catch (IOException error) { Console.Error.WriteLine(error.Message); }
            catch (UnauthorizedAccessException error) { Console.Error.WriteLine(error.Message); }
            Stage = null;
        }
    }

    public static Prepared Prepare(string root, string zipFile)
    {
        root = Path.GetFullPath(root);
        RejectLink(root);
        using var archive = ZipFile.OpenRead(zipFile);
        if (archive.Entries.Count > 20000) throw new InvalidDataException("插件包文件数量超过 20000。");
        var entries = archive.Entries.Select(entry => (Entry: entry, Path: SafeZipPath(entry))).ToArray();
        var manifests = entries.Where(e => !e.Path.EndsWith('/') &&
            e.Path.Split('/').Last().Equals("plugin.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("插件 ZIP 必须包含唯一的 plugin.json。");
        var manifestPath = manifests[0].Path;
        if (manifestPath.Count(c => c == '/') > 1)
            throw new InvalidDataException("plugin.json 须位于 ZIP 根目录或唯一的一级目录内。");
        var prefix = manifestPath[..^("plugin.json".Length)];
        var stage = Path.Combine(root, ".plugin-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            long expanded = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (entry, path) in entries)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("插件 ZIP 不能夹带插件目录之外的文件。");
                var relative = path[prefix.Length..];
                if (relative.Length == 0) continue; // Optional wrapper directory entry.
                if (!paths.Add(relative.TrimEnd('/')))
                    throw new InvalidDataException($"插件 ZIP 存在重复路径：{path}");
                var destination = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                if (relative.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                var name = Path.GetFileName(destination);
                if (name.Equals(RemovedManifest, StringComparison.OrdinalIgnoreCase) || name.Equals(".pending.json", StringComparison.OrdinalIgnoreCase) ||
                    new[] { "Gkms.Sdk.", "Gkms.Host.", "Gkms.Manager.", "Gkms.BepInEx.", "Gkms.Runtime.", "ImGui.NET.", "cimgui.", "libcimgui." }
                        .Any(shared => name.StartsWith(shared, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"插件包不能携带框架文件：{name}");
                if (entry.Length > MaxExpandedBytes - expanded)
                    throw new InvalidDataException("插件 ZIP 解压后不能超过 512 MiB。");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = entry.Open();
                using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                var buffer = new byte[81920];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    expanded += count;
                    if (expanded > MaxExpandedBytes) throw new InvalidDataException("插件 ZIP 解压后不能超过 512 MiB。");
                    target.Write(buffer, 0, count);
                }
            }
            var manifest = Catalog.ReadManifest(Path.Combine(stage, "plugin.json"));
            _ = AssemblyName.GetAssemblyName(Path.Combine(stage, manifest.Assembly));
            return new(root, stage, manifest);
        }
        catch
        {
            try { DeleteTree(stage); }
            catch (IOException error) { Console.Error.WriteLine(error.Message); }
            catch (UnauthorizedAccessException error) { Console.Error.WriteLine(error.Message); }
            throw;
        }
    }

    public static Package Install(Prepared prepared, PluginSource? source = null)
    {
        if (prepared.Stage == null) throw new ObjectDisposedException(nameof(Prepared));
        var root = prepared.Root;
        using var operation = AcquireLock(root);
        if (source != null) PluginRepositoryStore.RequireSource(root, source);
        var pluginRoot = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginRoot);
        RejectLink(pluginRoot);
        // Inspect every manifest, including IDs omitted by duplicate/retired-package filtering.
        foreach (var folder in Directory.EnumerateDirectories(pluginRoot))
        {
            RejectLink(folder);
            var file = Path.Combine(folder, "plugin.json");
            if (!File.Exists(file)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetString() == prepared.Manifest.Id)
                throw new InvalidOperationException($"{prepared.Manifest.Id} 已安装。请先卸载旧版，再安装新版；设置会保留。");
        }
        var catalog = Catalog.Discover(pluginRoot, _ => { });
        ProfileEditor.ApplyLocked(root, catalog, new Dictionary<string, bool> { [prepared.Manifest.Id] = false });
        // A fresh path avoids reusing files still mapped by an old collectible load context.
        var destination = Path.Combine(pluginRoot, prepared.Manifest.Id + "-" + Guid.NewGuid().ToString("N"));
        var retained = Directory.EnumerateDirectories(pluginRoot)
            .Where(folder => File.Exists(Path.Combine(folder, RemovedManifest)) && !File.Exists(Path.Combine(folder, "plugin.json")))
            .OrderByDescending(folder => File.GetLastWriteTimeUtc(Path.Combine(folder, RemovedManifest)))
            .Where(folder =>
            {
                CheckTree(folder);
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, RemovedManifest)));
                return json.RootElement.GetProperty("id").GetString() == prepared.Manifest.Id;
            }).ToArray();
        // Retained user files take precedence over defaults shipped in a reinstallation.
        foreach (var name in ResourceFolders)
            if (retained.Any(folder => Directory.Exists(Path.Combine(folder, name))) && Directory.Exists(Path.Combine(prepared.Stage, name)))
                DeleteTree(Path.Combine(prepared.Stage, name));
        foreach (var folder in retained)
            foreach (var name in ResourceFolders)
                if (Directory.Exists(Path.Combine(folder, name))) CopyMissing(Path.Combine(folder, name), Path.Combine(prepared.Stage, name));
        MoveDirectory(prepared.Stage, destination);
        try { PluginRepositoryStore.SetOriginLocked(root, prepared.Manifest.Id, source); }
        catch { MoveDirectory(destination, prepared.Stage); throw; }
        prepared.Stage = null;
        // Only clear the retained copies after the new package and its resources are in place.
        foreach (var folder in retained)
        {
            try
            {
                foreach (var name in ResourceFolders)
                    if (Directory.Exists(Path.Combine(folder, name))) DeleteTree(Path.Combine(folder, name));
                TryRemove(folder, null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine($"插件已安装，旧资源副本等待手动清理: {folder}: {error.Message}"); }
        }
        return new(destination, prepared.Manifest);
    }

    public static async Task<bool> UninstallAsync(string root, string id)
    {
        using var operation = AcquireLock(root);
        var pluginRoot = Path.Combine(Path.GetFullPath(root), "plugins");
        RejectLink(pluginRoot);
        var catalog = Catalog.Discover(pluginRoot, _ => { });
        if (!catalog.TryGetValue(id, out var package)) throw new InvalidDataException($"找不到插件 {id}。");
        CheckTree(package.Directory);
        using var resourceOperation = new FileStream(Path.Combine(package.Directory, "resources.edit"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        ProfileEditor.ApplyLocked(root, catalog, new Dictionary<string, bool> { [id] = false });
        var reply = await RuntimeClient.SendAsync(root, "reload-profile").ConfigureAwait(false);
        if (reply == null && RuntimeProcessExists(root))
            throw new IOException("已保存停用选择，但未能连接运行中的宿主确认停用。插件文件保留，请稍后重试卸载。");
        if (reply != null && (!reply.Success || reply.Active.Contains(id)))
            throw new IOException("宿主未确认停用，插件文件保留：" + reply.Message);

        // Removing the discovery manifest atomically makes the operation durable even if a DLL is locked.
        File.Move(Path.Combine(package.Directory, "plugin.json"), Path.Combine(package.Directory, RemovedManifest));
        File.SetLastWriteTimeUtc(Path.Combine(package.Directory, RemovedManifest), DateTime.UtcNow);
        resourceOperation.Dispose();
        return TryRemove(package.Directory, null);
    }

    // Returns the number still waiting for file handles to be released.
    public static int Cleanup(string root, Action<string>? log = null, bool beforeHostStart = false)
    {
        using var operation = AcquireLock(root);
        PluginUpgrade.RecoverLocked(root, log, beforeHostStart);
        var plugins = Path.Combine(root, "plugins");
        if (!Directory.Exists(plugins)) return 0;
        RejectLink(plugins);
        var pending = 0;
        foreach (var folder in Directory.EnumerateDirectories(plugins))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            if (!File.Exists(Path.Combine(folder, RemovedManifest)) || File.Exists(Path.Combine(folder, "plugin.json"))) continue;
            if (!TryRemove(folder, log)) pending++;
        }
        return pending;
    }

    internal static bool TryRemove(string folder, Action<string>? log)
    {
        try
        {
            CheckTree(folder);
            // Keep the marker until every payload file/subdirectory has been removed so a retry is safe.
            foreach (var child in Directory.EnumerateFileSystemEntries(folder))
            {
                if (Path.GetFileName(child) == RemovedManifest || ResourceFolders.Contains(Path.GetFileName(child))) continue;
                if (Directory.Exists(child)) Directory.Delete(child, true);
                else File.Delete(child);
            }
            if (!ResourceFolders.Any(name => Directory.Exists(Path.Combine(folder, name))))
            {
                File.Delete(Path.Combine(folder, RemovedManifest));
                Directory.Delete(folder);
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"等待清理 {Path.GetFileName(folder)}：{error.Message}");
            return false;
        }
    }

    private static void CopyMissing(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (var folder in Directory.EnumerateDirectories(source))
            CopyMissing(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    internal static bool RuntimeProcessExists(string root)
    {
        var file = Path.Combine(root, "runtime.json");
        if (!File.Exists(file)) return false;
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var pid = document.RootElement.GetProperty("processId").GetInt32();
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static string SafeZipPath(ZipArchiveEntry entry)
    {
        var path = entry.FullName.Replace('\\', '/');
        var parts = path.TrimEnd('/').Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase)) ||
            ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"插件 ZIP 路径无效或包含链接：{entry.FullName}");
        return path;
    }

    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"插件操作不支持链接目录或文件：{path}");
    }

    internal static void CheckTree(string path)
    {
        RejectLink(path);
        if (Directory.Exists(path))
            foreach (var child in Directory.EnumerateFileSystemEntries(path)) CheckTree(child);
    }

    internal static void DeleteTree(string path)
    {
        CheckTree(path);
        Directory.Delete(path, true);
    }

    internal static void MoveDirectory(string source, string destination)
    {
        // Windows can briefly deny a rename after a file handle closes. Retry only
        // sharing/access failures while neither endpoint has changed ownership.
        var delays = new[] { 25, 50, 100, 200, 400 };
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException error) when (attempt < delays.Length && (error.HResult & 0xffff) is 5 or 32 or 33 &&
                Directory.Exists(source) && !Directory.Exists(destination))
            { Thread.Sleep(delays[attempt]); }
        }
    }
}
