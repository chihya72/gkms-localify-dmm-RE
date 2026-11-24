using System.Runtime.CompilerServices;

namespace Campus.Common
{
    public enum WindowOrientationFixture { Game = 0, Portrait = 2, Landscape = 3 }
    public class ScreenOrientationControllerBase
    {
        public WindowOrientationFixture LastOrientation;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InternalSetOrientationAsync(WindowOrientationFixture orientation, bool animate, CancellationToken token) => LastOrientation = orientation;
    }
}

namespace Campus.Common.Proto.Client.Master
{
    public sealed class LateMaster : Google.Protobuf.IMessage
    {
        public string Id { get; set; } = "late";
        public string Name { get; set; } = "后来下载资源的主表原文";
        public void InternalMergeFrom() { }
    }
}

namespace Campus.Common.Proto.Client.Enums
{
    public enum ProduceStepType { Normal, Sp }
}

namespace Campus.InGame
{
    using Campus.Common.Proto.Client.Enums;
    public static class ExamExtensions
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ProduceStepType ExchangeSpToNormal(ProduceStepType type) => ProduceStepType.Normal;
    }
}

namespace Campus.Common.Master
{
    public record Skin(string Id, string ProduceCardId, int CutInType);
    public sealed class ProduceCardSkinMaster
    {
        public List<Skin> Items = new()
        {
            new("small", "card", 1), new("grand", "card", 3), new("irrelevant", "other", 3)
        };
        public int Reads;
        public List<Skin> GetAllWithSortByKey(int sort) { Reads++; return Items; }
    }
    public static partial class MasterManager { public static ProduceCardSkinMaster ProduceCardSkinMaster { get; } = new(); }
}

namespace Campus.Common.User
{
    public class UserProduceCardSkinCollection
    {
        public HashSet<string> Items { get; } = new();
        public bool IsOwned(string id) => Items.Contains(id);
    }
    public partial class UserDataManagerBase<T> { public static UserProduceCardSkinCollection UserProduceCardSkinList { get; } = new(); }
    public partial class UserDataManager : UserDataManagerBase<UserDataManager> { }
}

namespace Campus.InGame.Card
{
    public class ExamCardData
    {
        public string Selection = "";
        public string Id => "card";
        public string ProduceCardSkinId { [MethodImpl(MethodImplOptions.NoInlining)] get => Selection; }
    }
}

namespace VL
{
    public static class VLUtility
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static UnityEngine.Vector2 GetLimitedResolution(int width, int height, UnityEngine.Vector2 aspectRatio,
            int maxPixels, float bufferScale, bool firstCall) => new(width, height);
    }
}
