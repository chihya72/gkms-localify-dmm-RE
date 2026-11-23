using System.Runtime.CompilerServices;

namespace Campus.Common.Master
{
    public sealed class MasterRows<T> { public List<T> Items = new(); public List<T> GetAllWithSortByKey(int order) => Items; }
    public record Costume(string Id, string CostumeHeadId = "", string DefaultCostumeHeadId = "", string CostumeColorGroupId = "", long ViewStartTime = 0);
    public record CostumeHead(string Id);
    public record IdolSkin(string IdolCardId, string CostumeId, string CostumeHeadId)
    {
        public Costume GetCostume() => new(CostumeId);
        public CostumeHead GetCostumeHead() => new(CostumeHeadId);
    }
    public record PhotoGroup(string[] CostumeIds);
    public static partial class MasterManager
    {
        public static MasterRows<Costume> CostumeMaster { get; } = new();
        public static MasterRows<IdolSkin> IdolCardSkinMaster { get; } = new();
        public static MasterRows<PhotoGroup> CostumePhotoGroupMaster { get; } = new();
    }
}
namespace Campus.Common.User
{
    public interface IUserRow { string Id { get; } }
    public class UserCostume : IUserRow { public string CostumeId { get; set; } = ""; public string Id => CostumeId; public UserCostume Clone() => new() { CostumeId = CostumeId }; }
    public class UserHead : IUserRow { public string CostumeHeadId { get; set; } = ""; public string Id => CostumeHeadId; public UserHead Clone() => new() { CostumeHeadId = CostumeHeadId }; }
    public class UserRows<T> where T : IUserRow
    {
        private readonly Dictionary<string,T> items = new();
        public int Count => items.Count;
        [MethodImpl(MethodImplOptions.NoInlining)] public Dictionary<string,T> GetAll() => items;
        [MethodImpl(MethodImplOptions.NoInlining)] public IEnumerable<T> FindBy(Predicate<T> filter) => items.Values.Where(v => filter(v));
        public List<T> GetAllList(Comparison<T>? comparison) => items.Values.ToList();
        public bool Exists(string id) => items.ContainsKey(id);
        public T? FindById(string id) => items.GetValueOrDefault(id);
        public void Add(T row) => items[row.Id] = row;
        public bool Remove(T row) => items.Remove(row.Id);
    }
    // Give Harmony stable, non-shared CLR entry points. Shared IL2CPP entry points are
    // exercised by NativeBridgeTests; detouring CLR's generic stubs is not equivalent
    // and can corrupt its runtime method cache when collectible packages are reloaded.
    public class UserCostumeCollection : UserRows<UserCostume>
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public new Dictionary<string, UserCostume> GetAll() => base.GetAll();
        [MethodImpl(MethodImplOptions.NoInlining)] public new IEnumerable<UserCostume> FindBy(Predicate<UserCostume> filter) => base.FindBy(filter);
    }
    public class UserCostumeHeadCollection : UserRows<UserHead>
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public new Dictionary<string, UserHead> GetAll() => base.GetAll();
        [MethodImpl(MethodImplOptions.NoInlining)] public new IEnumerable<UserHead> FindBy(Predicate<UserHead> filter) => base.FindBy(filter);
    }
    public class UserIdolCardSkinCollection
    { [MethodImpl(MethodImplOptions.NoInlining)] public bool Exists(string id) => false; }
    public partial class UserDataManagerBase<T> : UserDataManagerState { }
    public class UserDataManagerState
    {
        public static UserCostumeCollection Bodies = new();
        public static UserCostumeHeadCollection Heads = new();
        public static UserCostumeCollection _userCostumeList { [MethodImpl(MethodImplOptions.NoInlining)] get => Bodies; }
        public static UserCostumeHeadCollection _userCostumeHeadList { [MethodImpl(MethodImplOptions.NoInlining)] get => Heads; }
    }
}
namespace Campus.OutGame
{
    public class MeishiBasePIdolTabPageModel
    {
        public static bool Throw;
        public bool Owned;
        [MethodImpl(MethodImplOptions.NoInlining)] public MeishiBasePIdolTabPageModel()
        {
            Owned = new Campus.Common.User.UserIdolCardSkinCollection().Exists("i_card-skin-test");
            if (Throw) throw new InvalidOperationException("namecard fixture");
        }
    }
}
namespace Campus.OutGame.PictureBook
{
    public class PictureBookLiveThumbnailView
    {
        public bool Unlocked, Skin, Exempt;
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetReleaseDataAsync(object live, string id, bool unlocked, bool isNew, bool hasSkin, CancellationToken ct)
        { Unlocked = unlocked; Skin = hasSkin; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetUnReleaseDataAsync(object live, bool exempt, string id, CancellationToken ct) => Exempt = exempt;
    }
}
namespace Campus.Photography
{
    public class PhotographyCostumeSettingListItemModel { public bool IsDisabled { [MethodImpl(MethodImplOptions.NoInlining)] get => true; } }
    public class PhotographyCostumeSettingData
    {
        public List<Campus.Common.Master.Costume> Costumes = new() { new("skin") };
        public List<Campus.Common.Master.CostumeHead> Heads = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public List<Campus.Common.Master.Costume> GetCostumes(string id) => Costumes;
        [MethodImpl(MethodImplOptions.NoInlining)] public List<Campus.Common.Master.CostumeHead> GetCostumeHeads(string id) => Heads;
    }
    public class Photography3DPresenter { public bool IsGyroSupported { [MethodImpl(MethodImplOptions.NoInlining)] get => false; } }
    public class PhotographyActivityController
    {
        public Campus.Common.CampusGyroController? Controller;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Initialize(UnityEngine.Rendering.VolumeProfile profile, Campus.Common.CampusGyroController? gyro, float angle) => Controller = gyro;
    }
}
namespace UnityEngine.Rendering { public class VolumeProfile { } }
namespace Campus.Common
{
    public class CampusGyroController
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public CampusGyroController() { }
        public VL.VLGyro? _gyro { get; set; }
        public bool _initialized { get; set; }
        public bool _isGyro { get; set; }
        public UnityEngine.Quaternion _baseRotation { get; set; } = new(0,0,0,1);
        public int _lastAccessFrame { get; set; }
        public UnityEngine.Quaternion _rotation { get; set; } = new(0,0,0,1);
        public int NativeCalls;
        public UnityEngine.Quaternion Rotation { [MethodImpl(MethodImplOptions.NoInlining)] get { UpdateRotation(); return _rotation; } }
        [MethodImpl(MethodImplOptions.NoInlining)] public void ResetRotation()
        { NativeCalls++; if (_gyro != null) _baseRotation = UnityEngine.Quaternion.Inverse(_gyro.gyroRotationRaw); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateRotation()
        { NativeCalls++; if (_gyro != null) _rotation = _baseRotation * _gyro.gyroRotationRaw; }
    }
}
namespace VL
{
    // A call-routing fixture, not a model of VLGyro filtering. Native cache and
    // force-init tests separately check the original DMM entry points and values.
    public class VLGyro
    {
        public int _updateType;
        public bool gyroSupported { get; private set; }
        public bool _defaultGyroEnable, _initialized, _firstUpdate;
        public UnityEngine.Quaternion _rotation, _gestureRotation, _startRotation, _hybridInvRotation;
        public int Resets;
        public UnityEngine.Quaternion gyroRotationRaw
        {
            get
            {
                var q = new UnityEngine.Quaternion(0,0,0,1);
                UnityEngine.Gyroscope.attitude_Internal_Injected(0, ref q);
                return new(-q.x, -q.y, q.z, q.w);
            }
        }
        public void ResetRotation() => Resets++;
    }
}
namespace UnityEngine
{
    public static class SystemInfo { [MethodImpl(MethodImplOptions.NoInlining)] public static bool IsGyroAvailable() => false; }
    public static class Gyroscope
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool getEnabled_Internal(int i) => false;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void setEnabled_Internal(int i, bool v) => throw new InvalidOperationException("No desktop hardware");
        [MethodImpl(MethodImplOptions.NoInlining)] public static void attitude_Internal_Injected(int i, ref Quaternion q) => q = new(0,0,0,1);
        [MethodImpl(MethodImplOptions.NoInlining)] public static void gravity_Internal_Injected(int i, ref Vector3 v) => v = default;
    }
}
namespace Campus.Live
{
    public class LivePanoramaCameraManager
    {
        public Campus.Common.CampusGyroController? _gyroController { get; set; }
        public bool IsGyroReady { [MethodImpl(MethodImplOptions.NoInlining)] get => false; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void LateUpdate() { }
    }
}
namespace UnityEngine
{
    public static class Screen
    {
        public static int width { get; set; } = 1280;
        public static int height { get; set; } = 720;
        public static void SetResolution(int w, int h, bool full) { width = w; height = h; }
    }
}
namespace Campus.Common.StandAloneWindow
{
    public class WindowManager { public int Calls; [MethodImpl(MethodImplOptions.NoInlining)] public void ApplyOrientationSettings(int orientation) => Calls++; }
    public class WindowHandle { public int Calls; [MethodImpl(MethodImplOptions.NoInlining)] public IntPtr SetWindowLong(int key, IntPtr value) { Calls++; return value; } }
    public class AspectRatioHandler { public int Calls; [MethodImpl(MethodImplOptions.NoInlining)] public void NudgeWindow() => Calls++; }
}
namespace UnityEngine.UI
{
    public class Text { public string Value = ""; public string text { get => Value; [MethodImpl(MethodImplOptions.NoInlining)] set => Value = value; } }
}
namespace UnityEngine.UIElements
{
    public class TextField { public string Value = ""; public string value { get => Value; [MethodImpl(MethodImplOptions.NoInlining)] set => Value = value; } }
}
