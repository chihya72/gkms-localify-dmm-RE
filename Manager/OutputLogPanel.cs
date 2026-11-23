using System.Globalization;
using System.Numerics;
using System.Text;
using Gkms.Sdk;
using ImGuiNET;

namespace Gkms.Manager;

internal sealed record OutputLogEntry(PluginLogLevel? Level, string Text);

internal sealed class OutputLogBuffer
{
    internal const int Limit = 1000;
    internal readonly List<OutputLogEntry> Entries = new();
    private string partial = "";
    internal void Reset() { Entries.Clear(); partial = ""; }
    internal void Append(string text)
    {
        var lines = (partial + text).Split('\n');
        partial = lines[^1];
        if (partial.Length > 32768) partial = partial[^32768..];
        foreach (var raw in lines.Take(lines.Length - 1))
        {
            var line = raw.TrimEnd('\r');
            var timestamp = line.Length >= 26 && line[0] == '[' && line[24] == ']' &&
                DateTime.TryParseExact(line.Substring(1, 23), "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            var message = timestamp ? line[26..] : line;
            var level = PluginLog.ReadLevel(message);
            if (!timestamp && level == null && Entries.Count > 0)
            {
                var previous = Entries[^1];
                if (previous.Text.Length < 32768) Entries[^1] = previous with { Text = previous.Text + "\n" + line };
            }
            else Entries.Add(new(level, line.Length > 32768 ? line[..32768] : line));
        }
        if (Entries.Count > Limit) Entries.RemoveRange(0, Entries.Count - Limit);
    }
    internal IEnumerable<OutputLogEntry> Filter(bool[] levels, string query) => Entries.Where(entry =>
        levels[entry.Level.HasValue ? (int)entry.Level.Value : 5] &&
        (string.IsNullOrWhiteSpace(query) || entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase)));
}

internal sealed class OutputLogPanel
{
    private readonly string file;
    private readonly OutputLogBuffer buffer = new();
    private readonly bool[] levels = { true, true, true, true, true, true };
    private readonly byte[] bytes = new byte[128 * 1024];
    private readonly char[] characters = new char[128 * 1024];
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private bool visible, paused, follow = true, firstRead = true;
    private long position;
    private DateTime nextRead, creation;
    private string search = "", error = "";
    internal OutputLogPanel(string root) => file = Path.Combine(root, "runtime.log");
    internal void Open() { visible = true; nextRead = default; }
    private void Read()
    {
        if (paused || DateTime.UtcNow < nextRead) return;
        nextRead = DateTime.UtcNow.AddMilliseconds(500);
        try
        {
            if (!File.Exists(file)) { error = "尚无日志输出。"; return; }
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var created = File.GetCreationTimeUtc(file);
            var reset = firstRead || stream.Length < position || created != creation;
            var skipPartial = false;
            if (reset || stream.Length - position > bytes.Length)
            {
                position = Math.Max(0, stream.Length - bytes.Length);
                skipPartial = position > 0;
                decoder.Reset(); buffer.Reset();
            }
            creation = created; firstRead = false;
            stream.Position = position;
            var count = stream.Read(bytes, 0, bytes.Length); position += count;
            var start = skipPartial ? Array.IndexOf(bytes, (byte)'\n', 0, count) + 1 : 0;
            // A tail beginning inside an UTF-8 sequence must start at a complete line.
            if (skipPartial && start == 0) { error = "当前日志行过长，等待下一条输出。"; return; }
            var length = decoder.GetChars(bytes, start, count - start, characters, 0, false);
            buffer.Append(new string(characters, 0, length)); error = "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { error = "读取日志失败: " + exception.Message; }
    }
    internal void Draw()
    {
        if (!visible) return;
        Read();
        var display = ImGui.GetIO().DisplaySize;
        var available = Vector2.Max(Vector2.One, display - new Vector2(24));
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(new Vector2(440, 280), available), available);
        ImGui.SetNextWindowSize(Vector2.Min(new Vector2(920, 620), available), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(display * .5f, ImGuiCond.Appearing, new Vector2(.5f));
        ImGui.SetNextWindowBgAlpha(1);
        if (ImGui.Begin(UiText.T("输出日志", "輸出日誌", "Output logs", "ログ出力") + "###output-log", ref visible, ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.SetWindowPos(ImGuiAppearance.VisiblePosition(ImGui.GetWindowPos(),ImGui.GetWindowSize(),display));
            var labels = new[] { "调试", "信息", "警告", "错误", "严重错误", "未分级" };
            for (var i = 0; i < labels.Length; i++)
            {
                if (i > 0) ImGuiAppearance.SameLineIfFits(ImGui.CalcTextSize(labels[i]).X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
                ImGui.Checkbox(labels[i], ref levels[i]);
            }
            ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##log-search", "搜索日志内容", ref search, 512);
            ImGui.Checkbox("暂停刷新", ref paused); ImGui.SameLine(); ImGui.Checkbox("跟随最新", ref follow);
            var entries = buffer.Filter(levels, search).ToArray();
            if (ImGui.Button("复制筛选结果")) ImGui.SetClipboardText(string.Join("\n", entries.Select(entry => entry.Text)));
            ImGui.SameLine(); ImGui.TextDisabled($"{entries.Length} / {buffer.Entries.Count} 条（最近 {OutputLogBuffer.Limit} 条）");
            if (error.Length > 0) PluginUiText.Wrapped(error);
            ImGui.BeginChild("log-lines", Vector2.Zero, true, ImGuiWindowFlags.HorizontalScrollbar);
            foreach (var entry in entries)
            {
                var color = entry.Level switch
                {
                    PluginLogLevel.Warning => new Vector4(1,.78f,.35f,1),
                    PluginLogLevel.Error or PluginLogLevel.Critical => new Vector4(1,.45f,.4f,1),
                    PluginLogLevel.Debug => new Vector4(.6f,.65f,.7f,1),
                    _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text]
                };
                ImGui.PushStyleColor(ImGuiCol.Text, color); ImGui.TextUnformatted(entry.Text); ImGui.PopStyleColor();
            }
            if (entries.Length == 0) ImGui.TextDisabled("没有符合筛选条件的日志。");
            if (follow && !paused) ImGui.SetScrollHereY(1);
            ImGui.EndChild();
        }
        ImGui.End();
    }
}
