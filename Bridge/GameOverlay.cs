using System.Numerics;
using System.Runtime.InteropServices;
using Gkms.Host;
using Gkms.Manager;
using ImGuiNET;

namespace Gkms.Bridge;

// All ImGui calls, plugin settings callbacks and draw data remain on the Present
// thread. Game actions are sent to RuntimeControl and run in Unity's Update.
public sealed unsafe class GameOverlay : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Frame(int width,int height,float dpi,float seconds,int generation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Start(Frame frame,nint window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Toggle(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Capture(int mouse,int keyboard);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Number();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Pointer();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Stop();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(Input* values,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Font(byte* rgba,int width,int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Render(Draw* draw);
    [StructLayout(LayoutKind.Sequential)] private struct Input {public uint Message; public nuint WParam; public nint LParam;}
    [StructLayout(LayoutKind.Sequential)] private struct Command {public Vector4 Clip; public nint Texture; public uint Count,Index,Vertex; public nint Callback,List,Original;}
    [StructLayout(LayoutKind.Sequential)] private struct List {public nint Vertices;public int VertexCount;public nint Indices;public int IndexCount;public Command* Commands;public int CommandCount;}
    [StructLayout(LayoutKind.Sequential)] private struct Draw {public Vector2 Position,Size,Scale;public int Count;public List* Lists;}
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window,ref Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
    private readonly string root;
    private readonly bool pollCursor;
    private readonly Action<string> log;
    private readonly Frame frame;
    private readonly Toggle toggle;
    private readonly Capture capture;
    private readonly Number capturesInput;
    private readonly Stop stop;
    private readonly Number isOpen;
    private readonly Pointer window;
    private readonly Read read;
    private readonly Font font;
    private readonly Render render;
    private readonly Input[] input=new Input[256];
    private nint context;
    private int generation,thread;
    private OverlayPanel? panel;
    private string lastError="";
    public bool Visible=>isOpen()!=0;
    public int RenderedFrames {get;private set;}
    public GameOverlay(string root,string nativeLibrary,Action<string> log,nint explicitWindow=0)
    {
        this.root=root; this.log=log; pollCursor=explicitWindow==0;
        var module=NativeLibrary.Load(nativeLibrary);
        T Function<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
        toggle=Function<Toggle>("GkmsOverlaySetOpen"); stop=Function<Stop>("GkmsOverlayStop"); isOpen=Function<Number>("GkmsOverlayIsOpen");
        capture=Function<Capture>("GkmsOverlaySetInputCapture"); capturesInput=Function<Number>("GkmsOverlayCapturesInput");
        window=Function<Pointer>("GkmsOverlayWindow"); read=Function<Read>("GkmsOverlayReadInput"); font=Function<Font>("GkmsOverlayFont"); render=Function<Render>("GkmsOverlayRender");
        frame=DrawFrame;
        var result=Function<Start>("GkmsOverlayStart")(frame,explicitWindow);
        if(result!=0) throw new InvalidOperationException($"DX11 overlay hook initialization failed: {result}");
        Gkms.Sdk.LiveGameState.InputCapture = () => capturesInput()!=0;
        log("Dear ImGui game overlay ready. Ctrl+U toggles the in-game panel.");
    }
    public void SetVisible(bool value)=>toggle(value?1:0);
    private int DrawFrame(int width,int height,float dpi,float seconds,int currentGeneration)
    {
        try
        {
            if(width<=0 || height<=0) return 1;
            if(context==0 || generation!=currentGeneration)
            {
                Destroy(); thread=Environment.CurrentManagedThreadId;
                context=ImGui.CreateContext(); ImGui.SetCurrentContext(context);
                ImGuiAppearance.Configure(dpi);
                var io=ImGui.GetIO(); io.BackendFlags|=ImGuiBackendFlags.RendererHasVtxOffset;
                io.Fonts.GetTexDataAsRGBA32(out byte* rgba,out int fw,out int fh,out _);
                var texture=font(rgba,fw,fh);
                if(texture==0) throw new InvalidOperationException("Cannot upload ImGui font atlas.");
                io.Fonts.SetTexID(texture); io.Fonts.ClearTexData();
                panel=new OverlayPanel(root,log) {Status=lastError}; generation=currentGeneration;
            }
            if(Environment.CurrentManagedThreadId!=thread) throw new InvalidOperationException("DX11 Present moved to a different UI thread.");
            ImGui.SetCurrentContext(context);
            // Dear ImGui's Win32 IME callback uses this HWND to position candidates.
            var viewport=ImGui.GetMainViewport(); viewport.PlatformHandleRaw=window();
            var inputIo=ImGui.GetIO(); inputIo.DisplaySize=new Vector2(width,height); inputIo.DisplayFramebufferScale=Vector2.One; inputIo.DeltaTime=seconds;
            // Explicit fixture windows are driven by their window messages, independent of the user's desktop pointer.
            if(pollCursor && GetCursorPos(out var point) && ScreenToClient(window(),ref point)) inputIo.AddMousePosEvent(point.X,point.Y);
            fixed(Input* buffer=input)
            {
                int count;
                do {count=read(buffer,input.Length); for(int i=0;i<count;i++) InputEvent(input[i]);} while(count==input.Length);
            }
            ImGui.NewFrame(); var open=panel!.Draw(); ImGui.Render();
            capture(inputIo.WantCaptureMouse?1:0,inputIo.WantCaptureKeyboard?1:0);
            Submit(ImGui.GetDrawData()); ++RenderedFrames; return open?1:0;
        }
        catch(Exception error)
        {
            lastError=error.GetBaseException().Message;
            try {log("[Error] overlay: "+error);} catch { }
            // A plugin can leave ImGui stacks unbalanced when it throws. Drop this
            // context without rendering it; reopening starts on the plugin list.
            Destroy(); return 0;
        }
    }
    private void InputEvent(Input value)
    {
        var io=ImGui.GetIO(); var code=(int)value.WParam; long position=(long)value.LParam;
        switch(value.Message)
        {
            case 0x7: io.AddFocusEvent(true); break;
            case 0x8: io.AddFocusEvent(false); break;
            case 0x200: io.AddMousePosEvent((short)position,(short)(position>>16)); break;
            case 0x201: case 0x203: io.AddMouseButtonEvent(0,true); break;
            case 0x202: io.AddMouseButtonEvent(0,false); break;
            case 0x204: case 0x206: io.AddMouseButtonEvent(1,true); break;
            case 0x205: io.AddMouseButtonEvent(1,false); break;
            case 0x207: case 0x209: io.AddMouseButtonEvent(2,true); break;
            case 0x208: io.AddMouseButtonEvent(2,false); break;
            case 0x20b: case 0x20d: io.AddMouseButtonEvent((code>>16)==1?3:4,true); break;
            case 0x20c: io.AddMouseButtonEvent((code>>16)==1?3:4,false); break;
            case 0x20a: io.AddMouseWheelEvent(0,(short)(code>>16)/120f); break;
            case 0x20e: io.AddMouseWheelEvent(-(short)(code>>16)/120f,0); break;
            case 0x102: io.AddInputCharacterUTF16((ushort)code); break;
            case 0x100: case 0x101: case 0x104: case 0x105:
                bool down=value.Message is 0x100 or 0x104; var key=Key(code);
                if(key!=ImGuiKey.None) io.AddKeyEvent(key,down);
                if(code is 0x11 or 0xa2 or 0xa3) io.AddKeyEvent(ImGuiKey.ModCtrl,down);
                if(code is 0x10 or 0xa0 or 0xa1) io.AddKeyEvent(ImGuiKey.ModShift,down);
                if(code is 0x12 or 0xa4 or 0xa5) io.AddKeyEvent(ImGuiKey.ModAlt,down);
                if(code is 0x5b or 0x5c) io.AddKeyEvent(ImGuiKey.ModSuper,down);
                break;
        }
    }
    private static ImGuiKey Key(int key)=>key switch
    {
        >=0x41 and <=0x5a=>ImGuiKey.A+(key-0x41), >=0x30 and <=0x39=>ImGuiKey._0+(key-0x30), >=0x70 and <=0x7b=>ImGuiKey.F1+(key-0x70),
        0x09=>ImGuiKey.Tab,0x25=>ImGuiKey.LeftArrow,0x27=>ImGuiKey.RightArrow,0x26=>ImGuiKey.UpArrow,0x28=>ImGuiKey.DownArrow,
        0x21=>ImGuiKey.PageUp,0x22=>ImGuiKey.PageDown,0x24=>ImGuiKey.Home,0x23=>ImGuiKey.End,0x2d=>ImGuiKey.Insert,0x2e=>ImGuiKey.Delete,
        0x08=>ImGuiKey.Backspace,0x20=>ImGuiKey.Space,0x0d=>ImGuiKey.Enter,0x1b=>ImGuiKey.Escape,
        0x11 or 0xa2=>ImGuiKey.LeftCtrl,0xa3=>ImGuiKey.RightCtrl,0x10 or 0xa0=>ImGuiKey.LeftShift,0xa1=>ImGuiKey.RightShift,
        0x12 or 0xa4=>ImGuiKey.LeftAlt,0xa5=>ImGuiKey.RightAlt,0x5b=>ImGuiKey.LeftSuper,0x5c=>ImGuiKey.RightSuper,
        0xba=>ImGuiKey.Semicolon,0xbb=>ImGuiKey.Equal,0xbc=>ImGuiKey.Comma,0xbd=>ImGuiKey.Minus,0xbe=>ImGuiKey.Period,
        0xbf=>ImGuiKey.Slash,0xc0=>ImGuiKey.GraveAccent,0xdb=>ImGuiKey.LeftBracket,0xdc=>ImGuiKey.Backslash,0xdd=>ImGuiKey.RightBracket,0xde=>ImGuiKey.Apostrophe,_=>ImGuiKey.None
    };
    private void Submit(ImDrawDataPtr data)
    {
        var lists=new List[data.CmdListsCount]; var count=0;
        for(int i=0;i<lists.Length;i++) count+=data.CmdListsRange[i].CmdBuffer.Size;
        var commands=new Command[count];
        fixed(List* nativeLists=lists) fixed(Command* nativeCommands=commands)
        {
            int cursor=0;
            for(int i=0;i<lists.Length;i++)
            {
                var list=data.CmdListsRange[i];
                lists[i]=new List {Vertices=list.VtxBuffer.Data,VertexCount=list.VtxBuffer.Size,Indices=list.IdxBuffer.Data,IndexCount=list.IdxBuffer.Size,Commands=nativeCommands+cursor,CommandCount=list.CmdBuffer.Size};
                for(int j=0;j<list.CmdBuffer.Size;j++) {var cmd=list.CmdBuffer[j]; commands[cursor++]=new Command {Clip=cmd.ClipRect,Texture=cmd.TextureId,Count=cmd.ElemCount,Index=cmd.IdxOffset,Vertex=cmd.VtxOffset,Callback=cmd.UserCallback,List=(nint)list.NativePtr,Original=(nint)cmd.NativePtr};}
            }
            var draw=new Draw {Position=data.DisplayPos,Size=data.DisplaySize,Scale=data.FramebufferScale,Count=lists.Length,Lists=nativeLists};
            var result=render(&draw); if(result!=0) throw new InvalidOperationException($"DX11 overlay draw failed: {result}");
        }
    }
    private void Destroy()
    {
        if(context==0) return;
        ImGui.SetCurrentContext(context);
        try {panel?.Dispose();}
        catch(Exception error) {try {log("overlay cleanup: "+error.GetBaseException().Message);} catch { }}
        finally {panel=null; ImGui.DestroyContext(context); context=0;}
    }
    public void Dispose() {Gkms.Sdk.LiveGameState.InputCapture = null; stop(); Destroy(); GC.KeepAlive(frame);}
}
