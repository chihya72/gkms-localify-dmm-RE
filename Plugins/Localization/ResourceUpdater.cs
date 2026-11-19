using Gkms.Resources;
using Gkms.Sdk;

namespace Gkms.Plugins.Localization;

public sealed class ResourceUpdater : IResourceUpdater
{
    public Task<int> UpdateAsync(string dataDirectory, string? source) =>
        ArchiveUpdate.RunAsync(dataDirectory, source, textures: false,
            "useAPIAssetsURL", "https://uma.chinosk6.cn/api/gkms_trans_data");
}
