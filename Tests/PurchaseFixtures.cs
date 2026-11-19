using System.Runtime.CompilerServices;

namespace Cysharp.Threading.Tasks
{
    public readonly record struct UniTask<T>(T Result);
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
