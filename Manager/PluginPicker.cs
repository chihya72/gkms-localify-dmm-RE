using Gkms.Host;

namespace Gkms.Manager;

internal sealed class PluginPicker : Form
{
    private readonly string root;
    private readonly IReadOnlyDictionary<string, Package> catalog;
    private readonly HashSet<string> initial;
    private readonly ListView plugins = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
    private readonly Button save = new() { Text = "保存选择", AutoSize = true, Enabled = false };

    public PluginPicker(string root)
    {
        this.root = root;
        Text = "Gakumas Localify · 插件管理";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 420);
        ClientSize = new Size(610, 560);
        Padding = new Padding(16);
        plugins.Columns.Add("插件", 270);
        plugins.Columns.Add("ID", 170);
        plugins.Columns.Add("接口版本", 95);
        var errors = new List<string>();
        catalog = Catalog.Discover(Path.Combine(root, "plugins"), errors.Add);
        initial = Catalog.ReadProfile(Path.Combine(root, "profile.json"), catalog).Enabled.ToHashSet();
        foreach (var (id, package) in catalog.OrderBy(p => p.Key))
        {
            var m = package.Manifest;
            plugins.Items.Add(new ListViewItem(new[] { m.Name, id, m.ApiVersion.ToString() })
                { Checked = initial.Contains(id), Tag = id });
        }
        foreach (var missing in initial.Except(catalog.Keys))
            plugins.Items.Add(new ListViewItem(new[] { "插件包缺失", missing, "—" }) { Checked = true, Tag = missing });
        var status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Text = errors.Count == 0 ? "勾选需要的插件。保存后，下次进入游戏时生效。" : string.Join(Environment.NewLine, errors) };
        var edit = new Button { Text = "编辑参数", AutoSize = true, Enabled = false };
        plugins.SelectedIndexChanged += (_, _) => edit.Enabled = plugins.SelectedItems.Count == 1 &&
            catalog.ContainsKey((string)plugins.SelectedItems[0].Tag);
        edit.Click += (_, _) =>
        {
            if (plugins.SelectedItems.Count != 1) return;
            try { PluginOptions.Edit(this, root, catalog[(string)plugins.SelectedItems[0].Tag], catalog); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 3, Padding = new Padding(0, 8, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(status, 0, 0); bar.Controls.Add(edit, 1, 0); bar.Controls.Add(save, 2, 0);
        Controls.Add(plugins); Controls.Add(bar);
        plugins.ItemChecked += (_, _) => save.Enabled = true;
        save.Click += (_, _) =>
        {
            try
            {
                var changes = plugins.Items.Cast<ListViewItem>().Where(i => i.Checked != initial.Contains((string)i.Tag))
                    .ToDictionary(i => (string)i.Tag, i => i.Checked);
                ProfileEditor.Apply(root, catalog, changes);
                initial.Clear();
                initial.UnionWith(plugins.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag));
                save.Enabled = false;
                status.Text = "选择已保存，下次进入游戏时生效。";
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
    }
}
