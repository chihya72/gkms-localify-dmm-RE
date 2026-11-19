using Gkms.Host;

namespace Gkms.Manager;

internal sealed class ResourceUpdateDialog : Form
{
    public int Installed { get; private set; }

    public ResourceUpdateDialog(Package package, string dataDirectory)
    {
        Text = package.Manifest.Name + " · 更新资源";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
        var label = new Label { AutoSize = true, Text = "资源来源（留空使用默认更新源）" };
        var source = new TextBox { Width = 340, PlaceholderText = "本地 ZIP 路径、ZIP 网址或发布 API 地址" };
        var browse = new Button { AutoSize = true, Text = "浏览…" };
        var status = new Label { AutoSize = true, MaximumSize = new Size(335, 0), Anchor = AnchorStyles.Left };
        var install = new Button { AutoSize = true, Text = "更新" };
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(source, 0, 1);
        layout.Controls.Add(browse, 1, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(install, 1, 2);
        Controls.Add(layout);
        AcceptButton = install;

        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "ZIP 资源包 (*.zip)|*.zip", CheckFileExists = true };
            if (picker.ShowDialog(this) == DialogResult.OK) source.Text = picker.FileName;
        };
        var busy = false;
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        install.Click += async (_, _) =>
        {
            var input = source.Text;
            busy = true;
            install.Enabled = browse.Enabled = source.Enabled = false;
            status.Text = "正在更新资源…";
            try
            {
                Installed = await Task.Run(() => PluginHost.UpdateResourcesAsync(package, dataDirectory, input));
                busy = false;
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) { status.Text = error.GetBaseException().Message; }
            finally { busy = false; install.Enabled = browse.Enabled = source.Enabled = true; }
        };
    }
}
