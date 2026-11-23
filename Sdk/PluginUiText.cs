using System.Globalization;
using System.Text;
using ImGuiNET;

namespace Gkms.Sdk;

public static class PluginUiText
{
    // The bundled native ImGui skips wrapped text incorrectly above a scroll clip.
    // Explicit line breaks keep measurement and rendering on its unwrapped path.
    public static void Wrapped(string? text)
    {
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var output = new StringBuilder();
        var line = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator((text ?? "").Replace("\r", ""));
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element == "\n") { output.Append(line).Append('\n'); line.Clear(); continue; }
            if (line.Length > 0 && ImGui.CalcTextSize(line.ToString() + element).X > width)
            { output.Append(line).Append('\n'); line.Clear(); }
            line.Append(element);
        }
        output.Append(line);
        ImGui.TextUnformatted(output.ToString());
    }
}
