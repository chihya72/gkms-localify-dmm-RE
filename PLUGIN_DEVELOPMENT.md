# Gakumas Localify 插件开发规范（API 2）

第一次开发插件请先阅读 [第三方插件开发入门](PLUGIN_GETTING_STARTED.md)，其中提供完整可编译示例、SDK 还原命令及打包订阅流程。本文件用于查阅接口与行为细节。

本规范适用于全部内置插件和第三方插件。新插件只依赖公开 SDK，以 DLL + `plugin.json` 安装到自己的插件目录；不需要在宿主或管理器中登记名称、参数或资源更新入口。插件内部的类组织、算法和功能范围由作者决定。

当前运行环境：Windows x64，原生 `version.dll` 入口托管 .NET 6，运行时 IL2CPP API 与 MinHook 提供游戏接入。使用 .NET SDK 8 或更新版本编译，插件目标框架为 `net6.0`。插件运行时依赖由其包提供，SDK 由宿主统一提供；无需 BepInEx 或生成 interop 程序集。

## 1. 插件组成

插件以稳定 ID、清单和独立程序集发布，通过 SDK 与框架交互：

| 接口或文件 | 用途 |
| --- | --- |
| `ILocalifyPlugin.Start(IPluginContext)` / `Dispose()` | 启动与释放插件 |
| `context.Services()` / `IPluginServices` | 获取宿主服务，无需引用 Host 或 Bridge |
| `plugin.json` | 声明 ID、版本、元数据和 API 等级 |
| 配置快照与保存服务 | 读取参数，由管理器统一保存并请求应用 |
| `IPluginUiBuilder` | 注册绘制和配置页事件，使用 ImGui.NET 绘制界面 |

## 2. 必须遵守的核心规则

1. 一个包用一个永久不变的 ID；第三方推荐 `作者.插件名`，例如 `example.greeting`。ID 只用小写字母、数字、点、短横线，以字母开头，不以点结尾（避免 Windows 路径归一化冲突）。显示名可变。所有重复 ID 的包都会跳过。
2. 清单指定一个入口类型。入口必须是公开、非抽象、非泛型类，实现 `ILocalifyPlugin`，有公开无参构造函数。组件可以实现同一接口，宿主只启动清单指定的入口。
3. 构造函数和静态初始化不能操作游戏、注册回调或启动后台任务。宿主在 Unity 线程调用 `Start`；每个实例最多调用一次。重启使用新实例。
4. `Dispose` 必须支持 `Start` 执行到任意位置后失败的情况，并可安全重复调用。释放自己创建的对象、取消订阅并清空持有的游戏对象。不要释放宿主根作用域。
5. 通过 `context.Patch` / `PatchAdvanced` 注册 Hook；不要直接安装原生 Hook 绕过所有权管理，不要删除其他插件的回调。回调的参数约定包括 `__instance`、`__0` 等位置参数、`__result`、`__state`、`__originalMethod` 和 `__exception`。
6. 公共事件订阅、计时器等资源用 `services.Own(IDisposable)` 交给作用域，或在自己的 `Dispose` 中释放。不要同一资源既交给 `Own` 又手动重复释放。
7. 操作 Unity 对象、注册或释放作用域必须在 Unity 线程。后台只做文件、网络及纯计算；使用 `Enqueue` 回到 Unity 线程，并传递 `Stopping` 取消令牌。
8. 仅读写本插件参数与自己的私有存储。不要直接修改整个 `profile.json`，不要修改别的插件目录。共享汉化包等资源需有明确约定。
9. 仅依赖 `Gkms.Sdk` 的公开接口，不依赖 `Gkms.Host`、`Gkms.Runtime` 的内部实现，不复制 SDK 到插件包。游戏反射或原生调用需要作者自行处理游戏版本差异。
10. 可恢复的临时改动在停用时恢复；已经产生的游戏业务结果不要求回滚。仅在下次赋值或场景重建时恢复的功能，要在说明中写清楚。后台工作必须观察取消，不得在停止后继续操作游戏。

## 3. 包与清单

安装位置示例：

```text
gakumas-local\re\plugins\Example.Greeting\
  plugin.json
  Example.Greeting.dll
  Example.Greeting.deps.json      可选，有第三方依赖时建议携带
  私有依赖 DLL / runtimes\       保持构建输出中的相对路径
```

```json
{
  "id": "example.greeting",
  "name": "问候示例",
  "author": "Example developer",
  "version": "1.0.0",
  "description": "在加载时写入问候日志。",
  "apiVersion": 2,
  "assembly": "Example.Greeting.dll",
  "entryPoint": "Example.Greeting.Plugin",
  "settings": [
    { "key": "message", "label": "问候内容", "type": "string", "default": "Hello!" },
    { "key": "logOnStart", "label": "加载时记录日志", "type": "boolean", "default": true }
  ]
}
```

API 2 必填：`id`、`name`、`author`、`version`、`description`、`apiVersion`、`assembly`、`entryPoint`。`version` 使用 `主.次.补丁`，可附预发布及构建标识；它与 SDK API 等级相互独立。`assembly` 必须是包根目录里的 DLL 文件名，不能含路径。新版本保持 ID 不变并递增版本。

可选字段：

| 字段 | 含义 |
| --- | --- |
| `repositoryUrl` | 作者提供的项目地址；框架不会自动执行或下载它 |
| `punchline` / `category` / `tags` | 插件库简介、分类及搜索标签；官方 14 个插件均提供，第三方可省略 |
| `names` | 按语言代码映射显示名，如 `zh-TW`、`en`、`ja`；找不到时用 `name` |
| `settings` | 配置字段数组；省略等同于无声明字段 |
| `presets` | `{ "name": "预设名", "values": { ... } }` 数组，只能设置已声明字段 |
| `resourceUpdater` | 实现资源更新接口的公开无参类型，位于同一程序集 |
| `canReloadResources` | 为 `true` 时入口必须实现 `IResourceReloadable`，管理器显示资源重载操作 |
| `requiresRestart` | 默认 `false`。为 `true` 时，未显式声明的热启用、热停用能力默认关闭，参数更新默认延后；不再控制资源重载。文本插件使用此模式，其资源重载独立声明为关闭。 |
| `canHotEnable` / `canHotDisable` | 分别声明运行中启用、停用能力；省略时取 `!requiresRestart`。运行中安装的新插件也遵守声明。 |
| `canReloadSettings` | 参数热更新能力；省略时仅在同时支持热启用、热停用时启用。优先调用 `ISettingsReloadable`；否则通过释放并创建实例应用。不能热启停却声明可更新参数时必须实现该接口。 |
| `settingsUi` | 同一插件程序集内实现 `IPluginSettingsUi` 的公开无参类型；不填且有配置字段时使用通用配置页，两者皆无则不显示配置页。 |

发现插件只读取 JSON，不加载 DLL。启用时实例化游戏入口；打开设置时单独加载设置入口。设置入口不需要游戏或游戏插件已启用。包私有依赖由独立的 `AssemblyLoadContext` 和 `.deps.json` 解析，SDK 和 ImGui.NET 共享宿主版本。加载上下文管理依赖与回收，不是权限沙箱；游戏插件拥有游戏进程权限，设置入口拥有管理器进程权限。

## 4. 生命周期与失败处理

```text
读取清单 → 检查 API 与配置 → 构造入口 → Start → 活动
Start 失败或停用：取消令牌 → 逆序释放作用域资源/Hook → 插件 Dispose → 请求卸载程序集
```

某个插件失败不阻止其他插件加载。某个取消回调、资源释放或插件 `Dispose` 抛异常时，宿主记录错误并继续后续清理。已停止作用域拒绝新 Hook、子作用域与资源注册；排队但尚未执行的操作丢弃。

配置重载由框架完成：选择和参数未改变的插件保持运行；改变的插件停止后重新启动。声明字段验证失败时保留原实例；启动失败时尝试恢复上一次有效参数。恢复也可能因游戏版本等外部原因失败，此时日志报告错误，不把失败插件标记为活动。

`Dispose` 是停止回调和恢复可恢复改动的保证，不代表 CLR 已立即卸载 DLL。未释放的静态事件、任务或原生回调会阻止程序集回收。安装新 DLL 建议在游戏关闭时进行；不支持同版本文件改动自动热更新二进制。

## 5. 宿主服务

游戏类型是运行时元数据视图，不是 CLR 游戏程序集。使用 `context.FindType` / `RequireType` 获取类型，使用其 `GetMethods` / `GetProperty` / `Invoke` 等接口操作。对游戏对象调用 `Interop.TypeOf(obj)`，创建实例使用 `Interop.New(type, args)`，转换使用 `Interop.Cast(obj, type)`，传递游戏的 `System.Type` 使用 `Interop.NativeTypeObject(type)`。字段同时可通过同名属性视图访问。基础数值、字符串和枚举转换为对应的 CLR 值；游戏类和其他值类型通过 SDK 的 `INativeObject` 包装，不要假设它们能直接转换成 CLR 游戏类型。

泛型回调使用 `handler.MakeGenericMethod(Interop.CallbackType(gameType))`；结构体回调应允许装箱对象，不应添加 `where T : struct`。创建游戏对象时保存查询到的游戏 Type，不要从回调的 `typeof(T)` 推断游戏类型。游戏数组可通过 `Interop.Items` 遍历。包和配置格式继续沿用 API 2；依赖旧生成程序集或直接调用 CLR `GetType()` 的第三方插件需按此规则调整并重编译。

宿主还可提供 `INativeInternalCalls` 和 `INativeCachedInternalCalls`。前者解析完整 icall 签名；后者的 `ReplaceInternalCallCache(wrapper, signature, delegate)` 返回需交给 `services.Own` 的释放对象，用于原版陀螺仪那种被内联的包装函数缓存。只支持 SDK 声明的四类 Cdecl 委托，布尔 ABI 使用 `byte`；不要直接 detour 可能被链接器折叠合并的引擎常量桩。接口或模式不可用时明确报告未接入。宿主保留自身跳板，释放时恢复缓存并清除插件委托，允许已进入的调用退回原入口。此可选能力不改变 API 2 的必需服务。

```csharp
var services = context.Services();
var settings = context.ReadSettings<MyOptions>();
context.Log("插件启动");
services.Enqueue(() => { /* Unity 线程操作 */ });
services.Own(subscription); // subscription 实现 IDisposable
```

| 服务 | 规则 |
| --- | --- |
| `Id` / `PackageDirectory` | 插件身份及安装位置；程序集与清单不应在运行中改写，下载资源可放在其 `resources` 子目录 |
| `StorageDirectory` | `gakumas-local\re\data\<id>`，首次访问创建；保存缓存和插件自有文件 |
| `DataDirectory` | 共享的 `gakumas-local` 根目录，用于读取旧版配置等；汉化资源使用插件自己的 `resources`，不要回退到共享 `local-files` |
| `Settings` | 当前启动的 JSON 快照；`Option<T>` 按键读取，`ReadSettings<T>` 读取公开属性的类型化配置 |
| `Log` | 自动加插件 ID 前缀，可在后台线程调用；不记录凭据 |
| `FindType` / `Find` / `Require` | 通过游戏接入层查找类型及精确方法签名 |
| `Patch` | 前置、后置 Hook，默认优先级 400 |
| `PatchAdvanced` | 构造函数及 finalizer 回调；后端未提供时抛 `NotSupportedException` |
| `OpenScope` | 为子功能创建独立作用域；子作用域提供相同 API 2 服务 |
| `Own` | 作用域按登记的逆序释放 `IDisposable`；登记与释放限 Unity 线程 |
| `Stopping` | 停用或启动失败时取消；后台任务传递给可取消 I/O，并在投递结果前检查 |
| `Enqueue` | 可从后台投递，在宿主更新中执行；已停止时抛 `ObjectDisposedException` |

取消检查与投递之间可能发生停用；后台调用 `Enqueue` 还应处理 `ObjectDisposedException`。不要在 Unity 线程同步等待一个需要 Unity 队列完成的任务。宿主不强制终止任意第三方线程，也不保证任意游戏 Hook 回调本身都在 Unity 线程。

## 6. 配置与通用参数 UI

Ctrl+U 打开的管理面板与「插件配置」页均使用游戏内 Dear ImGui + DX11 覆盖层，不依赖 WinForms。内置插件与第三方插件共用 `IPluginSettingsUi` 加载流程。14 个内置插件在自己的程序集中提供 `Gkms.Plugins.SettingsUi` 入口，继承 SDK 的 `SchemaSettingsUi`，从清单生成控件。第三方可继承它进行扩展，也可完整实现接口，自由编写 ImGui 布局、表格、分页和交互。所有保存入口仍经过同一配置校验。

支持的 `type`：

| 类型 | 对应控件/值 |
| --- | --- |
| `boolean` | 开关；JSON `true` / `false` |
| `integer` | 32 位整数输入；不接受字符串或 `1.0` 形式 |
| `number` | 数值输入；值须可由 .NET `decimal` 表示 |
| `string` | 文本框；提供 `choices` 后为下拉选择 |
| `integer-array` / `number-array` | 固定长度数值组，长度由 `default` 决定，1–16 项 |

每个字段必填 `key`、`label`、`type`、`default`；key 是以字母开头的字母/数字/下划线标识。可用 `min` / `max` 限定数值及数组元素，`elementLabels` 标注各数组元素，`labels` 按语言映射标签。所有声明字段都通过对应控件编辑，旧 `advanced` 标记不再隐藏字段。显示缺失参数时使用声明的默认值。

宿主不会把 UI 默认值自动写进启动快照；未提供的字段由插件自己的默认值或资源配置处理。作者应让声明默认值与代码默认值一致；如 `autoCheckUpdates` 省略时继承原 DMM 配置，要在说明中明确。通用 ImGui 页仅写入修改过的字段，保留未修改的缺省状态。未知字段会保留；跨字段约束由插件在注册游戏回调前校验。

配置唯一来源是 `profile.json` 的 `settings.<id>`。游戏入口读取启动快照，设置入口通过 `ISettingsUiContext.SaveSettings` 保存自己的参数。文件锁与原子替换避免写入一半或覆盖其他插件；保存成功后管理器立即请求运行中的宿主应用配置。游戏未连接时保留保存结果并显示离线状态。需要自己的历史记录或复杂私有数据时使用 `StorageDirectory`。

### 自定义 ImGui 设置入口

清单增加 `"settingsUi": "Example.Greeting.SettingsUi"`，对应类型示例：

```csharp
using Gkms.Sdk;
using ImGuiNET;

public sealed class SettingsUi : IPluginSettingsUi
{
    private ISettingsUiContext context = null!;
    private bool visible, enabled = true;
    public void Initialize(ISettingsUiContext context)
    {
        this.context = context;
        enabled = context.ReadSettings<Options>().Enabled;
        context.UiBuilder.OpenConfigUi += Open;
        context.UiBuilder.Draw += Draw;
    }
    private void Open() => visible = true;
    private void Draw()
    {
        if (!visible) return;
        ImGui.TextUnformatted("My plugin settings");
        ImGui.Checkbox("Enabled", ref enabled);
        if (ImGui.Button("Save")) context.SaveSettings(new Options { Enabled = enabled });
    }
    public void Dispose()
    {
        if (context == null) return;
        context.UiBuilder.Draw -= Draw;
        context.UiBuilder.OpenConfigUi -= Open;
    }
    public sealed class Options { public bool Enabled { get; set; } = true; }
}
```

上例应放入清单指定的命名空间。完整可编译示例是 `Tests\ExampleSettingsUi.cs`，包含读取、保存、重新读取和错误显示。

| 设置服务 | 行为 |
| --- | --- |
| `UiBuilder.Draw` | 每帧在 ImGui 内容区域内调用，可直接绘制控件；需要额外 ImGui 窗口时自行配对 `Begin` / `End` |
| `UiBuilder.OpenConfigUi` | 设置页打开时通知插件，设置可见标志；此回调本身不绘制 |
| `UiBuilder.Enqueue` | 后台结果投递回 UI 线程；停止后拒绝，未执行队列在卸载时丢弃 |
| `ReadSettings<T>` / `Settings` | 读取当前设置快照；JSON 属性采用 camelCase，大小写敏感 |
| `ReloadSettings` | 重新读取磁盘配置并刷新快照 |
| `SaveSettings<T>` / `SaveSettings(JsonElement)` | 合并提供的顶层字段到本插件最新配置，保留未知字段及其他插件配置 |
| `ReplaceSettings` | 自定义界面需要删除字段时可明确替换本插件的整个配置对象；不影响其他插件 |
| `Fields` / `Presets` | 清单中的配置声明；可混合通用控件和自定义布局 |
| `CanReloadResources` / `ReloadResourcesAsync` | 请求游戏内本插件重载资源；返回实际状态，未连接时不会伪报成功 |
| `Stopping` / `Own` / `Log` | 取消后台工作、释放设置资源及带插件 ID 的日志 |

绘制规则：

- 设置入口在游戏进程的 DX11 Present 渲染线程运行，使用独立程序集加载上下文，不能假定与游戏入口共享静态状态，也不能在绘制线程直接操作 Unity 对象。游戏状态变更通过保存配置或资源重载通道排入 Unity 更新线程。
- **只在 `Draw` 中调用 ImGui**。宿主拥有上下文、字体、输入、帧循环、DX11 设备与渲染；插件不得调用 `CreateContext`、`DestroyContext`、`NewFrame`、`Render` 或替换全局后端。
- 自动换行文本使用 `PluginUiText.Wrapped`：按当前可用宽度生成显式换行，避免随附旧原生库在滚动裁剪时将段落文字画入后续控件。插件说明由宿主绘制，通用配置页不重复显示。
- 宿主提供 ImGui.NET **1.89.2** 及匹配的原生 cimgui。使用 SDK 的依赖版本，不在插件包内复制 ImGui.NET 或 cimgui，也不要替换 DLL；原生结构布局必须一致。
- `Begin` / `End`、`Push` / `Pop` 要配对，遇到可抛异常的代码使用 `try/finally`。对预期的校验或文件错误在界面内显示，避免让异常逸出绘制回调。
- 未处理的初始化或绘制异常停止该设置会话；绘制失败后不提交可能损坏的 ImGui 栈，而是隐藏面板并销毁其上下文，记录错误；再次打开从插件列表开始。原生断言、内存破坏等进程级故障无法靠托管异常隔离。
- 切换插件或释放面板上下文时先取消工作、清空事件和队列、释放 `Own` 资源，再调用设置入口 `Dispose` 并请求卸载程序集。Ctrl+U 隐藏面板会保留当前编辑状态；隐藏期间不调用 Draw。`Dispose` 只做清理；初始化失败也会执行。不要在释放后访问配置服务或继续绘制。
- 后台不得调用 ImGui 或保存服务。通过 `UiBuilder.Enqueue` 投递，处理停用时的 `ObjectDisposedException`。不要阻塞 UI 线程等待依赖 UI 队列的任务。

目前提供游戏内管理面板及其设置界面扩展，不提供任意插件间 IPC、独立 HUD 绘制接口或第三方自建图形设备服务。所有插件共享宿主的 ImGui.NET 1.89.2/cimgui 上下文；不能自行替换渲染后端或持有交换链后缓冲区。

## 7. 可选资源能力

- `IResourceReloadable.ReloadResources()` 在 Unity 线程执行，由插件决定如何安全替换资源。先读取和验证新资源，再替换当前有效资源；保持接口幂等。
- `IResourceUpdateService` 提供 `CheckAsync` / `InstallAsync`，支持取消和进度；`IResourceUpdater.UpdateAsync` 提供一键更新入口。声明的更新类型必须可独立构造，不得依赖插件已启动或游戏已运行。
- 资源更新入口必须自行用 `using` 等方式释放文件、网络和临时资源，不得留下后台任务；它不接收宿主游戏上下文。更新方法返回前完成工作。包安装失败应保留旧资源。
- 内置更新入口从自身程序集的实际加载位置定位插件目录，与游戏入口使用同一路径；不依赖文件夹名称，也不改写旧插件资源目录。资源位于 `resources`，备份位于 `resources.previous`；管理器卸载时保留这两个目录，重装同 ID 时接回资源。更新期间持有插件目录的 `resources.edit` 写锁，卸载遵循同一锁。

更新来源接受返回发布 JSON 的 API（含 `tag_name`、`assets[].name`、`assets[].browser_download_url`）、HTTP(S) ZIP 直链或本地 ZIP 文件，不接受普通网页或本地文件夹。无 `.zip` 后缀的下载地址按返回内容识别。插件配置中的“默认更新源”会保存，供启动检查和手动更新共用；插件配置内“资源更新”分区的“本次来源”只覆盖当前操作，不保存。来源优先级为：本次输入 → 已保存的 `updateSource` → 原 DMM 配置 → 内置默认源。
- `ISettingsReloadable.ReloadSettings(JsonElement)` 在 Unity 线程更新参数；失败时抛出异常并保留旧的有效设置。宿主仅在成功返回后更新设置快照，能力声明为禁止时不调用此接口。
- iOS 登录和充值保护共用一个插件；Live、服装及头饰解锁共用一个插件。这些业务组合不改变通用接口。文本启动词典下次启动生效，剧情文件在后续请求时读取，不提供主动资源重载。
- 运行时返回结构化状态：`Disabled`、`Running`、`RestartRequired`、`Failed`；管理器无连接时显示“未连接”。`IPluginDiagnostics.Warnings` 只报告实际错误，不用来表示等待认证、余额请求中或测试证据不足。操作失败保留原因，成功重试或重新启用后清除对应错误；等待重启与错误分开处理。

## 8. 当前开发版本

宿主只接受当前 `apiVersion: 2`，全部官方插件、第三方示例及元数据使用同一规则；其他 API 等级在发现阶段被拒绝。

SDK 包版本为 `2.0.6`，程序集版本保持 `2.0.0.0`，API 等级仍为 2；日志等级扩展兼容现有插件，原有 `Log(string)` 仍可使用。本版修复插件更新前的订阅索引刷新，接口保持兼容。兼容更新递增包版本，破坏兼容性的接口变更须提升 API 等级并重新编译插件。SDK 包在[框架 Release](https://github.com/chihya72/gkms-localify-dmm-RE/releases/latest) 下载。原 DMM 用户配置导入属于资源与业务数据迁移。

## 9. 开始编写自己的插件

仓库提供已参与构建及离线验证的 `Tests\ExamplePlugin.cs`、`Tests\ExampleSettingsUi.cs`、`Tests\example.plugin.json` 和 `Tests\Gkms.ExamplePlugin.csproj`。示例通过 SDK 依赖取得 ImGui.NET，不使用 Host、Bridge、Manager。测试专用 `ContractProbe.cs` 不需要复制到自己的项目。NuGet SDK 包内也附带示例源文件与清单。

独立项目可以直接引用发行包中的 SDK DLL：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Gkms.Sdk">
      <HintPath>$(GkmsSdkPath)</HintPath>
      <Private>false</Private>
    </Reference>
    <PackageReference Include="ImGui.NET" Version="[1.89.2]" ExcludeAssets="runtime" />
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

在自己的项目目录用 PowerShell 7 构建（替换 SDK 路径和项目名）：

```powershell
$sdk = 'D:\gkms-localify-dmm-RE\Sdk\bin\Release\net6.0\Gkms.Sdk.dll'
[string[]]$arguments = @('build', '.\Example.Greeting.csproj', '-c', 'Release', "-p:GkmsSdkPath=$sdk")
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "插件构建失败 $LASTEXITCODE" }
```

也可从框架本地打包产物安装 `Gkms.Sdk.2.0.6.nupkg`；本仓库不假定该包已发布到公共 NuGet。打包命令在仓库根目录执行：

```powershell
$project = Join-Path $PWD 'Sdk\Gkms.Sdk.csproj'
$output = Join-Path $PWD 'out\Release\sdk'
[string[]]$arguments = @('pack', $project, '-c', 'Release', '-o', $output)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "SDK 打包失败 $LASTEXITCODE" }
```

发布时携带 `plugin.json`、主 DLL、`.deps.json` 及私有依赖；不携带 SDK、ImGui.NET、cimgui、Host、Manager、Bridge。参考独立官方插件仓库的 `Plugins\Plugin.targets`；独立作者可采用自己的构建工具，无需复制仓库目录结构。管理器支持在线插件源及版本更新；不自动下载依赖插件，也不提供签名审核服务。

### 插件订阅源格式与发布

源为 HTTPS JSON 索引，每个 ID 只列一个当前可安装版本。支持数组式 `pluginmaster.json` 和对象式 `index.json`，两者共用订阅、安装、更新、校验和回滚流程。索引须声明 Gkms API 等级，版本使用 SemVer。

推荐发布格式：

```json
[
  {
    "InternalName": "example.greeting",
    "Name": "示例插件",
    "AssemblyVersion": "1.2.0",
    "GkmsApiLevel": 2,
    "Author": "示例作者",
    "Description": "插件功能说明。",
    "Punchline": "在游戏内显示问候。",
    "Category": "开发工具",
    "Tags": ["示例", "greeting"],
    "Assembly": "Example.Greeting.dll",
    "DownloadLinkInstall": "Example.Greeting-1.2.0.zip",
    "DownloadLinkUpdate": "Example.Greeting-1.2.0.zip",
    "Changelog": "修复配置重载。",
    "RepoUrl": "https://example.com/greeting",
    "Names": { "zh-CN": "示例插件", "en": "Example greeting" },
    "RequiresRestart": false,
    "HasSettingsUi": true,
    "HasResourceUpdater": false
  }
]
```

`InternalName` 对应 ZIP 中的 `id`，`AssemblyVersion` 对应 `version`，`GkmsApiLevel` 对应 `apiVersion`；这些字段与 `Name`、`Author`、`Description`、`DownloadLinkInstall` 必填。没有有效 `GkmsApiLevel` 的索引会显示格式错误。`DownloadLinkUpdate` 可省略，此时更新复用安装地址；提供时更新使用该地址，仍须下载完整 ZIP 并验证清单。其他字段可省略。`RepoUrl` 仅供展示和复制，框架不会自动打开它。

版本使用 SemVer（例如 `1.2.0` 或 `1.3.0-beta.1`），与 ZIP 内清单的版本字符串完全一致；升级按 SemVer 优先级比较，构建元数据不触发更新。API 必须与 ZIP 清单一致；不兼容的 Gkms API 版本可显示，但不能安装。当前每个 ID 只有一个发布版本，暂不提供测试通道或下载统计。

兼容的对象式索引：

```json
{
  "schemaVersion": 1,
  "name": "我的插件源",
  "plugins": [
    {
      "id": "example.greeting",
      "name": "示例插件",
      "version": "1.2.0",
      "apiVersion": 2,
      "author": "示例作者",
      "description": "插件功能说明。",
      "downloadUrl": "Example.Greeting-1.2.0.zip",
      "assembly": "Example.Greeting.dll",
      "releaseNotes": "修复配置重载。",
      "punchline": "在游戏内显示问候。",
      "category": "开发工具",
      "tags": ["示例", "greeting"],
      "names": { "zh-CN": "示例插件", "en": "Example greeting" }
    }
  ]
}
```

对象式索引中 `assembly`、`releaseNotes`、`names`、`punchline`、`category`、`tags` 可省略；还支持可选的 `repositoryUrl`、`updateUrl`、`requiresRestart`、`hasSettingsUi`、`hasResourceUpdater`，对应数组式元数据。安装和更新地址都可为完整 HTTPS 地址，或相对于索引地址的 ZIP 路径。ZIP 根目录（或单层外包装目录）包含一个 `plugin.json`。索引上限 4 MiB、下载包 256 MiB、解包后 512 MiB；不得包含链接、越界路径、宿主 DLL、退休/暂存标记或重复文件。仅本机离线测试允许回环 HTTP。

独立官方插件仓库提供 `Plugins\Build-Repository.ps1 -SdkFeed <SDK包目录>`，生成 `plugin-packages\pluginmaster.json`、`plugin-packages\index.json`、14 个插件 ZIP，以及供管理器离线使用的 `gakumas-local\re\official-plugins.zip`。元数据从各插件 `plugin.json` 导出；API、版本和配置/资源能力不另行维护副本。脚本仅依赖 SDK NuGet 包和 PowerShell，不依赖框架源码、Host 或 Manager。将离线包放入 RE 的 `re` 目录，或将索引和 ZIP 托管到 HTTPS 服务即可订阅。正式发布应使用按版本区分的 ZIP 地址，先上传包，最后更新索引。本脚本不创建远程仓库、不上传。

### 官方插件独立仓库

官方 14 个插件的实现、分类设计与发布脚本由独立的 `gkms-localify-dmm-RE-official-plugins` 仓库维护。RE 框架只消费其索引和安装包；默认构建不编译或附带官方插件。插件仓库通过 `Gkms.Sdk` NuGet 包依赖框架 API，不通过跨仓库项目引用依赖框架源码。

下载和验证阶段不加载插件代码。安装成功后记录来源，统一更新只从同一来源选择版本；源名称不作为身份依据。更新用新目录避免 Windows DLL 占用，重用该 ID 的配置/私有存储并迁移插件资源；插件不能缓存旧安装路径。停用和启动都必须遵守生命周期要求；启动失败时框架尝试回滚旧版。第三方升级涉及配置格式变更时，应保留旧配置字段，或实现能向前、向后读取的迁移，以便回滚。

## 可选的停用收尾

同步 `Dispose` 应立即恢复功能状态。若恢复显示还需要等待游戏的只读请求，可实现 `IPluginStopCleanup.CreateStopCleanup()`，返回 `IPluginCleanup`。宿主只在普通停用时、移除原作用域 Hook 并完成 Dispose 后调用它；启动失败、配置替换和宿主退出不会启动收尾。

收尾得到独立作用域，`Update()` 在 Unity 线程每帧调用，完成时返回 true，失败时抛出可读错误。宿主最多保留 30 秒，并在重新启用同一 ID 或退出时先取消收尾、清理 Hook，再释放加载上下文。收尾必须在 Dispose 中取消未完成请求，且不得重新启用已经关闭的功能。停止时丢弃的原作用域队列仍不会执行。此扩展需要同步更新宿主与 SDK；仅给旧版宿主替换插件 DLL 不受支持。既有未使用此扩展的 API 2 插件保持原生命周期。

## 10. 如何验证合规

RE 默认构建只验证框架编译。另行运行 `Tests\Test-OfficialPlugins.ps1 -OfficialPluginOutput <官方插件输出目录>`，会用真实官方产物检查 14 个包的元数据、配置定义、默认值、预设、游戏与设置入口、资源能力及禁止的框架实现依赖。联合测试不引用插件源码，也不属于框架默认解决方案。独立 SDK 示例经真实发现和加载验证配置读取、热重载、失败清理、父子作用域、取消异常、停止后禁止注册和丢弃旧队列。ImGui 专项创建原生 ImGui 上下文与 DX11 离屏目标，绘制全部官方设置页并验证交互及异常处理；可选 `-Overlay` 额外打开独立 DX11 测试窗口。现有功能离线测试继续覆盖插件的 Hook 和恢复行为。

第三方至少测试：启用、停用、重复启停；启动中途失败；缺少可选游戏方法；坏配置；卸载时后台工作取消；多个插件同时存在时互不移除回调。通过这些测试不等于已经验证当前 DMM 的所有 IL2CPP 行为，游戏版本更新后仍要验证实际 Hook 签名和资源可用性。
