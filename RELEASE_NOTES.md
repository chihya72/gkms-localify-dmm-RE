## 下载

- `Gkms-RE-2.0.7-win-x64.zip`：Windows x64 框架，解压到游戏目录。
- `Gkms.Sdk.2.0.7.nupkg`：第三方插件开发 SDK，包含入门说明、接口规范和示例；API 等级仍为 2，程序集版本保持 `2.0.0.0`。

## 本次更新

- 参数保存与运行中应用分开处理。单个字段可声明 `requiresRestart: true`；同一次保存中，其余可更新参数立即应用，延后字段保留当前值并显示“待重启”。撤销延后变更或下次启动后清除该状态；实际应用异常仍显示“失败”。
- 移除详情中的“参数热更新”展示行，保留底层 `ISettingsReloadable` 接口及 `canReloadSettings` 声明；插件热启停和资源热重载仍单独判断。
- 通用配置页统一使用“卸载插件”右侧的“保存配置”，同时保存普通参数与资源设置；移除分区内重复的保存和重新读取按钮。自定义配置页继续管理自己的保存入口。
- 默认更新源的标签置于输入框上方，输入框填满可用宽度；“本次来源”的浏览按钮与其右边缘对齐。
- SDK 增加 `PluginSetting.RequiresRestart`、`JsonElement.Option<T>()` 和 `SchemaSettingsUi.SaveSettings()`，同步配置文档及示例版本。
- 补充混合参数保存、撤销待重启、重新启动采用保存值、原地更新及统一保存入口的回归测试。

官方插件仍通过订阅独立更新，不包含在框架 ZIP 内。

## 升级

从 v2.0.6 升级时，关闭游戏后覆盖框架文件，保留已有配置、插件和资源。插件 API 保持为 2，现有兼容插件无需因本次框架升级重新编译。使用 SDK 2.0.7 新方法、字段级延后生效或统一保存入口的插件须配套框架 2.0.7 或更新版本，不能只更新插件 DLL。更早版本遗留的 `Gkms.Manager.exe`、`Gkms.Manager.dll`、`Gkms.Manager.deps.json` 和 `Gkms.Manager.runtimeconfig.json` 可删除。

[安装说明](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.7/README.md) · [插件开发入门](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.7/PLUGIN_GETTING_STARTED.md)

验证范围为 Release 构建、14 个官方插件的离线联合回归、SDK 配置与生命周期测试以及独立 DX11 面板交互；不启动游戏，不代表完整游戏内实测。
