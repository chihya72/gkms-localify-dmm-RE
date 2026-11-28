using System.Collections.Concurrent;
using System.Numerics;
using Gkms.Host;
using Gkms.Sdk;
using ImGuiNET;

namespace Gkms.Manager;

internal sealed class ResourceUpdatePanel : IDisposable
{
    private readonly Package package;
    private readonly string root;
    private readonly Action<string> log;
    private readonly ConcurrentQueue<Action> completed=new();
    private readonly ImGuiFilePicker picker=new();
    private string source="",status="";
    private ResourceRelease? release;
    private CancellationTokenSource? stop;
    private float progress;
    private bool busy;
    public ResourceUpdatePanel(Package package,string root,Action<string> log) => (this.package,this.root,this.log)=(package,root,log);
    public bool Busy => busy;
    public string PackageDirectory => package.Directory;
    public string Status => status;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public void ProcessUpdates() {while(completed.TryDequeue(out var done)) done();}
    public void Draw()
    {
        ProcessUpdates();
        Gkms.Sdk.PluginUiText.Wrapped(UiText.T("本次来源（不保存）：留空使用插件配置的默认更新源。支持发布 API、ZIP 下载地址、本地 ZIP 文件；不支持文件夹。","本次來源（不儲存）：空白使用個別設定的預設來源。支援發佈 API、ZIP 網址與本機 ZIP 檔案；不支援資料夾。","Source for this operation (not saved). Blank uses the configured default. Supports release APIs, ZIP URLs and local ZIP files, not folders.","今回の更新元（保存されません）。空欄は設定済みの既定値。リリース API・ZIP URL・ローカル ZIP に対応。フォルダーは非対応。"));
        ImGui.BeginDisabled(busy);
        var browseLabel=UiText.T("浏览","瀏覽","Browse","参照");
        var right=ImGui.GetCursorPosX()+ImGui.GetContentRegionAvail().X-1;
        var browseWidth=ImGui.CalcTextSize(browseLabel).X+ImGui.GetStyle().FramePadding.X*2;
        ImGui.SetNextItemWidth(Math.Max(1,right-ImGui.GetCursorPosX()-browseWidth-ImGui.GetStyle().ItemSpacing.X));
        if(ImGui.InputText("##source",ref source,4096)) release=null;
        ImGui.SameLine(); if(ImGui.Button(browseLabel,new Vector2(browseWidth,0))) picker.Open("ZIP",path=>{source=path; release=null;});
        if(ImGui.Button(UiText.T("检查更新","檢查更新","Check updates","更新確認"))) Check();
        ImGui.SameLine(); ImGui.BeginDisabled(release==null);
        if(ImGui.Button(release?.IsCurrent==true?UiText.T("重新安装","重新安裝","Reinstall","再インストール"):UiText.T("安装资源","安裝資源","Install resources","インストール"))) Install();
        ImGui.EndDisabled(); ImGui.EndDisabled();
        if(busy) {ImGui.SameLine(); if(ImGui.Button(UiText.Cancel)) stop?.Cancel(); ImGui.ProgressBar(progress,new Vector2(-1,0),$"{progress:P0}");}
        Gkms.Sdk.PluginUiText.Wrapped(status);
        if(release!=null) {ImGui.Separator(); ImGui.TextUnformatted($"{release.InstalledVersion??"—"} → {release.Version??"ZIP"}"); Gkms.Sdk.PluginUiText.Wrapped(release.Description??"");}
        picker.Draw();
    }
    private void Report(string message) => log(PluginLog.WithSource(message,package.Manifest.Id));
    private void Run(string phase,Func<CancellationToken,Task<Action>> work)
    {
        if(busy) return; busy=true; progress=0; status=phase; Report(phase); stop=new(); var token=stop.Token;
        Completion=Task.Run(async()=>
        {
            try {var result=await work(token); completed.Enqueue(result);}
            catch(OperationCanceledException) {Report("资源操作已取消。"); completed.Enqueue(()=>status=UiText.T("资源操作已取消。","資源操作已取消。","Resource operation canceled.","リソース操作をキャンセルしました。"));}
            catch(Exception e) {Report("[Error] 资源操作失败: "+e); completed.Enqueue(()=>status=UiText.Error+": "+e.GetBaseException().Message);}
            finally {completed.Enqueue(()=>{busy=false; stop?.Dispose(); stop=null;});}
        });
    }
    internal void Check(string? input=null)
    {
        if(busy) return;
        input??=source; release=null;
        Run(UiText.T("正在检查资源版本…","正在檢查資源版本…","Checking resource version…","バージョン確認中…"),async token=>
        {
            var found=await PluginHost.CheckResourcesAsync(package,Path.Combine(root,".."),input,token);
            Report($"资源检查完成: 已安装 {found.InstalledVersion??"—"}，可用 {found.Version??"ZIP"}。");
            return ()=>{release=found; status=UiText.T("版本检查完成，可安装资源。","版本檢查完成。","Version checked; ready to install.","確認完了。インストールできます。");};
        });
    }
    internal void Install()
    {
        if(busy || release==null) return;
        var selected=release!;
        Run(UiText.T("正在准备并安装资源…","正在準備並安裝資源…","Preparing and installing resources…","リソースを準備・インストール中…"),async token=>
        {
            // Queue synchronously: Progress<T>'s thread-pool callbacks can arrive after installation completes.
            var reporter=new DownloadProgress(p=>completed.Enqueue(()=>
            {
                progress=p.Total>0?(float)Math.Clamp(p.Received/(double)p.Total.Value,0,1):0;
                status=p.Total>0 && p.Received>=p.Total
                    ? UiText.T("下载完成，正在校验并安装…","下載完成，正在驗證並安裝…","Downloaded; validating and installing…","ダウンロード完了。検証・インストール中…")
                    : $"{p.Received/1024} / {(p.Total.HasValue?(p.Total/1024).ToString():"?")} KiB";
            }));
            var count=await PluginHost.InstallResourcesAsync(package,Path.Combine(root,".."),selected,reporter,token);
            Report($"资源安装成功: {count} 个文件，目录 {Path.Combine(package.Directory,"resources")}。");
            var installed=await PluginHost.CheckResourcesAsync(package,Path.Combine(root,".."),selected.Download??selected.Source);
            string runtime;
            if (package.Manifest.Id == "localization") runtime=UiText.T("资源已安装。启动词典下次启动生效；剧情文件在后续请求时读取。","資源已安裝。啟動詞典下次啟動生效；劇情檔案於後續請求時讀取。","Installed. Startup dictionaries apply next launch; story files are read on subsequent requests.","インストール済み。辞書は次回起動時、シナリオは次の読み込み時に適用されます。");
            else if (!package.Manifest.CanReloadResources) runtime=UiText.T("资源已安装；生效时机见插件说明。","資源已安裝；生效時機請見外掛說明。","Resources installed; see the plugin description for when they apply.","リソースをインストールしました。適用時期はプラグインの説明をご覧ください。");
            else try {var reply=await RuntimeClient.SendAsync(root,"reload-resources",package.Manifest.Id); runtime=reply?.Message??UiText.T("游戏未连接；下次启用插件时加载。","遊戲未連線；下次啟用時載入。","Game offline; resources load when the plugin next starts.","ゲームに未接続。次回有効化時に読み込みます。");}
            catch(Exception e) {runtime=UiText.T("资源已安装，但重载失败: ","資源已安裝，但重載失敗: ","Installed, but reload failed: ","インストール済み、再読込失敗: ")+e.GetBaseException().Message;}
            Report(runtime);
            return ()=>{release=selected with {InstalledVersion=installed.InstalledVersion}; progress=1; status=$"{count} "+UiText.T("个资源文件已安装。","個資源檔案已安裝。","resource files installed.","ファイルをインストールしました。")+" "+runtime;};
        });
    }
    private sealed class DownloadProgress(Action<ResourceProgress> report) : IProgress<ResourceProgress>
    { public void Report(ResourceProgress value) => report(value); }
    public void Dispose() {stop?.Cancel();}
}
