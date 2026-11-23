using System.Reflection;
using System.Runtime.InteropServices;
using Gkms.Host;

namespace Gkms.Bridge;

public sealed class Entry : IGameServices,IAdvancedGameServices
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Startup { public nint GameAssembly,Create,Enable,Disable,GameDirectory; }
    private static Entry? current;
    private static readonly object logLock=new();
    private readonly NativeRuntime runtime;
    private readonly NativeHooks hooks;
    private readonly PluginHost host;
    private readonly GameOverlay? overlay;
    private readonly string logFile;
    private Entry(Startup startup)
    {
        var game=Marshal.PtrToStringUni(startup.GameDirectory) ?? throw new InvalidDataException("Missing game directory.");
        var data=Path.Combine(game,"gakumas-local");
        var root=Path.Combine(data,"re");
        logFile=Path.Combine(root,"runtime.log");
        runtime=new NativeRuntime(startup.GameAssembly);
        hooks=new NativeHooks(Marshal.GetDelegateForFunctionPointer<NativeHooks.CreateHook>(startup.Create),
            Marshal.GetDelegateForFunctionPointer<NativeHooks.ToggleHook>(startup.Enable),Marshal.GetDelegateForFunctionPointer<NativeHooks.ToggleHook>(startup.Disable),Log);
        host=new PluginHost(this,Log);
        Log("Gakumas Localify RE: native DMM loader, live IL2CPP API and MinHook.");
        Log("framework: 原生反射元数据缓存已启用；对象属性值仍实时读取。");
        try
        {
            Gkms.Manager.PluginInstallation.Cleanup(root, Log, beforeHostStart: true);
            host.Start(Path.Combine(root,"plugins"),Path.Combine(root,"profile.json"),data);
            host.EnableRuntimeControl();
        }
        catch(Exception error) { Log(error.ToString()); host.Dispose(); throw; }
        try
        {
            // Resolve the same cimgui core used by plugin settings, with a single
            // in-game context. No Windows Forms or external GUI process is loaded.
            var imgui=NativeLibrary.Load(Path.Combine(root,"runtimes","win-x64","native","cimgui.dll"));
            NativeLibrary.SetDllImportResolver(typeof(ImGuiNET.ImGui).Assembly,(name,_,_)=>name is "cimgui" or "cimgui.dll"?imgui:IntPtr.Zero);
            overlay=new GameOverlay(root,Path.Combine(game,"version.dll"),Log);
            RuntimeClient.OpenPanel=()=>overlay.SetVisible(true);
        }
        catch(Exception error) { Log("Game overlay unavailable: "+error); }
        Log("Runtime started.");
    }
    [UnmanagedCallersOnly]
    public static int Initialize(nint arguments,int length)
    {
        try
        {
            if(length!=Marshal.SizeOf<Startup>() || arguments==0) return -1;
            if(current!=null) return 0;
            current=new Entry(Marshal.PtrToStructure<Startup>(arguments));
            return 0;
        }
        catch(Exception error)
        {
            System.Diagnostics.Trace.WriteLine(error);
            try
            {
                var startup=Marshal.PtrToStructure<Startup>(arguments);
                var root=Marshal.PtrToStringUni(startup.GameDirectory)!;
                File.AppendAllText(Path.Combine(root,"gakumas-local","re","runtime.log"),error+Environment.NewLine);
            }
            catch { /* Never let a logging failure cross the unmanaged entry boundary. */ }
            return -2;
        }
    }
    private void Log(string message)
    {
        lock(logLock) File.AppendAllText(logFile,$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
    }
    public Type? FindType(string assembly,string fullName)=>runtime.FindType(assembly,fullName);
    public IDisposable Patch(string owner,MethodInfo target,MethodInfo? prefix,MethodInfo? postfix,int priority)=>hooks.Patch(owner,target,prefix,postfix,null,priority);
    public IDisposable PatchAdvanced(string owner,MethodBase target,MethodInfo? prefix,MethodInfo? postfix,MethodInfo? finalizer,int priority)=>hooks.Patch(owner,target,prefix,postfix,finalizer,priority);
}
