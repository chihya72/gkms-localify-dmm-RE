# Gakumas Localify RE

学园偶像大师 DMM 版的模块化插件框架。玩家在 Windows 管理器中勾选功能，每个功能有独立的 DLL、清单和配置。只开剧情翻译时，宿主只加载剧情插件。已有汉化资源继续放在游戏目录的 `gakumas-local` 下。

## 使用

1. 运行环境为 Windows x64。游戏侧入口使用 **BepInEx 6 Unity IL2CPP x64**，本项目编译使用 `6.0.0-be.793`。独立管理器使用 **.NET 6 Windows Desktop Runtime x64**。[BepInEx 构建](https://builds.bepinex.dev/projects/bepinex_be) · [.NET 桌面运行时](https://dotnet.microsoft.com/download/dotnet/6.0)
2. 从下文构建得到的 `out\Release` 中，将 `BepInEx` 和 `gakumas-local` 两个目录合并到已配置好 BepInEx 的游戏目录。旧汉化的 `version.dll` 与 RE 会处理相同的游戏方法。切换时先在游戏关闭的状态下备份并停用旧汉化入口，保留 `gakumas-local\local-files` 和 `gakumas-local\texture2d`。
3. 打开 `gakumas-local\re\Gkms.Manager.exe`，勾选功能并保存。第一次使用时所有插件默认关闭。仅需常用汉化可选 `i18n`、`text`、`story`、`master` 和 `font`。保留游戏原字体时可以单独取消 `font`。
4. 选择在下一次游戏加载宿主时生效。BepInEx 日志会按插件 ID 记录加载结果。资源缺失、API 版本不兼容或启动失败的插件会给出单独的诊断。

管理器也支持命令行。以下示例在部署后的 `gakumas-local\re` 目录中用 PowerShell 7 执行，各命令可单独使用。

```powershell
.\Gkms.Manager.exe list
.\Gkms.Manager.exe enable i18n text story master font
.\Gkms.Manager.exe disable font
.\Gkms.Manager.exe import-legacy
```

`import-legacy` 会读取上一级的 `localizationConfig.json`，将已有的功能选择、帧率、画质、旋转方向、陀螺仪和骨骼参数写入新配置。日后可以在管理器中继续修改选择。用户安装的其他插件和自定义参数会保留。

## 可选插件

| 插件 ID | 功能及数据来源 |
| --- | --- |
| `i18n` | 用 `local-files\localization.json` 替换 Qua.UI 的键值文本。 |
| `text` | TextMeshPro、旧版 Unity UI 与文本框的通用翻译。支持 `generic.json`、`genericTrans` 下的分段词典、数字占位符、带加号卡名及字符数组。 |
| `story` | 从 `local-files\resource` 读取同名剧情文本，缺失时回到游戏资源加载流程。 |
| `master` | 读取 `local-files\masterTrans`，按表名、复合主键和子项主键匹配反序列化后的主数据。 |
| `font` | 从 `local-files\gakumasassets` 加载旧字体包，为 TMP 增加中文回退字体。 |
| `textures` | 用 `texture2d` 下的 PNG 替换 CanvasRenderer 界面贴图，支持嵌套目录和 sactx 图集名称。 |
| `subtitles` | 读取旧版 `home_voice_subtitles.json`，在主页语音播放时通过游戏提示显示字幕。此版使用提示框，旧版对白气泡的尺寸和定位配置尚未移植。 |
| `capture` | 将观察到的界面键和通用原文写入 `dump-files\observed.jsonl`。 |
| `texture-capture` | 将 CanvasRenderer 使用的贴图导出至 `dump-files\texture2d`，可独立于贴图替换启用。 |
| `frame-rate` | 设置游戏及 Unity 的目标帧率，参数为 `fps`。 |
| `graphics` | 设置渲染比例、缓冲区像素、质量、体积效果与反射、LOD 档位。 |
| `window` | 支持窗口边框缩放和横竖屏选择，参数为 `resizable` 和 `orientation`。 |
| `camera` | F8 切换摄影机。`mode` 可选 `free`、`follow` 或 `first-person`。自由模式用 WASD 移动、Q/E 升降、鼠标右键转动。跟随和第一人称支持角色序号 `actorIndex` 和位置偏移 `offset`。 |
| `gyro` | 全景与摄影界面使用鼠标右键或小键盘 4/6、2/8 控制姿态，小键盘 5 归中。 |
| `physics` | 设置角色骨骼的阻尼、刚度、弹力、摆幅、权重和手臂修正。 |
| `lyrics` | 首次显示 Live 歌词，之后可以使用游戏自己的开关。 |
| `auto-photo` | 配置 Live 自动摄影的三个张数档位，默认 10、20、30。 |
| `unity-log` | 将游戏的 Unity 日志消息转发至 BepInEx 日志。 |

`gakumas-local\re\profile.json` 同时存储选择和插件参数。以下配置启用界面翻译，并把目标帧率设为 120。

```json
{
  "enabled": ["i18n", "frame-rate"],
  "settings": {
    "frame-rate": { "fps": 120 }
  }
}
```

**资源更新**独立为 `Gkms.Resources.exe`，在游戏关闭后按需运行。第一个参数为 `text` 或 `textures`，第二个参数接受本地 ZIP、ZIP 地址或包含 `tag_name` 和 `assets` 的发布 API 地址。旧配置中的 `useAPIAssetsURL` 和 `useAPITextureAssetsURL` 可用于相应的资源包。更新先解压到暂存目录，再切换资源，上一份内容保存在 `re\local-files.previous` 或 `re\texture2d.previous`。更新文本时会保留包外的本地资源和字体。

```powershell
.\Gkms.Resources.exe text 'D:\Downloads\GakumasTranslationData.zip'
```

## 框架与插件开发

插件生命周期和由宿主提供服务的模式参考了 Dalamud 的 `IDalamudPlugin` 与 `IDalamudPluginInterface`。DMM 接入采用 BepInEx 和 Il2CppInterop，游戏初始化完成后由 HarmonyX 注册所选模块的回调。[Dalamud 插件接口](https://github.com/goatcorp/Dalamud/tree/master/Dalamud/Plugin) · [BepInEx](https://github.com/BepInEx/BepInEx) · [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)

- `Sdk` 提供 `ILocalifyPlugin` 和 `IPluginContext`。插件可取得数据目录、自己的配置、日志以及方法解析和回调注册服务。
- `Host` 只读每个包的 `plugin.json` 进行发现。它按选择顺序用独立的程序集加载上下文加载 DLL，共享 SDK 类型。启动部分失败时会移除该插件已经安装的回调，再继续加载下一个。宿主结束时按逆序清理插件。加载上下文用于管理依赖，安装的 DLL 与游戏具有相同的进程权限。
- `Bridge` 提供 DMM 进程入口和 Unity IL2CPP 服务。`Manager` 读取相同的插件清单，支持选择、保存和旧配置导入。
- `Plugins\I18n` 是一个完整的小型插件示例。新插件引用 `Gkms.Sdk`、实现 `Start` 和 `Dispose`、提供清单即可发布。将清单、插件 DLL 和私有依赖放在 `re\plugins` 下同一个独立子目录，管理器下次打开时即可发现。添加插件无需修改宿主。

```json
{
  "id": "i18n",
  "name": "界面键值翻译",
  "apiVersion": 1,
  "assembly": "Gkms.I18n.dll",
  "entryPoint": "Gkms.Plugins.I18n.Plugin"
}
```

## 构建与验证

使用 .NET SDK 8 或以上版本，PowerShell 7，在仓库根目录执行。NuGet 源已随仓库配置。构建产物放在 `out\Release`。

```powershell
dotnet build .\Gkms.sln -c Release
if ($LASTEXITCODE -ne 0) { throw "构建失败 $LASTEXITCODE" }

$tests = Join-Path $PWD 'Tests\bin\Release\net6.0\Gkms.Tests.dll'
$plugins = Join-Path $PWD 'out\Release\gakumas-local\re\plugins'
$scratch = Join-Path $PWD 'Tests\obj\fixtures'
[string[]]$arguments = @($tests, $plugins, $scratch)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "离线验证失败 $LASTEXITCODE" }
```

可将已有 `gakumas-local` 的绝对路径作为测试程序的第四个参数，额外验证真实词典与主数据格式。测试在独立夹具中验证 DLL 加载、真实 Harmony 回调、禁用、重复选择、启动失败回滚、资源更新和管理器配置迁移。DMM 内的字体渲染、字幕时机、场景切换、镜头和硬件输入仍需游戏内验证。

旧版的 iOS 登录模拟及其关联充值保护、全曲和全服装解锁、强制服装、第一人称下的头部模型隐藏和旧版 ImGui 设置面板尚未移植。当前管理器在游戏外运行，插件选择在下次启动时应用。

本项目沿用原 DMM 汉化项目的资源约定与部分功能实现思路，遵循 GPL-3.0。对照的本地旧版提交为 `914f6a9`。[原 DMM 汉化仓库](https://git.chinosk6.cn/chinosk/gkms-localify-dmm)
