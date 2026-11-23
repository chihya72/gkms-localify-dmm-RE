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
    private string message = "", resourceMessage = "";
    private bool open, reloading;
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
            "重新读取" => "重新讀取", "重载资源" => "重新載入資源", "配置已保存。" => "設定已儲存。", "此插件无需参数。" => "此插件無需參數。", _ => cn },
        "ja" => cn switch { "预设" => "プリセット", "保存配置" => "設定を保存",
            "重新读取" => "設定を再読込", "重载资源" => "リソース再読込", "配置已保存。" => "設定を保存しました。", "此插件无需参数。" => "設定項目はありません。", _ => cn },
        _ => cn
    };
    public virtual void Draw()
    {
        DrawSection(Context.Fields.Where(f => f.Section != "resources").ToArray(), ref message, false);
    }
    public virtual void DrawResourceSettings()
    {
        DrawSection(Context.Fields.Where(f => f.Section == "resources").ToArray(), ref resourceMessage, true);
    }
    private void DrawSection(IReadOnlyList<PluginSetting> fields, ref string sectionMessage, bool resources)
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
                    sectionMessage = "";
                }
                catch (Exception error) { sectionMessage = error.Message; }
            }
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (ImGui.Button(L("保存配置", "Save settings")))
        {
            try
            {
                Context.SaveSettings(JsonSerializer.SerializeToElement(values.Where(p => keys.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value)));
                sectionMessage = L("配置已保存。", "Settings saved.");
            }
            catch (Exception error) { sectionMessage = error.GetBaseException().Message; }
        }
        ImGui.SameLine();
        if (ImGui.Button(L("重新读取", "Reload settings")))
        {
            try
            {
                Context.ReloadSettings();
                foreach (var key in keys)
                {
                    if (Context.Settings.ValueKind == JsonValueKind.Object && Context.Settings.TryGetProperty(key, out var saved))
                        values[key] = JsonNode.Parse(saved.GetRawText());
                    else values.Remove(key);
                }
                sectionMessage = "";
            }
            catch (Exception error) { sectionMessage = error.GetBaseException().Message; }
        }
        if (Context.CanReloadResources && (resources || !Context.Fields.Any(f => f.Section == "resources")))
        {
            ImGui.SameLine(); ImGui.BeginDisabled(reloading);
            if (ImGui.Button(L("重载资源", "Reload resources"))) { reloading = true; _ = Reload(resources); }
            ImGui.EndDisabled();
        }
        if (sectionMessage.Length > 0) { ImGui.Spacing(); PluginUiText.Wrapped(sectionMessage); }
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
    private async Task Reload(bool resources)
    {
        try
        {
            string result;
            try { result = await Context.ReloadResourcesAsync().ConfigureAwait(false); }
            catch (Exception error) { result = error.GetBaseException().Message; }
            Context.UiBuilder.Enqueue(() => { if (resources) resourceMessage = result; else message = result; reloading = false; });
        }
        catch (ObjectDisposedException) { }
    }
    public virtual void Dispose()
    {
        open = false;
        if (Context == null) return;
        Context.UiBuilder.Draw -= Draw; Context.UiBuilder.OpenConfigUi -= Open;
    }
}
