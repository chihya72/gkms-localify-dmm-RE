using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Gkms.Sdk;

namespace Gkms.Host;

public sealed record RuntimeRequest(string Command, string? Plugin = null);
public enum PluginRunState { Disabled, Running, RestartRequired, Failed }
public sealed record PluginRuntimeStatus(PluginRunState State, bool Active, string[] Details);
public sealed record RuntimeReply(bool Success, string Message, string[] Active)
{
    public Dictionary<string, string[]>? Warnings { get; init; }
    public Dictionary<string, PluginRuntimeStatus>? States { get; init; }
}

public static class RuntimeClient
{
    public static Action? OpenPanel { get; set; }
    public static async Task<RuntimeReply?> SendAsync(string root, string command, string? plugin = null)
    {
        var marker = Path.Combine(root, "runtime.json");
        if (!File.Exists(marker)) return null;
        int pid;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(marker)); pid = doc.RootElement.GetProperty("processId").GetInt32(); }
        catch (IOException) { return null; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var pipe = new NamedPipeClientStream(".", "gkms-localify-" + pid, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(1500, timeout.Token).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new RuntimeRequest(command, plugin), new JsonSerializerOptions(JsonSerializerDefaults.Web))).ConfigureAwait(false);
        // The wire protocol is one JSON object per line.
        var reply = await reader.ReadLineAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
        return reply == null ? throw new IOException("宿主连接已关闭。") : JsonSerializer.Deserialize<RuntimeReply>(reply, Catalog.Json);
    }
}

internal sealed class RuntimeControl : IDisposable
{
    private static RuntimeControl? current;
    private readonly PluginHost host;
    private readonly string profile, root, marker;
    private readonly Action<string> log;
    private readonly CancellationTokenSource stop = new();
    private IPluginScope? scope;
    private DateTime lastWrite, nextPoll;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public RuntimeControl(PluginHost host, string data, string profile, Action<string> log)
    {
        this.host = host; this.profile = profile; this.log = log;
        root = Path.Combine(data, "re"); marker = Path.Combine(root, "runtime.json");
    }
    public void Start(IPluginScope context)
    {
        scope = context.OpenScope();
        scope.Patch(context.Require("campus-submodule.Runtime", "Campus.Common.CampusQualityManager", "Update"),
            postfix: GameMethods.Handler<RuntimeControl>(nameof(Update)), priority: 0);
        Directory.CreateDirectory(root);
        lastWrite = File.GetLastWriteTimeUtc(profile);
        current = this;
        File.WriteAllText(marker, JsonSerializer.Serialize(new { processId = Environment.ProcessId }, Wire));
        _ = Task.Run(ListenAsync);
    }
    public static void Update()
    {
        var self = current;
        if (self == null) return;
        self.host.Pump();
        if (DateTime.UtcNow < self.nextPoll) return;
        self.nextPoll = DateTime.UtcNow.AddSeconds(1);
        var stamp = File.GetLastWriteTimeUtc(self.profile);
        if (stamp == self.lastWrite) return;
        self.lastWrite = stamp;
        try { self.host.ReloadProfile(); self.log("framework: 配置已处理。" + self.host.DescribeStatus()); }
        catch (Exception error) { self.log("framework: " + error.GetBaseException().Message); }
    }
    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream("gkms-localify-" + Environment.ProcessId, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                var line = await reader.ReadLineAsync().WaitAsync(requestTimeout.Token).ConfigureAwait(false);
                if (line == null) continue;
                var request = JsonSerializer.Deserialize<RuntimeRequest>(line, Wire) ?? throw new InvalidDataException("空请求。");
                var completion = new TaskCompletionSource<RuntimeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                host.Enqueue(() =>
                {
                    try
                    {
                        switch (request.Command)
                        {
                            case "reload-profile": host.ReloadProfile(); lastWrite = File.GetLastWriteTimeUtc(profile); break;
                            case "reload-resources": host.ReloadResources(request.Plugin ?? "localization"); break;
                            case "status": break;
                            case "open-panel": (RuntimeClient.OpenPanel ?? throw new InvalidOperationException("游戏内面板尚未初始化。"))(); break;
                            default: throw new InvalidDataException("未知运行时命令。");
                        }
                        completion.TrySetResult(new(true, host.DescribeStatus(), host.Active.ToArray()) { Warnings = host.GetWarnings(), States = host.GetStates() });
                    }
                    catch (Exception error) { completion.TrySetResult(new(false, error.GetBaseException().Message, host.Active.ToArray()) { Warnings = host.GetWarnings(), States = host.GetStates() }); }
                });
                var reply = await completion.Task.WaitAsync(requestTimeout.Token).ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply, Wire)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error) { log("framework: " + error.GetBaseException().Message); }
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        scope?.Dispose(); scope = null;
        if (current == this) current = null;
        try
        {
            if (File.Exists(marker))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(marker));
                if (doc.RootElement.GetProperty("processId").GetInt32() == Environment.ProcessId) File.Delete(marker);
            }
        }
        catch (Exception error) { log(error.Message); }
    }
}
