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
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetStyle(IntPtr window, int index, int value);
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
                SetStyle(window, -16, 0x00cf0000);
                quality.Update(); GetWindowRect(window, out actual);
                Check(actual.Equals(info.Monitor) && (GetStyle(window, -16) & 0x00c00000) == 0, "fullscreen reapplies borderless frame when game restores styles");
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
            File.WriteAllText(profile, """{"enabled":["window"],"settings":{"window":{"resizable":false}}}"""); host.ReloadProfile();
            quality.Update();
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var pluginType = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Gkms.Window")
                .Select(a => a.GetType("Gkms.Plugins.Window.Plugin")!).Single(type => type.GetField("session", flags)!.GetValue(null) != null);
            Check(!(bool)pluginType.GetField("resize", flags)!.GetValue(null)!, "fixed-size profile is applied");
            var poll = pluginType.GetMethod("PollFullscreenKey", flags)!;
            var pending = pluginType.GetField("toggleRequested", flags)!;
            var held = pluginType.GetField("f11Down", flags)!;
            void Poll(bool down, bool foreground) => poll.Invoke(null, new object[] { down, foreground });
            Poll(true, false); Check((int)pending.GetValue(null)! == 0, "background F11 is ignored");
            Poll(true, true); Check((int)pending.GetValue(null)! == 0, "held key on focus return does not toggle");
            Poll(false, true); Poll(true, true);
            Check((int)pending.GetValue(null)! == 1, "poll catches a swallowed keydown");
            pending.SetValue(null, 0); Poll(true, true);
            Check((int)pending.GetValue(null)! == 0, "poll filters held key");
            SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero);
            Check((int)pending.GetValue(null)! == 0, "message following polled press does not toggle twice");
            held.SetValue(null, false);
            GetWindowRect(window, out var beforePoll);
            Poll(true, true); quality.Update();
            Check((bool)pluginType.GetField("fullscreen", flags)!.GetValue(null)!, "poll-only press reaches fullscreen without a window message");
            Poll(false, true); Poll(true, true); quality.Update();
            GetWindowRect(window, out var afterPoll); Check(afterPoll.Equals(beforePoll), "poll-only press restores window");
            foreach (var info in monitors)
            {
                SetWindowPos(window, IntPtr.Zero, info.Work.Left+20, info.Work.Top+20, 600, 500, 0x14);
                GetWindowRect(window, out var original);
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out var actual); Check(actual.Equals(info.Monitor), "F11 works with resizable=false on each monitor");
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out actual); Check(actual.Equals(original), "fixed-size mode restores original bounds");
            }
            foreach (var orientation in new[] { "portrait", "landscape" })
            {
                File.WriteAllText(profile, System.Text.Json.JsonSerializer.Serialize(new { enabled = new[] { "window" }, settings = new { window = new { orientation, resizable = false } } }));
                host.ReloadProfile(); quality.Update();
                Check(host.Active.Contains("window"), "orientation plugin active: " + orientation);
                var controller = new Campus.Common.ScreenOrientationControllerBase();
                controller.InternalSetOrientationAsync(Campus.Common.WindowOrientationFixture.Game, false, default);
                Check((int)controller.LastOrientation == (orientation == "portrait" ? 2 : 3), "orientation hook is exercised before F11");
                GetWindowRect(window, out var original);
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out var actual);
                Check(monitors.Any(info => actual.Equals(info.Monitor)), "F11 overrides forced orientation: " + orientation);
                SendMessage(window, 0x100, new UIntPtr(0x7a), IntPtr.Zero); quality.Update();
                GetWindowRect(window, out actual); Check(actual.Equals(original), "F11 restores forced-orientation window");
            }
            Check(!logs.Any(line => line.Contains("Native hook") || line.Contains("无法接入")), string.Join("\n", logs));
            Console.WriteLine($"Window messages PASS: F11, repeat filtering, maximize, disable restore, actual monitor count={monitors.Count}.");
        }
        finally { DestroyWindow(window); GC.KeepAlive(procedure); }
    }
}
