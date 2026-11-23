using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json.Nodes;
using Gkms.Host;
using ImGuiNET;

namespace Gkms.Manager;

internal sealed class OverlayPanel : IDisposable
{
    private readonly string root,packagesPath;
    private readonly Action<string> log;
    private readonly ConcurrentQueue<Action> completed=new();
    private readonly ImGuiFilePicker picker=new();
    private readonly PluginRepositoryPanel repositories;
    private readonly object lifetime=new();
    private IReadOnlyDictionary<string,Package> catalog=new Dictionary<string,Package>();
    private readonly Dictionary<string,bool> pending=new();
    private HashSet<string> enabled=new();
    private SettingsUiSession? session;
    private readonly Dictionary<string,ResourceUpdatePanel> updates=new();
    private PluginInstallation.Prepared? prepared;
    private string selected="",filter="",status="";
    private string[] active=Array.Empty<string>();
    private Dictionary<string,string[]> warnings=new();
    private Dictionary<string,PluginRuntimeStatus> runtimeStates=new();
    private bool connected, pollingStatus;
    private int runtimeRevision;
    private DateTime nextStatusPoll;
    private bool busy,confirmInstall,confirmRemove;
    private bool disposed;
    private Task? operation;
    private string? observedProfile;
    private DateTime nextProfilePoll;
    public OverlayPanel(string root,Action<string> log,string? packagesPath=null)
    {
        this.root=root; this.log=log; this.packagesPath=packagesPath??Path.Combine(root,"plugins"); UiText.Load(root); Refresh();
        repositories=new(root,CloseSettings,Refresh,log);
    }
    public string Status { get=>status; set=>status=value; }
    private void Refresh()
    {
        CloseSettings(); var issues=new List<string>(); catalog=Catalog.Discover(packagesPath,issues.Add);
        foreach(var (id,update) in updates.ToArray())
            if(!update.Busy && (!catalog.TryGetValue(id,out var package) || package.Directory!=update.PackageDirectory))
            {update.Dispose(); updates.Remove(id);}
        enabled=Catalog.ReadProfile(Path.Combine(root,"profile.json"),catalog).Enabled.ToHashSet();
        observedProfile = File.Exists(Path.Combine(root,"profile.json")) ? File.ReadAllText(Path.Combine(root,"profile.json")) : "";
        foreach(var id in pending.Keys.Where(id=>!catalog.ContainsKey(id) && !enabled.Contains(id)).ToArray()) pending.Remove(id);
        status=string.Join("\n",issues);
        repositories?.SyncLocal();
    }
    private void CloseSettings() { session?.Dispose(); session=null; }
    private void Select(string id) { if(selected==id) return; CloseSettings(); selected=id; }
    private void Run(Func<Task<Action>> work)
    {
        if(busy) return; busy=true;
        operation=Task.Run(async()=>
        {
            try {var next=await work(); completed.Enqueue(next);}
            catch(Exception e) {log(e.ToString()); completed.Enqueue(()=>status=e.GetBaseException().Message);}
            finally {completed.Enqueue(()=>busy=false);}
        });
    }
    private void Runtime(string command,string? id=null) => Run(async()=>
    { var reply=await RuntimeClient.SendAsync(root,command,id); return ()=>ApplyReply(reply); });
    private void ApplyReply(RuntimeReply? reply, bool showMessage=true)
    {
        runtimeRevision++;
        if(showMessage || (reply==null && connected)) status=reply?.Message??UiText.Offline;
        else if(reply!=null && !connected && status==UiText.Offline) status="";
        connected=reply!=null;
        active=reply?.Active??Array.Empty<string>(); warnings=reply?.Warnings??new();
        runtimeStates=reply?.States??new();
    }
    private void PollStatus()
    {
        if(busy || pollingStatus || DateTime.UtcNow<nextStatusPoll) return;
        nextStatusPoll=DateTime.UtcNow.AddSeconds(1); pollingStatus=true;
        var revision=runtimeRevision;
        _=Task.Run(async()=>
        {
            RuntimeReply? reply=null;
            try { reply=await RuntimeClient.SendAsync(root,"status"); }
            catch(Exception error) { log("读取插件状态失败: "+error.GetBaseException().Message); }
            var result=reply;
            completed.Enqueue(()=>{pollingStatus=false; if(!busy && revision==runtimeRevision) ApplyReply(result,false);});
        });
    }
    public bool Draw()
    {
        while(completed.TryDequeue(out var done)) done();
        PollStatus();
        if (!busy && DateTime.UtcNow >= nextProfilePoll)
        {
            nextProfilePoll = DateTime.UtcNow.AddSeconds(1);
            try
            {
                var file = Path.Combine(root, "profile.json");
                var contents = File.Exists(file) ? File.ReadAllText(file) : "";
                if (contents != observedProfile)
                {
                    // Refresh the persisted selection without discarding unsaved local edits.
                    var profile = Catalog.ReadProfile(file, catalog);
                    enabled = profile.Enabled.ToHashSet();
                    observedProfile = contents;
                    Runtime("status");
                }
            }
            catch (Exception error) { status = error.GetBaseException().Message; }
        }
        repositories.Process();
        foreach(var update in updates.Values) update.ProcessUpdates();
        var io=ImGui.GetIO();
        var available=Vector2.Max(new Vector2(100,120),io.DisplaySize-new Vector2(24));
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(new Vector2(520,360),available),available);
        ImGui.SetNextWindowSize(new Vector2(Math.Min(1100,io.DisplaySize.X-24),Math.Min(760,io.DisplaySize.Y-24)),ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(io.DisplaySize*.5f,ImGuiCond.FirstUseEver,new Vector2(.5f));
        bool open=true;
        if(!ImGui.Begin("Gakumas Localify RE###manager",ref open,ImGuiWindowFlags.NoSavedSettings)) {ImGui.End(); return open;}
        ImGui.SetWindowPos(ImGuiAppearance.VisiblePosition(ImGui.GetWindowPos(), ImGui.GetWindowSize(), io.DisplaySize));
        ImGui.TextDisabled("Ctrl+U  ·  Dear ImGui"); ImGui.SameLine();
        ImGui.SetNextItemWidth(90); var language=Array.IndexOf(UiText.Languages,UiText.Language);
        if(ImGui.Combo("##language",ref language,UiText.Languages,UiText.Languages.Length)) {CloseSettings(); UiText.Select(UiText.Languages[language]);}
        ImGui.Separator();
        if(ImGui.BeginTabBar("manager-pages"))
        {
            if(ImGui.BeginTabItem(UiText.T("已安装","已安裝","Installed","インストール済み")))
            { DrawInstalled(); ImGui.EndTabItem(); }
            if(ImGui.BeginTabItem(UiText.T("插件库","外掛程式庫","Plugin catalog","プラグイン一覧")))
            { ImGui.BeginDisabled(busy || updates.Values.Any(u=>u.Busy)); repositories.DrawLibrary(); ImGui.EndDisabled(); ImGui.EndTabItem(); }
            if(ImGui.BeginTabItem(UiText.T("订阅源","訂閱來源","Repositories","リポジトリ")))
            { ImGui.BeginDisabled(busy); repositories.DrawSources(); ImGui.EndDisabled(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.End();
        DrawConfirmations(); picker.Draw(); return open;
    }
    private void DrawInstalled()
    {
        ImGui.BeginDisabled(repositories.Changing);
        ImGui.BeginDisabled(busy);
        if(ImGui.Button(UiText.T("安装插件","安裝插件","Install plugin","プラグイン追加"))) picker.Open("Plugin ZIP",path=>Run(async()=>
        {
            var package=await Task.Run(()=>PluginInstallation.Prepare(root,path));
            lock(lifetime) {if(disposed) {package.Dispose(); return ()=>{};} prepared=package;}
            return ()=>confirmInstall=true;
        }));
        ImGui.SameLine();
        if(ImGui.Button(UiText.T("刷新列表","重新整理","Refresh","一覧更新"))) Run(async()=>{await Task.Run(()=>PluginInstallation.Cleanup(root)); return Refresh;});
        ImGui.SameLine(); if(ImGui.Button(UiText.T("重载配置","重新載入設定","Reload config","設定再読込"))) Runtime("reload-profile");
        ImGui.SameLine(); if(ImGui.Button(UiText.T("运行状态","執行狀態","Status","稼働状態"))) Runtime("status");
        ImGui.EndDisabled();
        ImGui.Separator();
        var width=Math.Clamp(ImGui.GetContentRegionAvail().X*.36f,190,360);
        ImGui.BeginChild("plugins",new Vector2(width,-75),true);
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##filter",UiText.T("搜索插件","搜尋插件","Search plugins","プラグイン検索"),ref filter,256);
        ImGui.BeginDisabled(busy);
        foreach(var id in catalog.Keys.Union(enabled).OrderBy(id=>id))
        {
            var package=catalog.GetValueOrDefault(id); var name=package?.Manifest.DisplayName(UiText.Language)??id+" (?)";
            if(filter.Length>0 && !name.Contains(filter,StringComparison.OrdinalIgnoreCase) && !id.Contains(filter,StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.PushID(id);
            var on=pending.GetValueOrDefault(id,enabled.Contains(id));
            if(ImGui.Checkbox("##enabled",ref on)) {if(on==enabled.Contains(id)) pending.Remove(id); else pending[id]=on;}
            ImGui.SameLine(); if(ImGui.Selectable(name,selected==id)) Select(id);
            if(ImGui.IsItemHovered()) ImGui.SetTooltip(id+" · "+package?.Manifest.Version);
            ImGui.PopID();
        }
        ImGui.EndDisabled(); ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("detail",new Vector2(0,-75),true);
        if(catalog.TryGetValue(selected,out var current)) DrawDetails(current);
        else Gkms.Sdk.PluginUiText.Wrapped(UiText.T("选择插件查看说明并编辑插件配置。","選擇插件查看說明與外掛設定。","Select a plugin to view details and edit its settings.","プラグインを選択して個別設定を編集してください。"));
        ImGui.EndChild();
        ImGui.Separator(); ImGui.BeginDisabled(busy || pending.Count==0);
        if(ImGui.Button(UiText.T("保存功能开关","儲存功能開關","Save switches","有効状態を保存")))
        {
            var changes=new Dictionary<string,bool>(pending); var snapshot=catalog;
            Run(async()=>{ProfileEditor.Apply(root,snapshot,changes); var reply=await RuntimeClient.SendAsync(root,"reload-profile"); return ()=>{pending.Clear(); Refresh(); ApplyReply(reply);};});
        }
        ImGui.EndDisabled(); ImGui.SameLine(); ImGui.TextDisabled(busy?UiText.T("处理中…","處理中…","Working…","処理中…"):pending.Count>0?$"{pending.Count} "+UiText.T("项未保存","項未儲存","unsaved","未保存"):"");
        Gkms.Sdk.PluginUiText.Wrapped(status); ImGui.EndDisabled();
    }
    private void DrawDetails(Package package)
    {
        var m=package.Manifest;
        ImGui.TextUnformatted(m.DisplayName(UiText.Language)); ImGui.TextDisabled($"{m.Id} · {m.Version} · API {m.ApiVersion}");
        DrawPluginState(m.Id);
        Gkms.Sdk.PluginUiText.Wrapped(m.Description??"");
        DrawCapability(UiText.T("插件热启停","外掛即時啟停","Plugin hot toggle","プラグイン即時切替"), m.SupportsHotEnable && m.SupportsHotDisable);
        DrawCapability(UiText.T("参数热更新","參數即時更新","Settings hot reload","設定即時更新"), m.HasSettingsUi ? m.SupportsSettingsReload : null);
        DrawCapability(UiText.T("资源热重载","資源即時重載","Resource hot reload","リソース即時再読込"), m.CanReloadResources);
        ImGui.BeginDisabled(busy);
        ImGui.BeginDisabled(updates.GetValueOrDefault(m.Id)?.Busy==true);
        if(ImGui.Button(UiText.T("卸载插件","解除安裝","Uninstall","削除"))) confirmRemove=true;
        ImGui.EndDisabled();
        if(m.CanReloadResources)
        {
            ImGui.SameLine(); ImGui.BeginDisabled(!connected || !active.Contains(m.Id));
            if(ImGui.Button(UiText.T("重载资源","重新載入資源","Reload resources","リソース再読込"))) Runtime("reload-resources",m.Id);
            ImGui.EndDisabled();
        }
        ImGui.EndDisabled(); ImGui.Separator();
        if(!Catalog.Supports(m.ApiVersion)) {Gkms.Sdk.PluginUiText.Wrapped("API incompatible"); return;}
        if(!m.HasSettingsUi && m.ResourceUpdater==null) return;
        if(!ImGui.BeginTabBar("details")) return;
        if(ImGui.BeginTabItem(UiText.T("插件配置","外掛設定","Plugin settings","プラグイン設定")))
        {
            if(m.HasSettingsUi && session==null) try
            {
                session=new SettingsUiSession(package,UiText.Language,
                    ()=>Catalog.ReadProfile(Path.Combine(root,"profile.json"),catalog).Settings?.GetValueOrDefault(m.Id)??default,
                    (values,replace)=>{ProfileEditor.Apply(root,catalog,new Dictionary<string,bool>(),replace:new(m.Id,JsonNode.Parse(values.GetRawText())!.AsObject()),merge:!replace); Runtime("reload-profile");},
                    async token=>(await RuntimeClient.SendAsync(root,"reload-resources",m.Id).WaitAsync(token))?.Message??UiText.Offline,log);
                session.OpenConfigUi();
            }
            catch(Exception e) {session?.Dispose(); session=null; status=e.GetBaseException().Message;}
            ImGui.BeginDisabled(busy);
            if(session!=null && !session.DrawFrame()) throw new InvalidOperationException(session.Fault);
            ImGui.EndDisabled();
            if(m.ResourceUpdater!=null)
            {
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                ImGui.TextUnformatted(UiText.T("资源更新","資源更新","Resource updates","リソース更新"));
                if(!updates.TryGetValue(m.Id,out var resources)) updates[m.Id]=resources=new ResourceUpdatePanel(package,root,log);
                ImGui.PushID("resource-update");
                ImGui.BeginDisabled(busy);
                if(session!=null && !session.DrawResourceSettings()) throw new InvalidOperationException(session.Fault);
                ImGui.EndDisabled();
                ImGui.Spacing();
                resources.Draw(); ImGui.PopID();
            }
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
    private static void DrawCapability(string label, bool? supported)
    {
        ImGui.TextUnformatted(label+":"); ImGui.SameLine();
        var color=supported==true?new Vector4(.4f,.85f,.6f,1):supported==false?new Vector4(1,.4f,.4f,1):new Vector4(.65f,.65f,.65f,1);
        ImGui.TextColored(color,supported==true?UiText.T("支持","支援","Yes","対応"):
            supported==false?UiText.T("不支持","不支援","No","非対応"):UiText.T("无参数","無參數","No settings","設定なし"));
    }
    private void DrawPluginState(string id)
    {
        if(!connected) { ImGui.TextDisabled(UiText.T("未连接","未連線","Disconnected","未接続")); return; }
        if(!runtimeStates.TryGetValue(id,out var state))
        {
            var issues=warnings.GetValueOrDefault(id)??Array.Empty<string>();
            state=new(issues.Length>0?PluginRunState.Failed:active.Contains(id)?PluginRunState.Running:PluginRunState.Disabled,active.Contains(id),issues);
        }
        var label=state.State switch
        {
            PluginRunState.Running=>UiText.T("运行中","執行中","Running","稼働中"),
            PluginRunState.RestartRequired=>UiText.T("待重启","待重啟","Restart required","再起動待ち"),
            PluginRunState.Failed=>UiText.T("失败","失敗","Failed","失敗"),
            _=>UiText.T("已停用","已停用","Disabled","無効")
        };
        var color=state.State switch
        {
            PluginRunState.Running=>new Vector4(.4f,.85f,.6f,1),
            PluginRunState.Failed=>new Vector4(1,.4f,.4f,1),
            PluginRunState.RestartRequired=>new Vector4(1,.7f,.3f,1),
            _=>new Vector4(.65f,.65f,.65f,1)
        };
        ImGui.TextColored(color,label);
        if(state.State==PluginRunState.Failed)
            foreach(var detail in state.Details) Gkms.Sdk.PluginUiText.Wrapped(detail);
    }
    private void DrawConfirmations()
    {
        var installTitle=UiText.T("安装插件","安裝插件","Install plugin","プラグイン追加")+"###install-plugin";
        var removeTitle=UiText.T("卸载插件","解除安裝","Uninstall plugin","プラグイン削除")+"###uninstall-plugin";
        if(confirmInstall) {confirmInstall=false; ImGui.OpenPopup(installTitle);}
        bool modalOpen=true;
        if(ImGui.BeginPopupModal(installTitle,ref modalOpen,ImGuiWindowFlags.AlwaysAutoResize))
        {
            if(prepared!=null)
            {
                ImGui.TextUnformatted(prepared.Manifest.DisplayName(UiText.Language)+" · "+prepared.Manifest.Version);
                Gkms.Sdk.PluginUiText.Wrapped(prepared.Manifest.Description??"");
                Gkms.Sdk.PluginUiText.Wrapped(UiText.T("安装后默认停用，配置与资源分别保存。","安裝後預設停用。","Installed plugins are disabled by default.","追加直後は無効です。"));
                if(ImGui.Button(UiText.T("确认安装","確認安裝","Install","追加")))
                {var package=prepared; prepared=null; ImGui.CloseCurrentPopup(); Run(async()=>{using(package){var installed=await Task.Run(()=>PluginInstallation.Install(package)); return ()=>{Refresh(); Select(installed.Manifest.Id);};}});}
            }
            ImGui.SameLine(); if(ImGui.Button(UiText.Cancel)) {prepared?.Dispose(); prepared=null; ImGui.CloseCurrentPopup();}
            ImGui.EndPopup();
        }
        if(!modalOpen) {prepared?.Dispose(); prepared=null;}
        modalOpen=true;
        if(confirmRemove) {confirmRemove=false; ImGui.OpenPopup(removeTitle);}
        if(ImGui.BeginPopupModal(removeTitle,ref modalOpen,ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(selected);
            Gkms.Sdk.PluginUiText.Wrapped(UiText.T("卸载前先停用；保留插件配置、私有数据与汉化资源。","先停用並保留設定與資源。","Disable and uninstall? Settings, private data and translation resources are kept.","無効化して削除します。個別設定とリソースは保持します。"));
            if(ImGui.Button(UiText.T("确认卸载","確認解除安裝","Uninstall","削除")))
            {var id=selected; CloseSettings(); if(updates.Remove(id,out var update)) update.Dispose(); ImGui.CloseCurrentPopup(); Run(async()=>{var removed=await PluginInstallation.UninstallAsync(root,id); return ()=>{pending.Remove(id); Select(""); Refresh(); if(!removed) status=UiText.T("已停用；占用文件将在下次启动后清理。","已停用；下次啟動清理檔案。","Disabled; locked files will be cleaned up next launch.","無効化済み。使用中のファイルは次回起動後に削除します。");};});}
            ImGui.SameLine(); if(ImGui.Button(UiText.Cancel)) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
    public void Dispose()
    {
        repositories.Dispose();
        CloseSettings(); lock(lifetime) {disposed=true; prepared?.Dispose(); prepared=null;}
        foreach(var update in updates.Values) update.Dispose(); updates.Clear();
        // Package transactions already running finish on their worker; they retain
        // their own prepared package and lock until completion.
    }
}
