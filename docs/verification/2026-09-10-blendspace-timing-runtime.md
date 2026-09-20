# 第十五批：BlendSpace 原生 Tick 与时间收尾

日期：2026-09-10。归属：完整性恢复计划 A/B 与原 P5A Sync 接线前置工作。

## 修复方向

当前不是完整 ALS 算法已经移植、只剩参数微调。资产、状态图、引擎节点求值、
源时间和事件提交必须形成同一条经过验证的链。此前部分内容只有组件实现，
Demo 仍使用不完整路径。修复顺序保持为：

1. 补 Cycle/Detail/ShouldMove/Stop/Pivot、图内曲线及真实采样时序。
2. 统一 P5 播放身份、组历史、Notify 和姿势事务，失败时一起回滚。
3. 接动态上身分层、手部 IK，再推进 P5B Overlay 与道具。
4. 按原计划继续 P5C、P6、P7；音频暂缓，保留已确认的键鼠行为。

## 原生证据

新增 `AlsBlendSpaceTickCommandlet` 只读加载六个 WalkRun BlendSpace、Lean 和两个
Sprint Sequence，用真实 `UE::Anim::FAnimSync::TickAssetPlayerInstances` 推进。
21 条轨迹共 1,344 帧，保存更新前缓存、更新后样本、归一化时间、Tick delta、
组 Leader、标记位置和原生队列通知。不是完整 AnimBP 或角色运动回放。

所有七个 BlendSpace 的实际标志为：

- `bUseLegacySamplePointAnimationLengthCalculations=true`。
- `bShouldMatchSyncPhases=false`。
- `bAllowMarkerBasedSync=true`，通知策略为 HighestWeightedAnimation。

WalkPose、RunPose 都有标记，并非可以默认停在零时间的单帧求值器。实际样本时间
不仅由片段时长决定，还受组标记及 BlendSpace 内部样本 Leader 影响。这个证据
确认旧策略不等价，但不单独证明所有骨骼轨道随时间改变或唯一解释滑步。

引擎依据：`Animation/BlendSpace.cpp` 的 `TickAssetPlayer`、
`GetAnimationLengthFromSampleData`，以及 `AnimationRuntime.cpp` 的 `AdvanceTime`。
全部读取本机 UE 源码，不以 Godot 结果生成原生期望。

## 实现边界

`src/Als.Core/Sync/AlsBlendSpaceTiming.cs` 扩展现有 `AlsSyncRuntime`：

- 有效长度分别支持旧版加权时长与新版加权归一化速度；实际 ALS 使用旧版。
- 有效长度消费缓存 SamplePlayRate，最终样本时差消费资产倍率乘样本 RateScale。
- 同权重保留缓存顺序；最高标记样本与最高通知样本分别选择。
- 无标记长度模式支持零推进、反向、循环、精确端点和非循环钳制。
  负有效长度明确拒绝推进，不冒充通用新版有符号速度支持。
- 最终样本时间保留归一化映射、标记已解算时间、反向及循环 delta 修正。
- NotifyEligible 只表示应做资产提取，不代表通过队列过滤或已经触发回调。
- 纯值候选、调用方时间和缓冲区；无独立时钟、队列或堆分配，失败不修改输出。

仅支持有效、非镜像、非 SingleFrame 的普通 BlendSpace player 样本。
不支持的 evaluator 行为不能借此入口伪装成普通 player。

特别注意：有标记轨迹测试把原生标记求解后的 PreviousTime/Time 作为输入，
只对照最后收尾计算。它没有验证标记推进本身。无标记 Lean 则连续使用 Core
上一帧自己的时间回放，192 帧验证不借用原生下一帧时间修正漂移。

## 构建记录

按 `ue-diagnosing-plugin-build-load` 技能完成整个项目 Editor target 构建和插件审计。
保留了此前私有 NotifyQueue 访问、非导出 Reset 符号和标记常量拼写错误的失败日志。
最终探针通过公开 PreUpdate/PostUpdate 生命周期提取队列，不分发 gameplay 回调，
不修改引擎或保存 UE 资产。失效标记的未初始化距离仅在 JSON 序列化时规范为零。

引擎构建标识变化后，确认属于项目本地的旧 receipt/manifest/DLL/PDB 已可恢复移动到
`D:/AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260910T080246192Z`。
没有删除或还原源码、配置、资产和用户修改。

最终完整构建/审计日志前缀：
`20260910T081120505Z-07057889a01f45dea7f03abc10177dc7`。
BuildId：`a62acd02-ebd3-4df3-b93a-71842617113c`。
构建状态 fingerprint：`034E6DA48727AA84FEEB1D0C76A7D39A4598B4C8C6896A8EFBC87D3EDF83AFD0`。

本次没有进行 UE GUI 重启、全项目 DataValidation、打包构建或新的可玩 Demo 骨骼
截图验收；构建/命令行探针通过不能替代这些项目级门禁。

## 验证结果

- 两次独立冷启动均退出 0，输出 `ALS_BLENDSPACE_TICK_OK assets=9 traces=21 frames=1344 assets_saved=0`。
- fixture 含 6,492 次 BlendSpace player Tick、16,933 个有效样本、75 条原生队列通知。
  队列通知仅记录为后续移植证据，本批不宣称 Core 已复现队列过滤和跨帧生命周期。
- 两份 JSON 逐字节一致，SHA256：
  `245677E2F3D9CD833780032222F2B6597C391BD84BFC0F09D6712DFABCD82E7C`。
- `AlsBlendSpaceTimingTests`：Debug 15/15，Release 15/15；包含 192 帧 Lean
  自主时钟回放、最终时间窗口对照、端点/反向/多次循环、失败不写输出及 0B 分配。
- Core 回归 1,562/1,562；明确排除了耗时的 `AlsP5aGoldenTests` 和
  `AlsP5aTraceSchemaTests`，未将它们计为本批通过。
- `dotnet build GodotALS.csproj --no-restore`：0 warning、0 error。
- 镜头/输入文件 SHA256 与本批开始前一致，未操作正在运行的 Godot 窗口。

原生日志：`D:/AdvancedLocomotionSystemV/Saved/Logs/AlsBlendSpaceTick-20260910-0814.log`
和 `AlsBlendSpaceTick-20260910-repeat.log`。fixture 位于
`tests/Als.Core.Tests/Fixtures/P3/v4_blendspace_tick_native.json`。

## 未完成

完整组历史、组 Leader 与内部样本 Leader 的切换和标记推进仍需移植；本批两个
资产标志还需加入正式源图导出/编译及绑定 digest，不能只存在测试 fixture 中。
全局 P5 布局、统一事务、Detail/外层起停/Demo 接线和动态分层仍按原计划继续。
本批不宣称起步滑步、交错步或上身观感已经修复。
