using System.Runtime.InteropServices;
using System.Text.Json;
using Gkms.Bridge;

internal static unsafe class OverlayTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Number();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Action();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Resize(int width,int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Mouse(int x,int y,int down);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Key(int key,int down);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Character(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Pixels(byte* data,int capacity,out int width,out int height);
    private static void Check(bool ok,string message) {if(!ok) throw new Exception("Overlay: "+message);}
    public static void Run(string work,string packages)
    {
        var fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","obj","native","OverlayFixture.dll"));
        var module=NativeLibrary.Load(fixture);
        T Function<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
        var window=Function<Create>("fixture_overlay_create")(); Check(window!=0,"independent DX11 test window");
        var root=Path.Combine(work,"overlay"); Directory.CreateDirectory(root);
        // This UI fixture exercises the bundled catalog without depending on a published release or network timing.
        File.WriteAllText(Path.Combine(root,"plugin-repositories.json"), """{"officialUrl":""}""");
        File.Copy(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(packages))!,"official-plugins.zip"),Path.Combine(root,"official-plugins.zip"),true);
        var profile=Path.Combine(root,"profile.json");
        File.WriteAllText(profile,"""{"enabled":[],"settings":{"another.plugin":{"keep":42}}}""");
        var targetPackages=Path.Combine(root,"plugins"); Directory.CreateDirectory(targetPackages);
        foreach(var folder in Directory.GetDirectories(packages))
        {
            var target=Path.Combine(targetPackages,Path.GetFileName(folder)); Directory.CreateDirectory(target);
            foreach(var file in Directory.GetFiles(folder)) File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
        }
        var messages=new List<string>();
        using(var overlay=new GameOverlay(root,fixture,messages.Add,window))
        {
            var frame=Function<Number>("fixture_overlay_frame"); var toggle=Function<Action>("fixture_overlay_toggle");
            Check(frame()==0 && !overlay.Visible && overlay.RenderedFrames==0,"hidden overlay leaves game rendering alone");
            toggle(); Check(overlay.Visible,"Ctrl+U opens overlay without launching a process");
            for(int i=0;i<3;i++) Check(frame()==0,"Present callback restores Unity viewport and render target");
            Check(overlay.RenderedFrames==3 && overlay.Visible,"Dear ImGui panel renders on the swap chain: "+string.Join("\n",messages));
            var pixels=Function<Pixels>("fixture_overlay_pixels"); var size=pixels(null,0,out var width,out var height); Check(size>0,"read back buffer");
            var rgba=new byte[size]; fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read rendered pixels");
            var colors=new HashSet<int>(); for(int i=0;i<rgba.Length;i+=4) colors.Add(rgba[i]|rgba[i+1]<<8|rgba[i+2]<<16);
            Check(colors.Count>100,"panel and Chinese glyphs reached the game's render target");
            SaveBmp(Path.Combine(work,"overlay-preview.bmp"),rgba,width,height);
            var mouse=Function<Mouse>("fixture_overlay_mouse"); var inputs=Function<Number>("fixture_overlay_game_inputs"); var before=inputs();
            void Click(int x,int y) {mouse(x,y,-1); Check(frame()==0,"mouse hover frame"); mouse(x,y,1); Check(frame()==0,"mouse down frame"); mouse(x,y,0); Check(frame()==0,"mouse up frame");}
            Click(5,5); Check(inputs()==before+1,"open overlay forwards clicks outside its windows to the game");
            Check(frame()==0 && Gkms.Sdk.LiveGameState.InputCapture?.Invoke()==false,"game input remains available after focusing outside the panel");
            before=inputs();
            var mouseUps=Function<Number>("fixture_overlay_game_mouse_ups"); var beforeMouseUps=mouseUps();
            Click(150,90); Check(inputs()==before,"panel clicks do not reach the game");
            mouse(150,419,-1); Check(frame()==0,"hover selectable row before panel drag");
            mouse(150,419,1); Check(frame()==0,"begin panel drag");
            mouse(5,5,-1); Check(frame()==0,"drag beyond panel edge");
            mouse(5,5,0); Check(frame()==0,"release panel drag outside");
            Check(inputs()==before,"panel-owned drag remains isolated outside its bounds");
            Check(mouseUps()==beforeMouseUps,"panel drag release does not leak into the game");
            mouse(5,5,-1); Check(frame()==0,"hover game before game-owned drag");
            mouse(5,5,1); Check(frame()==0,"begin game-owned drag");
            mouse(150,90,-1); Check(frame()==0,"drag from game into panel");
            mouse(150,90,0); Check(frame()==0,"release game-owned drag over panel");
            Check(inputs()==before+1 && mouseUps()==beforeMouseUps+1,$"game receives both edges of a drag across the panel (down {inputs()-before}, up {mouseUps()-beforeMouseUps})");
            before=inputs();
            void WaitForManager()
            {
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var panel=typeof(GameOverlay).GetField("panel",flags)!.GetValue(overlay)!;
                var busy=panel.GetType().GetField("busy",flags)!;
                for(int i=0;i<300;i++)
                {
                    Thread.Sleep(10); Check(frame()==0,"manager completion frame");
                    if(!(bool)busy.GetValue(panel)!) return;
                }
                throw new Exception("manager work did not finish");
            }
            Click(62,419); Click(105,691);
            for(int i=0;i<100;i++) {Thread.Sleep(10); Check(frame()==0,"save completion frame"); using var saved=JsonDocument.Parse(File.ReadAllText(profile)); if(saved.RootElement.GetProperty("enabled").GetArrayLength()>0) break;}
            using(var saved=JsonDocument.Parse(File.ReadAllText(profile)))
                Check(saved.RootElement.GetProperty("enabled").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(new[]{"frame-rate"}) && saved.RootElement.GetProperty("settings").GetProperty("another.plugin").GetProperty("keep").GetInt32()==42,"in-game checkbox/save persists only the selected switch");
            // Disk persistence can precede the render-thread completion callback.
            WaitForManager();
            Click(150,419); Check(frame()==0,"settings detail frame");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read settings pixels");
            SaveBmp(Path.Combine(work,"overlay-settings.bmp"),rgba,width,height);
            Click(500,474);
            var key=Function<Key>("fixture_overlay_key"); var character=Function<Character>("fixture_overlay_char");
            key(0x11,1); key('A',1); Check(frame()==0,"select field text"); key('A',0); key(0x11,0); Check(frame()==0,"release editor modifiers");
            foreach(var c in "144") character(c); Check(frame()==0,"type parameter");
            key(0x0d,1); Check(frame()==0,"commit parameter"); key(0x0d,0); Check(frame()==0,"release enter");
            Click(530,386);
            WaitForManager();
            using(var saved=JsonDocument.Parse(File.ReadAllText(profile)))
                Check(saved.RootElement.GetProperty("settings").GetProperty("frame-rate").GetProperty("fps").GetInt32()==144 && saved.RootElement.GetProperty("settings").GetProperty("another.plugin").GetProperty("keep").GetInt32()==42,"in-game parameter editing preserves independent plugin configuration");
            Check(frame()==0,"saved settings frame");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read edited settings pixels");
            SaveBmp(Path.Combine(work,"overlay-settings.bmp"),rgba,width,height);
            Click(150,536); Check(frame()==0,"iOS details frame");
            const System.Reflection.BindingFlags inspect=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var detailPanel=typeof(GameOverlay).GetField("panel",inspect)!.GetValue(overlay)!;
            Check((string)detailPanel.GetType().GetField("selected",inspect)!.GetValue(detailPanel)! == "ios-login" &&
                detailPanel.GetType().GetField("session",inspect)!.GetValue(detailPanel)==null,"iOS does not construct an empty settings page");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read iOS details pixels");
            SaveBmp(Path.Combine(work,"overlay-ios.bmp"),rgba,width,height);
            Click(150,615); Check(frame()==0,"translation details frame");
            mouse(750,360,-1); Check(frame()==0,"hover scrollable details");
            ImGuiNET.ImGui.GetIO().AddMouseWheelEvent(0,-3);
            for(int i=0;i<4;i++) Check(frame()==0,"scroll translation details");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read scrolled translation pixels");
            SaveBmp(Path.Combine(work,"overlay-localization-scrolled.bmp"),rgba,width,height);
            Click(120,122);
            for(int i=0;i<60;i++) {Thread.Sleep(10); Check(frame()==0,"repository catalog frames");}
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read repository layout pixels");
            SaveBmp(Path.Combine(work,"overlay-repositories.bmp"),rgba,width,height);
            Click(115,354); Check(frame()==0,"repository metadata selection frame");
            var repositoryPanel=detailPanel.GetType().GetField("repositories",inspect)!.GetValue(detailPanel)!;
            var rowSelection=repositoryPanel.GetType().GetField("selection",inspect)!;
            Check(!string.IsNullOrEmpty((string?)rowSelection.GetValue(repositoryPanel)),"version/status line selects the complete plugin row");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read repository pixels");
            SaveBmp(Path.Combine(work,"overlay-repositories.bmp"),rgba,width,height);
            Click(200,199);
            foreach(var c in "FPS") character(c);
            Check(frame()==0,"repository tag search frame");
            Click(115,354); Check(frame()==0,"filtered repository row frame");
            Check((string?)rowSelection.GetValue(repositoryPanel)=="official/frame-rate","typing a tag filters the catalog to the corresponding plugin");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read filtered repository pixels");
            SaveBmp(Path.Combine(work,"overlay-repository-search.bmp"),rgba,width,height);
            Click(180,122); Check(frame()==0,"repository sources frame");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read source pixels");
            SaveBmp(Path.Combine(work,"overlay-sources.bmp"),rgba,width,height);
            Click(55,122); Click(65,160); Check(frame()==0,"in-game ZIP picker frame");
            fixed(byte* ptr=rgba) Check(pixels(ptr,size,out width,out height)==size,"read picker pixels");
            SaveBmp(Path.Combine(work,"overlay-picker.bmp"),rgba,width,height);
            mouse(70,150,-1); Check(frame()==0,"hover panel before input isolation check");
            var beforePanelClick=inputs();
            mouse(70,150,1); mouse(70,150,0); Check(inputs()==beforePanelClick,"panel-owned mouse input stays isolated while open");
            toggle(); Check(!overlay.Visible,"Ctrl+U closes overlay"); mouse(70,150,1); Check(inputs()==beforePanelClick+1,"closed overlay restores game input");
            Check(Function<Resize>("fixture_overlay_resize")(900,720)==0,"ResizeBuffers releases overlay references");
            toggle(); Check(frame()==0 && overlay.Visible,"render after resizing");
            var changed = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(profile))!.AsObject();
            changed["enabled"] = new System.Text.Json.Nodes.JsonArray("camera-control");
            File.WriteAllText(profile, changed.ToJsonString());
            Thread.Sleep(1100); Check(frame()==0, "external profile poll frame");
            const System.Reflection.BindingFlags privateFields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var currentPanel = typeof(GameOverlay).GetField("panel", privateFields)!.GetValue(overlay)!;
            var selection = (HashSet<string>)currentPanel.GetType().GetField("enabled", privateFields)!.GetValue(currentPanel)!;
            Check(selection.SetEquals(new[] { "camera-control" }), "external CLI/profile changes synchronize checkbox selection without a manual refresh");
            Check(!messages.Any(m=>m.StartsWith("overlay:")),"overlay exception boundary: "+string.Join("\n",messages));
        }
        Function<Action>("fixture_overlay_destroy")();
        Console.WriteLine("In-game overlay PASS: real DX11 Present, Dear ImGui pixels, Ctrl+U, switch saving, independent parameter editing, input isolation, state restoration and ResizeBuffers.");
    }
    private static void SaveBmp(string path,byte[] rgba,int width,int height)
    {
        using var writer=new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4d42); writer.Write(54+rgba.Length); writer.Write(0); writer.Write(54); writer.Write(40); writer.Write(width); writer.Write(-height);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(rgba.Length); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        for(int i=0;i<rgba.Length;i+=4) {writer.Write(rgba[i+2]); writer.Write(rgba[i+1]); writer.Write(rgba[i]); writer.Write(rgba[i+3]);}
    }
}
