# Gakumas Localify RE

学园偶像大师 DMM 版的模块化插件框架。玩家在 Windows 管理器中勾选功能，每个插件有独立的 DLL、清单和配置。`localization` 是共用文本资源包的唯一汉化插件。它统一启停界面与通用文本、剧情、字体、主页语音气泡、主数据翻译和 Live 歌词。已有汉化资源继续放在游戏目录的 `gakumas-local` 下。

## 使用

1. 运行环境为 Windows x64。游戏侧入口使用 **BepInEx 6 Unity IL2CPP x64**，本项目编译使用 `6.0.0-be.793`。独立管理器使用 **.NET 6 Windows Desktop Runtime x64**。[BepInEx 构建](https://builds.bepinex.dev/projects/bepinex_be) · [.NET 桌面运行时](https://dotnet.microsoft.com/download/dotnet/6.0)
2. 从下文构建得到的 `out\Release` 中，将 `BepInEx` 和 `gakumas-local` 两个目录合并到已配置好 BepInEx 的游戏目录。旧汉化的 `version.dll` 与 RE 会处理相同的游戏方法。切换时先在游戏关闭的状态下备份并停用旧汉化入口，保留 `gakumas-local\local-files` 和 `gakumas-local\texture2d`。
3. 打开 `gakumas-local\re\Gkms.Manager.exe`，勾选功能并保存。第一次使用时所有插件默认关闭。勾选一次「文本汉化」即可启用整套汉化，取消勾选即可全部停用。
4. 选择在下一次游戏加载宿主时生效。框架启动后即可按 Ctrl+U 打开管理器，Unity 日志会自动转发到 BepInEx。即使所有可选插件均关闭，这两项功能也会运行。BepInEx 日志会按插件 ID 记录加载结果。资源缺失、API 版本不兼容或启动失败的插件会给出单独的诊断。

管理器也支持命令行。以下示例在部署后的 `gakumas-local\re` 目录中用 PowerShell 7 执行，各命令可单独使用。

```powershell
.\Gkms.Manager.exe list
.\Gkms.Manager.exe enable localization
.\Gkms.Manager.exe disable localization
.\Gkms.Manager.exe import-legacy
```

`import-legacy` 会读取上一级的 `localizationConfig.json`，将已有的功能选择、帧率、画质、旋转方向、陀螺仪和骨骼参数写入新配置。日后可以在管理器中继续修改选择。用户安装的其他插件和自定义参数会保留。旧 DMM 配置中启用了 `loginAsIOS` 时，导入会开启充值保护。升级 RE 时，原先启用了 `localization`、`master`、`lyrics` 或更早的任一文本插件，就会启用 `localization`。原先全部停用的用户仍保持停用。保存配置时会移除旧分项开关。旧配置中启用了 `capture` 或 `texture-capture` 就会启用合并后的素材采集。`settings-panel` 和 `unity-log` 的旧选择也会自动迁出插件列表。

## 13 个可选插件

| 插件 ID | 功能及数据来源 |
| --- | --- |
| `localization` | 使用同一文本资源包翻译界面、通用文本、剧情和主数据，加载中文回退字体，随主页语音显示字幕气泡，并在进入 Live 时默认显示歌词。游戏内仍可手动收起歌词。插件内置文本资源更新。 |
| `textures` | 用 `texture2d` 下的 PNG 替换 CanvasRenderer 界面贴图，支持嵌套目录和 sactx 图集名称。插件内置贴图包更新。 |
| `capture` | 同一个开关采集界面键与通用原文到 `dump-files\observed.jsonl`，并将界面使用的贴图以 PNG 导出到 `dump-files\texture2d`。 |
| `frame-rate` | 设置游戏及 Unity 的目标帧率，参数为 `fps`。 |
| `graphics` | 设置渲染比例、缓冲区像素、质量、体积效果与反射、LOD 档位。 |
| `window` | 支持窗口边框缩放和横竖屏选择，参数为 `resizable` 和 `orientation`。 |
| `camera` | F8 切换摄影机。`mode` 可选 `free`、`follow` 或 `first-person`。滚轮调整视野，镜头启用时关闭景深。自由模式用 WASD 移动、Q/E 升降、鼠标右键转动。跟随和第一人称支持角色序号 `actorIndex` 和位置偏移 `offset`。第一人称默认隐藏当前角色的脸和头发，离开时恢复，可用 `hideHead` 关闭。 |
| `gyro` | 全景与摄影界面使用鼠标右键或小键盘 4/6、2/8 控制姿态，小键盘 5 归中。 |
| `physics` | 设置角色骨骼的阻尼、刚度、弹力、摆幅、权重、手臂修正、缩放以及 X/Y/Z 轴双向限位。 |
| `schedule-sp` | 在培育日程中保留 SP 课程标记。 |
| `card-cut-in` | 卡牌未指定皮肤时，查询账号已持有的卡牌皮肤，选用演出等级最高的一款。保留手动指定的皮肤。 |
| `auto-photo` | 配置 Live 自动摄影的三个张数档位，默认 10、20、30。 |
| `purchase-guard` | 启用后取消游戏内充值预检，阻止进入购买流程。 |

`gakumas-local\re\profile.json` 同时存储选择和插件参数。文本插件读取 `local-files` 下的界面词典、剧情文件、字体和 `masterTrans` 主数据表。Live 歌词译文和主页语音字幕随 `genericTrans` 同包更新。以下配置启用整套文本汉化，并把目标帧率设为 120。

```json
{
  "enabled": ["localization", "frame-rate"],
  "settings": {
    "frame-rate": { "fps": 120 }
  }
}
```

### 更新文本包或贴图包

在管理器中选中「文本汉化」或「界面贴图替换」，点击「更新资源…」。可浏览本地 ZIP、输入 ZIP 网址或发布 API 地址，也可以留空使用该插件的默认更新源。更新可在插件首次启用前完成。游戏在下次加载插件时读取新资源。

两个插件分别从 `gakumas-local\config.json` 读取 `useAPIAssetsURL` 和 `useAPITextureAssetsURL`，没有配置时使用旧版的对应默认源。更新先解压到暂存目录，再替换 `local-files` 或 `texture2d`。上一份资源保存在 `re\local-files.previous` 或 `re\texture2d.previous`，更新文本包时会保留包外已有的本地资源和字体。

```powershell
.\Gkms.Manager.exe update localization 'D:\Downloads\GakumasTranslationData.zip'
.\Gkms.Manager.exe update textures 'D:\Downloads\texture2d.zip'
```

## 框架与插件开发

插件生命周期和由宿主提供服务的模式参考了 Dalamud 的 `IDalamudPlugin` 与 `IDalamudPluginInterface`。DMM 接入采用 BepInEx 和 Il2CppInterop，游戏初始化完成后由 HarmonyX 注册所选模块的回调。[Dalamud 插件接口](https://github.com/goatcorp/Dalamud/tree/master/Dalamud/Plugin) · [BepInEx](https://github.com/BepInEx/BepInEx) · [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)

- `Sdk` 提供 `ILocalifyPlugin` 和 `IPluginContext`。插件可取得数据目录、自己的配置、日志以及方法解析和回调注册服务。
- `Host` 只读每个包的 `plugin.json` 进行发现。它按选择顺序用独立的程序集加载上下文加载 DLL，共享 SDK 类型。启动部分失败时会移除该插件已经安装的回调，再继续加载下一个。宿主结束时按逆序清理插件。加载上下文用于管理依赖，安装的 DLL 与游戏具有相同的进程权限。
- `Bridge` 提供 DMM 进程入口和 Unity IL2CPP 服务。`Manager` 读取相同的插件清单，支持选择、保存、旧配置导入和调用插件清单中声明的 `IResourceUpdater` 更新入口。
- `Plugins\FrameRate` 是一个完整的小型插件示例。新插件引用 `Gkms.Sdk`、实现 `Start` 和 `Dispose`、提供清单即可发布。将清单、插件 DLL 和私有依赖放在 `re\plugins` 下同一个独立子目录，管理器下次打开时即可发现。添加插件无需修改宿主。

```json
{
  "id": "frame-rate",
  "name": "帧率设置",
  "apiVersion": 1,
  "assembly": "Gkms.FrameRate.dll",
  "entryPoint": "Gkms.Plugins.FrameRate.Plugin"
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

可将已有 `gakumas-local` 的绝对路径作为上述 `dotnet` 参数数组的第四项，额外读取真实词典、字幕和主数据。测试在独立夹具中验证 DLL 加载、真实 Harmony 回调、插件启停与部分加载失败回滚、共享文本包更新及旧配置迁移。用替身模拟语音播放与页面生命周期、第一人称部件恢复、骨骼重复初始化和充值取消。DMM 内的字体渲染、字幕时机、场景切换、镜头和硬件输入仍需游戏内验证。

当前发行包提供独立的充值保护。旧版的在线 iOS 平台身份模拟、绕过账号持有条件的全曲和服装解锁，以及强制服装均未收录。

本项目沿用原 DMM 汉化项目的资源约定与部分功能实现思路，遵循 GPL-3.0。对照的本地旧版提交为 `914f6a9`。[原 DMM 汉化仓库](https://git.chinosk6.cn/chinosk/gkms-localify-dmm)
