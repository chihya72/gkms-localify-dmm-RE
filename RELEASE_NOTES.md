## 下载

- `Gkms-RE-2.0.5-win-x64.zip`：Windows x64 框架，解压到游戏目录。
- `Gkms.Sdk.2.0.5.nupkg`：第三方插件开发 SDK，包含入门说明、接口规范和示例；API 等级仍为 2，程序集版本保持 `2.0.0.0`。

## 本次更新

- 输出日志新增插件／来源筛选，可与日志等级、关键词组合使用，并复制筛选结果。
- 兼容相机插件从 `camera` 更名为 `camera-control`：迁移启用状态和旧参数，已有新参数优先，并识别退役的旧官方插件；旧 DMM 配置导入也使用当前插件 ID。
- 调整文本、贴图等插件的资源设置分组：“保存配置／重新读取”紧跟默认更新源，分界线放在按钮下方，将保存的配置与“本次来源（不保存）”分开。
- 移除资源更新区域重复的“重载资源”按钮，保留插件详情上方的资源重载入口。
- 补充配置迁移、插件设置界面、资源来源及官方插件兼容性回归；发布说明与项目 README 分开维护。

官方插件仍通过订阅独立更新。iOS 登录、文本采集、贴图候选回退和窗口行为的修复由对应插件提供，不包含在框架 ZIP 内。

## 升级

从 v2.0.4 升级时，关闭游戏后覆盖框架文件，保留已有配置、插件和资源。插件 API 保持为 2，现有兼容插件无需因本次框架升级重新编译。更早版本遗留的 `Gkms.Manager.exe`、`Gkms.Manager.dll`、`Gkms.Manager.deps.json` 和 `Gkms.Manager.runtimeconfig.json` 可删除。

[安装说明](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.5/README.md) · [插件开发入门](https://github.com/chihya72/gkms-localify-dmm-RE/blob/v2.0.5/PLUGIN_GETTING_STARTED.md)

已通过离线兼容性测试、DX11 面板和输入回归，以及订阅下载与安装验证；不代表完整游戏内实测。
