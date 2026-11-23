using System.Runtime.InteropServices;
using Gkms.Host;

// Real User32/comctl32 messages on our own hidden window, never the user's game.
internal static class WindowMessageTests
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Style; public IntPtr Proc; public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background, Menu;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
    }
    private delegate IntPtr Procedure(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    private static readonly Procedure procedure = DefWindowProc;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint msg, UIntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetStyle(IntPtr window, int index);
    private static void Check(bool value, string message) { if (!value) throw new Exception("Window: " + message); }
    public static void Run(string packages, string work)
    {
        Directory.CreateDirectory(work);
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data) =>
        { var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; Check(GetMonitorInfo(monitor, ref info), "read monitor"); monitors.Add(info); return true; }, IntPtr.Zero);
        Check(monitors.Count > 0, "at least one monitor");
        var cls = new WindowClass { Name = "UnityWndClass", Instance = GetModuleHandle(null), Proc = Marshal.GetFunctionPointerForDelegate(procedure) };
        Check(RegisterClass(ref cls) != 0, "register hidden test class");
        var first = monitors[0].Work;
        var window = CreateWindowEx(0, cls.Name, "Localify window regression (hidden)", 0x00cf0000, first.Left+20, first.Top+20, 600, 500, IntPtr.Zero, IntPtr.Zero, cls.Instance, IntPtr.Zero);
        Check(window != IntPtr.Zero, "create hidden window");
        var profile = Path.Combine(work, "profile.json");
        File.WriteAllText(profile, """{"enabled":["window"]}""");
        var logs = new List<string>();
        try
        {
            using var host = new PluginHost(new TestGame(), logs.Add);
            host.Start(packages, profile, work);
            var quality = new Campus.Common.CampusQualityManager(); quality.Update();
            Check(host.Active.Contains("window"), string.Join("\n", logs));
            foreach (var info in monitors)
            {
                SetWindowPos(window, IntPtr.Zero, info.Work.Left+20, info.Work.Top+20, 600, 500, 0x14);
                GetWindowRect(window, out var original);
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out var actual);
                Check(actual.Equals(info.Monitor) && (GetStyle(window, -16) & 0x00c00000) == 0,
                    $"F11 fills the window's own monitor, including secondary/negative origin {info.Monitor.Left},{info.Monitor.Top}");
                SendMessage(window, 0x100, new UIntPtr(0x7a), new IntPtr(1L << 30)); quality.Update();
                GetWindowRect(window, out actual); Check(actual.Equals(info.Monitor), "held F11 does not oscillate");
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out actual); Check(actual.Equals(original), "F11 restores position and size");
                SendMessage(window, 0x112, new UIntPtr(0xf030), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out actual); Check(actual.Equals(info.Monitor), "maximize routes to fullscreen");
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
            }
            GetWindowRect(window, out var beforeStop);
            SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
            File.WriteAllText(profile, """{"enabled":[]}"""); host.ReloadProfile();
            GetWindowRect(window, out var restored); Check(restored.Equals(beforeStop), "disable restores fullscreen window");
            Check(!logs.Any(line => line.Contains("Native hook") || line.Contains("无法接入")), string.Join("\n", logs));
            Console.WriteLine($"Window messages PASS: F11, repeat filtering, maximize, disable restore, actual monitor count={monitors.Count}.");
        }
        finally { DestroyWindow(window); GC.KeepAlive(procedure); }
    }
}
