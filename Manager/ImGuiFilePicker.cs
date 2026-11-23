using System.Numerics;
using ImGuiNET;

namespace Gkms.Manager;

// A file picker rendered in the game, including navigation and path entry.
internal sealed class ImGuiFilePicker
{
    private string directory=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), selected="", title="", error="";
    private string[] folders=Array.Empty<string>(),files=Array.Empty<string>();
    private Action<string>? accept;
    private bool open,requestOpen;
    public void Open(string title,Action<string> accept)
    { this.title=title; this.accept=accept; selected=""; open=true; requestOpen=true; Refresh(); }
    private void Refresh()
    {
        try { directory=Path.GetFullPath(directory); folders=Directory.GetDirectories(directory).OrderBy(s=>s).ToArray(); files=Directory.GetFiles(directory,"*.zip").OrderBy(s=>s).ToArray(); error=""; }
        catch(Exception e) { folders=files=Array.Empty<string>(); error=e.Message; }
    }
    public void Draw()
    {
        if(!open) return;
        var popup=UiText.T("选择 ZIP 文件","選擇 ZIP 檔案","Select ZIP file","ZIP ファイルを選択")+"###file-picker";
        if(requestOpen) {ImGui.OpenPopup(popup); requestOpen=false;}
        ImGuiAppearance.PrepareModal(700,540);
        if(!ImGui.BeginPopupModal(popup,ref open,ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoResize)) return;
        ImGuiAppearance.PlaceModal();
        Gkms.Sdk.PluginUiText.Wrapped(title);
        var go=UiText.T("前往","前往","Go","移動");
        ImGui.SetNextItemWidth(-ImGui.CalcTextSize(go).X-ImGui.GetStyle().FramePadding.X*2-ImGui.GetStyle().ItemSpacing.X);
        if(ImGui.InputText("##directory",ref directory,4096,ImGuiInputTextFlags.EnterReturnsTrue)) {selected=""; Refresh();}
        ImGui.SameLine(); if(ImGui.Button(go)) {selected=""; Refresh();}
        if(ImGui.Button(UiText.T("上一级","上一層","Parent","上へ")))
        {try {var parent=Directory.GetParent(directory); if(parent!=null) {directory=parent.FullName; selected=""; Refresh();}} catch(Exception e) {error=e.Message;}}
        foreach(var drive in DriveInfo.GetDrives()) { ImGuiAppearance.SameLineIfFits(ImGui.CalcTextSize(drive.Name).X+ImGui.GetStyle().FramePadding.X*2); if(ImGui.SmallButton(drive.Name)) {directory=drive.Name; selected=""; Refresh();} }
        ImGui.BeginChild("files",new Vector2(0,-ImGui.GetFrameHeightWithSpacing()*3),true);
        string? navigate=null;
        foreach(var folder in folders) if(ImGui.Selectable(UiText.T("文件夹  ","資料夾  ","Folder  ","フォルダー  ")+Path.GetFileName(folder))) navigate=folder;
        foreach(var file in files) if(ImGui.Selectable(Path.GetFileName(file),selected==file)) selected=file;
        ImGui.EndChild();
        if(navigate!=null) {directory=navigate; selected=""; Refresh();}
        ImGui.TextUnformatted(selected.Length==0?UiText.T("请选择 .zip 文件","請選擇 .zip 檔案","Select a .zip file",".zip ファイルを選択"):Path.GetFileName(selected));
        if(selected.Length>0 && ImGui.IsItemHovered()) ImGui.SetTooltip(selected);
        ImGui.TextColored(new Vector4(1,.45f,.4f,1),error);
        if(error.Length>0 && ImGui.IsItemHovered()) ImGui.SetTooltip(error);
        ImGui.BeginDisabled(selected.Length==0);
        if(ImGui.Button(UiText.T("选择","選擇","Select","選択"))) { open=false; ImGui.CloseCurrentPopup(); accept?.Invoke(selected); }
        ImGui.EndDisabled(); ImGui.SameLine();
        if(ImGui.Button(UiText.Cancel)) {open=false; ImGui.CloseCurrentPopup();}
        ImGui.EndPopup();
    }
}
