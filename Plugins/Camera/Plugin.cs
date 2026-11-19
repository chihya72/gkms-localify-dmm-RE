using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Camera;

public sealed class Plugin : ILocalifyPlugin
{
    private static MethodInfo held = null!, pressed = null!, axis = null!, translate = null!;
    private static PropertyInfo time = null!, frame = null!, output = null!, transform = null!, position = null!, angles = null!;
    private static float speed, sensitivity, pitch, yaw;
    private static object? savedPosition, trackedBone;
    private static string mode = "free";
    private static int actorIndex;
    private static PropertyInfo? index, rotation;
    private static MethodInfo? getBone, point, lookAt, alive;
    private static object? headBone, offset;
    private static PropertyInfo? rootBody;
    private static HeadVisibility? head;
    private static bool active;
    private static float? zoom;
    private static object? zoomCamera;
    private static float originalZoom;
    private static PropertyInfo? fieldOfView;
    private static int lastFrame = -1;
    public void Start(IPluginContext context)
    {
        speed = context.Option("speed", 2f);
        sensitivity = context.Option("sensitivity", 2f);
        if (!float.IsFinite(speed) || !float.IsFinite(sensitivity) || speed <= 0 || sensitivity <= 0) throw new InvalidDataException("移动速度与灵敏度须为正数。");
        const string input = "UnityEngine.InputLegacyModule", type = "UnityEngine.Input";
        held = context.Require(input, type, "GetKey", "System.String");
        pressed = context.Require(input, type, "GetKeyDown", "System.String");
        axis = context.Require(input, type, "GetAxisRaw", "System.String");
        var clock = context.RequireType("UnityEngine.CoreModule", "UnityEngine.Time");
        time = clock.GetProperty("unscaledDeltaTime")!;
        frame = clock.GetProperty("frameCount")!;
        var brain = context.RequireType("Cinemachine", "Cinemachine.CinemachineBrain");
        output = brain.GetProperty("OutputCamera")!;
        transform = output.PropertyType.GetProperty("transform")!;
        fieldOfView = output.PropertyType.GetProperty("fieldOfView");
        var depthOfField = context.Find("Unity.RenderPipelines.Universal.Runtime", "VL.Rendering.VLDOF", "IsActive");
        if (depthOfField != null)
            context.Patch(depthOfField, postfix: GameMethods.Handler<Plugin>(nameof(DepthOfField)));
        position = transform.PropertyType.GetProperty("position")!;
        angles = transform.PropertyType.GetProperty("eulerAngles")!;
        translate = transform.PropertyType.GetMethod("Translate", new[] { typeof(float), typeof(float), typeof(float) })!;
        mode = context.Option("mode", "free");
        if (mode is not ("free" or "follow" or "first-person")) throw new InvalidDataException("mode 应为 free、follow 或 first-person。");
        if (mode != "free")
        {
            actorIndex = context.Option("actorIndex", 0);
            var actor = context.RequireType("campus-submodule.Runtime", "Campus.Common.CampusActorController");
            index = actor.GetProperty("index", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            getBone = actor.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .First(m => m.Name == "GetHumanBodyBoneTransform" && m.GetParameters().Length == 1);
            var boneType = getBone.GetParameters()[0].ParameterType;
            headBone = boneType.IsEnum ? Enum.Parse(boneType, "Head") : 10;
            point = context.Require("UnityEngine.CoreModule", "UnityEngine.Transform", "TransformPoint", "UnityEngine.Vector3");
            lookAt = context.Require("UnityEngine.CoreModule", "UnityEngine.Transform", "LookAt", "UnityEngine.Transform");
            alive = context.Require("UnityEngine.CoreModule", "UnityEngine.Object", "op_Implicit", "UnityEngine.Object");
            if (mode == "first-person" && context.Option("hideHead", true))
            {
                rootBody = actor.GetProperty("_rootBody", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (rootBody != null) head = new HeadVisibility(alive);
            }
            rotation = transform.PropertyType.GetProperty("rotation")!;
            var displacement = context.Option("offset", mode == "follow" ? new[] { 0f, .25f, -1.5f } : new[] { 0f, .064f, .12f });
            if (displacement.Length != 3 || displacement.Any(v => !float.IsFinite(v))) throw new InvalidDataException("offset 需要三个有限数值。");
            offset = Activator.CreateInstance(position.PropertyType, displacement.Cast<object>().ToArray());
            context.Patch(context.Require("campus-submodule.Runtime", "Campus.Common.CampusActorController", "LateUpdate"),
                postfix: GameMethods.Handler<Plugin>(nameof(Track)));
        }
        context.Patch(brain.GetMethods(GameMethods.Declared).Single(m => m.Name == "PushStateToUnityCamera"),
            postfix: GameMethods.Handler<Plugin>(nameof(Update)));
    }
    private static float Key(string name) => (bool)held.Invoke(null, new object[] { name })! ? 1f : 0f;
    private static float Axis(string name) => (float)axis.Invoke(null, new object[] { name })!;
    public static void Track(object __instance)
    {
        if (!active || index != null && Convert.ToInt32(index.GetValue(__instance)) != actorIndex) return;
        trackedBone = getBone!.Invoke(__instance, new[] { headBone });
        if (head != null)
        {
            var body = rootBody!.GetValue(__instance);
            head.Track(trackedBone == null || body == null ? null : body.GetType().GetProperty("parent")!.GetValue(body));
        }
    }

    public static void DepthOfField(ref bool __result) { if (active) __result = false; }

    private static void ResetZoom()
    {
        if (zoomCamera != null && zoom.HasValue && (alive == null || (bool)alive.Invoke(null, new[] { zoomCamera })!) &&
            (float)fieldOfView!.GetValue(zoomCamera)! == zoom.Value)
            fieldOfView.SetValue(zoomCamera, originalZoom);
        zoomCamera = null;
        zoom = null;
    }

    public static void Update(object __instance)
    {
        var currentFrame = (int)frame.GetValue(null)!;
        if (currentFrame == lastFrame) return;
        lastFrame = currentFrame;
        if ((bool)pressed.Invoke(null, new object[] { "f8" })!)
        {
            active = !active; savedPosition = trackedBone = null; ResetZoom();
            if (!active) head?.Restore();
        }
        if (!active || output.GetValue(__instance) is not { } camera || transform.GetValue(camera) is not { } t) return;
        if (fieldOfView != null)
        {
            var wheel = Axis("Mouse ScrollWheel");
            if (zoomCamera != null && !Equals(camera, zoomCamera)) ResetZoom();
            if (wheel != 0)
            {
                if (zoomCamera == null) { zoomCamera = camera; originalZoom = (float)fieldOfView.GetValue(camera)!; }
                zoom = Math.Clamp((zoom ?? originalZoom) - wheel * 10, 1, 179);
            }
            if (zoom.HasValue) fieldOfView.SetValue(camera, zoom.Value);
        }
        if (mode != "free")
        {
            if (trackedBone == null || !(bool)alive!.Invoke(null, new[] { trackedBone })!) return;
            position.SetValue(t, point!.Invoke(trackedBone, new[] { offset }));
            if (mode == "follow") lookAt!.Invoke(t, new[] { trackedBone });
            else rotation!.SetValue(t, rotation.GetValue(trackedBone));
            return;
        }
        if (savedPosition == null)
        {
            savedPosition = position.GetValue(t);
            var a = angles.GetValue(t)!;
            pitch = (float)a.GetType().GetField("x")!.GetValue(a)!;
            if (pitch > 180) pitch -= 360;
            yaw = (float)a.GetType().GetField("y")!.GetValue(a)!;
        }
        if (Key("mouse 1") > 0) { yaw += Axis("Mouse X") * sensitivity; pitch = Math.Clamp(pitch - Axis("Mouse Y") * sensitivity, -89f, 89f); }
        angles.SetValue(t, Activator.CreateInstance(angles.PropertyType, pitch, yaw, 0f));
        position.SetValue(t, savedPosition);
        var step = speed * (float)time.GetValue(null)!;
        translate.Invoke(t, new object[] { (Key("d")-Key("a"))*step, (Key("e")-Key("q"))*step, (Key("w")-Key("s"))*step });
        savedPosition = position.GetValue(t);
    }
    public void Dispose() { head?.Restore(); ResetZoom(); head = null; active = false; savedPosition = null; trackedBone = null; }
}
