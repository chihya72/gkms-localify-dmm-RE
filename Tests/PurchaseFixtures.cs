using System.Runtime.CompilerServices;

namespace Google.Protobuf.WellKnownTypes { public sealed class Empty { } }

namespace Campus.Common.Proto.Client.Api
{
    public sealed class AuthLoginRequest { [MethodImpl(MethodImplOptions.NoInlining)] public AuthLoginRequest() { } }
    public sealed class AuthLoginResponse : Google.Protobuf.IMessage
    {
        public string GameAuthToken { get; set; } = "";
        [MethodImpl(MethodImplOptions.NoInlining)] public void InternalMergeFrom() { }
    }
    public sealed class UserGetBalanceResponse
    {
        public int FreeBalance { get; set; }
        public int PaidBalance { get; set; }
        public int WebStorePaidBalance { get; set; }
    }
    public sealed class HomeLoginResponse : Google.Protobuf.IMessage
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void InternalMergeFrom() { }
    }
}
namespace Firebase.Platform
{
    public static class PlatformInformation
    {
        public static bool IsIOS { [MethodImpl(MethodImplOptions.NoInlining)] get => false; }
        public static bool IsAndroid { [MethodImpl(MethodImplOptions.NoInlining)] get => true; }
    }
}
namespace Qua.Network
{
    public interface IApiError { string Code { get; } string Action { get; } }
    public sealed class ApiError : IApiError { public string Code { get; set; } = ""; public string Action { get; set; } = ""; }
    public sealed class ApiCallOption(bool disableApiErrorHandler, bool disableOnWillExecute, bool disableOnDidComplete,
        bool disableOnStartApi, bool disableOnEndApi)
    {
        public bool DisableApiErrorHandler { get; } = disableApiErrorHandler;
        public bool DisableOnWillExecute { get; } = disableOnWillExecute;
        public bool DisableOnDidComplete { get; } = disableOnDidComplete;
        public bool DisableOnStartApi { get; } = disableOnStartApi;
        public bool DisableOnEndApi { get; } = disableOnEndApi;
    }
    public interface IApiRequest<TRequest,TResponse>
    {
        bool IsDone { get; }
        TResponse? Response { get; }
        IApiError? Error { get; }
    }
    public sealed class ApiRequest<TRequest,TResponse> : IApiRequest<TRequest,TResponse>
    {
        public bool IsDone { get; set; }
        public TResponse? Response { get; set; }
        public IApiError? Error { get; set; }
    }
    public class ApiBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public ApiBase() { }
        public string _authToken = "";
        public string _platform = "DMM";
        private static readonly Campus.Common.Network.Api instance = new();
        public static Campus.Common.Network.Api Instance { [MethodImpl(MethodImplOptions.NoInlining)] get => instance; }
        public static bool HasInstance => true;
        public string PlatformHeaderKey => "x-platform";
        [MethodImpl(MethodImplOptions.NoInlining)] public string GetPlatformString() => _platform;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void SetAuthToken(string token) => instance._authToken = token;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Initialize() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void ApiSuccessLog(string apiName, object request, object requestHeader,
            object response, object responseHeader, long elapsedMilliseconds) { }
    }
}
namespace Campus.Common.Network
{
    using Campus.Common.Proto.Client.Api;
    public sealed class Api : Qua.Network.ApiBase
    {
        public static class User
        {
            public sealed class Pending
            {
                public readonly TaskCompletionSource<Qua.Network.IApiRequest<Google.Protobuf.WellKnownTypes.Empty,UserGetBalanceResponse>> Completion = new();
                public readonly List<KeyValuePair<string, string>> Headers = new();
                public CancellationToken Token;
                public Qua.Network.ApiCallOption? Options;
                public void RejectDateChanged()
                {
                    if (Options?.DisableApiErrorHandler != true) TitleTransitions++;
                    Completion.TrySetResult(new Qua.Network.ApiRequest<Google.Protobuf.WellKnownTypes.Empty,UserGetBalanceResponse> {
                        IsDone = true, Error = new Qua.Network.ApiError { Code = "2006", Action = "title" } });
                }
                public void Complete(int free, int paid, int web = 0, bool logHeaders = true, bool isDone = true, bool error = false, bool noResponse = false)
                {
                    var response = new UserGetBalanceResponse { FreeBalance = free, PaidBalance = paid, WebStorePaidBalance = web };
                    if (logHeaders) Instance.ApiSuccessLog("User/GetBalance", new object(), Headers, response, Array.Empty<object>(), 1);
                    Completion.TrySetResult(new Qua.Network.ApiRequest<Google.Protobuf.WellKnownTypes.Empty,UserGetBalanceResponse> {
                        IsDone = isDone, Response = noResponse ? null : response, Error = error ? new Qua.Network.ApiError() : null });
                }
            }
            public static readonly List<Pending> Requests = new();
            public static int TitleTransitions;
            public static int LoadingEvents;
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Cysharp.Threading.Tasks.UniTask<Qua.Network.IApiRequest<Google.Protobuf.WellKnownTypes.Empty,UserGetBalanceResponse>> GetBalanceAsync(CancellationToken? ct,
                Qua.Network.ApiCallOption? callOption, object? errorHandler, string? requestIdForResponseCache)
            {
                if (requestIdForResponseCache != null) throw new Exception("余额刷新不可使用旧响应缓存");
                var pending = new Pending { Token = ct ?? default, Options = callOption };
                if (callOption?.DisableOnWillExecute != true || callOption.DisableOnDidComplete != true ||
                    callOption.DisableOnStartApi != true || callOption.DisableOnEndApi != true) LoadingEvents++;
                pending.Headers.Add(new(Instance.PlatformHeaderKey, Instance._platform));
                pending.Headers.Add(new("authorization", Instance._authToken));
                pending.Token.Register(() => pending.Completion.TrySetCanceled());
                Requests.Add(pending);
                return new(pending.Completion.Task);
            }
        }
    }
}
namespace Campus.Common.Proto.Client.Transaction
{
    public sealed class UserBalance { public int FreeBalance { get; set; } public int PaidBalance { get; set; } }
}
namespace Campus.Common.User
{
    using Campus.Common.Proto.Client.Transaction;
    public partial class UserDataManager
    {
        public static UserDataManager Instance { get; } = new();
        public static bool HasInstance { get; set; } = true;
        public UserBalance? UserBalance { get; private set; } = new();
        public int Notifications { get; private set; }
        public int DisplayedPaid { get; private set; }
        public void Reset(UserBalance? value) { UserBalance = value; Notifications = 0; DisplayedPaid = value?.PaidBalance ?? 0; }
        public void UpdateUserBalance(UserBalance value) { UserBalance = value; DisplayedPaid = value.PaidBalance; Notifications++; }
    }
}

namespace Cysharp.Threading.Tasks
{
    public readonly struct UniTask<T>
    {
        private readonly Task<T> task;
        public UniTask(T result) => task = Task.FromResult(result);
        public UniTask(Task<T> task) => this.task = task;
        public T Result => task.GetAwaiter().GetResult();
        public TaskAwaiter<T> GetAwaiter() => task.GetAwaiter();
        public UniTask AsUniTask() => new(task);
    }
    public readonly record struct UniTask(Task Task);
    public static class UniTaskExtensions
    {
        public static int Observed;
        public static void Forget(UniTask task)
        {
            Observed++;
            _ = task.Task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
namespace Qua.InAppPurchasing
{
    public interface IPurchaseItem { }
    public interface ICheckPurchaseResult { bool CanPurchase { get; } }
    public class CheckPurchaseResult : ICheckPurchaseResult { public bool CanPurchase { get; set; } = true; }
}
namespace Campus.Common.Purchase
{
    using Cysharp.Threading.Tasks;
    using Qua.InAppPurchasing;

    public sealed class InAppPurchaseManager
    {
        public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public UniTask<ICheckPurchaseResult> CheckPurchaseAsync(IPurchaseItem? item, CancellationToken token)
        {
            Calls++; return new(new CheckPurchaseResult());
        }
    }
}
