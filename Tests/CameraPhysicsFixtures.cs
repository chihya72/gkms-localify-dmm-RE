using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public record struct Vector3(float x, float y, float z);
    public record struct Quaternion(float x, float y, float z, float w);
    public record struct Vector2Int(int x, int y);
    public class Camera : Object { public Transform transform { get; } = new(); public float fieldOfView { get; set; } = 60; }
    public partial class Transform
    {
        public Vector3 position { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 TransformPoint(Vector3 p) => p;
        public void LookAt(Transform other) { }
        public void Translate(float x, float y, float z) { }
    }
    public static class Input
    {
        public static bool F8, U;
        public static int CtrlReads;
        public static float Scroll;
        public static float GetAxisRaw(string name)
        {
            if (name != "Mouse ScrollWheel") return 0;
            var value = Scroll; Scroll = 0; return value;
        }
        public static bool GetKey(string name)
        {
            if (name is "left ctrl" or "right ctrl") CtrlReads++;
            return false;
        }
        public static bool GetKeyDown(string name) { if (name == "u") { var u = U; U = false; return u; } if (name != "f8") return false; var hit = F8; F8 = false; return hit; }
    }
    public static class Time { public static int frameCount { get; set; } public static float unscaledDeltaTime => .016f; }
    public enum HumanBodyBones { Head = 10 }
}
namespace Cinemachine
{
    public class CinemachineBrain
    {
        public UnityEngine.Camera OutputCamera { get; } = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public void PushStateToUnityCamera(object? state) { }
    }
}
namespace Campus.Common
{
    public class CampusActorController
    {
        public UnityEngine.Transform _rootBody { get; } = new() { parent = new UnityEngine.Transform() };
        public int index => 0;
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
        public LimitInfo limitInfo { get; } = new();
        public UnityEngine.Transform leftBreast { get; } = new();
        public float damping { get; set; }
    }
    public class CampusActorAnimationInitializeData { public List<Bone> swingBreastBones { get; } = new(){ new() }; }
    public class CampusActorAnimation
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Setup(UnityEngine.Transform? root, CampusActorAnimationInitializeData data) { }
    }
}
