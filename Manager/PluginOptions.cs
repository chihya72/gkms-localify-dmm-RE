using System.Text.Json;
using System.Text.Json.Nodes;
using Gkms.Host;

namespace Gkms.Manager;

internal static class PluginOptions
{
    public static void Edit(IWin32Window owner, string root, Package package, IReadOnlyDictionary<string, Package> packages)
    {
        var id = package.Manifest.Id;
        var current = Catalog.ReadProfile(Path.Combine(root, "profile.json"), packages).Settings?.GetValueOrDefault(id) ?? default;
        using var form = new Form { Text = package.Manifest.Name + " · 参数", ClientSize = new Size(510, 350),
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, Padding = new Padding(12) };
        var changes = new Dictionary<string, bool>();
        Action save;
        if (id == "localization")
        {
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var boxes = new Dictionary<string, CheckBox>();
            foreach (var (key, label, fallback) in new[]
            {
                ("interfaceText", "界面键值翻译", true), ("genericText", "通用文本翻译", true),
                ("story", "剧情翻译", true), ("font", "中文回退字体", true), ("subtitles", "主页语音字幕气泡", true),
                ("textTest", "翻译测试标记", false)
            })
            {
                var check = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(4, 6, 4, 6),
                    Checked = current.ValueKind == JsonValueKind.Object && current.TryGetProperty(key, out var setting)
                        ? setting.GetBoolean() : fallback };
                list.Controls.Add(check);
                boxes.Add(key, check);
            }
            form.Controls.Add(list);
            save = () =>
            {
                var options = new JsonObject();
                foreach (var (key, check) in boxes) options[key] = check.Checked;
                ProfileEditor.Apply(root, packages, changes, new Dictionary<string, JsonObject> { [id] = options });
            };
        }
        else
        {
            var editor = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, AcceptsTab = true,
                Text = current.ValueKind == JsonValueKind.Object ? JsonSerializer.Serialize(current, Catalog.Json) : "{}" };
            form.Controls.Add(editor);
            save = () =>
            {
                if (JsonNode.Parse(editor.Text) is not JsonObject options) throw new InvalidDataException("参数应为 JSON 对象。");
                ProfileEditor.Apply(root, packages, changes, replace: new(id, options));
            };
        }
        var ok = new Button { Text = "保存参数", AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        form.Controls.Add(buttons); form.CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            try { save(); form.DialogResult = DialogResult.OK; }
            catch (Exception e) { MessageBox.Show(form, e.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        form.ShowDialog(owner);
    }
}
