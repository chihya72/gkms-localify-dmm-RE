using Gkms.Sdk;

namespace Gkms.Plugins.Localization;

public sealed class Plugin : ILocalifyPlugin
{
    private readonly Stack<ILocalifyPlugin> started = new();

    public void Start(IPluginContext context)
    {
        var root = Path.Combine(context.DataDirectory, "local-files");
        var diagnoseText = context.Option("textTest", false);
        StartFeature("界面翻译", new KeyedText(), diagnoseText || File.Exists(Path.Combine(root, "localization.json")));
        StartFeature("通用文本", new GenericText(), diagnoseText || Directory.Exists(Path.Combine(root, "genericTrans")) ||
            File.Exists(Path.Combine(root, "generic.json")) || File.Exists(Path.Combine(root, "generic.split.json")));
        StartFeature("剧情翻译", new StoryText(), Directory.Exists(Path.Combine(root, "resource")));
        StartFeature("回退字体", new FallbackFont(), File.Exists(Path.Combine(root, "gakumasassets")));
        StartFeature("语音字幕", new VoiceSubtitles(), File.Exists(VoiceSubtitles.ConfigFile(root)));

        StartFeature("主数据翻译", new MasterText(), Directory.Exists(Path.Combine(root, "masterTrans")));
        StartFeature("Live 歌词", new LiveLyrics());

        if (started.Count == 0) throw new InvalidOperationException("没有可用的文本汉化功能。");

        void StartFeature(string name, ILocalifyPlugin feature, bool installed = true)
        {
            if (!installed) { context.Log($"{name} 缺少对应资源，已跳过。"); return; }
            var scope = ((IPluginScope)context).OpenScope();
            try { feature.Start(scope); started.Push(feature); }
            catch (Exception error)
            {
                scope.Dispose();
                context.Log($"{name} 加载失败: {error.GetBaseException().Message}");
                try { feature.Dispose(); }
                catch (Exception cleanup) { context.Log($"{name} 清理失败: {cleanup.GetBaseException().Message}"); }
            }
        }
    }

    public void Dispose()
    {
        List<Exception>? errors = null;
        while (started.TryPop(out var feature))
            try { feature.Dispose(); }
            catch (Exception error) { (errors ??= new()).Add(error); }
        if (errors != null) throw new AggregateException(errors);
    }
}
