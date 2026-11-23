# DMM 插件框架与内置插件游戏内验收方案

本套验收回答三个问题：原 DMM 插件的有效功能是否完整移植、RE 在真实游戏中是否正确工作、启停和异常操作是否造成崩溃、数据污染或安全问题。测试对象是 `D:\Games\gakumas\gakumas.exe` 中实际加载的框架与 14 个插件；不以 CLR 替身、独立覆盖层程序或离线测试通过作为实机证据。

详细步骤见 [游戏内测试用例](IN_GAME_TEST_CASES.md)。本次交付是用例设计与源码差异梳理，所有实机用例初始状态为“未执行”。2026 年 11 月 21 日编写时未检测到游戏进程，未启动游戏、安装补丁、改变游戏配置或运行故障注入。

## 验收对象与对照方法

原版对照目录：`D:\chinosk6\gkms-localify-dmm`，当前提交 `914f6a9`，检查时工作区无修改。RE 目录：`D:\chinosk6\gkms-localify-dmm-RE`，现有 `main` 工作区有未提交修改，因此必须记录实际部署包版本；不能仅凭提交号认定源码与游戏中的 DLL 一致。

每项移植验收使用同一游戏版本、账号条件、角色、服装、场景、分辨率和同一份资源内容。依次记录以下独立会话，绝不能在同一游戏进程叠加原版与 RE 入口：

| 会话 | 用途 | 操作原则 |
| --- | --- | --- |
| 游戏原生 | 确认原画面、游戏原设置、真实持有与余额 | 游戏退出后由操作者选择无汉化入口的环境，再手动启动；RE 全关仍有框架 Hook，不能当作无框架环境 |
| 原 DMM | 确认原版有效行为、按键及参数语义 | 使用上述原版的可追溯构建，记录对应开关和资源；先完成正常登录与相关功能复现 |
| RE 全关 | 验证框架自身副作用和面板 | 14 个可选插件关闭，保留原生加载、状态观察、日志和面板 |
| RE 单插件 | 验证每个插件的功能与恢复 | 只开目标插件；只有用例明确要求时才加入依赖或组合 |
| RE 组合 | 验证共享 Hook、输入、资源和生命周期冲突 | 按用例指定次序启用/停用，最后运行全插件场景路径 |

无法运行当前客户端的原版功能，记为“原版对照阻塞”，不能把 RE 与自身对比当作移植通过。服务器活动、缓存预热、网络波动造成的不可比会话需要单列。旧 `AUDIT.md` 中的通过记录只用于选取回归重点，不自动填入本轮结果。

每个功能从“入口、触发条件、参数、实际结果、副作用、离开场景、停用恢复”七方面比较。一个插件有一项不满足，报告该插件“部分通过”，不能用插件数量替代功能覆盖率。

## 原版功能到 RE 的追踪

下面的文件路径以各自仓库为根。原版 `src\GakumasLocalify\Hook.cpp` 中的 `ADD_HOOK` 必须结合注释块和 Windows 条件编译判断；只有函数定义不能证明功能已接入。源码映射仅证明存在实现，不等于实际游戏命中。

| 原版有效功能与入口 | RE 对应实现 | 必测用例组 |
| --- | --- | --- |
| `I18nHelper_SetValue`、TMP Awake/set_text/SetText/切片/字符数组、旧 UI.Text、UIElements.TextField；`Local.cpp` 词典优先级和诊断标记 | `Plugins\Localization` 的 KeyedText、GenericText、Dictionary；`Sdk\DmmTextDictionary.cs`、DmmStringFormat、DmmTextRules | LOC-01～LOC-10、LOC-25～LOC-28 |
| Octo 文本资源加载、剧情布局；完整 `MessageExtensions_MergeFrom` 后的 Master 翻译 | StoryText、StoryDictionary、DialogueLayout、MasterText、MasterTable | LOC-11～LOC-17 |
| TMP 字体与回退、`HomeVoiceSubtitle.hpp` 的主页字幕、Live 歌词文字 | FallbackFont、VoiceSubtitles、DialogueBubble、SubtitleConfig；歌词由通用词典处理 | LOC-18～LOC-24、LOC-29～LOC-34 |
| `CanvasRenderer_SetTexture`、`HookTexture.cpp` 的分类/递归/sactx 命名和原地 LoadImage | `Plugins\Textures`、`Sdk\TextureNames.cs` | TEX-01～TEX-12 |
| `Local.cpp` 文本采集与小键盘分组、HookTexture PNG 导出 | `Plugins\Capture` 的 TextCapture、TextureCapture | CAP-01～CAP-10 |
| Unity targetFrameRate、CampusQualityManager TargetFrameRate 及 ApplySetting 中的限帧 | `Plugins\FrameRate` | FPS-01～FPS-05、COM-02 |
| ApplySetting、LOD/反射、GetLimitedResolution；原 GUI 五档画质快捷参数 | `Plugins\Graphics`；清单提供六项参数，未声明五档预设按钮 | GFX-01～GFX-07、MIG-04 |
| InternalSetOrientationAsync、WindowManager 方向/尺寸、NudgeWindow、SetWindowLong；`windowsPlatform.cpp` F11、最大化、边缘命中测试 | `Plugins\Window`；需单独检查原版 Win32 窗口交互是否遗漏 | WIN-01～WIN-08 |
| Cinemachine 状态、四个 Transform 注入入口、FOV、景深、渲染回调、Actor 骨骼；`camera\camera.cpp` 的键盘控制 | `Plugins\Camera` 的 Plugin、CameraMath、HeadVisibility | CAM-01～CAM-15 |
| 五个陀螺仪 icall 缓存、控制器初始化、全景/摄影入口；`GyroEmu.cpp` 输入与相对姿态 | `Plugins\Gyro`、`Bridge\NativeIcallCache.cs` | GYR-01～GYR-09 |
| CampusActorAnimation.Setup；七个物理参数、手臂、缩放、六个限位值；GUI 原预设 | `Plugins\Physics` 的 Plugin、BoneLimits、plugin.json | PHY-01～PHY-08 |
| `dbgMode` 下 ExchangeSpToNormal 保留输入类型 | `Plugins\ScheduleSp` 独立开关 | SP-01～SP-03 |
| `dbgMode` 下 ExamCardData.ProduceCardSkinId 选择最高 CutInType，手工皮肤优先 | `Plugins\CardCutIn` 独立开关 | CUT-01～CUT-05 |
| `dbgMode` 下 AutoPhotoCountTypeValues 和 GetAutoPhotoLimitCount | `Plugins\AutoPhoto`，增加自定义 counts | PHOTO-01～PHOTO-05 |
| AuthLoginRequest、完整认证响应、ApiBase 构造/实例/平台字符串、平台标志；PurchaseGuard 购买检查 | `Plugins\PurchaseGuard`，ID 为 `ios-login`；PlatformLogin 增加双向余额刷新 | IOS-01～IOS-11 |
| 持有判定、Live 缩略图、名片作用域、服装 GetAll/FindBy、摄影服装/头饰、首次默认歌词 | `Plugins\LiveUnlock`，live/costumes 两个子开关 | LIVE-01～LIVE-10 |
| 原 GUI、配置保存/重载、资源 API/ZIP 更新、Unity 日志、入口初始化 | `Bridge`、`Host`、`Manager`、`Tools\Resources`；框架内置面板和日志 | FW、PKG、RES、MIG |

### 需要分别判定的配置与交互差异

| 差异 | 判定方式 |
| --- | --- |
| 原 `replaceFont`、`useMasterTrans` 等独立选择并入 localization 总开关 | 必须记录为配置能力合并。LOC 与 MIG 同时验证；不能因总开关可用而写“所有原选项原样移植” |
| 原 `dbgMode` 密码键序列控制 SP、卡牌、自动摄影、Live 解锁等，RE 使用独立插件开关 | 验证原 debug 开/关导入及各插件独立选择。独立化属于行为设计变化，不能默认为旧快捷键仍可用 |
| 原 `lazyInit` 初始化策略和进度界面未按原样提供 | 验证新的 version.dll/hostfxr 入口首次加载、慢加载、失败诊断；记录启动交互变化 |
| 原 GUI 的五档画质快捷按钮、贴图下载后删除压缩包选项 | 参数可表达相同结果与按钮/文件保留能力是两件事；MIG-04、RES-09 分别记录 |
| RE 新增热启用、恢复原对象、安装卸载、订阅升级、独立资源目录、iOS 即时余额刷新 | 不能以原版没有该功能免测；这些是 RE 自身正确性与数据完整性测试 |

原版已注释的 AssetBundle/Resources/Sprite/Image 额外贴图 Hook、跳过 Live Loading、强制服装 ID、SQLite 主表路径、Windows 鼠标自由镜头与未接入手柄回调，不计入有效移植功能。`forceExportResource` 仅有配置声明而无有效功能入口；不得据此虚构验收。纯透传 Hook 可用真实场景非回归验证，无须要求 RE 安装同名空 Hook。

## 源码初查发现与实机复现优先级

以下不是已完成的游戏测试结果。差异与风险需要保留到验收报告中，不能预先写成通过，也不在本次用例任务中修改业务代码。

| 编号 | 观察与证据 | 实机判定 |
| --- | --- | --- |
| R-01 移植缺口候选 | 原 `src\windowsPlatform.cpp` 的 WndProcCallback 显式处理 F11、SC_MAXIMIZE、WM_NCHITTEST、WM_GETMINMAXINFO；RE `Plugins\Window\Plugin.cs` 只加入窗口样式并拦截游戏窗口方法，`Bridge\NativeOverlay.cpp` 没有对应全屏处理 | WIN-03～WIN-05。若游戏自身恰好提供部分功能，仍逐项核对全屏模式、尺寸恢复和边界行为；当前不能认定窗口功能完整移植 |
| R-02 输入穿透风险 | RE `Plugins\Gyro\Plugin.cs` 的 Held/Pointer 直接使用 GetAsyncKeyState/GetCursorPos，Sample 未检查前台窗口或面板打开状态；NativeOverlay 的消息拦截不能单独证明异步键状态被屏蔽 | GYR-06、COM-01：打开面板右键拖动、失焦后在其他窗口拖动，观察镜头是否变化 |
| R-03 镜头状态残留风险 | Camera.Rendered 在第一人称写 nearClipPlane=0.001，切回其他模式没有显式在该分支恢复；R 重置参数但仍须验证近裁剪、隐藏头部、景深与 FOV 的联动 | CAM-10～CAM-12；逐帧比较近物裁剪与退出状态。尚未实机复现 |
| R-04 无目标与数值边界风险 | Camera.Track 跳过不匹配 actorIndex，跟随 Pose 使用已保存目标；Step 累加 FOV 未见范围限制 | CAM-09、CAM-14：角色离场/无效索引不应继续跟随已销毁对象；长按 Q/E 应检查画面与日志。原版若同样异常，分别记录“移植一致”和“缺陷存在” |
| R-05 资源耗尽风险 | `Tools\Resources\ArchiveUpdate.cs` 下载流循环未见总字节上限；PackInstaller.Install 解压资源未见总展开量/条目数上限。插件 ZIP 安装的 512 MiB 限制不能替资源包证明安全 | SEC-04：仅使用受控大小、可恢复的测试资源，记录增长和取消行为；不要把磁盘填满来证明问题 |
| R-06 资源有效性与发布状态风险 | PackInstaller 对文本主要检查 localization.json，嵌套主表/字幕的验证在插件重载阶段；PNG 被计数不代表已成功解码；安装成功与运行重载失败可能不同步 | RES-06、TEX-08、SEC-06：区分磁盘安装版本、实际生效版本与重启后状态 |
| R-07 网络来源边界差异 | 插件订阅源 NormalizeUrl 要求 HTTPS（回环 HTTP 除外）；资源 ArchiveUpdate 接受 HTTP/HTTPS，且使用 HttpClient 默认重定向行为 | SEC-07：在受控服务验证 URL、跳转及凭据显示；明文资源兼容行为列为威胁面，不凭此直接宣称远程代码执行漏洞 |
| R-08 本地控制可用性风险 | RuntimeControl 每次服务一个管道连接，ReadLineAsync 有 15 秒超时但未见行长度上限；CurrentUserOnly 已限制用户边界 | SEC-08：受控空闲连接、有限长错误行后检查状态查询恢复；同用户控制能力本身不是权限提升 |

## 测试准备与证据

由操作者手动启动游戏。测试期间不自动启动或重启游戏，不覆盖正在使用的入口 DLL。涉及安装不同入口、删除资源、只读权限、磁盘限额、异常中断或实验插件的用例，先安排可恢复测试环境和可还原副本；本套用例不授权直接对现有账号数据做破坏性操作。

记录游戏版本、框架/各插件清单版本、实际包来源与部署时间、Windows/显卡驱动、DX11、屏幕/DPI/刷新率、已有注入插件、所用资源版本、启用项和完整参数。当前游戏根目录可见 `d3d11.dll` 等其他图形文件；不能仅凭文件名判断它是否加载或导致冲突，应在 COM-08 中记录真实加载情况。

准备的数据应来自真实游戏可到达的场景：

| 数据 | 最低要求 |
| --- | --- |
| 文本 | 已翻译/未翻译词条、同文异键、加号卡名、数字格式、富文本、长文本、剧情分支和历史记录、不同类别主表显示页面 |
| 字体与字幕 | 数字专用字体、中文生僻字/日文/符号、两名主页角色、两条连续语音、缺少字幕的语音、可重载的字幕资源 |
| 贴图 | image/atlas/others 各一张真实可见图；共享引用、透明图、非可读或压缩图；A/B 两套有可辨识差异的合法 PNG |
| 角色场景 | 单人、多人、换装/换发型、已有角色与新加载角色、普通 Live/全景/摄影、可反复进入退出的场景 |
| 解锁与卡牌 | 已持有、未持有但已发布、未发布/不可用资源三类；有/无手工皮肤、存在多个演出等级的卡牌；不得伪造服务端库存 |
| 账号与网络 | 若要判定平台余额，需有已确认且可区分的 DMM/iOS 余额与 WebStore 余额；不能通过付款来制造测试差异。缺少该条件时记录阻塞 |
| 更新素材 | 正常包 A/B、损坏包、ID/版本/API 不匹配包、路径异常但仅含无害文件的包；索引与慢响应由操作者控制的服务提供 |

用例中的参数化子项必须分别记录，例如“六个限位方向”“三档拍照”“四种子开关组合”不能只测一项。某个 TMP 重载或主表没有已知可达页面时，记录入口和触发路径缺口；不得使用替身调用补成游戏内通过。若现有日志不能说明入口命中，需要单独安排最小化、可撤销的实机只读诊断，不能凭截图推断底层调用覆盖。

每条结果至少包含：用例 ID/子项、会话与时间、前置数据 ID、实际步骤、配置前后值、实际结果、预期对照、截图/录像、该时间段的 loader/runtime 日志、缺陷号和恢复情况。截图证明画面，日志证明执行路径，配置文件证明持久化；三者用途不能互换。记录网络信息时仅保留平台、请求时间、状态和脱敏余额对照，不收集认证令牌或完整账号凭据。

## 执行顺序

1. **先打通入口**：FW-01～FW-06，IOS-01～IOS-03。启动崩溃或余额/平台异常先保留证据，恢复稳定环境后再继续不相关测试。
2. **先查移植遗漏**：MIG 全组、WIN-03～WIN-05，以及每个插件第一条正常路径，得到原版与 RE 的差异清单。
3. **逐插件实机覆盖**：LOC → TEX → CAP → FPS/GFX/WIN → CAM/GYR/PHY → SP/CUT/PHOTO/LIVE → IOS 余项。每组均完成冷进入、当前场景热启用、改参数、热关闭、再次进入。
4. **框架与更新**：FW 余项、PKG、RES；用真实游戏中的面板触发操作，并确认更新后的功能真实生效。
5. **冲突、异常与长期运行**：COM、SEC、PERF。异常项目分批执行，每次恢复正常后再开始下一项，避免混淆根因。

P0 表示崩溃、错误账号/余额、持久数据破坏或跨目录写入等优先问题；P1 表示核心功能和恢复；P2 表示次要交互、兼容与体验。优先级是执行顺序，不是自动发布门禁。

## 如何给出验收结论

结果只使用“通过、失败、阻塞、未执行、不适用”。“不适用”必须说明当前客户端确实无该功能，并保留依据；缺少账号、资源、触发路径或诊断能力属于“阻塞”。原版自己存在的问题不能自动成为 RE 的合格标准：同时记录功能一致性和缺陷结论。

分别汇总原版有效功能的实机覆盖、RE 新增能力的实机覆盖、配置/交互差异、未复现风险和已复现缺陷。只有完成对应数据与生命周期子项且有实机证据，才能判该项通过；“已启用”“无报错”“能进主页”均不充分。

性能使用同机同场景交替对照会话的原始测量值，不新增冻结基线或自动阈值。记录启动到可交互、页面加载、每帧耗时中位数/P95/P99、显存/私有内存、句柄、线程、磁盘增长和恢复时间。原版与 RE 各至少三次相同路径；冷加载与热加载分开。稳定的差异仍需说明缓存、网络、温度和帧率上限；不把正常高画质成本报成泄漏。

报告示例：“窗口 8 项中 5 通过、2 失败、1 阻塞；F11 和最大化全屏不一致；多屏未测。”不能写“14 个插件都能启用，所以全部完整移植/无 bug/无漏洞”。

## 单次执行记录模板

复制此模板记录每个用例及参数化子项，不覆盖原始用例描述。

```text
用例 ID / 子项：
执行人 / 时间 / 游戏会话：
游戏、框架、插件、资源版本与部署来源：
原版对照条件与结果：
前置场景 / 角色或资源 ID / 插件组合：
参数修改前 → 后：
实际操作：
预期结果：
实际结果：
结论：未执行 / 通过 / 失败 / 阻塞 / 不适用
证据路径与日志时间范围：
缺陷号 / 复现次数 / 影响范围：
恢复步骤与恢复后检查：
```

缺陷记录需区分“移植遗漏、行为差异、功能错误、异常恢复、性能、数据完整性、安全、可观测性”；附最短复现路径和原版同条件结果。修复后的复测包括失败用例、相邻场景和相关组合，不能只看新增日志消失。
