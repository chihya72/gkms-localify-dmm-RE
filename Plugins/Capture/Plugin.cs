using Gkms.Sdk;

namespace Gkms.Plugins.Capture;

public sealed class Plugin : ILocalifyPlugin
{
    private readonly PluginComponents components = new();
    public void Start(IPluginContext context)
    {
        components.Start(context, new TextCapture(), "原文采集");
        components.Start(context, new TextureCapture(), "贴图导出");
        if (components.Count == 0) throw new InvalidOperationException("没有可用的素材采集入口。");
    }
    public void Dispose() => components.Dispose();
}
