# Gakumas Localify RE

本仓库只维护 RE 框架：`Sdk`、`Host`、`Bridge`、`Manager` 以及测试工具。官方 14 个插件及汉化资源更新实现已迁至独立本地仓库 `D:\chinosk6\gkms-official-plugins`。两者使用独立 Git 仓库、解决方案和发行目录；框架默认构建不依赖官方插件源码，插件通过 SDK NuGet 包构建。

学园偶像大师 DMM 版的模块化插件框架。玩家按 Ctrl+U 打开游戏内 Dear ImGui 面板，管理插件、勾选功能并编辑插件配置；每个插件有自己的 DLL 和清单。整个界面不使用 WinForms。`localization` 统一启停界面与通用文本、剧情、主页语音气泡、主数据翻译及 Live 歌词文本。Live 首次自动显示歌词归属 `live-unlock` 的 Live 子开关。下载资源保存在各自插件安装目录的 `resources` 子目录中。

## 使用

1. 运行环境为 Windows x64、DX11，安装 **.NET 6 或更新版本的 x64 Runtime**。游戏侧使用原版 DMM 的 **`version.dll` 代理入口 + 运行时 IL2CPP API + MinHook**；原生入口通过 hostfxr 托管 C# 模块。无需 BepInEx、Cpp2IL、预生成 interop 程序集或 Windows Desktop Runtime。[.NET 运行时](https://dotnet.microsoft.com/download/dotnet)
2. 在游戏关闭后，从 `out\Framework\Release` 将 **`version.dll`** 和 **`gakumas-local`** 合并到与 `gakumas.exe` 同级的目录。备份旧汉化的 `version.dll` 后再替换，保留 `gakumas-local\re\profile.json` 及各插件的 `resources`。旧版的 `gakumas-local\local-files` 和 `texture2d` 不再作为 RE 资源来源；通过插件资源更新重新安装原版 ZIP，即可解包到独立目录。旧目录无需删除。如果之前装过 BepInEx 版 RE，先停用其根目录 `winhttp.dll`（例如改名为 `winhttp.dll.disabled`），避免旧加载器继续执行；不要同时启用两套 RE 入口。
3. 在游戏窗口按 **Ctrl+U** 显示或隐藏面板。在「已安装」页左侧勾选功能，点击「保存功能开关」；选择插件后在右侧「插件配置」编辑参数。第一次使用时所有插件默认关闭。「文本翻译」的启用或停用在下次启动游戏时生效，本次运行保持启动时的状态。
4. 保存选择或参数后，框架会在 Unity 更新中处理配置，也可以点击「重载配置」。文本插件的开关、参数和启动时加载的词典留到下次启动；剧情文件按原 DMM 在每次请求时读取，更新可在后续请求中生效；其他插件仍按原有方式即时应用。直接修改配置文件会在约一秒内被检测。原生入口日志位于 `gakumas-local\re\loader.log`，宿主、插件及 Unity 日志位于 `gakumas-local\re\runtime.log`。面板、配置重载和日志均为框架内置功能，所有可选插件关闭时仍可使用。无效 JSON 保留当前状态；支持热切换的插件新参数加载失败时尝试恢复上一版有效参数，并显示错误。
5. 面板提供简体中文、繁体中文、英语和日语。参数页支持数值、开关、镜头模式、位置偏移和原版物理预设，所有声明参数直接使用对应控件编辑。无参数且未提供自定义界面的插件不显示插件配置页。插件 ZIP 安装、卸载确认、文件浏览与资源更新也在 Dear ImGui 面板内完成。面板打开时接收窗口键鼠输入，关闭后恢复游戏输入。

`Gkms.Manager.exe` 保留命令行功能；无参数运行时请求已运行的游戏打开面板，不创建外部 GUI，也不启动游戏。离线可用命令行配置插件。以下示例在部署后的 `gakumas-local\re` 目录中用 PowerShell 7 执行，各命令可单独使用。

```powershell
.\Gkms.Manager.exe list
.\Gkms.Manager.exe enable localization
.\Gkms.Manager.exe disable localization
.\Gkms.Manager.exe import-legacy
.\Gkms.Manager.exe reload
.\Gkms.Manager.exe reload-resources textures
.\Gkms.Manager.exe status
```

`import-legacy` 会读取上一级的 `localizationConfig.json`，迁移功能选择、帧率、画质、旋转方向、陀螺仪和骨骼参数。用户安装的其他插件和自定义参数会保留。`loginAsIOS` 对应合并后的 `ios-login`；`dbgMode` 配合 `unlockAllLive` 或 `unlockAllLiveCostume` 对应 `live-unlock`。旧 RE 的 `purchase-guard` 自动迁移到 `ios-login`，`unlock-live` / `unlock-costumes` 合并为 `live-unlock`。任一旧文本插件迁移为 `localization`，旧 `texture-capture` 迁移为 `capture`；`settings-panel` 和 `unity-log` 迁出插件列表。保存时移除旧文本分项开关，原先全部停用的选择仍保持停用。

## 14 个可选插件

| 插件 ID | 功能及数据来源 |
| --- | --- |
| `localization` | 翻译界面、通用文本、剧情、主数据、主页语音字幕与 Live 歌词文本。开关、参数和启动词典下次启动生效，不支持热启停或主动重载；剧情文件在每次请求时读取；字体由框架固定提供。 |
| `textures` | 用 `Gkms.Textures\resources` 下的 PNG 替换 CanvasRenderer 界面贴图，支持嵌套目录和 sactx 图集名称。插件内置贴图包更新。 |
| `capture` | 可独立选择文本与贴图采集。覆盖 I18n、TMP 初始/字符串/切片/字符数组、旧 UI 和 UIElements；在插件自己的 `output` 中输出 JSON/JSONL，PNG 位于 `output\texture2d`，不写入旧共享 `dump-files`。小键盘 +/− 切换词典分组。卸载、重装保留采集结果。 |
| `frame-rate` | 设置游戏及 Unity 的目标帧率，参数为 `fps`。热启用立即设置 Unity 帧率，下一帧同步游戏管理器；停用恢复游戏请求的帧率。 |
| `graphics` | 设置渲染比例、缓冲区像素、质量、体积效果与反射、LOD 档位。 |
| `window` | 支持窗口缩放与横竖屏选择，解锁尺寸时阻止游戏恢复窗口过程和强制比例，方向切换保留窗口尺寸。F11 或最大化按钮切换无边框全屏，按游戏窗口所在显示器的边界与分辨率铺满，退出恢复原位置、尺寸及样式。参数 `resizable`、`orientation`。 |
| `camera` | 开启插件即启用镜头；F 循环自由/跟随/第一人称，V 切换跟随高度平滑或第一人称滚转锁定，R 重置。自由模式用 WASD、Ctrl/空格移动，方向键转动，Q/E 增减 FOV；跟随用 W/S 调距离、A/D 调横向注视偏移、Ctrl/空格调注视高度、I/K 调位置高度、J/L 调环绕角度。跟随/第一人称用左右方向键选角色，跟随用上下方向键选骨骼。两种跟踪模式默认隐藏头部，退出恢复。默认速度倍率 1、跟随偏移 `[0,0,1.5]`、第一人称偏移 `[0,0.064,0]`。原版 Windows 未接入的鼠标自由镜头与手柄回调不另行启用。 |
| `gyro` | 全景与摄影界面使用鼠标右键控制俯仰/偏航，小键盘 7/9 滚转、5 归中。保留游戏原生小键盘 4/6/2/8 操作，重置按四元数相对姿态计算。 |
| `physics` | 设置角色骨骼的阻尼、刚度、弹力、摆幅、权重、手臂修正、缩放以及 X/Y/Z 轴双向限位。默认值与原版 Default 预设一致；角色初始化时应用，停用恢复本次接管的值。 |
| `schedule-sp` | 在培育日程中保留 SP 课程标记。 |
| `card-cut-in` | 与原版一致：卡牌未指定皮肤时，从主表选择演出等级最高的一款，不要求持有该皮肤。保留手动指定的皮肤；游戏的考试记录也会读取该 ID。 |
| `auto-photo` | 配置 Live 自动摄影的三个张数档位，默认 10、20、30。 |
| `ios-login` | DMM 认证成功并写入令牌后切换后续请求到 iOS；热启用也会重新查询宝石余额，通过游戏的余额变更入口更新当前显示。校验实际请求平台头和余额读回，不把购买拦截成功当作余额切换成功。新登录前和停用后恢复原平台并取消旧余额请求；充值拦截共同启停。 |
| `live-unlock` | 同一插件内可分别开关 Live 与服装/头饰，旧配置导入保留两项选择。覆盖持有判定、缩略图、服装集合与摄影选择；名片保留真实持有判定，过滤未发布扩展服装；停用移除临时记录并保留后续真实数据。 |

`gakumas-local\re\profile.json` 同时存储选择和各插件的独立参数。文本插件读取 `gakumas-local\re\plugins\Gkms.Localization\resources` 下的界面词典、剧情文件和 `masterTrans` 主数据表。Live 歌词译文和主页语音字幕随 `genericTrans` 同包更新。ZIP 安装产生的插件文件夹名称可能不同，资源始终跟随实际插件安装位置。以下配置启用整套文本汉化，并把目标帧率设为 120。

```json
{
  "enabled": ["localization", "frame-rate"],
  "settings": {
    "frame-rate": { "fps": 120 }
  }
}
```

### 插件订阅、安装与版本更新

「插件库」提供名称/说明/标签搜索、分类及来源筛选、已安装/未安装/可更新筛选，选择插件后可查看作者、版本、简介、说明、来源及重启需求，下载安装、更新或卸载。官方 14 个插件分为汉化资源、画面与窗口、摄影与镜头、游戏辅助、开发工具五类。「检查所有插件更新」读取全部启用源；「更新全部」逐个更新可用版本，并保留每项结果。新安装的插件默认停用，在「已安装」页启用。

「订阅源」保留官方来源入口。框架发行包不附带插件；将独立官方插件仓库生成的 `official-plugins.zip` 放入 `re` 目录后，可以离线下载安装 14 个官方插件。当前尚未发布在线仓库；以后将官方 `pluginmaster.json` 或 `index.json` 的 HTTPS 地址填入「官方地址」，即可检查远程新版。第三方可添加自己的索引地址和名称，支持停用、重新启用和移除；移除来源不卸载插件。来源和安装归属保存在 `re\plugin-repositories.json`。合并部署的官方插件自动关联官方源，本地 ZIP 插件可在库中手动关联来源。同 ID 插件只跟随其原订阅源，其他源更高的版本不会接管；更换来源需先卸载再安装。

更新先下载、检查索引与 ZIP 的 ID、版本和 API，再停用旧实例并安装到新目录，保留配置、私有数据、`resources`、`resources.previous` 和 `output`；原来启用的插件会重新启用。下载可取消；应用文件事务期间完成或回滚后再结束。启动失败会尝试恢复旧版；若宿主无法确认停用，保留全部文件和更新记录，关闭游戏后下次启动恢复。卸载同样保留配置和资源。来源列表中的第三方名称由发布者填写；插件运行权限与游戏相同。

插件版本采用 SemVer，支持卫月风格的 `pluginmaster.json` 数组及原有 `index.json`，独立官方插件仓库的构建同时生成两种格式。这里只参考卫月的仓库设计，卫月插件本体不能安装；本框架索引须声明 `GkmsApiLevel`。索引格式、14 个插件的分类与发布步骤见 [插件开发规范](PLUGIN_DEVELOPMENT.md)。这些操作更新插件本体；文本和贴图资源由各插件自己的「资源更新」管理；字体内嵌于框架，只随框架版本更新，下次启动游戏生效。

```powershell
.\Gkms.Manager.exe sources
.\Gkms.Manager.exe sources add '示例源' 'https://example.com/plugins/index.json'
.\Gkms.Manager.exe plugins-online
.\Gkms.Manager.exe plugins-check
.\Gkms.Manager.exe plugins-install official frame-rate
.\Gkms.Manager.exe plugins-update all
```

### 更新文本包或贴图包

在游戏内面板中选中「文本翻译」或「贴图翻译」，打开「资源更新」。支持本地 ZIP、ZIP 网址或发布 API。先「检查更新」查看已安装版本、目标版本和更新说明，再安装；相同版本可重新安装，下载时显示进度并支持取消。更新可在插件首次启用前完成；运行中更新贴图后面板仍请求重载；文本插件不主动重载，启动词典留到下次启动，剧情文件在后续请求时读取。未启用或未连接时显示实际状态。

文本翻译不提供热启停或主动资源重载。I18n 和主数据的实际替换与通用翻译的已译文本保护共用一次启动读取/解析结果，不各自重新读取同一文件。按原 DMM，启动时缺少 `localization.json` 会跳过界面和通用词典加载，主数据与剧情独立处理；补齐启动词典需下次启动生效。其他插件仍按各自规则处理配置和资源重载。

`localization.json` 在 I18n.SetValue 入口按界面键翻译；通用文本入口按原版顺序处理收到的文字：精确词条、已译文集合、卡牌加号、数字格式、split 分段。若资源本身明确提供“通票”这个精确键，原版也会优先匹配它，RE 保留该行为。文本插件只在启动时装入，不保存用于热恢复的原文、控件来源或字符串身份，不反查其他来源的同文译文。运行中保存开关或更新启动词典不会主动改写当前文本；下次启动才采用新设置和新启动词典。剧情按原 DMM 请求时读取文件，不预读、不缓存快照；修改、新增或删除文件会影响后续剧情资源请求，已经交给游戏解析或缓存的内容不会被主动刷新。

剧情入口不依赖启动时 `resource` 目录存在，运行中补建目录后也可在后续请求中读取。剧情文件使用原 DMM 的 UTF-8 字节路径：保留 UTF-8 BOM，按 Windows 文本模式将 CRLF 转为 LF，并保留原版 Ctrl-Z 文件结束及 NUL 字符串结束语义；不自动识别 UTF-16。资源目录越界检查继续保留。

iOS 登录日志区分“本地 API 平台字段已读回确认”和“iOS 余额确认成功”。只有本次 `User/GetBalance` 的实际请求头为单一 `iOS`，任务正常返回且 `UpdateUserBalance` 更新后读回一致，才打印后者；日志包含免费、付费及独立的 WebStore 余额，不记录认证令牌。等待认证、请求中等过程只写日志，插件正常加载后显示运行中；实际请求失败或超时显示失败及原因，不改写当前余额。离线测试不能替代实际账号与服务器的验证。

字体是框架启动时必启用的内置功能，即使所有插件关闭也持续有效。内置 `gkamsZHFontMIX-new-5-game-Bold-complete.otf` 的字体数据覆盖 31,723 个 Unicode 字符：保留原汉化字形，新增游戏 NotoSansJP-Bold 的 1,907 个字符并横向收窄 5%。按原 DMM 的 `CampusAlphanumeric` 名称规则保留数字专用字体；其余 TMP 字体在原资产上替换 `sourceFontFile`，保留 FaceInfo、材质、字重与回退引用，首次设置动态/多图集与最低 4096 图集，清理字形表并广播字体属性变更。汉化启停只改变文本，不切换字体；不提供字体开关或字体热重载。场景回收源 Font 时仅从同一内嵌包取回，不重新清理已填充图集。画质和物理启用、配置重载会立即更新当前管理器与场景角色，关闭时恢复游戏原设置。贴图与原版一样原地更新 Texture2D，所有共享引用同步；停用恢复原图、尺寸和采样设置。可读原图优先还原原始格式和 mip 数据；不可读或 Crunched 原图使用修改前 GPU 读回保存的 PNG 恢复，不依赖 Reinitialize，内部压缩格式由 Unity LoadImage 决定。恢复失败保留备份并报告，可再次重载配置重试。

若已匹配的贴图替换失败，面板会显示“失败”、失败数量和原因；日志另记录具体文件、替换数量与关闭时每张原图的恢复耗时。资源重载成功后会清除失败状态。

两个插件优先使用各自 `updateSource` 参数，否则读取 `gakumas-local\config.json` 的 `useAPIAssetsURL` / `useAPITextureAssetsURL`；文本兼容 `useRemoteAssets` 和 `transRemoteZipUrl`。没有配置时使用原版默认源。`autoCheckUpdates` 控制插件启动检查；没有显式参数时继承对应 `useAPIAssets` / `useAPITextureAssets`，发现新版本后显示说明并由玩家选择是否下载。下载、暂存、写锁和上一份 `resources.previous` 均位于对应插件目录；文本更新保留包外已有本地资源；历史字体文件可能仍在资源目录，但框架不读取它们。版本分别保存在文本插件的 `resources\version.txt` 和贴图插件的 `resources\texture_version.txt`。原版 ZIP 的 `local-files` / `texture2d` 外层仅用于识别内容，不决定安装路径。卸载保留资源，重新安装同 ID 插件时自动接回资源。

更新来源接受返回发布 JSON 的 API（含 `tag_name`、`assets[].name`、`assets[].browser_download_url`）、HTTP(S) ZIP 直链或本地 ZIP 文件，不接受普通网页或本地文件夹。无 `.zip` 后缀的下载地址按返回内容识别。插件配置中的“默认更新源”会保存，供启动检查和手动更新共用；插件配置内“资源更新”分区的“本次来源”只覆盖当前操作，不保存。来源优先级为：本次输入 → 已保存的 `updateSource` → 原 DMM 配置 → 内置默认源。

下载完成后还需通过校验和安装才算成功；面板显示实际阶段，成功、失败和取消均记入 `runtime.log`。切换插件、保存功能开关或隐藏面板不会取消下载。

```powershell
.\Gkms.Manager.exe update localization 'D:\Downloads\GakumasTranslationData.zip'
.\Gkms.Manager.exe update textures 'D:\Downloads\texture2d.zip'
.\Gkms.Manager.exe check-update localization
```

## 框架与插件开发

完整规则见 [插件开发规范（API 2）](PLUGIN_DEVELOPMENT.md)。独立官方仓库中的 14 个插件与第三方插件使用同一套入口、生命周期、清单和配置声明。框架尚未正式发布，只支持当前 API，不维护历史接口。

插件生命周期和由宿主提供服务的模式参考了 Dalamud 的 `IDalamudPlugin` 与 `IDalamudPluginInterface`。DMM 接入保留原版的 `version.dll` 代理与 `LoadLibraryW` 时机：加载 `VuplexWebViewWindows.dll` 时，在调用游戏线程初始化宿主。通过 `il2cpp_domain_get_assemblies`、类/方法/字段 API 查询已初始化的游戏，通过 `il2cpp_runtime_invoke` 调用，通过 MinHook 拦截实际方法地址；不解析磁盘 `GameAssembly.dll` 或 `global-metadata.dat`。[Dalamud 插件接口](https://github.com/goatcorp/Dalamud/tree/master/Dalamud/Plugin) · [MinHook](https://github.com/TsudaKageyu/minhook) · [.NET 原生托管](https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting)

- `Sdk` 提供 `ILocalifyPlugin`、`IPluginContext` 和 API 2 的 `IPluginServices`。插件可取得自己的配置、带 ID 的日志、私有存储、主线程队列、取消令牌、作用域资源管理以及方法解析和回调注册服务。
- `Host` 只读每个包的 `plugin.json` 进行发现。它按选择顺序用独立的程序集加载上下文加载 DLL，共享 SDK 类型。启动部分失败时会移除该插件已经安装的回调，再继续加载下一个。宿主结束时按逆序清理插件。加载上下文用于管理依赖，安装的 DLL 与游戏具有相同的进程权限。
- `Bridge` 提供 DMM 进程入口和 Unity IL2CPP 服务。`Manager` 读取相同的插件清单，支持选择、保存、旧配置导入和调用插件清单中声明的 `IResourceUpdater` 更新入口。
- `Bridge` 通过运行时元数据提供反射形状的类型/成员对象。游戏对象用 IL2CPP GC handle 保持存活；插件用 SDK 的 `Interop.TypeOf`、`Interop.New`、`Interop.Cast` 和 `Interop.NativeTypeObject` 操作，不能对原生对象直接使用 CLR `GetType()` / `Activator.CreateInstance()`。
- Hook 支持前置、后置、状态与异常清理回调、参数和返回值替换、构造函数及作用域卸载。构造函数使用游戏传入的原实例。最后一个回调解除时 MinHook 恢复原始代码；惰性跳板与委托保留到进程结束，避免与正在返回的调用竞态。共享原生地址按隐藏的 MethodInfo 参数区分目标。HarmonyX 仅用于离线游戏替身测试，不随游戏运行时发布。
- `Tests\ExamplePlugin.cs` 与 `Tests\example.plugin.json` 提供不依赖游戏的最小示例；官方插件仓库的 `Plugins\FrameRate` 展示实际游戏 Hook。新插件只引用 `Gkms.Sdk`，实现 `Start` 和 `Dispose`，提供清单即可接入。将清单、插件 DLL、`.deps.json` 和私有依赖放在 `re\plugins` 下同一个独立子目录，管理器下次打开时即可发现。
- 整个管理面板使用 Dear ImGui，在游戏 DX11 `Present` 中绘制；原生后端接入游戏窗口输入，并在绘制后恢复渲染状态。插件通过清单 `settingsUi` 声明 `IPluginSettingsUi` 入口，在 `UiBuilder.Draw` 中直接调用 ImGui.NET 绘制，`OpenConfigUi` 通知界面打开。设置入口独立于游戏入口，插件未启用时也可编辑。UI 在渲染线程执行，游戏操作通过运行时队列回到 Unity 更新线程。
- 有参数的官方插件通过 SDK 的 `SchemaSettingsUi` 从清单生成参数、下拉选项、数组和预设；插件说明仅在详情区显示一次。第三方可复用或自定义界面，通过 `SaveSettings` 保存自己的配置并由框架应用；不用修改管理器。完整例子见 `Tests\ExampleSettingsUi.cs`。
- 插件状态统一为未连接、已停用、运行中、待重启、失败，并在面板打开时每秒查询。请求等待等过程只写日志；实际加载、参数应用、运行或停用恢复错误显示失败及原因。详情展示插件热启停、参数热更新和资源热重载，支持用绿色、不支持用红色；无参数显示灰色。资源更新与参数编辑放在同一个插件配置页，用分隔线分区。

```json
{
  "id": "frame-rate",
  "name": "帧率设置",
  "author": "Gakumas Localify contributors",
  "version": "1.0.0",
  "description": "设置游戏与 Unity 的目标帧率。",
  "apiVersion": 2,
  "assembly": "Gkms.FrameRate.dll",
  "entryPoint": "Gkms.Plugins.FrameRate.Plugin",
  "settingsUi": "Gkms.Plugins.SettingsUi",
  "settings": [
    { "key": "fps", "label": "目标帧率", "type": "integer", "default": 60 }
  ]
}
```

按原版调用链的逐插件审计、离线证据与实机待验证项见 [AUDIT.md](AUDIT.md)。记录中的源码对照与离线通过不代表完整实机还原。

## 构建与验证

使用 .NET SDK 8 或以上版本、PowerShell 7、Visual Studio 2022 C++ x64 工具和 Windows SDK，在仓库根目录执行。原生构建直接调用 MSVC/MASM，不调用 CMD 或修改执行策略。

```powershell
& (Join-Path $PWD 'Build.ps1') -Configuration Release
if (-not $? -or $LASTEXITCODE -ne 0) { throw '框架构建失败' }
```

`out\Framework\Release` 只包含框架部署文件，`out\sdk` 提供插件开发使用的 SDK NuGet 包。默认解决方案包含 SDK、宿主、管理器、原生运行时及 SDK 示例，不包含官方插件或需要它们的联合测试。此前的 `out\Release` 是拆分前的旧产物，保留但不再作为本仓库的默认发行目录。

先在独立官方插件仓库执行其 `Plugins\Build-Repository.ps1` 并传入 SDK 包目录，随后可在本仓库执行完整联合验证：

```powershell
$parameters = @{
    OfficialPluginOutput = 'D:\chinosk6\gkms-official-plugins\out\Release'
    Configuration = 'Release'
    Overlay = $true
}
& (Join-Path $PWD 'Tests\Test-OfficialPlugins.ps1') @parameters
if (-not $? -or $LASTEXITCODE -ne 0) { throw '联合验证失败' }
```

联合测试显式使用官方插件产物中的 DLL 与安装包，测试项目通过 `OfficialPluginOutput` 指定它们的位置，不引用插件源码。脚本把两边产物合并到 `out\Integration` 的独立测试目录，验证发行包之间的实际配合。加 `-Overlay` 会构建独立 DX11 测试窗口，不启动游戏。

功能回归使用 Harmony 回调及游戏替身，覆盖登录顺序/充值拦截、解锁去重/未来资源/名片异常/停用恢复、命名管道重载与错误参数回滚、字幕和贴图重载、内置字体持续有效及数字字体跳过、镜头/陀螺仪输入、窗口尺寸、分类采集、回环 HTTP 更新进度/版本/取消及旧配置迁移。独立原生测试程序验证系统 version.dll 转发、64 KiB 小栈线程的 DLL 通知与实际 LoadLibraryW Hook、hostfxr 托管、真实 MinHook 拦截、共享地址、浮点/字符串/结构体/引用参数、构造函数与停用恢复。测试不会启动游戏。真实 DMM 加载时机、IL2CPP 对象布局、异常传播、登录协议、字体渲染和场景切换仍需游戏内验证，离线通过不等于这些项目已经完成实测。

以原 DMM 仓库实际启用的功能为迁移依据；原版已注释的强制服装、跳过 Live Loading、额外贴图回调及未接入的手柄代码不启用。

2026-11-20 的逐插件对照、修复和验证记录见 [全插件核对记录](AUDIT.md)。管理器「运行状态」会列出组件加载失败、缺失资源和未接入的主表；插件被列为已启用并不代表每个可选组件都可用。资源包内的旧客户端兼容表只在对应当前表成功接入后标记为已替代，真正缺失的入口仍报告详情。

本项目沿用原 DMM 汉化项目的资源约定与部分功能实现思路，遵循 GPL-3.0。对照的本地旧版提交为 `914f6a9`。[原 DMM 汉化仓库](https://git.chinosk6.cn/chinosk/gkms-localify-dmm)
