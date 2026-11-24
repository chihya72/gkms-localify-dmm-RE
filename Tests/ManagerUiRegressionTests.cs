using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Gkms.Bridge;
using Gkms.Host;
using Gkms.Manager;
using Gkms.Sdk;
using ImGuiNET;

internal static unsafe class ManagerUiRegressionTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message) { if (!value) throw new Exception("Manager UI: " + message); }
    private static object Get(object value, string field) => value.GetType().GetField(field, Fields)!.GetValue(value)!;
    private static void Set(object value, string field, object content) => value.GetType().GetField(field, Fields)!.SetValue(value, content);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Number();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Call();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Character(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Mouse(int x,int y,int down);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Resize(int width,int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Pixels(byte* data,int capacity,out int width,out int height);

    public static void Run(string work)
    {
        Check(Directory.Exists(work), "use an existing output directory");
        Levels(); UnityLevels(); LocalCatalog(); ModalGeometry(); NativeInputAndScreenshots(work);
        Console.WriteLine("Manager UI PASS: modal bounds/centering at three sizes and two scales, local catalog/search, severity filtering, UTF-16 IME input without duplicates, IME restoration, DX11 screenshots.");
    }
    private sealed class LogContext : IPluginContext, IDisposable
    {
        private readonly HarmonyLib.Harmony patches=new("manager-ui.log-levels");
        internal readonly List<string> Messages=new();
        public string DataDirectory=>"";
        public System.Text.Json.JsonElement Settings=>default;
        public void Log(string message)=>Messages.Add(PluginLog.WithSource(message,"framework"));
        public Type? FindType(string assembly,string fullName)=>typeof(UnityEngine.DebugLogHandler);
        public void Patch(MethodInfo target,MethodInfo? prefix=null,MethodInfo? postfix=null,int priority=400)=>
            patches.Patch(target,prefix==null?null:new HarmonyLib.HarmonyMethod(prefix),postfix==null?null:new HarmonyLib.HarmonyMethod(postfix));
        public void Dispose()=>patches.UnpatchSelf();
    }
    private static void UnityLevels()
    {
        using var context=new LogContext();
        using var forwarder=(ILocalifyPlugin)Activator.CreateInstance(typeof(PluginHost).Assembly.GetType("Gkms.Host.UnityLogForwarder")!,true)!;
        forwarder.Start(context);
        foreach(var level in new[]{0,1,2,3,4}) UnityEngine.DebugLogHandler.Internal_Log(level,0,"message",null);
        Check(context.Messages.Select(PluginLog.ReadLevel).SequenceEqual(new PluginLogLevel?[]{PluginLogLevel.Error,PluginLogLevel.Error,PluginLogLevel.Warning,PluginLogLevel.Info,PluginLogLevel.Error}),"actual Unity callback preserves log type");
    }
    private static void Levels()
    {
        var buffer = new OutputLogBuffer();
        buffer.Append("[2026-11-23 10:00:00.000] [Info] no error occurred\n[2026-11-23 10:00:01.000] [Error] 插件异常\n   at Plugin.Start()\n[2026-11-23 10:00:02.000] [Warn");
        buffer.Append("ing] 兼容性警告\n[2026-11-23 10:00:03.000] 旧日志\n");
        var levels = new[] { false, false, true, true, false, false };
        Check(buffer.Filter(levels, "").Count() == 2, "multiple severity selection ignores misleading words and keeps stack traces");
        Check(buffer.Filter(levels, "at Plugin").Single().Level == PluginLogLevel.Error, "stack trace inherits severity");
        Check(buffer.Filter(levels, "兼容性").Single().Level == PluginLogLevel.Warning, "split reads retain complete Chinese messages");
        Check(buffer.Entries[^1].Level == null, "legacy messages remain unclassified");
        var scoped = new OutputLogBuffer();
        scoped.Append("[2026-11-23 10:00:00.000] [Error] window: failed\n  at Window.Start()\n[Info] gkms.dlss5: window: renderer ready\n[Warning] window-extra: warning\n[Info] framework: ready\n[Info] unscoped message\n");
        var all = new[] { true, true, true, true, true, true };
        Check(scoped.Filter(all, "", "window").Single().Text.Contains("at Window.Start()"), "plugin source is exact and stack traces inherit source");
        Check(scoped.Filter(all, "window", "gkms.dlss5").Count() == 1, "source and text filters intersect without parsing message content as source");
        Check(!scoped.Filter(levels, "", "gkms.dlss5").Any(), "source and severity filters intersect");
        Check(scoped.Filter(all, "", "").Count() == 1 && scoped.Filter(all, "", "framework").Count() == 1, "framework and unscoped logs remain accessible");
        scoped.Reset(); Check(!scoped.Sources.Any(), "rotation clears source inventory");
        foreach (var level in Enum.GetValues<PluginLogLevel>())
        {
            var message = PluginLog.Normalize(PluginLog.WithSource($"[{level}] 内容", "example.plugin"));
            Check(message == $"[{level}] example.plugin: 内容" && PluginLog.ReadLevel(message) == level, "scope preserves " + level);
        }
        for(var i=0;i<1100;i++) buffer.Append($"[Info] {i}\n");
        Check(buffer.Entries.Count == OutputLogBuffer.Limit, "bounded history");
    }
    private static Manifest Sample() => new("example.local", "Local plugin", 2, "Example.dll", "Example.Plugin")
    {
        Version="1.0.0", Author="作者", Description="照片替换支持；插件卸载仅移除程序文件。",
        Names=new() { ["zh-CN"]="照片替换支持" }, Category="工具", Tags=new[]{"照片"}
    };
    private static void LocalCatalog()
    {
        var manifest = Sample();
        var installed = new Dictionary<string,Package> { [manifest.Id]=new("unused",manifest) };
        var settings = new RepositorySettings();
        var rows = PluginRepositoryPanel.LocalOffers(installed,settings);
        Check(rows.Length==1 && rows[0].Plugin.Matches("照片") && rows[0].Plugin.Matches("作者") && rows[0].Plugin.Category=="工具", "local metadata and Chinese search without a repository offer");
        settings.Origins[manifest.Id]=new("official","https://example.invalid/index.json");
        Check(PluginRepositoryPanel.LocalOffers(installed,settings).Length==0, "subscribed plugin is excluded from local source");
    }
    private static void ModalGeometry()
    {
        var context=ImGui.CreateContext();
        try
        {
            ImGuiAppearance.Configure(); var io=ImGui.GetIO(); io.Fonts.GetTexDataAsRGBA32(out nint _,out int _,out int _);
            foreach(var display in new[]{new Vector2(720,1280),new Vector2(1040,760),new Vector2(480,320)})
            foreach(var scale in new[]{1f,1.5f})
            foreach(var centered in new[]{true,false})
            for(var frame=0;frame<4;frame++)
            {
                io.DisplaySize=display; io.DeltaTime=1f/60; io.FontGlobalScale=scale;
                ImGui.NewFrame(); ImGuiAppearance.ModalCenter=centered?display*.5f:new Vector2(260,180);
                if(frame==0) ImGui.OpenPopup("Confirmation");
                ImGuiAppearance.PrepareModal(); var open=true;
                Check(ImGui.BeginPopupModal("Confirmation",ref open,ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoMove),"open confirmation");
                ImGuiAppearance.PlaceModal();
                PluginUiText.Wrapped("安装照片替换支持 1.0.0");
                ImGui.BeginChild("description",new Vector2(0,Math.Min(180*scale,display.Y*.25f)));
                PluginUiText.Wrapped(string.Concat(Enumerable.Repeat("配合插件替换拍摄缓存照片。开启后保存或取消。",20)));
                ImGui.EndChild(); PluginUiText.Wrapped("安装后默认停用。"); ImGui.Button("确认安装"); ImGui.SameLine(); ImGui.Button("取消");
                if(frame>=2)
                {
                    var position=ImGui.GetWindowPos(); var size=ImGui.GetWindowSize();
                    Check(size.X>=Math.Min(560*scale,display.X-24)-1,"confirmation has readable width");
                    Check(position.X>=0 && position.Y>=0 && position.X+size.X<=display.X+1 && position.Y+size.Y<=display.Y+1,"confirmation remains in viewport");
                    if(centered) Check(Vector2.Distance(position+size*.5f,display*.5f)<2,"confirmation is centered");
                }
                if(frame==3) ImGui.CloseCurrentPopup(); ImGui.EndPopup(); ImGui.Render();
            }
        }
        finally { ImGui.DestroyContext(context); }
    }
    private static void NativeInputAndScreenshots(string work)
    {
        var fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","obj","native","OverlayFixture.dll"));
        var module=NativeLibrary.Load(fixture);
        T Function<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
        var window=Function<Create>("fixture_overlay_create")(); Check(window!=0,"hidden test window");
        var frame=Function<Number>("fixture_overlay_frame"); var toggle=Function<Call>("fixture_overlay_toggle");
        var messages=new List<string>();
        try
        {
            Function<Call>("fixture_overlay_disable_ime")();
            using var overlay=new GameOverlay(work,fixture,messages.Add,window);
            Check(frame()==0,"bind native overlay"); toggle();
            void Draw() { Check(frame()==0 && overlay.Visible,"render: "+string.Join("\n",messages)); }
            Draw(); Draw();
            Check(Function<Number>("fixture_overlay_has_ime")()==1,"overlay restores default IME when game disabled it");
            var panel=Get(overlay,"panel"); var repository=Get(panel,"repositories");
            Set(repository,"initialized",true); // This regression is offline and uses no repository downloads.
            var manifest=Sample(); var installed=new Dictionary<string,Package>{[manifest.Id]=new(work,manifest)};
            Set(panel,"catalog",installed); Set(repository,"installed",installed);
            var mouse=Function<Mouse>("fixture_overlay_mouse");
            void Click(int x,int y) {mouse(x,y,-1);Draw();mouse(x,y,1);Draw();mouse(x,y,0);Draw();}
            Click(120,122); Click(200,199);
            foreach(var c in "照片") Function<Character>("fixture_overlay_ime_char")(c);
            Draw(); Draw();
            Check((string)Get(repository,"search")=="照片","native IME characters enter catalog search once");
            Screenshot("manager-ui-local-search.bmp");
            toggle(); Check(Function<Number>("fixture_overlay_has_ime")()==0,"closing overlay restores original disabled IME context"); toggle(); Draw();
            Check(Function<Resize>("fixture_overlay_resize")(720,1280)==0,"portrait resize"); Draw();Draw();
            var picker=Get(panel,"picker");
            picker.GetType().GetMethod("Open")!.Invoke(picker,new object[]{"选择本地插件 ZIP 安装包",(Action<string>)(_=>{})});
            Draw(); Draw(); Screenshot("manager-ui-picker.bmp");
            // Recreate only our test overlay's context to isolate the next modal.
            typeof(GameOverlay).GetMethod("Destroy",Fields)!.Invoke(overlay,null); Draw();Draw();
            panel=Get(overlay,"panel"); Set(panel,"catalog",installed); Set(panel,"selected",manifest.Id); Set(panel,"confirmRemove",true);
            Draw();Draw(); Screenshot("manager-ui-uninstall.bmp");
            typeof(GameOverlay).GetMethod("Destroy",Fields)!.Invoke(overlay,null); Draw();Draw();
            panel=Get(overlay,"panel");
            var preparedType=panel.GetType().GetField("prepared",Fields)!.FieldType;
            var prepared=Activator.CreateInstance(preparedType,Fields,null,new object?[]{work,null,manifest with {Description=string.Concat(Enumerable.Repeat("配合 gkms-photo-swap 替换拍摄缓存照片。须在拍照前开启 photoHashBypass，并保持开启直到保存或取消；不修改已保存的照片。",5))}},null)!;
            Set(panel,"prepared",prepared); Set(panel,"confirmInstall",true);
            Draw();Draw(); Screenshot("manager-ui-install.bmp");
            typeof(GameOverlay).GetMethod("Destroy",Fields)!.Invoke(overlay,null); Draw();Draw();
            panel=Get(overlay,"panel"); var output=Get(panel,"outputLog"); var buffer=Get(output,"buffer");
            buffer.GetType().GetMethod("Append",Fields)!.Invoke(buffer,new object[]{"[2026-11-23 10:00:00.000] [Info] 插件已加载\n[2026-11-23 10:00:01.000] [Warning] 示例兼容性警告\n[2026-11-23 10:00:02.000] [Error] 示例资源更新失败\n  at Plugin.Update()\n"});
            Set(output,"paused",true); output.GetType().GetMethod("Open",Fields)!.Invoke(output,null);
            Draw();Draw(); Screenshot("manager-ui-logs.bmp");
            Check(!messages.Any(m=>m.Contains("[Error]")),string.Join("\n",messages));
            void Screenshot(string name)
            {
                Draw();Draw();Draw(); // Read a presented buffer after all queued UI events have settled.
                var pixels=Function<Pixels>("fixture_overlay_pixels"); var size=pixels(null,0,out var width,out var height); Check(size>0,"read pixels");
                var rgba=new byte[size]; fixed(byte* data=rgba) Check(pixels(data,size,out width,out height)==size,"copy pixels");
                using var writer=new BinaryWriter(File.Create(Path.Combine(work,name)));
                writer.Write((ushort)0x4d42); writer.Write(54+size); writer.Write(0); writer.Write(54); writer.Write(40); writer.Write(width); writer.Write(-height);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(size); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
                for(var i=0;i<rgba.Length;i+=4) {writer.Write(rgba[i+2]);writer.Write(rgba[i+1]);writer.Write(rgba[i]);writer.Write(rgba[i+3]);}
            }
        }
        finally { Function<Call>("fixture_overlay_destroy")(); }
    }
}
