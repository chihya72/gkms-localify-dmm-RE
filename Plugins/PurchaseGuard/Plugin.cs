using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.PurchaseGuard;

public sealed class Plugin : ILocalifyPlugin
{
    private static Func<object>? createDenial;
    private static object? fallback;
    private static MethodInfo? notify;
    private static IPluginContext? session;

    public void Start(IPluginContext context)
    {
        var purchase = context.RequireType("Assembly-CSharp", "Campus.Common.Purchase.InAppPurchaseManager");
        var check = purchase.GetMethods(GameMethods.Declared).Single(m => m.Name == "CheckPurchaseAsync" &&
            m.GetParameters() is [{ ParameterType.FullName: "Qua.InAppPurchasing.IPurchaseItem" }, { ParameterType.Name: "CancellationToken" }]);
        var task = check.ReturnType;
        if (!task.IsGenericType || task.GetGenericTypeDefinition().FullName != "Cysharp.Threading.Tasks.UniTask`1" ||
            task.GenericTypeArguments[0].FullName != "Qua.InAppPurchasing.ICheckPurchaseResult")
            throw new InvalidDataException("客户端充值检查的返回类型已改变。");

        var result = context.RequireType("quaunity-inapp-purchase-manager", "Qua.InAppPurchasing.CheckPurchaseResult");
        var construct = result.GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException(result.FullName, ".ctor()");
        var canPurchase = result.GetProperty("CanPurchase") ?? throw new MissingMemberException(result.FullName, "CanPurchase");
        var contract = task.GenericTypeArguments[0];
        var completed = task.GetConstructor(new[] { contract }) ?? throw new MissingMethodException(task.FullName, ".ctor(result)");
        var cast = contract.IsAssignableFrom(result) ? null : result.GetMethods()
            .Single(m => m.Name == "Cast" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(contract);
        createDenial = () =>
        {
            var denied = construct.Invoke(null);
            canPurchase.SetValue(denied, false);
            return completed.Invoke(new[] { cast == null ? denied : cast.Invoke(denied, null) });
        };
        fallback = createDenial();
        notify = context.Find("Assembly-CSharp", "Campus.Common.ToastManager", "Show", "System.String");
        session = context;
        context.Patch(check, GameMethods.Handler<Plugin>(nameof(Cancel)).MakeGenericMethod(task));
    }

    public static bool Cancel<T>(ref T __result)
    {
        try { __result = (T)createDenial!(); }
        catch (Exception e) { __result = (T)fallback!; session?.Log(e.GetBaseException().Message); }
        try { notify?.Invoke(null, new object[] { "充值保护已启用，本次充值已取消。" }); }
        catch (Exception e) { session?.Log(e.GetBaseException().Message); }
        return false;
    }
    public void Dispose() { createDenial = null; fallback = null; notify = null; session = null; }
}
