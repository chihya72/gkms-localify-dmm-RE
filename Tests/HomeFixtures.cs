using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public partial class Object
    {
        public static Object[] SceneObjects = Array.Empty<Object>();
        public static Object[] FindObjectsOfType(System.Type type, bool includeInactive) =>
            Resources.LevelButton is { } level && type.IsInstanceOfType(level) ? new Object[] { level } : SceneObjects.Where(type.IsInstanceOfType).ToArray();
        public static Object Instantiate(Object source, Transform parent, bool worldSpace)
        {
            if (source != Resources.Template) throw new Exception("应只克隆气泡。");
            var copy = new GameObject { transform = { parent = parent } };
            copy.Add(new Campus.ADV.MessageContainer(copy));
            return Resources.LastClone = copy;
        }
    }
    public record struct Vector2(float x, float y);
    public partial class Transform : Object
    {
        public Transform? parent { get; set; }
        public GameObject? gameObject { get; set; }
        public Dictionary<string, Transform> Children { get; } = new();
        public Transform? Find(string path) => Children.GetValueOrDefault(path);
        public void SetAsLastSibling() { }
    }
    public record struct Rect(float x, float y, float width, float height);
    public class RectTransform : Transform
    {
        public Rect rect { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchoredPosition { get; set; }
    }
    public class GameObject : Object
    {
        public RectTransform transform { get; } = new();
        public bool activeSelf { get; private set; } = true;
        private readonly Dictionary<Type, object> components = new();
        public GameObject() => transform.gameObject = this;
        public void SetActive(bool value) => activeSelf = value;
        public void Add(object value) => components[value.GetType()] = value;
        public object? GetComponentInChildren(System.Type type, bool includeInactive) => GetComponent(type);
        public object? GetComponent(System.Type type) => components.GetValueOrDefault(type);
        public object AddComponent(System.Type type)
        {
            var value = Activator.CreateInstance(type)!; Add(value); return value;
        }
    }
    public class CanvasGroup : Object { public bool interactable { get; set; } public bool blocksRaycasts { get; set; } public float alpha { get; set; } }
    public class Canvas : Object
    {
        public Canvas rootCanvas => this;
        public int renderMode => 0;
        public Camera? worldCamera => null;
    }
    public static class RectTransformUtility
    {
        public static Vector2 WorldToScreenPoint(Camera? camera, Vector3 world) => new(world.x, world.y);
        public static bool ScreenPointToLocalPointInRectangle(RectTransform parent, Vector2 point, Camera? camera, out Vector2 local)
        { local = point; return true; }
    }
    public static class Resources
    {
        public static GameObject Template { get; } = new();
        public static GameObject? LastClone;
        public static Campus.Common.ProduceLevelButtonView? LevelButton;
        public static Object Load(string path, System.Type type)
        {
            if (path != "verticaladv") throw new Exception(path);
            Template.Add(new Campus.ADV.MessageContainer(Template));
            var prefab = new GameObject(); prefab.Add(new Campus.Common.VerticalAdvPresenter()); return prefab;
        }
    }
}
namespace Campus.ADV
{
    public class MessageContainer
    {
        public UnityEngine.GameObject gameObject { get; }
        public MessageText _messageText { get; } = new();
        public string? Text;
        public MessageContainer(UnityEngine.GameObject gameObject) => this.gameObject = gameObject;
        public void Show() { }
        public void SetMessage(string text, bool user) => Text = text;
    }
    public class MessageText
    {
        public FakeTMP TargetTMP { get; } = new();
        public bool Complete;
        public void CompleteAnimation() => Complete = true;
    }
    public class FakeTMP
    {
        public UnityEngine.RectTransform transform { get; } = new();
        public bool enableAutoSizing { get; set; }
        public bool enableWordWrapping { get; set; }
        public bool richText { get; set; }
        public bool raycastTarget { get; set; }
        public float fontSize { get; set; }
        public int alignment { get; set; }
        public UnityEngine.Vector2 GetPreferredValues(string text, float width, float height) => new(180, 76);
    }
}
namespace Campus.Common
{
    public class ProduceLevelButtonView : UnityEngine.Object
    {
        public UnityEngine.Transform transform { get; } = new UnityEngine.GameObject().transform;
    }
    public class Engine { public UnityEngine.GameObject gameObject => UnityEngine.Resources.Template; }
    public class VerticalAdvPresenter { public Engine _enginePrefab { get; } = new(); }
    public class CampusQualityManager
    {
        public readonly List<string> Trace = new();
        private float rate = 30;
        public float TargetFrameRate
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get => rate;
            [MethodImpl(MethodImplOptions.NoInlining)] set { Trace.Add("fps"); rate = value; UnityEngine.Application.targetFrameRate = (int)value; }
        }
        public (int Level, int Pixels, float Scale, int Volume) Settings;
        public QualityLevel Reflection, Lod;
        public QualityLevel ReflectionQuality => Reflection;
        public QualityLevel LODQuality => Lod;
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetReflectionQuality(QualityLevel value) { Trace.Add("reflection"); Reflection = value; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetLODQuality(QualityLevel value) { Trace.Add("lod"); Lod = value; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void ApplySetting(int level, int pixels, float scale, int volume) { Trace.Add("apply"); Settings = (level, pixels, scale, volume); }
    }
    public enum QualityLevel { Lowest = 0, Low = 10, Middle = 20, High = 30, Higher = 40, Highest = 100 }
    public class CampusSoundManager { [MethodImpl(MethodImplOptions.NoInlining)] public void OnSoundPlayed(string cue) { } }
}
namespace Campus.Common.Sound
{
    public class CampusSoundPlayer : UnityEngine.Object
    {
        public string Cue = "";
        private bool ended;
        public bool IsEndOrStop() => ended;
        public bool FailPlay;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Play() { if (FailPlay) throw new InvalidOperationException("play failure"); ended = false; new CampusSoundManager().OnSoundPlayed(Cue); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void PlayWith(object actor, bool lipSync) => Play();
        [MethodImpl(MethodImplOptions.NoInlining)] public void Stop() => ended = true;
        [MethodImpl(MethodImplOptions.NoInlining)] public void ResetState() => ended = true;
    }
}
namespace Campus.OutGame
{
    public class OutGameHeaderView : UnityEngine.Object { public UnityEngine.CanvasGroup _contentFade { get; } = new() { alpha = .75f }; }
    public class HomePageViewBase : UnityEngine.Object
    {
        public UnityEngine.RectTransform transform { get; } = new UnityEngine.GameObject().transform;
        public HomePageViewBase()
        {
            var content = new UnityEngine.GameObject(); content.Add(new UnityEngine.Canvas());
            content.transform.name = "UIContentArea"; content.transform.rect = new(-500, -300, 1000, 600);
            transform.parent = content.transform;
            var panel = new UnityEngine.GameObject().transform;
            panel.rect = new(0, 0, 400, 600);
            var switcher = new UnityEngine.GameObject().transform;
            switcher.parent = panel; switcher.anchorMin = switcher.anchorMax = new(0, .25f);
            switcher.rect = new(0, -10, 20, 20); switcher.anchoredPosition = new(0, 20);
            transform.Children["PageRoot/ContentRoot/LeftRoot/CharacterSwitcherRoot"] = switcher;
            var header = new UnityEngine.GameObject();
            header.Add(new OutGameHeaderView()); header.Add(new UnityEngine.Canvas());
            header.transform.rect = new(-400, 0, 800, 600);
            var level = new Campus.Common.ProduceLevelButtonView();
            var rect = (UnityEngine.RectTransform)level.transform; rect.rect = new(0, 160, 40, 50);
            rect.parent = header.transform; UnityEngine.Resources.LevelButton = level;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Initialize() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnToggle(bool shown, int direction) { }
    }
    public class HomePageView : HomePageViewBase { }
    public class HomePagePresenter { public HomePageView _view { get; } = new(); }
    public class HomePageGroupPresenter
    {
        public HomePagePresenter Current { get; } = new();
        public HomePagePresenter GetCurrentPage() => Current;
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnScreenVisitStart() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnScreenVisitEnd() { }
    }
    public class HomeTopScreenPresenter
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnOpenOutInactive() { }
    }
    public class Home3DManager { [MethodImpl(MethodImplOptions.NoInlining)] public void SetFreeLookEnable(bool value) { } }
    public class HomeActorBase
    {
        public object Actor { get; } = new UnityEngine.Object();
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void OnLoadEnd() { }
    }
    public class HomeTopActor : HomeActorBase { [MethodImpl(MethodImplOptions.NoInlining)] public override void OnLoadEnd() { } }
}
