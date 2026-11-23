using System.Text.Json;
using Gkms.Host;

namespace Gkms.Manager;

internal static class PluginUpgrade
{
    private const string JournalFile = "plugin-update.json", Retired = ".uninstalled.json", Pending = ".pending.json";
    internal sealed record Journal(string Id, string OldFolder, string NewFolder, string StageFolder, bool Enabled,
        string[] PreservedFolders, bool Committed = false);

    public static async Task<Package> ApplyAsync(PluginInstallation.Prepared prepared, PluginSource source)
    {
        var root = prepared.Root;
        using var operation = PluginInstallation.AcquireLock(root);
        PluginRepositoryStore.RequireSource(root, source);
        if (!PluginRepositoryStore.Owns(PluginRepositoryStore.Read(root), prepared.Manifest.Id, source))
            throw new InvalidOperationException("插件订阅来源已改变，请重新检查更新。");
        if (File.Exists(Path.Combine(root, JournalFile))) throw new IOException("上次插件更新尚待恢复，请关闭游戏后重新打开管理器。");
        var packages = Path.Combine(root, "plugins");
        var catalog = Catalog.Discover(packages, _ => { });
        if (!catalog.TryGetValue(prepared.Manifest.Id, out var previous)) throw new InvalidOperationException("原插件已不存在，请重新刷新列表。");
        if (PluginVersion.Parse(prepared.Manifest.Version!).CompareTo(PluginVersion.Parse(previous.Manifest.Version!)) <= 0)
            throw new InvalidOperationException("更新包版本必须高于已安装版本。");
        PluginInstallation.CheckTree(previous.Directory);
        var stage = prepared.Stage ?? throw new ObjectDisposedException(nameof(prepared));
        PluginInstallation.CheckTree(stage);
        using var resources = new FileStream(Path.Combine(previous.Directory, "resources.edit"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var enabled = Catalog.ReadProfile(Path.Combine(root, "profile.json"), catalog).Enabled.Contains(previous.Manifest.Id);
        var destination = Path.Combine(packages, previous.Manifest.Id + "-" + Guid.NewGuid().ToString("N"));
        var journal = new Journal(previous.Manifest.Id, Path.GetFileName(previous.Directory), Path.GetFileName(destination),
            Path.GetFileName(stage), enabled, PluginInstallation.ResourceFolders.Where(n => Directory.Exists(Path.Combine(previous.Directory, n))).ToArray());
        var journalPath = Path.Combine(root, JournalFile);
        var attemptedActivation = false;
        PluginRepositoryStore.WriteAtomic(journalPath, journal);
        try
        {
            ProfileEditor.ApplyLocked(root, catalog, new Dictionary<string, bool> { [journal.Id] = false });
            await ConfirmStopped(root, journal.Id).ConfigureAwait(false);
            File.Move(Path.Combine(stage, "plugin.json"), Path.Combine(stage, Pending));
            PluginInstallation.MoveDirectory(stage, destination);
            prepared.Stage = null; // The journal now owns rollback/cleanup of the destination.
            foreach (var name in journal.PreservedFolders)
            {
                var target = Path.Combine(destination, name);
                if (Directory.Exists(target)) PluginInstallation.DeleteTree(target);
                PluginInstallation.MoveDirectory(Path.Combine(previous.Directory, name), target);
            }
            File.Move(Path.Combine(previous.Directory, "plugin.json"), Path.Combine(previous.Directory, Retired));
            File.Move(Path.Combine(destination, Pending), Path.Combine(destination, "plugin.json"));
            if (enabled)
            {
                attemptedActivation = true;
                ProfileEditor.ApplyLocked(root, Catalog.Discover(packages, _ => { }), new Dictionary<string, bool> { [journal.Id] = true });
                var reply = await RuntimeClient.SendAsync(root, "reload-profile").ConfigureAwait(false);
                if (reply == null && PluginInstallation.RuntimeProcessExists(root) || reply != null && (!reply.Success || !reply.Active.Contains(journal.Id)))
                    throw new IOException("新版插件未能在宿主中启用: " + (reply?.Message ?? "连接中断"));
            }
            PluginRepositoryStore.WriteAtomic(journalPath, journal with { Committed = true });
            // Once committed, a delayed journal deletion must not trigger rollback.
            try { File.Delete(journalPath); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine("插件更新已完成，提交记录将在下次启动清理: " + cleanup.Message); }
            resources.Dispose();
            PluginInstallation.TryRemove(previous.Directory, null);
            return new(destination, prepared.Manifest);
        }
        catch (Exception failure)
        {
            if (attemptedActivation)
            {
                try
                {
                    ProfileEditor.ApplyLocked(root, Catalog.Discover(packages, _ => { }), new Dictionary<string, bool> { [journal.Id] = false });
                    await ConfirmStopped(root, journal.Id).ConfigureAwait(false);
                }
                catch (Exception stopping)
                {
                    throw new AggregateException("新版停用尚未确认，已保留更新记录和旧版。关闭游戏后将恢复旧版。", failure, stopping);
                }
            }
            RestoreFiles(root, journal);
            prepared.Stage = null;
            ProfileEditor.ApplyLocked(root, Catalog.Discover(packages, _ => { }), new Dictionary<string, bool> { [journal.Id] = enabled });
            File.Delete(journalPath);
            if (enabled)
            {
                try
                {
                    var reply = await RuntimeClient.SendAsync(root, "reload-profile").ConfigureAwait(false);
                    if (reply is { Success: false }) throw new IOException(reply.Message);
                }
                catch (Exception restore) { throw new AggregateException("已恢复旧版文件及配置，但宿主重新启用失败。", failure, restore); }
            }
            throw new InvalidOperationException("插件更新失败，已恢复旧版及原来的启用状态。", failure);
        }
    }

    private static async Task ConfirmStopped(string root, string id)
    {
        var reply = await RuntimeClient.SendAsync(root, "reload-profile").ConfigureAwait(false);
        if (reply == null && PluginInstallation.RuntimeProcessExists(root) || reply != null && (!reply.Success || reply.Active.Contains(id)))
            throw new IOException("宿主未确认插件停用，保留原文件: " + (reply?.Message ?? "连接中断"));
    }
    internal static void RecoverLocked(string root, Action<string>? log, bool beforeHostStart)
    {
        var file = Path.Combine(root, JournalFile);
        if (!File.Exists(file)) return;
        if (!beforeHostStart && PluginInstallation.RuntimeProcessExists(root)) throw new IOException("运行中的插件更新待恢复，请先关闭游戏。");
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(file), Catalog.Json) ?? throw new InvalidDataException("插件更新记录为空。");
        Validate(root, journal);
        if (!journal.Committed)
        {
            RestoreFiles(root, journal);
            ProfileEditor.ApplyLocked(root, Catalog.Discover(Path.Combine(root, "plugins"), _ => { }),
                new Dictionary<string, bool> { [journal.Id] = journal.Enabled });
            log?.Invoke(journal.Id + ": 已从中断的更新恢复旧版、资源和启用状态。");
        }
        File.Delete(file);
    }
    private static void Validate(string root, Journal journal)
    {
        if (journal.PreservedFolders == null || journal.PreservedFolders.Any(n => !PluginInstallation.ResourceFolders.Contains(n)) ||
            new[] { journal.OldFolder, journal.NewFolder, journal.StageFolder }.Any(n => string.IsNullOrWhiteSpace(n) || Path.GetFileName(n) != n ||
                n is "." or ".." || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
            journal.OldFolder == journal.NewFolder || !journal.StageFolder.StartsWith(".plugin-install-", StringComparison.Ordinal))
            throw new InvalidDataException("插件更新恢复路径无效。");
        foreach (var name in new[] { journal.OldFolder, journal.NewFolder })
        {
            var directory = Path.Combine(root, "plugins", name);
            if (!Directory.Exists(directory)) continue;
            PluginInstallation.CheckTree(directory);
            var manifest = new[] { "plugin.json", Retired, Pending }.Select(n => Path.Combine(directory, n)).FirstOrDefault(File.Exists);
            if (manifest == null || Catalog.ReadManifest(manifest).Id != journal.Id) throw new InvalidDataException("更新恢复目录中的插件身份不符。");
        }
    }
    private static void RestoreFiles(string root, Journal journal)
    {
        Validate(root, journal);
        var previous = Path.Combine(root, "plugins", journal.OldFolder);
        var destination = Path.Combine(root, "plugins", journal.NewFolder);
        var stage = Path.Combine(root, journal.StageFolder);
        if (!Directory.Exists(previous)) throw new IOException("更新恢复找不到旧版目录，保留全部文件以便处理。");
        if (File.Exists(Path.Combine(destination, "plugin.json")))
            File.Move(Path.Combine(destination, "plugin.json"), Path.Combine(destination, Retired));
        foreach (var name in journal.PreservedFolders)
            if (!Directory.Exists(Path.Combine(previous, name)) && Directory.Exists(Path.Combine(destination, name)))
                PluginInstallation.MoveDirectory(Path.Combine(destination, name), Path.Combine(previous, name));
        if (!File.Exists(Path.Combine(previous, "plugin.json")))
            File.Move(Path.Combine(previous, Retired), Path.Combine(previous, "plugin.json"));
        if (Directory.Exists(destination))
        {
            if (File.Exists(Path.Combine(destination, Pending)))
                File.Move(Path.Combine(destination, Pending), Path.Combine(destination, Retired));
            PluginInstallation.TryRemove(destination, null);
        }
        if (Directory.Exists(stage)) PluginInstallation.DeleteTree(stage);
    }
}
