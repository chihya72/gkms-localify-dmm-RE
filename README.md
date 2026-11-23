# Gakumas Localify RE

学园偶像大师 DMM 版插件框架。按 **Ctrl+U** 打开游戏内面板，安装、配置和更新插件。

[下载框架](https://github.com/chihya72/gkms-localify-dmm-RE/releases/latest) · [官方 14 个插件](https://github.com/chihya72/gkms-localify-dmm-RE-official-plugins) · [插件开发入门](PLUGIN_GETTING_STARTED.md)

## 安装

1. 准备 Windows x64、DX11 和 [.NET x64 Runtime](https://dotnet.microsoft.com/download/dotnet)（6 或更新版本）。
2. 关闭游戏，解压 Release 中的 `Gkms-RE-*-win-x64.zip`，将 `version.dll` 和 `gakumas-local` 放到 `gakumas.exe` 同级目录。升级时保留已有配置和插件资源；旧 BepInEx 入口 `winhttp.dll` 需先停用。
3. 自行启动游戏，按 **Ctrl+U**，在「插件库」安装需要的插件，再到「已安装」启用。插件默认停用，标注「需要重启」的更改在下次启动生效。

从旧版升级时，可在游戏关闭后删除 `gakumas-local\re` 下遗留的 `Gkms.Manager.exe`、`Gkms.Manager.dll`、`Gkms.Manager.deps.json` 和 `Gkms.Manager.runtimeconfig.json`；插件管理统一在游戏内完成。

文本和贴图资源在对应插件的「资源更新」中下载。框架内置字体、面板和日志，无需另外安装插件。

面板打开时，点击面板外的游戏画面仍可操作游戏；面板控件和拖动操作不会穿透到游戏。

## 官方订阅源

新安装已内置以下地址，打开「插件库」即可获取官方 14 个插件：

```text
https://github.com/chihya72/gkms-localify-dmm-RE-official-plugins/releases/latest/download/pluginmaster.json
```

旧配置会保留原地址与停用状态；需要切换时，在「订阅源」点击「恢复默认官方源」。第三方来源可在同一页面添加。

离线使用：从[官方插件 Release](https://github.com/chihya72/gkms-localify-dmm-RE-official-plugins/releases/latest) 下载 `official-plugins.zip`，放入 `gakumas-local\re`，在「订阅源」清空官方地址后保存。

## 开发与构建

- [第三方插件入门](PLUGIN_GETTING_STARTED.md)：SDK、示例、打包和发布订阅源。
- [接口与插件规范](PLUGIN_DEVELOPMENT.md)：API 2、生命周期、配置及资源服务。
- [验证记录](AUDIT.md)：离线证据及尚需游戏内验证的项目。

SDK NuGet 包随框架 Release 发布。源码构建需要 PowerShell 7、.NET SDK 8 或以上、Visual Studio C++ x64 工具和 Windows SDK：

```powershell
& (Join-Path $PWD 'Pack-Release.ps1')
if (-not $? -or $LASTEXITCODE -ne 0) { throw '打包失败' }
```

框架 ZIP 输出到 `out\Release`，SDK 输出到 `out\sdk`。Actions 在提交和 PR 时构建并运行 14 插件联合测试；推送与版本一致的 `vX.Y.Z` 标签后，测试通过才发布 Release。

日志位于 `gakumas-local\re\loader.log` 和 `runtime.log`。离线测试通过不代表完整游戏内验证。

沿用[原 DMM 汉化项目](https://git.chinosk6.cn/chinosk/gkms-localify-dmm)的部分实现与资源约定，遵循 [GPL-3.0](LICENSE)。
