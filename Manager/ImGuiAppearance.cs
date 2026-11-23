using System.Numerics;
using ImGuiNET;

namespace Gkms.Manager;

internal static class ImGuiAppearance
{
    internal static Vector2 ModalCenter { get; set; }
    internal static void SameLineIfFits(float width)
    {
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <=
            ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X) ImGui.SameLine();
    }
    internal static void PrepareModal(float width = 560, float height = 0)
    {
        var display = ImGui.GetIO().DisplaySize;
        var available = Vector2.Max(Vector2.One, display - new Vector2(24));
        var scale = ImGui.GetFontSize() / 18f;
        width = Math.Min(width * scale, available.X);
        var size = new Vector2(width, height > 0 ? Math.Min(height * scale, available.Y) : 0);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, Math.Min(160 * scale, available.Y)), new Vector2(width, available.Y));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowPos(ModalCenter == Vector2.Zero ? display * .5f : ModalCenter, ImGuiCond.Always, new Vector2(.5f));
        ImGui.SetNextWindowBgAlpha(1);
    }
    internal static void PlaceModal()
    {
        var display=ImGui.GetIO().DisplaySize;
        var center=ModalCenter==Vector2.Zero?display*.5f:ModalCenter;
        ImGui.SetWindowPos(VisiblePosition(center-ImGui.GetWindowSize()*.5f,ImGui.GetWindowSize(),display));
    }
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
