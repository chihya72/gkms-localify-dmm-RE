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
        var available=Vector2.Max(new Vector2(100,120),ImGui.GetIO().DisplaySize-new Vector2(24));
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(new Vector2(360,280),available),available);
        ImGui.SetNextWindowSize(new Vector2(650,480),ImGuiCond.FirstUseEver);
        if(!ImGui.BeginPopupModal(popup,ref open,ImGuiWindowFlags.NoSavedSettings)) return;
        ImGui.TextUnformatted(title);
        ImGui.SetNextItemWidth(-100);
        if(ImGui.InputText("##directory",ref directory,4096,ImGuiInputTextFlags.EnterReturnsTrue)) Refresh();
        ImGui.SameLine(); if(ImGui.Button(UiText.T("前往","前往","Go","移動"))) Refresh();
        if(ImGui.Button(".."))
        {try {var parent=Directory.GetParent(directory); if(parent!=null) {directory=parent.FullName; Refresh();}} catch(Exception e) {error=e.Message;}}
        foreach(var drive in DriveInfo.GetDrives()) { ImGui.SameLine(); if(ImGui.SmallButton(drive.Name)) {directory=drive.Name; Refresh();} }
        ImGui.BeginChild("files",new Vector2(0,-100),true);
        string? navigate=null;
        foreach(var folder in folders) if(ImGui.Selectable("[DIR] "+Path.GetFileName(folder),false,ImGuiSelectableFlags.AllowDoubleClick) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) navigate=folder;
        foreach(var file in files) if(ImGui.Selectable(Path.GetFileName(file),selected==file)) selected=file;
        ImGui.EndChild();
        if(navigate!=null) {directory=navigate; selected=""; Refresh();}
        Gkms.Sdk.PluginUiText.Wrapped(selected.Length==0?"ZIP":selected); Gkms.Sdk.PluginUiText.Wrapped(error);
        ImGui.BeginDisabled(selected.Length==0);
        if(ImGui.Button(UiText.T("选择","選擇","Select","選択"))) { open=false; ImGui.CloseCurrentPopup(); accept?.Invoke(selected); }
        ImGui.EndDisabled(); ImGui.SameLine();
        if(ImGui.Button(UiText.Cancel)) {open=false; ImGui.CloseCurrentPopup();}
        ImGui.EndPopup();
    }
}
