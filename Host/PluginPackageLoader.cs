using System.Reflection;
using System.Runtime.Loader;
using Gkms.Sdk;
using ImGuiNET;

namespace Gkms.Host;

internal sealed class PluginPackageLoader : AssemblyLoadContext
{
    private readonly string directory;
    private readonly AssemblyDependencyResolver resolver;
    public PluginPackageLoader(Package package) : base(isCollectible: true)
    {
        directory = Path.GetFullPath(package.Directory);
        resolver = new AssemblyDependencyResolver(Path.Combine(directory, package.Manifest.Assembly));
    }
    protected override Assembly? Load(AssemblyName name)
    {
        // A plugin must use the manager's SDK and ImGui binding, including its native context.
        if (name.Name == typeof(ILocalifyPlugin).Assembly.GetName().Name) return typeof(ILocalifyPlugin).Assembly;
        if (name.Name == "ImGui.NET") return typeof(ImGui).Assembly;
        var path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(directory, name.Name + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }
    protected override IntPtr LoadUnmanagedDll(string name) => resolver.ResolveUnmanagedDllToPath(name) is { } path
        ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
}
