using System.Numerics;
using ImGuiNET;

namespace Gkms.Manager;

internal static class ImGuiAppearance
{
    internal static Vector2 VisiblePosition(Vector2 position, Vector2 size, Vector2 display) =>
        Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, display - size));
    // Called between frames by both the native settings window and offline rendering tests.
    public static unsafe void Configure(float scale = 1)
    {
        var io = ImGui.GetIO(); io.NativePtr->IniFilename = null; io.NativePtr->LogFilename = null;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.Fonts.TexDesiredWidth=2048;
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
        if (File.Exists(font))
        {
            io.Fonts.Clear(); io.Fonts.AddFontFromFileTTF(font, 18 * scale, default, io.Fonts.GetGlyphRangesChineseFull());
        }
        ImGui.StyleColorsDark(); var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(18, 16); style.FramePadding = new Vector2(8, 6);
        style.ItemSpacing = new Vector2(10, 9); style.FrameRounding = 3;
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(.78f, .69f, .47f, 1);
    }
}
