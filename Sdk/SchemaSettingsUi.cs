using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImGuiNET;

namespace Gkms.Sdk;

/// <summary>Reusable ImGui settings implementation; custom plugins may replace or extend it.</summary>
public class SchemaSettingsUi : IPluginSettingsUi
{
    protected ISettingsUiContext Context { get; private set; } = null!;
    private JsonObject values = new();
    private bool open;
    private int preset = -1;
    public virtual void Initialize(ISettingsUiContext context)
    {
        Context = context;
        values = context.Settings.ValueKind == JsonValueKind.Object ? JsonNode.Parse(context.Settings.GetRawText())!.AsObject() : new();
        context.UiBuilder.OpenConfigUi += Open;
        context.UiBuilder.Draw += Draw;
    }
    private void Open() => open = true;
    private string L(string cn, string en) => Context.Language switch
    {
        "en" => en,
        "zh-TW" => cn switch { "预设" => "預設", "保存配置" => "儲存設定",
            _ => cn },
        "ja" => cn switch { "预设" => "プリセット", "保存配置" => "設定を保存",
            _ => cn },
        _ => cn
    };
    public virtual void Draw()
    {
        DrawSection(Context.Fields.Where(f => f.Section != "resources").ToArray());
    }
    public virtual void DrawResourceSettings()
    {
        DrawSection(Context.Fields.Where(f => f.Section == "resources").ToArray());
    }
    /// <summary>Save the edited settings from all sections through the manager's shared action.</summary>
    public void SaveSettings()
    {
        var keys = Context.Fields.Select(field => field.Key).ToHashSet();
        Context.SaveSettings(JsonSerializer.SerializeToElement(values.Where(pair => keys.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }
    private void DrawSection(IReadOnlyList<PluginSetting> fields)
    {
        if (!open || fields.Count == 0) return;
        var keys = fields.Select(f => f.Key).ToHashSet();
        foreach (var field in fields)
        {
            ImGui.PushID(field.Key);
            try { DrawField(field); }
            catch (Exception error) { PluginUiText.Wrapped(field.Key + ": " + error.GetBaseException().Message); }
            finally { ImGui.PopID(); }
        }
        var presets = Context.Presets.Where(p => p.Values.EnumerateObject().All(v => keys.Contains(v.Name))).ToArray();
        if (presets.Length > 0)
        {
            var names = presets.Select(p => p.Name).ToArray();
            if (ImGui.Combo(L("预设", "Preset"), ref preset, names, names.Length))
            {
                try
                {
                    foreach (var pair in presets[preset].Values.EnumerateObject()) values[pair.Name] = JsonNode.Parse(pair.Value.GetRawText());
                }
                catch (Exception error) { PluginUiText.Wrapped(error.GetBaseException().Message); }
            }
        }
    }
    private void DrawField(PluginSetting field)
    {
        var node = values[field.Key] ?? JsonNode.Parse(field.Default.GetRawText())!;
        var label = field.DisplayLabel(Context.Language);
        ImGui.SetNextItemWidth(Math.Max(160, ImGui.GetContentRegionAvail().X * .52f));
        switch (field.Type)
        {
            case "boolean":
                var check = node.GetValue<bool>();
                if (ImGui.Checkbox(label, ref check)) values[field.Key] = check;
                break;
            case "integer":
                var integer = node.GetValue<int>();
                if (ImGui.InputInt(label, ref integer)) values[field.Key] = integer;
                break;
            case "number":
                var number = node.GetValue<double>();
                if (ImGui.InputDouble(label, ref number, .05, 1, "%.4f")) values[field.Key] = number;
                break;
            case "string" when field.Choices != null:
                var selected = Array.IndexOf(field.Choices, node.GetValue<string>());
                if (ImGui.Combo(label, ref selected, field.Choices, field.Choices.Length)) values[field.Key] = field.Choices[selected];
                break;
            case "string":
                var text = node.GetValue<string>();
                if (field.Section == "resources")
                {
                    PluginUiText.Wrapped(label);
                    ImGui.SetNextItemWidth(-1);
                    label = "##value";
                }
                if (ImGui.InputText(label, ref text, (uint)Math.Max(4096, System.Text.Encoding.UTF8.GetByteCount(text) + 1024))) values[field.Key] = text;
                break;
            default:
                ImGui.TextUnformatted(label);
                var array = node.AsArray();
                for (var i = 0; i < array.Count; i++)
                {
                    ImGui.PushID(i); ImGui.SetNextItemWidth(160);
                    try
                    {
                        var component = field.ElementLabels?[i] ?? (i + 1).ToString();
                        if (field.Type == "integer-array")
                        {
                            var value = array[i]!.GetValue<int>();
                            if (ImGui.InputInt(component, ref value)) { array[i] = value; if (values[field.Key] == null) values[field.Key] = array; }
                        }
                        else
                        {
                            var value = array[i]!.GetValue<double>();
                            if (ImGui.InputDouble(component, ref value, .05, 1, "%.4f")) { array[i] = value; if (values[field.Key] == null) values[field.Key] = array; }
                        }
                    }
                    finally { ImGui.PopID(); }
                }
                break;
        }
    }
    public virtual void Dispose()
    {
        open = false;
        if (Context == null) return;
        Context.UiBuilder.Draw -= Draw; Context.UiBuilder.OpenConfigUi -= Open;
    }
}
