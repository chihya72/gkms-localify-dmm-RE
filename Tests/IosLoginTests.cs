using Gkms.Host;
using Campus.Common.Network;
using Campus.Common.User;
using Campus.Common.Proto.Client.Api;
using Campus.Common.Proto.Client.Transaction;

internal static class IosLoginTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string work, string packages)
    {
        var profile = Path.Combine(work, "ios-profile.json");
        var api = Api.Instance; var user = UserDataManager.Instance;
        var quality = new Campus.Common.CampusQualityManager();
        var logs = new List<string>();
        void Profile(bool enabled) => File.WriteAllText(profile, enabled ? "{\"enabled\":[\"ios-login\"]}" : "{\"enabled\":[]}");
        Api.SetAuthToken(""); api._platform = "DMM"; api.Initialize(); Api.User.Requests.Clear();
        user.Reset(null); Profile(true);
        var game = new TestGame();
        using var host = new PluginHost(game, logs.Add);
        host.Start(packages, profile, work);
        Check(host.Active.Contains("ios-login"), string.Join("\n", logs));
        Check(!game.Patched.Any(m => m.DeclaringType == typeof(Qua.Network.ApiBase) && m.Name == "Initialize"),
            "iOS 模块不再安装导致原生启动闪退的 Initialize 钩子");
        Check(!host.GetWarnings().ContainsKey("ios-login") && host.GetStates()["ios-login"].State == PluginRunState.Running,
            "等待认证不应伪装成插件失败或余额未确认警告");
        quality.Update();
        Check(Api.User.Requests.Count == 0 && api._platform == "DMM", "未认证时不得发送 iOS 余额请求");
        new AuthLoginResponse { GameAuthToken = "secret-do-not-log" }.Read();
        quality.Update();
        Check(Api.User.Requests.Count == 0, "解析响应后必须等待令牌写入");
        Api.SetAuthToken("secret-do-not-log"); api.Initialize();
        Check(api._platform == "iOS", "不挂钩无业务逻辑的 Initialize，平台由原版构造/取实例/取平台链维护");
        var recreated = new Api(); Check(recreated._platform == "iOS", "认证后的新 API 实例也必须沿用 iOS 平台");
        quality.Update(); Check(Api.User.Requests.Count == 0, "等待初始用户数据，避免刷新被初始化覆盖");
        user.Reset(new() { PaidBalance = 11 }); quality.Update();
        var first = Api.User.Requests.Last();
        Check(!host.GetWarnings().ContainsKey("ios-login"), "余额请求过程中不显示开发诊断状态");
        Check(first.Headers[0].Value == "iOS", "实际发送的请求头使用 iOS");
        first.Complete(200, 600, 9); quality.Update();
        Check(user.UserBalance!.FreeBalance == 200 && user.DisplayedPaid == 600 && user.Notifications == 1,
            "服务器余额通过游戏更新入口刷新当前显示，WebStore 余额不得混入 iOS 余额");
        Check(!host.GetWarnings().ContainsKey("ios-login") && logs.Any(l => l.Contains("iOS 余额确认成功")), "收到且应用余额后才能报告成功");
        Check(!logs.Any(l => l.Contains("secret-do-not-log")), "平台和余额日志不得泄露认证令牌");
        quality.Update(); Check(Api.User.Requests.Count == 1, "每次启用仅刷新一次，不逐帧请求");

        void Reload() { Profile(false); host.ReloadProfile(); Profile(true); host.ReloadProfile(); quality.Update(); }
        Reload(); var wrong = Api.User.Requests.Last(); wrong.Headers[0] = new(api.PlatformHeaderKey, "DMM");
        wrong.Complete(1, 2); quality.Update();
        Check(user.DisplayedPaid == 600 && host.GetWarnings().ContainsKey("ios-login"), "字段为 iOS 但实际请求头为 DMM 时不假报成功");
        Check(host.GetStates()["ios-login"].State == PluginRunState.Failed, "真实刷新失败统一显示失败");
        Reload(); var duplicate = Api.User.Requests.Last(); duplicate.Headers.Add(new(api.PlatformHeaderKey, "DMM"));
        duplicate.Complete(1, 2); quality.Update();
        Check(user.DisplayedPaid == 600 && host.GetWarnings().ContainsKey("ios-login"), "重复平台头不得通过验证");
        Reload(); Api.User.Requests.Last().Complete(1, 2, logHeaders: false); quality.Update();
        Check(user.DisplayedPaid == 600 && host.GetWarnings().ContainsKey("ios-login"), "没有实际请求头证据时不得把缓存或未观察的响应当作验证成功");
        Reload(); Api.User.Requests.Last().Completion.SetException(new IOException("模拟通信错误")); quality.Update();
        Check(user.DisplayedPaid == 600 && host.GetWarnings().ContainsKey("ios-login"), "请求失败保留原余额并报告未确认");
        Reload(); Api.User.Requests.Last().Complete(0, 0); quality.Update();
        Check(user.DisplayedPaid == 0 && !host.GetWarnings().ContainsKey("ios-login"), "合法零余额也必须应用");
        Check(host.GetStates()["ios-login"].State == PluginRunState.Running, "成功重试清除失败状态");
        Reload(); var cancelled = Api.User.Requests.Last(); var notified = user.Notifications;
        _ = new AuthLoginRequest();
        Check(cancelled.Token.IsCancellationRequested && api._platform == "DMM", "重新认证取消旧请求并恢复 DMM");
        Api.SetAuthToken("secret-next-login"); quality.Update();
        Check(Api.User.Requests.Last() == cancelled && api._platform == "DMM", "写入令牌本身不能代替原版的完整认证响应");
        new AuthLoginResponse { GameAuthToken = "secret-next-login" }.Read(); quality.Update(); var next = Api.User.Requests.Last();
        cancelled.Complete(99, 999); quality.Update();
        Check(user.Notifications == notified, "上一登录会话的迟到响应不能改写当前余额");
        next.Complete(3, 4); quality.Update(); Check(user.DisplayedPaid == 4, "新认证完成后刷新新会话余额");
        Reload(); var unload = Api.User.Requests.Last();
        Profile(false); host.ReloadProfile(); unload.Complete(9, 999); quality.Update();
        Check(unload.Token.IsCancellationRequested && user.DisplayedPaid == 4 && api._platform == "DMM", "停用取消刷新，不应用迟到响应，恢复原平台");
        Check(recreated._platform == "DMM", "停用恢复所有接管过的 API 实例");
        Check(Cysharp.Threading.Tasks.UniTaskExtensions.Observed >= 2, "停用和重登后的未完成任务交回游戏观察器处理");
        host.Pump(); var dmm = Api.User.Requests.Last();
        Check(dmm != unload && dmm.Headers[0].Value == "DMM" && !host.Active.Contains("ios-login"), "停用后仅刷新原平台余额，iOS 功能已卸载");
        Check(host.GetStates()["ios-login"].State == PluginRunState.Disabled, "停用后的后台刷新不是余额未确认状态");
        dmm.Complete(8, 18); host.Pump();
        Check(user.DisplayedPaid == 18 && !host.GetWarnings().ContainsKey("ios-login"), "停用后 DMM 服务器余额通过原游戏通知入口显示");

        Reload(); Api.User.Requests.Last().Complete(1, 2, isDone: false); quality.Update();
        Check(user.DisplayedPaid == 18 && host.GetWarnings().ContainsKey("ios-login"), "实际 IApiRequest 未完成时不能应用 Response");
        Reload(); Api.User.Requests.Last().Complete(1, 2, error: true); quality.Update();
        Check(user.DisplayedPaid == 18 && host.GetWarnings().ContainsKey("ios-login"), "实际 IApiRequest.Error 存在时不能应用余额");
        Reload(); Api.User.Requests.Last().Complete(1, 2, noResponse: true); quality.Update();
        Check(user.DisplayedPaid == 18 && host.GetWarnings().ContainsKey("ios-login"), "缺少 Response 保留旧余额");

        Reload(); var stale = Api.User.Requests.Last();
        user.UpdateUserBalance(new() { FreeBalance = 40, PaidBalance = 50 });
        stale.Complete(1, 2); quality.Update();
        Check(user.DisplayedPaid == 50, "刷新期间游戏自己更新余额时不能被旧响应覆盖");
        quality.Update(); var retry = Api.User.Requests.Last();
        Check(retry != stale, "变化后的余额只补发一次新查询");
        retry.Complete(40, 50); quality.Update();
        Check(!host.GetWarnings().ContainsKey("ios-login"), "重新读取当前余额后解除未确认状态");

        Reload(); var unchanged = Api.User.Requests.Last();
        user.UpdateUserBalance(new() { FreeBalance = 40, PaidBalance = 50 });
        unchanged.Complete(40, 51); quality.Update();
        Check(user.DisplayedPaid == 51, "游戏替换等值余额对象不应误判为金额变化而反复查询");
        Reload(); Api.User.Requests.Last().Complete(0, -1); quality.Update();
        Check(user.DisplayedPaid == 51 && host.GetWarnings().ContainsKey("ios-login"), "异常负余额不写入游戏模型");

        Profile(false); host.ReloadProfile(); host.Pump(); var oldDmm = Api.User.Requests.Last();
        Profile(true); host.ReloadProfile(); quality.Update(); var newIos = Api.User.Requests.Last();
        Check(oldDmm.Token.IsCancellationRequested && newIos.Headers[0].Value == "iOS", "快速重新启用先取消 DMM 收尾，再注册 iOS 功能");
        oldDmm.Complete(999, 999); host.Pump();
        Check(user.DisplayedPaid == 51, "旧停用请求不能覆盖重新启用后的显示");
        newIos.Complete(60, 70); quality.Update();
        Check(user.DisplayedPaid == 70, "快速开关后只应用最新启用请求");

        Profile(false); host.ReloadProfile(); host.Pump(); var stopError = Api.User.Requests.Last();
        stopError.Completion.SetException(new IOException("DMM 刷新通信失败")); host.Pump();
        Check(user.DisplayedPaid == 70 && host.GetWarnings().ContainsKey("ios-login"), "关闭后的通信失败也必须可见且不得清空余额");
        Reload(); Api.User.Requests.Last().Complete(60, 70); quality.Update();
        Profile(false); host.ReloadProfile(); host.Pump(); var stopLogin = Api.User.Requests.Last();
        _ = new AuthLoginRequest(); host.Pump();
        Check(stopLogin.Token.IsCancellationRequested && user.DisplayedPaid == 70, "停用收尾中重登必须取消旧用户请求");
        Reload(); Profile(false); host.ReloadProfile(); host.Pump(); var stopShutdown = Api.User.Requests.Last();
        host.Dispose();
        Check(stopShutdown.Token.IsCancellationRequested && game.Live == 0, "宿主关闭取消停用收尾并移除全部临时 Hook");
        Api.SetAuthToken(""); user.Reset(new());
        Console.WriteLine("iOS 登录专项通过：实机 IApiRequest 返回结构、双向余额刷新、失败保留、并发更新防覆盖、快速开关、重登/关机取消、平台头与令牌脱敏。");
    }
}
