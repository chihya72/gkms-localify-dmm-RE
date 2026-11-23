using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public record struct Vector3(float x, float y, float z);
    public record struct Quaternion(float x, float y, float z, float w)
    {
        private System.Numerics.Quaternion Native => new(x,y,z,w);
        private static Quaternion From(System.Numerics.Quaternion q) => new(q.X,q.Y,q.Z,q.W);
        public static Quaternion Euler(float x, float y, float z) => From(System.Numerics.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180));
        public static Quaternion Inverse(Quaternion q) => From(System.Numerics.Quaternion.Inverse(q.Native));
        public static Quaternion operator *(Quaternion a, Quaternion b) => From(a.Native * b.Native);
    }
    public record struct Vector2Int(int x, int y);
    public class Camera : Object
    {
        public static List<Camera> Instances = new();
        public Camera() { Instances.Add(this); }
        private readonly Transform node = new();
        public Transform transform { [MethodImpl(MethodImplOptions.NoInlining)] get => node; }
        private float fov = 60;
        public float fieldOfView { [MethodImpl(MethodImplOptions.NoInlining)] get => fov; [MethodImpl(MethodImplOptions.NoInlining)] set => fov = value; }
        public float nearClipPlane { get; set; } = .3f;
        public static Camera? main { [MethodImpl(MethodImplOptions.NoInlining)] get => Instances.LastOrDefault(); }
        public static Camera[] allCameras => Instances.ToArray();
    }
    public partial class Transform
    {
        private static readonly Dictionary<IntPtr, Transform> nodes = new();
        public IntPtr m_CachedPtr { get { var id = new IntPtr(GetInstanceID()); nodes[id] = this; return id; } }
        public Vector3 position { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; } = new(0,0,0,1);
        public Vector3 forward { get { var q = rotation; var v = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, new System.Numerics.Quaternion(q.x,q.y,q.z,q.w)); return new(v.X,v.Y,v.Z); } }
        [MethodImpl(MethodImplOptions.NoInlining)] public static void set_position_Injected(IntPtr self, ref Vector3 value) => nodes[self].position = value;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void set_localPosition_Injected(IntPtr self, ref Vector3 value) => nodes[self].position = value;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void set_rotation_Injected(IntPtr self, ref Quaternion value) => nodes[self].rotation = value;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void SetPositionAndRotation_Injected(IntPtr self, ref Vector3 position, ref Quaternion rotation)
        { nodes[self].position = position; nodes[self].rotation = rotation; }
        public Vector3 TransformPoint(Vector3 p) => new(position.x+p.x, position.y+p.y, position.z+p.z);
        public void LookAt(Transform other) { }
        public void LookAt(Vector3 point) { }
        public void Translate(float x, float y, float z) => position = new(position.x+x,position.y+y,position.z+z);
    }
    public static class Input
    {
        public static bool F8, U;
        public static int CtrlReads;
        public static float Scroll;
        public static readonly HashSet<string> Held = new(), Pressed = new();
        public static float MouseX, MouseY;
        public static Vector3 mousePosition { get; set; }
        public static float GetAxisRaw(string name)
        {
            if (name != "Mouse ScrollWheel") return name == "Mouse X" ? MouseX : name == "Mouse Y" ? MouseY : 0;
            var value = Scroll; Scroll = 0; return value;
        }
        public static bool GetKey(string name)
        {
            if (name is "left ctrl" or "right ctrl") CtrlReads++;
            return Held.Contains(name);
        }
        public static bool GetKeyDown(string name) { if (Pressed.Remove(name)) return true; if (name == "u") { var u = U; U = false; return u; } if (name != "f8") return false; var hit = F8; F8 = false; return hit; }
    }
    public static class Time { public static int frameCount { get; set; } public static float unscaledDeltaTime => .016f; }
    public enum HumanBodyBones { Hips = 0, Head = 10 }
}
namespace Cinemachine
{
    public struct LensSettings { public float FieldOfView; }
    public struct CameraState
    {
        public LensSettings Lens { get; set; }
        public UnityEngine.Vector3 RawPosition { get; set; }
        public UnityEngine.Quaternion RawOrientation { get; set; }
        public UnityEngine.Vector3 PositionDampingBypass { get; set; }
        public UnityEngine.Vector3 PositionCorrection { get; set; }
        public UnityEngine.Quaternion OrientationCorrection { get; set; }
    }
    public class CinemachineBrain
    {
        public UnityEngine.Camera OutputCamera { get; } = new();
        public CameraState Applied;
        [MethodImpl(MethodImplOptions.NoInlining)] public void PushStateToUnityCamera(CameraState state)
        {
            Applied = state;
            OutputCamera.fieldOfView = state.Lens.FieldOfView;
            var transform = OutputCamera.transform;
            var position = state.RawPosition; var rotation = state.RawOrientation;
            UnityEngine.Transform.SetPositionAndRotation_Injected(transform.m_CachedPtr, ref position, ref rotation);
        }
    }
}
namespace VL.Rendering
{
    public class VLDOF { [MethodImpl(MethodImplOptions.NoInlining)] public bool IsActive() => true; }
}
namespace UnityEngine.Rendering
{
    public struct ScriptableRenderContext { }
    public static class RenderPipelineManager
    { [MethodImpl(MethodImplOptions.NoInlining)] public static void EndCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera) { } }
}
namespace Campus.Common
{
    public class CampusActorController
    {
        public UnityEngine.Transform _rootBody { get; } = new() { parent = new UnityEngine.Transform() };
        public int index { get; set; }
        public UnityEngine.Transform GetHumanBodyBoneTransform(UnityEngine.HumanBodyBones id) => _rootBody;
        [MethodImpl(MethodImplOptions.NoInlining)] public void LateUpdate() { }
    }
    public class LimitInfo
    {
        public int useLimit { get; set; }
        public UnityEngine.Vector2Int axisX { get; set; } = new(10,20);
        public UnityEngine.Vector2Int axisY { get; set; }
        public UnityEngine.Vector2Int axisZ { get; set; }
    }
    public class Bone
    {
        public bool isDirty { get; set; }
        public LimitInfo limitInfo { get; } = new();
        public UnityEngine.Transform leftBreast { get; } = new();
        public UnityEngine.Transform leftBreastEnd { get; } = new();
        public UnityEngine.Transform rightBreast { get; } = new();
        public UnityEngine.Transform rightBreastEnd { get; } = new();
        public float damping { get; set; }
        public float stiffness { get; set; }
        public bool useArmCorrection { get; set; }
        public float spring { get; set; }
        public float pendulum { get; set; }
        public float pendulumRange { get; set; }
        public float average { get; set; }
        public float rootWeight { get; set; }
    }
    public class CampusActorAnimationInitializeData { public List<Bone> swingBreastBones { get; } = new(){ new() }; }
    public class CampusActorAnimation
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Setup(UnityEngine.Transform? root, CampusActorAnimationInitializeData data) { }
    }
}
