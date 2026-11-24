using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gkms.Host;
using Gkms.Manager;
using Gkms.Sdk;
using ImGuiNET;
using Veldrid;

internal static class PluginUiTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    public static void Run(string work, string packages)
    {
        Check(ImGuiAppearance.VisiblePosition(new(10,238), new(746,409), new(770,433)) == new Vector2(10,24),
            "竖屏切横屏后面板应回到可见范围，保存按钮不能留在窗口外");
        Check(ImGuiAppearance.VisiblePosition(new(-40,-20), new(520,360), new(578,1025)) == Vector2.Zero,
            "面板负坐标应恢复到可见范围");
        var log = new List<string>();
        var catalog = Catalog.Discover(packages, log.Add);
        var root = Directory.CreateDirectory(Path.Combine(work, "ui")).FullName;
        var saved = Path.Combine(root, "profile.json");
        using (var canvas = new Canvas())
        {
            canvas.CheckWrappedScrolling();
            // Production manifest entries, shared SDK/ImGui identity, real native frames and a DX11 offscreen target.
            foreach (var package in catalog.Values)
            {
                var state = Json("{}");
                using var session = new SettingsUiSession(package, "zh-CN", () => state, (v, _) => state = v,
                    _ => Task.FromResult("offline"), log.Add);
                session.OpenConfigUi();
                for (var frame = 0; frame < 2; frame++) Check(canvas.Frame(session, true), package.Manifest.Id + ": ImGui 绘制失败");
                if (package.Manifest.Id is "localization" or "textures")
                {
                    var expected = package.Manifest.Id == "localization"
                        ? "https://uma.chinosk6.cn/api/gkms_trans_data"
                        : "https://texture.gakumas.cn/api/gkms_texture_data";
                    Check(session.Services.Fields.Single(field => field.Key == "updateSource").Default.GetString() == expected,
                        "资源地址输入框的默认值必须是 DMM 原版地址");
                    var rendered = Path.Combine(root, package.Manifest.Id + "-resource-ui.txt");
                    Check(canvas.Frame(session, true, rendered), "资源更新设置绘制成功");
                    var text = File.ReadAllText(rendered).Replace("\r", "").Replace("\n", "");
                    Check(text.Contains("原版默认地址") && text.Contains(expected), "资源设置必须实际绘制完整默认地址");
                    Check(state.GetRawText() == "{}", "查看默认地址不能悄悄写入或覆盖设置");
                }
                if (!package.Manifest.HasSettingsUi) Check(canvas.ContentVertices == 0, package.Manifest.Id + ": 不应绘制空配置、重复说明或 JSON 编辑器");
                if (package.Manifest.Id == "textures")
                {
                    Check(canvas.ContentVertices > 0, "贴图更新设置在资源分区绘制");
                    Check(canvas.Frame(session) && canvas.ContentVertices == 0, "更新源及启动检查不得留在普通参数区");
                }
                Check(canvas.Vertices > 0, package.Manifest.Id + ": 未产生绘制数据");
            }
            Check(canvas.HasRenderedPixels(), "DX11 离屏目标没有控件像素");
        }
        using (var canvas = new Canvas())
        {
            var state = Json("""{"textTest":false,"autoCheckUpdates":true,"updateSource":"saved.zip"}""");
            JsonElement written = default;
            using var session = new SettingsUiSession(catalog["localization"], "zh-CN", () => state,
                (value, _) => { written = value; var next = JsonNode.Parse(state.GetRawText())!.AsObject(); foreach (var pair in value.EnumerateObject()) next[pair.Name] = JsonNode.Parse(pair.Value.GetRawText()); state = JsonSerializer.SerializeToElement(next); },
                _ => Task.FromResult("offline"), log.Add);
            session.OpenConfigUi(); canvas.Frame(session, true); canvas.Frame(session, true);
            // The last item is the section's Reload button; Save is on the same row.
            var resourceSave = new Vector2(40, canvas.LastItemTop + 10);
            state = Json("""{"textTest":true,"autoCheckUpdates":true,"updateSource":"saved.zip"}""");
            var io = ImGui.GetIO(); io.AddMousePosEvent(resourceSave.X, resourceSave.Y); canvas.Frame(session, true);
            io.AddMouseButtonEvent(0, true); canvas.Frame(session, true);
            io.AddMouseButtonEvent(0, false); canvas.Frame(session, true);
            Check(written.TryGetProperty("autoCheckUpdates", out _) && written.TryGetProperty("updateSource", out _) && !written.TryGetProperty("textTest", out _) && state.GetProperty("textTest").GetBoolean(),
                "资源分区保存两项更新设置，不覆盖普通参数的外部修改");
            canvas.Frame(session); var settingsSave = new Vector2(40, canvas.LastItemTop + 10);
            state = Json("""{"textTest":true,"autoCheckUpdates":false,"updateSource":"new.zip"}""");
            io.AddMousePosEvent(settingsSave.X, settingsSave.Y); canvas.Frame(session);
            io.AddMouseButtonEvent(0, true); canvas.Frame(session);
            io.AddMouseButtonEvent(0, false); canvas.Frame(session);
            Check(written.TryGetProperty("textTest", out _) && !written.TryGetProperty("updateSource", out _) && state.GetProperty("updateSource").GetString() == "new.zip",
                "普通参数保存不得覆盖资源更新设置");
        }
        foreach (var orientation in new[] { "game", "portrait", "landscape" })
        using (var canvas = new Canvas())
        {
            var state = JsonSerializer.SerializeToElement(new { orientation, resizable = false, keep = 42 });
            JsonElement written = default;
            using var session = new SettingsUiSession(catalog["window"], "zh-CN", () => state,
                (value, _) => written = value, _ => Task.FromResult("offline"), log.Add);
            session.OpenConfigUi(); canvas.Frame(session); canvas.Frame(session);
            var save = new Vector2(40, canvas.LastItemTop + 10);
            var io = ImGui.GetIO();
            io.AddMousePosEvent(save.X, save.Y); canvas.Frame(session);
            io.AddMouseButtonEvent(0, true); canvas.Frame(session);
            io.AddMouseButtonEvent(0, false); canvas.Frame(session);
            Check(written.ValueKind == JsonValueKind.Object && written.GetProperty("orientation").GetString() == orientation &&
                !written.GetProperty("resizable").GetBoolean() && !written.TryGetProperty("keep", out _),
                "中文窗口选项保存原配置值，仅合并本插件两项设置：" + orientation);
        }
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "example", "net6.0"));
        var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(sample, "plugin.json")), Catalog.Json)!;
        var normal = new Package(sample, m);
        var probe = normal with { Manifest = m with { SettingsUi = "Example.Greeting.ProbeSettingsUi" } };
        var ownCatalog = new Dictionary<string, Package> { [m.Id] = probe };
        File.WriteAllText(saved, """{"enabled":[],"settings":{"example.greeting":{"message":"original","unknown":{"keep":true}},"another.plugin":{"keep":42}}}""");
        JsonElement Read() => Catalog.ReadProfile(saved, ownCatalog).Settings![m.Id];
        var savedCount = 0;
        void Save(JsonElement value, bool replace)
        {
            ProfileEditor.Apply(root, ownCatalog, new Dictionary<string, bool>(),
                replace: new(m.Id, JsonNode.Parse(value.GetRawText())!.AsObject()), merge: !replace);
            savedCount++;
        }
        log.Clear();
        using (var canvas = new Canvas())
        {
            ISettingsUiContext services;
            var queued = false;
            using (var session = new SettingsUiSession(probe, "en", Read, Save, _ => Task.FromResult("reloaded"), log.Add))
            {
                services = session.Services;
                session.OpenConfigUi(); canvas.Frame(session); canvas.Frame(session);
                var coordinates = log.Last(s => s.Contains("button:")).Split("button:")[1].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                var io = ImGui.GetIO(); io.AddMousePosEvent(coordinates[0], coordinates[1]);
                canvas.Frame(session);
                io.AddMouseButtonEvent(0, true); canvas.Frame(session);
                io.AddMouseButtonEvent(0, false); canvas.Frame(session);
                Check(savedCount == 1 && Read().GetProperty("message").GetString() == "edited-by-imgui", "ImGui 按钮没有保存第三方配置");
                Check(Read().GetProperty("unknown").GetProperty("keep").GetBoolean() &&
                    Catalog.ReadProfile(saved).Settings!["another.plugin"].GetProperty("keep").GetInt32() == 42, "保存覆盖了未知字段或其他插件");
                Check(log.Any(s => s.EndsWith("ui-open")) && !log.Any(s => s.Contains("已加载")), "设置入口不应启动游戏插件");
                var rejection = Task.Run(() =>
                {
                    try { services.SaveSettings(Json("{}")); return false; } catch (InvalidOperationException) { return true; }
                }).GetAwaiter().GetResult();
                Check(rejection, "UI 线程限制");
                services.UiBuilder.Enqueue(() => queued = true);
                session.Dispose();
            }
            Check(services.Stopping.IsCancellationRequested && !queued && log.Count(s => s.EndsWith("ui-owned:True")) == 1 &&
                log.Count(s => s.EndsWith("ui-disposed")) == 1, "UI 取消、资源和事件卸载");
            try { services.UiBuilder.Enqueue(() => { }); throw new Exception("停止后投递应拒绝"); } catch (ObjectDisposedException) { }
            using var example = new SettingsUiSession(normal, "en", Read, Save, _ => Task.FromResult("offline"), log.Add);
            example.OpenConfigUi(); Check(canvas.Frame(example), "正式第三方设置示例绘制");
        }
        foreach (var fail in new[] { "throwInit", "throwDraw" })
        {
            log.Clear();
            using var canvas = new Canvas();
            var state = Json("{\"" + fail + "\":true,\"throwDispose\":true}");
            if (fail == "throwInit")
            {
                try { using var unexpected = new SettingsUiSession(probe, "en", () => state, (_, _) => { }, _ => Task.FromResult(""), log.Add); throw new Exception("初始化异常应传播"); }
                catch (InvalidOperationException e) when (e.Message == "ui-init-failure") { }
            }
            else
            {
                using var session = new SettingsUiSession(probe, "en", () => state, (_, _) => { }, _ => Task.FromResult(""), log.Add);
                session.OpenConfigUi(); Check(!canvas.Frame(session) && session.Fault == "ui-draw-failure", "绘制异常应停止该 UI");
                // No Render/EndFrame after a failed callback: it deliberately left an ImGui window unbalanced.
            }
            Check(log.Any(s => s.EndsWith("ui-owned:True")) && log.Any(s => s.EndsWith("ui-disposed")), "异常 UI 仍需完整清理");
        }
        Console.WriteLine("ImGui 专项通过：14 个内置设置页、第三方按钮交互、配置合并、初始化/绘制异常、卸载与 DX11 离屏渲染。");
    }

    private sealed class Renderer(GraphicsDevice device, OutputDescription output, int width, int height) : ImGuiRenderer(device, output, width, height)
    {
        public void Begin() { BeginUpdate(1f / 60); EndUpdate(); }
    }
    private sealed class Canvas : IDisposable
    {
        private readonly GraphicsDevice device;
        private readonly Texture color, staging;
        private readonly Framebuffer target;
        private readonly CommandList commands;
        private readonly Renderer renderer;
        private readonly IntPtr context;
        public int Vertices { get; private set; }
        public int ContentVertices { get; private set; }
        public float LastItemTop { get; private set; }
        public unsafe Canvas()
        {
            device = GraphicsDevice.CreateD3D11(new GraphicsDeviceOptions());
            color = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(900, 760, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
            staging = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(900, 760, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            target = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, color));
            commands = device.ResourceFactory.CreateCommandList();
            renderer = new Renderer(device, target.OutputDescription, 900, 760); context = ImGui.GetCurrentContext();
            ImGui.Render(); ImGuiAppearance.Configure(); renderer.RecreateFontDeviceTexture(device);
            Check(ImGui.GetIO().Fonts.Fonts[0].FindGlyphNoFallback('译').NativePtr != null, "中文字体未构建");
        }
        public bool Frame(SettingsUiSession session, bool showResources = false, string? logFile = null)
        {
            ImGui.SetCurrentContext(context); renderer.Begin();
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(900, 760));
            ImGui.Begin("Settings", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
            if (logFile != null) ImGui.LogToFile(-1, logFile);
            var before = ImGui.GetWindowDrawList().VtxBuffer.Size;
            if (!session.DrawFrame()) return false;
            if (showResources)
            {
                ImGui.PushID("resource-update");
                if (!session.DrawResourceSettings()) return false;
                ImGui.PopID();
            }
            LastItemTop = ImGui.GetItemRectMin().Y;
            ContentVertices = ImGui.GetWindowDrawList().VtxBuffer.Size - before;
            if (logFile != null) ImGui.LogFinish();
            ImGui.End();
            commands.Begin(); commands.SetFramebuffer(target); commands.ClearColorTarget(0, RgbaFloat.Black);
            renderer.Render(device, commands); commands.End(); device.SubmitCommands(commands);
            Vertices = ImGui.GetDrawData().TotalVtxCount;
            return true;
        }
        public void CheckWrappedScrolling()
        {
            const string text = "翻译界面、剧情、主数据和主页字幕。开关、参数及启动时加载的词典下次启动游戏生效；剧情文件按原 DMM 在每次请求时读取，更新后可在后续请求中生效。补全字体由框架启动时固定启用。";
            foreach (var width in new[] { 220, 390, 600 })
            foreach (var scale in new[] { 1f, 1.5f })
            foreach (var scroll in new[] { 0, 60, 100, 140, 170 })
            for (var frame = 0; frame < 3; frame++)
            {
                ImGui.SetCurrentContext(context); renderer.Begin();
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(width + 40, 500));
                ImGui.Begin("Scroll regression", ImGuiWindowFlags.NoSavedSettings);
                ImGui.BeginChild("detail", new Vector2(width, 420), true);
                ImGui.SetWindowFontScale(scale); ImGui.SetScrollY(scroll);
                ImGui.Dummy(new Vector2(10, 70));
                var draw = ImGui.GetWindowDrawList(); var start = draw.VtxBuffer.Size;
                PluginUiText.Wrapped(text);
                var bottom = ImGui.GetItemRectMax().Y;
                for (var i = start; i < draw.VtxBuffer.Size; i++)
                    Check(draw.VtxBuffer[i].pos.Y <= bottom + 1, $"滚动文字越过段落底边: width={width}, scale={scale}, scroll={scroll}");
                PluginUiText.Wrapped("下一段不得被上方文字覆盖。\nAPI / ZIP / 本地文件");
                ImGui.Dummy(new Vector2(10, 900)); ImGui.EndChild(); ImGui.End();
                commands.Begin(); commands.SetFramebuffer(target); commands.ClearColorTarget(0, RgbaFloat.Black);
                renderer.Render(device, commands); commands.End(); device.SubmitCommands(commands);
            }
        }
        public bool HasRenderedPixels()
        {
            commands.Begin(); commands.CopyTexture(color, staging); commands.End(); device.SubmitCommands(commands); device.WaitForIdle();
            var mapped = device.Map(staging, MapMode.Read);
            try
            {
                var colors = new HashSet<int>();
                for (var y = 0; y < 760; y += 5)
                    for (var x = 0; x < 900; x += 5) colors.Add(Marshal.ReadInt32(mapped.Data, checked((int)(y * mapped.RowPitch + x * 4))));
                return colors.Count > 4;
            }
            finally { device.Unmap(staging); }
        }
        public void Dispose()
        {
            device.WaitForIdle(); renderer.Dispose(); commands.Dispose(); target.Dispose(); color.Dispose(); staging.Dispose(); device.Dispose();
            ImGui.DestroyContext(context);
        }
    }
}
