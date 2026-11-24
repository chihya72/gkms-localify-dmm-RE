# Gakumas Localify RE

学园偶像大师 DMM 版插件框架。按 **Ctrl+U** 打开游戏内面板，安装、配置和更新插件。

[下载框架](https://github.com/chihya72/gkms-localify-dmm-RE/releases/latest) · [官方插件库](https://github.com/chihya72/gkms-localify-dmm-RE-official-plugins) · [插件开发入门](PLUGIN_GETTING_STARTED.md)

本仓库发布框架和 SDK。插件通过各自的订阅源独立安装、更新和发布，不随框架 Release 打包，也不要求使用相同版本号。

## 安装

1. 准备 Windows x64、DX11 和 [.NET x64 Runtime](https://dotnet.microsoft.com/download/dotnet)（6 或更新版本）。
2. 关闭游戏，解压 Release 中的 `Gkms-RE-*-win-x64.zip`，将 `version.dll` 和 `gakumas-local` 放到 `gakumas.exe` 同级目录。升级时保留已有配置和插件资源；旧 BepInEx 入口 `winhttp.dll` 需先停用。
3. 自行启动游戏，按 **Ctrl+U**，在「插件库」安装需要的插件，再到「已安装」启用。插件默认停用，标注「需要重启」的更改在下次启动生效。

从旧版升级时，可在游戏关闭后删除 `gakumas-local\re` 下遗留的 `Gkms.Manager.exe`、`Gkms.Manager.dll`、`Gkms.Manager.deps.json` 和 `Gkms.Manager.runtimeconfig.json`；插件管理统一在游戏内完成。

框架内置字体、面板和日志，无需另外安装插件。插件的功能说明、参数和资源下载方式见对应插件的文档。

面板打开时，点击面板外的游戏画面仍可操作游戏；面板控件和拖动操作不会穿透到游戏。

## 插件安装与订阅

新安装已内置官方订阅源，打开「插件库」即可浏览。官方插件列表、订阅地址及插件更新说明统一维护在[官方插件库](https://github.com/chihya72/gkms-localify-dmm-RE-official-plugins#订阅安装)。

可在「订阅源」添加第三方来源或恢复默认官方源，也可安装本地插件 ZIP。在线来源使用 HTTPS；本机测试支持 localhost HTTP。框架会自动迁移旧的默认订阅地址，保留自定义来源、停用状态和已安装插件的来源归属。

「输出日志」可按插件／来源、日志级别和关键词组合筛选，并复制筛选结果。日志文件位于 `gakumas-local\re\loader.log` 和 `runtime.log`。

## 开发与构建

- [第三方插件入门](PLUGIN_GETTING_STARTED.md)：SDK、示例、打包和发布订阅源。
- [接口与插件规范](PLUGIN_DEVELOPMENT.md)：API 2、生命周期、配置及资源服务。
- [验证记录](AUDIT.md)：离线证据及尚需游戏内验证的项目。

SDK NuGet 包随框架 Release 发布。源码构建需要 PowerShell 7、.NET SDK 8 或以上、Visual Studio C++ x64 工具和 Windows SDK：

```powershell
& (Join-Path $PWD 'Pack-Release.ps1')
if (-not $? -or $LASTEXITCODE -ne 0) { throw '打包失败' }
```

框架 ZIP 输出到 `out\Release`，SDK 输出到 `out\sdk`。`Pack-Release.ps1` 仅构建框架和 SDK，不需要检出插件仓库。

Actions 在提交和 PR 时构建框架，并使用独立插件仓库执行兼容性测试；测试产物不混入框架 ZIP，也不会触发插件仓库发布。离线测试通过不代表完整游戏内验证。

## 框架 Release

Release 附件仅包含 `Gkms-RE-<版本>-win-x64.zip` 和 `Gkms.Sdk.<版本>.nupkg`。更新内容仅记录框架、SDK、插件管理器及框架升级事项；插件功能更新由插件仓库维护。

发布前更新 `Directory.Build.props`、`Sdk\Gkms.Sdk.csproj` 中的版本，以及源码根目录 [RELEASE_NOTES.md](https://github.com/chihya72/gkms-localify-dmm-RE/blob/main/RELEASE_NOTES.md) 中的本次说明、附件名称和版本链接，再推送与版本一致的 `vX.Y.Z` 标签。Actions 在测试通过后读取该标签中的说明发布 Release，不再使用自动生成的提交汇总。README 描述当前源码能力；已发布版本的功能以对应标签和 Release 说明为准。

沿用[原 DMM 汉化项目](https://git.chinosk6.cn/chinosk/gkms-localify-dmm)的部分实现与资源约定，遵循 [GPL-3.0](LICENSE)。
