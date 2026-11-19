using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.Localization;

public sealed class GenericText : ILocalifyPlugin
{
    private static TextDictionary? dictionary;
    private static bool textTest;
    public void Start(IPluginContext context)
    {
        dictionary = new TextDictionary(Path.Combine(context.DataDirectory, "local-files"));
        textTest = context.Option("textTest", false);
        if (dictionary.Count == 0 && !textTest) throw new InvalidDataException("未找到通用文本词典。");
        const string tmp = "Unity.TextMeshPro";
        const string type = "TMPro.TMP_Text";
        var count = 0;
        void Bind(MethodInfo? target, string handler)
        {
            if (target == null) return;
            context.Patch(target, GameMethods.Handler<GenericText>(handler));
            count++;
        }
        Bind(context.Find(tmp, type, "set_text", "System.String"), nameof(Translate));
        Bind(context.Find(tmp, type, "SetText", "System.String"), nameof(Translate));
        Bind(context.Find(tmp, type, "SetText", "System.String", "System.Boolean"), nameof(Translate));
        Bind(context.Find(tmp, type, "PopulateTextBackingArray", "System.String", "System.Int32", "System.Int32"), nameof(TranslateSlice));
        Bind(context.Find("UnityEngine.UI", "UnityEngine.UI.Text", "set_text", "System.String"), nameof(Translate));
        Bind(context.Find("UnityEngine.UIElementsModule", "UnityEngine.UIElements.TextField", "set_value", "System.String"), nameof(Translate));
        var charTarget = context.FindType(tmp, type)?.GetMethods(GameMethods.Declared).SingleOrDefault(m =>
            m.Name == "SetCharArray" && m.GetParameters() is [{ ParameterType: var array }, { ParameterType: var a }, { ParameterType: var b }]
            && (array == typeof(char[]) || array.IsGenericType && array.GetGenericArguments().SequenceEqual(new[] { typeof(char) }))
            && a == typeof(int) && b == typeof(int));
        Bind(charTarget, nameof(TranslateChars));
        var awake = context.Find(tmp, "TMPro.TextMeshProUGUI", "Awake");
        if (awake?.DeclaringType?.GetProperty("text") is { CanRead: true, CanWrite: true })
            Bind(awake, nameof(TranslateInitial));
        if (count == 0) throw new MissingMethodException("未找到文本显示入口。");
        context.Log($"{dictionary.Count} 个词条，{count} 个显示入口。");
    }

    public static void Translate(ref string __0, MethodBase __originalMethod)
    {
        if (__0 == null || dictionary == null) return;
        var translated = dictionary.Translate(__0);
        if (textTest && translated == __0 && !dictionary.IsKnown(__0))
        {
            var marker = __originalMethod.Name switch
            {
                "set_text" when __originalMethod.DeclaringType?.FullName == "UnityEngine.UI.Text" => "[UI]",
                "set_text" => "[TT]",
                "SetText" => __originalMethod.GetParameters().Length == 1 ? "[T1]" : "[TS]",
                _ => ""
            };
            translated = Mark(__0, marker);
        }
        __0 = translated;
    }

    public static void TranslateInitial(object __instance)
    {
        var property = __instance.GetType().GetProperty("text")!;
        if (property.GetValue(__instance) is not string value || dictionary == null) return;
        var translated = dictionary.Translate(value);
        if (translated != value) property.SetValue(__instance, textTest ? "[TA]" + translated : translated);
    }

    private static string Mark(string source, string marker) =>
        source.Length == 0 || source.StartsWith('[') ? source : marker + source;

    public static void TranslateSlice(ref string __0, ref int __1, ref int __2)
    {
        if (__0 == null || dictionary == null || __1 < 0 || __2 < 0 || __1 > __0.Length - __2) return;
        var segment = __0.Substring(__1, __2);
        var translated = dictionary.Translate(segment);
        if (textTest && segment == translated && !dictionary.IsKnown(segment)) translated = Mark(segment, "[TP]");
        if (segment == translated) return;
        __0 = translated; __1 = 0; __2 = translated.Length;
    }

    public static bool TranslateChars(object __instance, object __0, int __1, int __2)
    {
        if (__0 == null || dictionary == null || __1 < 0 || __2 <= 0) return true;
        var length = __0 is char[] chars ? chars.Length : Convert.ToInt32(__0.GetType().GetProperty("Length")?.GetValue(__0));
        if (__1 > length - __2) return true;
        var item = __0.GetType().GetProperty("Item");
        if (__0 is not char[] && item == null) return true;
        var source = __0 is char[] array ? new string(array, __1, __2) :
            new string(Enumerable.Range(__1, __2).Select(i => (char)item!.GetValue(__0, new object[] { i })!).ToArray());
        var result = dictionary.Translate(source);
        if (textTest && result == source && !dictionary.IsKnown(source)) result = Mark(source, "[CA]");
        var text = __instance.GetType().GetProperty("text");
        if (result == source || text?.CanWrite != true) return true;
        text.SetValue(__instance, result);
        return false;
    }
    public void Dispose() => dictionary = null;
}
