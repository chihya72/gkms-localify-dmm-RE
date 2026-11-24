## 下载

- `Gkms-RE-2.0.4-win-x64.zip`：Windows x64 框架，解压到游戏目录。
- `Gkms.Sdk.2.0.4.nupkg`：第三方插件开发 SDK，包含入门说明、接口规范和示例；API 等级仍为 2。

## 本次更新

- 更新插件管理器的默认订阅地址，并自动迁移旧的默认地址。
- 迁移时保留自定义来源、停用状态及已安装插件的来源归属。
- 延续 2.0.3 的游戏内 Ctrl+U 管理面板、面板外游戏点击、输入法及日志等级筛选功能。

## 升级

关闭游戏后覆盖框架文件，保留已有配置、插件和资源。更早版本遗留的 `Gkms.Manager.exe`、`Gkms.Manager.dll`、`Gkms.Manager.deps.json` 和 `Gkms.Manager.runtimeconfig.json` 可删除。

[安装说明](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.4/README.md) · [插件开发入门](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.4/PLUGIN_GETTING_STARTED.md)

已通过离线兼容性测试、DX11 面板和输入回归，以及订阅下载与安装验证；不代表完整游戏内实测。
