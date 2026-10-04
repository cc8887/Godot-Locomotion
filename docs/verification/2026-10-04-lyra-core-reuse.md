# Lyra locomotion：ALS 资源和共享 Core

本批按用户修订后的目标推进：迁移 Lyra 的运动思路和核心算法，沿用 ALS 人物；武器覆盖手枪和步枪。当前验收以普通 Godot 角色的实际行为为准。URO、全部 UE 内部阶段和所有 Provider 的精确复刻进入后续路线。当前统一口径见 `ROADMAP.md` 顶部，后面的历史记录仍保留其原验证范围。

## 人物和骨架

生产路径继续使用现有 ALS 人物、材质和蒙皮。源动画已经重定向到 ALS，采样和后处理使用 69 个 raw bone、81 个 logical bone，最终向 68 个 skin bone 发布。原 Manny 164 骨只是部分只读 UE 对照夹具的骨架，不能据此替换普通角色骨架或认为两者骨索引一致。

ALS 缺少 Manny 的 `spine_04/05`；骨遮罩、脊柱控制、虚拟骨、武器挂点和双手握持均按 ALS 目标布局处理。虚拟骨在逻辑源采样阶段参与混合。已有资源沿用，本批没有重新导出或格式化资源 JSON。

## Animation Interface 和 Layer

| UE 概念 | 本项目实现 | 所在层 |
| --- | --- | --- |
| 普通 Blueprint Interface | 类型化角色服务、命令与事件消费者 | 玩法和 Godot 适配 |
| Animation Layer Interface | 参数与 Pose 签名、实现兼容性检查、生成调用包装 | Core 合同＋Lyra 元数据 |
| Linked Animation Layer | 调用点路由、实例绑定、同类保留与换类替换 | Core 绑定／执行＋实际图宿主 |
| Layer Group | 每个角色按实现类和组分配有状态实例 | Core 所有权策略 |
| Layered Blend / additive | 目标骨遮罩、空间与 additive 基底合成 | 既有 Core 算子＋图配置 |
| 最终输出 | Main、骨控制、Rig 后向 ALS 蒙皮发布一次 | Godot 骨架适配 |

现有十四个入口有真实图宿主和源遍历。手枪和步枪以装备层覆盖资源与参数，主状态机、角色运动和物理 Montage bank 持续归角色所有。共享资源是不可变数据；播放器、缓存、滤波和过渡历史属于实际角色／图实例。

AnimationTree 可以作为编辑和预览入口；当前复杂的距离匹配、同步、Linked 实例和缓存执行沿用 C# 宿主与 Core 算子。接口签名的生成不能替代图执行，Layer Group 也不能直接当作骨混合遮罩或动画同步组。

## 本批代码

新增五个通用 Core 实现，删除 Godot/Lyra 下原来两份通用历史实现，调用者直接使用 Core 类型：

| Core 文件 | 作用 |
| --- | --- |
| `Animation/AlsAnimationProxyTraversal.cs` | 实际图实例的阶段候选、继承、取消与提交 |
| `Animation/AlsAnimationBoneCacheGate.cs` | 同一实例多个函数根共用骨缓存失效门控，候选故障禁止提交 |
| `Animation/AlsAnimationGraphStartup.cs` | 延迟根初始化和骨缓存入口 |
| `Locomotion/AlsPoseCacheLifecycle.cs` | owner 的初始化、骨缓存、求值和权重历史 |
| `Locomotion/AlsPoseCacheHistory.cs` | ALS 既有求值器和新图宿主共用的缓存历史规则 |

既有 `AlsPoseCacheEvaluation` 已使用同一历史规则；Lyra 的延迟缓存更新继续使用原 `AlsPoseCacheTraversal`。没有另建一套采样、Sync、混合和惯性化基础层。

原生入口对照还定位并修复 Additives 准备顺序：实际 Main 更新应为 Skeletal → Aiming → Additives → LeftHand → Locomotion/Idle。Additives 的 Prepare 和 worker 现位于 Locomotion 源准备之前；所有动画源仍在统一 Source/Sync 批次登记和求解。

Provider 的实际 Update 根先继承 Main 的 Update，再使用自己的骨缓存上下文执行一次失效缓存。隐藏实例保留失效状态；同一实例的后续函数根命中门控。整个角色取消时恢复候选，避免失败或重试污染已提交历史。

## 已复用的 ALS 能力

- Distance Matching 和地面停止／转向距离预测。
- 原序列与曲线采样、BlendSpace 时间／滤波、标记同步和共同 Source bank。
- 精确姿态混合、mesh-space／骨遮罩混合、过渡 stack 和惯性化。
- 缓存延迟遍历、源通知／Montage、骨控制、脚部与最终骨架发布基础。

状态机的图定义和 Lyra 条件仍是特定配置；通用过渡、混合和惯性算法已使用 Core。`LyraMainPoseCacheScope` 的自定义曲线、属性和 root-motion 载荷适配及其它宿主的通用控制部分仍需审计，后续优先扩展既有 Core 框架，不能宣称所有引擎能力已经迁完。

## 验证记录

Core Release 相关测试 **197 通过、0 失败、0 跳过**，其中新增生命周期测试 8 项，包含缓存故障、取消重试、跨 owner 拒绝和阶段同步；既有 ALS 精确缓存、遍历、Linked Layer 和原生 counter 回归一起通过。

Debug 与 ExportRelease 构建均 **0 错误、0 警告**。最终 Debug 15 个、实际 Optimize 14 个 Godot 进程，共 **29 个均退出 0、无 Godot ERROR/WARNING**；Optimize 临时替换的六份 DLL/PDB 按原 SHA256 完整恢复。

| 最终运行范围 | 每构建结果 |
| --- | --- |
| 根骨缓存与 Main 启动 | 根 36 cases / 212088 比较；启动 36 cases / 9024 比较 |
| Main/Provider 缓存、作用域、Slot | 相关三个缓存场景和 Slot 合成通过 |
| 完整 Main Pose，两种反馈模式 | 各 11340 帧，保持真实 owner 与取消重试检查 |
| 最终 Rig | 7560 帧 / 7296 姿态 / 188184 sweeps，三频和三装备 |
| 共同 Sync 与手枪／步枪资源 | 对应三个场景通过 |
| 武器装备、通知与挂点 | 5040 帧 / 4323 姿态 / 36 挂点，取消重试通过 |
| 普通十角色 | 4800 角色提交；玩家 480 次重试、61 空中／72 蹲姿／60 瞄准帧，六次换类／六次同类保留 |
| Debug 渲染场景 | 480 物理帧、7 张 FramePostDraw 图 |

普通十角色的 Debug/Optimize 完整 JSON 报告精确相同。这里记录的是功能／回归结果；没有据此声称性能基准或完整自然调度等价。

Debug 最终渲染七张 post-draw 画面已逐张查看：站立、移动、手枪瞄准、步枪蹲姿、跳跃、落地和反向移动。角色全身与对应装备可见；这次是基础画面抽查，模型受强光影响偏白，尚不能据此关闭近景握持、复杂地形脚部或全部连续观感验收。

Computer Use 启动实际窗口并注入键鼠。独立只读 GDScript 输入探针发现 Q 的逻辑键码为 81、Ctrl 为 4194326，但两者物理键码均为 4194313；项目按物理键绑定，因此 `switch/crouch=false`。右键事件则正确报告 `aim=true`。这是当前自动输入路径的物理键码不匹配证据，不能把窗口键盘检查记为通过，也没有因此修改用户的按键绑定。两次交互进程均正常退出 0；探针与日志位于 `lyra-core-reuse-v2-input-probe.*`，直接启动日志位于 `lyra-core-reuse-v2-keyboard.log`。自动普通场景的动作输入覆盖与硬件键盘验收分别保留。

追加 `lyra-core-reuse-v2-input-logical-probe.*` 只在诊断进程内登记逻辑键别名，不保存项目配置。原 Windows Q 事件此时 `switch=true`，画面 HUD 从 pistol 变为 rifle、实际枪模型随之替换；Ctrl `crouch=true`，步枪角色进入蹲姿。该进程正常退出 0，无 ERROR/WARNING。另观察到物理码正确的 Ctrl 事件，但来源未独立确认，不将其扩大成完整硬件输入验收。受控实验确认实际装备／姿态路径可由原 OS 事件驱动，原项目物理绑定仍需单独验收。

只读 UE 根缓存夹具：36 个角色／布局／绑定时序组合、1668 行；两个独立进程的 request/native/closure 三份 JSON 逐字相同。原函数根额外缓存 342 次，108 次自己的骨缓存 counter 与 Main 不同；没有写入 counter/global frame 或保存资产。生产根调用和直接节点阶段测试分开，直接测试只比较阶段，未实现完整任意直接姿态执行。

当前 Godot 根缓存测试已通过：212088 个比较、684 次实际入口（含取消重试）、216 次与 Main 不同的上下文、180 次生产取消重试、24 次入口故障及 264 次非法操作拒绝。原生 Manny 与运行时 ALS logical81 分别标注，不能把夹具等同于自然场景调度。

## 证据与边界

证据位于 `artifacts/lyra-analysis/lyra-core-reuse-v2-*`。原生捕获位于 `root-bones-native-v1-*` 和 `root-bones-native-v2-*`，新只读探针包位于 `artifacts/unreal/lyra-root-bone-oracle/package-root-bones-v1`。源文件和资产使用独立 SHA256 审计，原失败构建、执行和审计日志保留。

最终审计 `lyra-core-reuse-v2-audit.json` 通过：27 份最终实施／验证／探针源冻结，4583 份其余基线字节保持，原两份 Godot 通用实现已删除；870 份导出 JSON、710 原 UE 包、9 配置与实际安装 UE5.8 的源码副本均保持。初始与最终冻结快照仅审计工具不同，运行实现和测试源不变。当前目标范围不要求把这些原生夹具扩大为 UE5.9 或自然场景全量验收。

首轮 Core Debug 构建失败于对 `ReadOnlySpan` 使用 LINQ，已改为原布局遍历，最终构建通过。根缓存首轮运行发现 Additives/Idle 顺序错误，修复后通过。审计首轮误将“所有 Main/Provider counter 不同”仅计成“Provider 尚未更新”，已按实际运行断言修正；仅审计工具变化，运行实现和测试源码在两份冻结快照之间相同。

本批不关闭全部引擎通用能力迁移或总体 locomotion 验收。URO、自然组件／global-frame／并行调度、全部 Provider、UE/Jolt 全轨迹逐位等价继续在后续路线。音频、道具物理和头颈专项保持暂缓。
