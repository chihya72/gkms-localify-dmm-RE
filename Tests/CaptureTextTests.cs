using System.Reflection;
using System.Text;
using System.Text.Json;
using Gkms.Sdk;

internal static class CaptureTextTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string work, string packages)
    {
        // Reuse the caller's existing scratch directory; never touch installed output.
        if (!Directory.Exists(work)) throw new DirectoryNotFoundException(work);
        var package = Gkms.Host.Catalog.Discover(packages, _ => { })["capture"].Directory;
        var type = Assembly.LoadFrom(Path.Combine(package, "Gkms.Capture.dll")).GetType("Gkms.Plugins.Capture.TextCapture", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        object? Call(string method, params object?[] args) => type.GetMethod(method, flags)!.Invoke(null, args);
        void Set(string field, object value) => type.GetField(field, flags)!.SetValue(null, value);
        const string source = "原文「アイドル」 <b>中文&文字</b> 😀\n\"引用\"\\n\t";
        var controls = new string(Enumerable.Range(0, 32).Select(i => (char)i).ToArray());
        var map = new Dictionary<string, string> { [source] = controls, ["换行"] = "日文\n中文", ["字面量"] = @"\u7968" };
        var json = (string)Call("SerializeDictionary", map)!;
        Check(json.StartsWith("{\n    \"原文") && json.Contains("😀") && json.Contains("<b>中文&文字</b>") &&
            !json.Contains(@"\u539f", StringComparison.OrdinalIgnoreCase) && json.Contains("\"换行\": \"日文\\n中文\""),
            "原版四空格、直接 Unicode、标签、真实换行转义");
        var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Check(map.Count == decoded.Count && map.All(p => decoded[p.Key] == p.Value),
            "JSON 往返保留引号、反斜杠、全部控制字符、emoji 与字面量 Unicode 转义");
        Check((string)Call("SerializeDictionary", new Dictionary<string, string>())! == "{}", "原版空字典格式");

        var names = new[] { "localization.json", "generic_orig_7331.json", "generic_7331.fmt.json",
            "generic_7331.json", "generic_orig_7332.json" };
        Check(names.All(n => !File.Exists(Path.Combine(work, n))), "采集测试不能覆盖已有文件");
        var journal = Path.Combine(work, "observed.jsonl");
        var oldJournal = File.Exists(journal) ? File.ReadAllBytes(journal) : null;
        var instance = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        try
        {
            var originalPath = Path.Combine(work, names[1]);
            File.WriteAllText(originalPath, "{\"保留原文\":\"已有译文\"}");
            Set("root", work); Set("fileIndex", 7331); Set("active", true);
            var dictionary = new DmmTextDictionary(Path.Combine(work, "unused-capture-resources"));
            ((Dictionary<string, string>)typeof(DmmTextDictionary).GetField("exact", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dictionary)!)["已知原文"] = "已知译文";
            ((HashSet<string>)typeof(DmmTextDictionary).GetField("translated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dictionary)!).Add("已知译文");
            ((Dictionary<string, string>)typeof(DmmTextDictionary).GetField("keyed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dictionary)!)["known"] = "已知界面";
            Set("dictionary", dictionary);
            Call("KeyValue", "known", "既知"); Call("KeyValue", "new", "日文と中文");
            Call("KeyValue", "new", "重复不覆盖");
            foreach (var text in new[] { "保留原文", source, source, "已知原文", "已知译文", "1234", "word", "第12回<b>完成</b>" }) Call("Text", text);
            Call("Slice", "前切片后", 1, 2);
            Call("Characters", "字符集".ToCharArray(), 0, 3);
            Call("Flush");
            var originals = Interop.ReadDictionary(originalPath);
            Check(originals["保留原文"] == "已有译文" && originals.ContainsKey(source) &&
                originals.ContainsKey("切片") && originals.ContainsKey("字符集") &&
                !originals.ContainsKey("已知原文") && !originals.ContainsKey("已知译文") &&
                !originals.ContainsKey("1234") && originals.ContainsKey("word"),
                "保留已有译文、原文入口与原版过滤规则");
            Check(Interop.ReadDictionary(Path.Combine(work, names[0])).SequenceEqual(new[] { new KeyValuePair<string, string>("new", "日文と中文") }),
                "I18n 仅采集缺失键并去重");
            Check(Interop.ReadDictionary(Path.Combine(work, names[2])).ContainsKey("第{0}回<b>完成</b>") &&
                Interop.ReadDictionary(Path.Combine(work, names[3])).ContainsKey("[__split__]第"), "fmt 与分段分类");
            Set("key", typeof(UnityEngine.Input).GetMethod("GetKeyDown", new[] { typeof(string) })!);
            UnityEngine.Input.Pressed.Add("[+]"); Call("ChangeFile"); Call("Text", "分组原文");
            instance.Dispose();
            Check(Interop.ReadDictionary(Path.Combine(work, names[4])).ContainsKey("分组原文"), "切组与停用写出");
            var before = File.ReadAllBytes(Path.Combine(work, names[4])); Call("Text", "停用后文本"); Call("Flush");
            Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(work, names[4]))), "停用后不再采集");
            Check(!File.ReadAllBytes(originalPath).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()) &&
                File.ReadAllText(originalPath).Contains("已有译文"), "UTF-8 无 BOM 与已有文件可读重写");
            Check(oldJournal == null ? !File.Exists(journal) : oldJournal.SequenceEqual(File.ReadAllBytes(journal)),
                "不创建或追加 observed.jsonl");
        }
        finally
        {
            instance.Dispose();
            foreach (var name in names)
                foreach (var suffix in new[] { "", ".tmp" }) File.Delete(Path.Combine(work, name + suffix));
        }
        Console.WriteLine("文本采集专项通过：原版 JSON 格式、Unicode/控制字符往返、分类过滤、去重、保留译文、切组、停用写出，无 observed.jsonl。");
    }
}
