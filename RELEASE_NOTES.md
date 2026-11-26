## 下载

- `Gkms-RE-2.0.6-win-x64.zip`：Windows x64 框架，解压到游戏目录。
- `Gkms.Sdk.2.0.6.nupkg`：第三方插件开发 SDK，包含入门说明、接口规范和示例；API 等级仍为 2，程序集版本保持 `2.0.0.0`。

## 本次更新

- 单个插件及“更新全部”在执行更新时重新获取对应订阅源的索引，直接安装新索引声明的版本，避免沿用旧列表而需要再次更新。
- 重新获取索引失败、插件已下架或最新版本的 API 不兼容时，报告失败，不回退安装旧列表中的版本。
- 已安装版本不低于订阅源版本时跳过安装，避免重复安装或降级；更新结果显示实际安装的版本。
- 补充跨版本更新、索引刷新失败、插件下架、API 兼容性变化及用户资源保留的回归测试。

官方插件仍通过订阅独立更新，不包含在框架 ZIP 内。

## 升级

从 v2.0.5 升级时，关闭游戏后覆盖框架文件，保留已有配置、插件和资源。插件 API 保持为 2，现有兼容插件无需因本次框架升级重新编译。更早版本遗留的 `Gkms.Manager.exe`、`Gkms.Manager.dll`、`Gkms.Manager.deps.json` 和 `Gkms.Manager.runtimeconfig.json` 可删除。

[安装说明](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.6/README.md) · [插件开发入门](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.6/PLUGIN_GETTING_STARTED.md)

本次修改已通过 Release 构建与订阅更新回归测试；未进行完整游戏内实测。
