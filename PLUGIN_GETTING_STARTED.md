# 第三方插件开发入门（API 2）

本指南带你完成一个可配置的问候插件：获取 SDK → 编写插件 → 构建 ZIP → 本地安装 → 发布订阅索引 → 安装和更新。示例只写日志，不修改游戏状态。完整接口与线程规则见 [插件开发规范](PLUGIN_DEVELOPMENT.md)。

## 1. 准备 SDK 和开发环境

需要 Windows x64、PowerShell 7 和 .NET SDK 8 或以上。插件编译目标为 `net6.0`；实际使用插件还需要匹配当前 API 的 RE 框架及其运行环境。开发普通 C# 插件不需要编译框架的 C++ 入口，也不需要 BepInEx 或预生成的游戏 interop DLL。

当前 SDK 包为 `Gkms.Sdk.2.0.7.nupkg`，插件清单使用 `apiVersion: 2`。SDK 包版本、API 等级和你自己的插件版本是三个不同的值。使用本版新增方法或字段级重启声明时，运行环境须为框架 2.0.7 或更新版本。

从[框架 Release](https://github.com/chihya72/gkms-localify-dmm-RE/releases/latest) 下载 SDK NuGet 包，放到自己的本地 NuGet 源目录，例如 `D:\PluginWork\sdk-feed`。本地框架构建生成的包位于 `out\sdk`；目前不要假定公共 NuGet 已发布 `Gkms.Sdk`。如果自行构建框架，可在框架仓库运行 `Build.ps1`，具体要求见框架 README。

SDK 包内包含：

- `lib\net6.0\Gkms.Sdk.dll`：编译接口。
- 本入门指南、`PLUGIN_DEVELOPMENT.md` 和许可证。
- `examples` 中的完整游戏入口、自定义设置界面和清单示例。

下面使用自己的独立目录，例如 `D:\PluginWork\Hello`。不需要复制框架或官方插件仓库，也不要将项目建在游戏安装目录中。

## 2. 创建三个文件

将以下内容分别保存为 `Example.Hello.csproj`、`Plugin.cs` 和 `plugin.json`。发布自己的插件前，把 `example.hello`、作者和命名空间替换为自己的标识，并保持代码、清单和后面的订阅索引一致。

### Example.Hello.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <AssemblyName>Example.Hello</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Gkms.Sdk" Version="[2.0.7]" ExcludeAssets="runtime" />
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

SDK 在运行时由宿主提供，所以排除其运行时资产。自定义界面使用 SDK 带来的 ImGui.NET 依赖，不自行升级或随包分发 ImGui.NET/cimgui。

### Plugin.cs

```csharp
using Gkms.Sdk;

namespace Example.Hello;

public sealed class Plugin : ILocalifyPlugin
{
    public void Start(IPluginContext context)
    {
        var options = context.ReadSettings<Options>();
        if (options.LogOnStart)
            context.Log(options.Message);
    }

    public void Dispose()
    {
        // 本示例没有 Hook、事件订阅或后台任务，因此无需释放资源。
    }
}

public sealed class Options
{
    public string Message { get; set; } = "Hello, Localify!";
    public bool LogOnStart { get; set; } = true;
}
```

入口类必须公开、非抽象，实现 `ILocalifyPlugin`，并有公开无参构造函数。构造函数不操作游戏，实际初始化放在 `Start`。`Dispose` 也可能在启动中途失败后调用，需要允许重复清理。

### plugin.json

```json
{
  "id": "example.hello",
  "name": "问候入门插件",
  "author": "Example developer",
  "version": "1.0.0",
  "description": "启用时把可配置的问候写入框架日志。",
  "punchline": "第一个可配置插件。",
  "category": "开发工具",
  "tags": ["示例", "hello"],
  "apiVersion": 2,
  "assembly": "Example.Hello.dll",
  "entryPoint": "Example.Hello.Plugin",
  "settings": [
    { "key": "message", "label": "问候内容", "type": "string", "default": "Hello, Localify!" },
    { "key": "logOnStart", "label": "启用时写入日志", "type": "boolean", "default": true }
  ]
}
```

`id` 是永久标识，只用小写字母、数字、点和短横线，以字母开头，不以点结尾。建议使用 `作者.插件名`，避免与其他插件重复。`assembly` 必须是包根目录里的 DLL 文件名；`entryPoint` 必须与代码中的完整类型名相同。

声明 `settings` 后，框架自动提供配置页，本例不需要自己编写 ImGui。参数存储在框架的 `profile.json` 中，由管理器保存；插件只读取自己的设置。清单默认值与代码属性默认值应保持一致：缺少的设置不会自动补入启动快照。

本例支持热启停。保存有变化的配置后，宿主通过停止并创建新实例应用参数；若需要原地更新，可以实现 `ISettingsReloadable`。不能热启停的插件应声明 `requiresRestart: true`，详细能力字段见开发规范。

参数更新能力独立于插件热启停：不能热启停但可以原地调参的插件，实现 `ISettingsReloadable` 并声明 `canReloadSettings: true` 即可。只在启动时使用的字段可在对应 `settings` 项中加上 `requiresRestart: true`；框架保存新值，向运行实例保留该字段旧值，其余参数照常应用，并显示「待重启」。未实现即时应用并不阻止保存；真正的应用异常仍须抛出并显示「失败」。

继承 `SchemaSettingsUi` 时，框架在详情工具栏提供统一的「保存配置」按钮，调用 `SaveSettings()` 保存普通参数和资源分区，插件无需重复绘制保存或重新读取按钮。完全自定义的 `IPluginSettingsUi` 仍通过 `ISettingsUiContext.SaveSettings()` 管理自己的保存交互。

## 3. 安装 SDK 依赖并构建

在插件项目目录中执行，替换 `$sdkFeed` 为实际存放 SDK 包的目录。命令为本次还原追加本地来源，不修改全局 NuGet 配置；ImGui.NET 等公共依赖通过已有的 nuget.org 源还原。

```powershell
$sdkFeed = 'D:\PluginWork\sdk-feed'
$project = Join-Path $PWD 'Example.Hello.csproj'
[string[]]$arguments = @(
    'restore', $project,
    "-p:RestoreAdditionalProjectSources=$sdkFeed"
)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "依赖还原失败：$LASTEXITCODE" }

[string[]]$arguments = @('build', $project, '-c', 'Release', '--no-restore')
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "插件构建失败：$LASTEXITCODE" }
```

编译输出位于 `bin\Release\net6.0`。SDK 不会安装进游戏；这里只还原编译依赖。不要引用 `Gkms.Host`、`Gkms.Runtime` 的实现程序集。

## 4. 打包成本地安装 ZIP

本教程只有 SDK 依赖，最小包包含三个文件，直接放在 ZIP 根目录：

```text
example.hello-1.0.0.zip
  plugin.json
  Example.Hello.dll
  Example.Hello.deps.json
```

在同一项目目录执行：

```powershell
$output = Join-Path $PWD 'bin\Release\net6.0'
$manifest = Get-Content -LiteralPath (Join-Path $output 'plugin.json') -Raw | ConvertFrom-Json
$zipPath = Join-Path $PWD ("{0}-{1}.zip" -f $manifest.id, $manifest.version)
$files = @('plugin.json', 'Example.Hello.dll', 'Example.Hello.deps.json')
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $files) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Join-Path $output $name), $name)
    }
} finally {
    $archive.Dispose()
}
Write-Output $zipPath
```

同名 ZIP 已存在时，此命令会报错。仅在本地调试时手动删除旧 ZIP 后重打；已发布的版本请递增版本号。新增私有依赖后，需要扩充打包列表，并保留 `.deps.json` 和私有 `runtimes` 的相对目录结构；不要只复制主 DLL。

插件包不能包含 `Gkms.Sdk`、ImGui.NET、cimgui、Host、Manager、Runtime，也不要加入 `bin/obj` 整个目录、源码、用户配置、日志或缓存。不要直接压缩已经在游戏中使用的插件目录，以免混入个人资源和采集结果。

## 5. 本地安装和验证

自行启动已部署框架的游戏，按 **Ctrl+U** →「已安装」→ ZIP 安装入口，选择 `example.hello-1.0.0.zip`。新插件默认停用，勾选「问候入门插件」并保存功能开关；日志 `gakumas-local\re\runtime.log` 应出现带插件 ID 的问候。

在插件配置页修改「问候内容」并保存，支持热更新时应看到新问候。停用、再启用一次，确认无异常。包安装成功只说明文件校验通过，运行入口和游戏行为仍需在游戏中验证。

同 ID 本地 ZIP 不能覆盖已安装插件。调试时可先卸载后重装；配置、插件资源和采集输出会保留。改动 DLL 本身不等于热更新，正式升级应增加插件版本并走订阅更新流程。

## 6. 发布插件及订阅索引

插件 ZIP 是代码包，`pluginmaster.json` 是可订阅的发布目录，两者不是同一个文件。将下面内容保存为发布目录中的 `pluginmaster.json`，与上面的 ZIP 放在一起：

```json
[
  {
    "InternalName": "example.hello",
    "Name": "问候入门插件",
    "AssemblyVersion": "1.0.0",
    "GkmsApiLevel": 2,
    "Author": "Example developer",
    "Description": "启用时把可配置的问候写入框架日志。",
    "Punchline": "第一个可配置插件。",
    "Category": "开发工具",
    "Tags": ["示例", "hello"],
    "Assembly": "Example.Hello.dll",
    "HasSettingsUi": true,
    "DownloadLinkInstall": "example.hello-1.0.0.zip",
    "DownloadLinkUpdate": "example.hello-1.0.0.zip"
  }
]
```

将两个文件上传到自己的 HTTPS 静态托管目录或发布服务。订阅地址必须直接返回 JSON，下载地址必须返回 ZIP，不能是需要登录的分享页或 HTML 文件预览页。这里不预设任何已发布的远程地址；下文 `example.com` 均为占位符。

- `InternalName`、`AssemblyVersion`、`GkmsApiLevel`、`Assembly` 分别与包内的 ID、版本、API、DLL 名对应。下载后管理器会核对这些值。
- 下载地址可写为相对索引的路径，也可以写完整 HTTPS Release 附件地址。更新仍需完整 ZIP，不是差分补丁。
- 每个源内，同 ID 只列一个当前版本。当前没有稳定/测试双通道；插件版本采用 SemVer，例如 `1.0.0`、`1.1.0-beta.1`，不是四段程序集版本。
- `GkmsApiLevel` 必须与插件清单的 `apiVersion` 一致；插件必须引用本框架 SDK。
- 正式发布先上传版本化 ZIP，最后更新索引。不要覆盖旧版本 URL 对应的安装包。

框架也兼容对象式 `index.json`，格式见开发规范。索引和包可托管于独立发布位置，无需为 SDK 另建源码仓库。

## 7. 添加订阅源并安装

游戏内按 Ctrl+U →「订阅源」→ 在第三方来源中填写名称和索引 HTTPS 地址 →「添加订阅源」。随后进入「插件库」，按名称、ID 或标签搜索，确认来源后下载安装，再到「已安装」页启用。

第三方不要把自己的地址填入「官方地址」。删除或停用订阅源不会卸载插件，但该源不再参与更新检查。

如果你已按第 5 步本地安装同 ID 插件，不要再次安装；在库中选择该来源下的插件，点「订阅此来源」。自动更新只跟随绑定来源，其他来源的同 ID 高版本不会接管；更换成不同来源需先卸载再安装。

## 8. 发布下一版

保持 ID 不变，将 `plugin.json` 和项目的插件版本改为 `1.0.1`，重新构建并生成 `example.hello-1.0.1.zip`。先上传 ZIP，再将索引中的 `AssemblyVersion` 及两个下载地址改为新版本；可以用 `Changelog` 添加更新说明。

用户在插件库点击「检查所有插件更新」，再更新单个插件或「更新全部」。

更新保留配置、资源及原启用状态；启动失败时尝试回滚。需要重启且当前已启用的插件，先在面板停用并保存，重新启动游戏后再在插件库更新。配置格式变化时保留旧字段或兼容旧格式，使回滚后的旧插件仍能读取配置。SDK/API 升级与插件自身功能升级分别管理，不为普通插件功能改动修改 `apiVersion`。

## 9. 常见问题和下一步

| 现象 | 检查方向 |
| --- | --- |
| 找不到 `Gkms.Sdk` 包 | 本地源是否直接包含 `.nupkg`，版本是否为 `2.0.7`；不要假定它已在公共 NuGet 发布 |
| 找不到插件入口 | DLL 文件名、完整命名空间、公开类型和无参构造函数是否与清单一致 |
| 已安装但没有问候 | 新安装默认停用；运行验证需已启动游戏并启用插件，查看 `runtime.log` |
| 参数页没有控件 | `settings` 是否存在且有效；本例不需要声明 `settingsUi` |
| 订阅源解析失败 | 地址是否返回 JSON，是否使用 `GkmsApiLevel`，是否存在重复 ID 或无效版本 |
| 检查更新提示官方离线包缺失 | 若暂不使用官方源，可在「订阅源」停用官方源；第三方来源仍可独立安装 |
| 下载包与索引不一致 | 检查 ZIP 内的 ID、版本、API、程序集，确认新 ZIP 已上传而非旧附件 |
| 本地 ZIP 插件看不到自动更新 | 在库中选择对应来源的插件，点击「订阅此来源」 |
| 相同版本 DLL 改动没有更新 | 增加插件版本并重新发布；`+build` 元数据变化不会触发版本升级 |

继续开发时，先阅读规范中的生命周期、主线程调度与配置章节。实际游戏 Hook 通过 SDK 注册；后台网络和文件工作使用 `Stopping` 取消令牌，Unity 操作用 `Enqueue` 调度。恢复自己接管的状态，释放事件及资源，不释放宿主根作用域。自定义 ImGui 设置页可参考 SDK 包内 `examples\ExampleSettingsUi.cs`，保持与游戏入口分离。

发布前实际验证启停、配置修改、重装、升版及异常清理。编译和 ZIP 安装检查无法代替对应游戏版本中的 Hook 与场景验证。
