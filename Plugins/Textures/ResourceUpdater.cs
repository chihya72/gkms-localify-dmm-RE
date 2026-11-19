using Gkms.Resources;
using Gkms.Sdk;

namespace Gkms.Plugins.Textures;

public sealed class ResourceUpdater : IResourceUpdater
{
    public Task<int> UpdateAsync(string dataDirectory, string? source) =>
        ArchiveUpdate.RunAsync(dataDirectory, source, textures: true,
            "useAPITextureAssetsURL", "https://texture.gakumas.cn/api/gkms_texture_data");
}
