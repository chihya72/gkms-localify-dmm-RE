using System.Numerics;
using Gkms.Sdk;
using ImGuiNET;

namespace Example.Greeting;

public sealed class SettingsUi : IPluginSettingsUi
{
    private ISettingsUiContext context = null!;
    private Options options = new();
    private bool visible;
    private string message = "";
    public void Initialize(ISettingsUiContext context)
    {
        this.context = context;
        options = context.ReadSettings<Options>();
        context.UiBuilder.OpenConfigUi += Open;
        context.UiBuilder.Draw += Draw;
    }
    private void Open() => visible = true;
    private void Draw()
    {
        if (!visible) return;
        ImGui.TextColored(new Vector4(.8f, .7f, .45f, 1), "Custom plugin settings");
        var text = options.Message;
        if (ImGui.InputText("Greeting", ref text, 1024)) options.Message = text;
        var enabled = options.LogOnStart;
        if (ImGui.Checkbox("Log on start", ref enabled)) options.LogOnStart = enabled;
        if (ImGui.Button("Save"))
        {
            try { context.SaveSettings(options); message = "Saved"; }
            catch (Exception error) { message = error.Message; }
        }
        ImGui.SameLine();
        if (ImGui.Button("Reload")) { context.ReloadSettings(); options = context.ReadSettings<Options>(); }
        ImGui.TextUnformatted(message);
    }
    public void Dispose()
    {
        if (context == null) return;
        context.UiBuilder.Draw -= Draw;
        context.UiBuilder.OpenConfigUi -= Open;
        visible = false;
    }
}
