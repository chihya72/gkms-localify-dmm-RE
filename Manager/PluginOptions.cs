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
        var editor = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, AcceptsTab = true,
            Text = current.ValueKind == JsonValueKind.Object ? JsonSerializer.Serialize(current, Catalog.Json) : "{}" };
        form.Controls.Add(editor);
        var ok = new Button { Text = "保存参数", AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        form.Controls.Add(buttons); form.CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            try
            {
                if (JsonNode.Parse(editor.Text) is not JsonObject options) throw new InvalidDataException("参数应为 JSON 对象。");
                ProfileEditor.Apply(root, packages, new Dictionary<string, bool>(), replace: new(id, options));
                form.DialogResult = DialogResult.OK;
            }
            catch (Exception e) { MessageBox.Show(form, e.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        form.ShowDialog(owner);
    }
}
