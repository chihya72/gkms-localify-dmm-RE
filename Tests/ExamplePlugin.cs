using Gkms.Sdk;
using System.Text.Json;

namespace Example.Greeting;

// This project references only Gkms.Sdk. Copy the project and manifest to start a third-party plugin.
public sealed class Plugin : ILocalifyPlugin, IResourceReloadable, ISettingsReloadable
{
    private IPluginServices? services;
    private Options options = new();

    public void Start(IPluginContext context)
    {
        services = context.Services();
        options = context.ReadSettings<Options>();
        if (options.LogOnStart) context.Log(options.Message);
        // Enqueue may be called by a worker; its action executes on the Unity thread.
        services.Enqueue(() => services.Log("示例已就绪。"));
    }

    public void ReloadResources() => services?.Log("示例资源重载入口。");
    public void ReloadSettings(JsonElement settings)
    {
        var next = settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? new Options() :
            settings.Deserialize<Options>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new Options();
        options = next;
        services?.Log("参数已重载: " + options.Message);
    }
    public void Dispose() => services = null;
}

public sealed class Options
{
    public string Message { get; set; } = "Hello, Localify!";
    public bool LogOnStart { get; set; } = true;
}
