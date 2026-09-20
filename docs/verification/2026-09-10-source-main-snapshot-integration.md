# 第三十批：来源接入 P5 主快照及实际 Cycle

## 结果与边界

上一批 occurrence v3 布局已接到现有 `AlsP5CoreRuntimeBindingCompiler/Snapshot`，
不是新增旁路快照或事件调度器。主绑定 v3 包含来源表、样本身份、Sync 元数据、
按 occurrence 展开的事件定义和既有动作/曲线/过渡绑定。图构建视图升级为 v2，
保留六方向 WalkRun 配置并补齐局部所有来源及其加法基准的资源闭包。

实际 `AlsP3RuntimeContext` 在初始化时编译一次新主快照，各角色复用；Cycle 的 Tick
构造从该主快照读取来源，不再每个角色单独编译源 Profile。当前候选历史除原有完整
SourceStamp 外，再携带主 BindingDigest 和 LayoutDigest，三类失配都拒绝，随既有
Controller/Worker 提交与回滚。实际线程 smoke 验证提交身份来自对应 Context。

这仍不是完整 P5 执行链。旧 `AlsP5Runtime.TryPrepare` 保留只接受 v2 的保护，不能
拿新定义直接运行旧 cursor/winner 规则。实际 Demo 仍只有七个 Cycle 来源，外层
MovingWeight 仍是临时实现；完整 Standing/Detail/Stop、源通知队列与状态事件没有
接通。主快照中有数据，不等于对应姿势或事件已经在 Demo 执行。

## 实现

- 现有主编译器新增 `CompileSourceAware`，与旧入口复用严格资产校验、曲线、P4、
  过渡和动作编译流程。新入口精确对账来源布局、SourceStamp 和当前动画集摘要；
  旧 `Compile` 仍拒绝新图，不将新版降格为旧 Base22。
- 持续播放的 47 个样本按自己的 handle 展开时间线，同资产跨状态不合并。12 个
  TeleportEvaluator 有身份但不生成普通播放通知定义。
- 新版保留四个旧蹲姿同步成员，组内索引重新压紧，原声明的所有成员元数据仍逐项
  校验。来源表中各命名组和独立 Tick 信息通过 `Sources` 提供，不将 BlendSpace
  样本错误展开成同步组成员。旧同步视图与新来源表尚未组成全角色生产 Tick 调度。
- 新主绑定与图摘要覆盖现有编译数据、版本、布局、完整来源 SHA-256、完整原生清单
  SHA-256、动画集摘要、来源映射、未绑定清单和有序方向角色。布局不变但来源或清单
  来源变化时，两个主摘要都会变化。旧 v2 摘要字节保持不变。
- Snapshot 保留不可变私有表；Core/Graph/Occurrence 来源视图和未绑定清单均为借用
  只读视图，没有公开可变数组。10000 次主视图读取零分配。
- SourceGraphVersion=3 要求非空来源与匹配布局头；旧版本不允许夹带来源元数据。
  图视图 v2 显式携带 StandingWalkRun/Sources，不伪装成旧图视图 v1。
- Godot GraphBuilder 接受 Context 的主快照；独立图测试通过同一编译入口创建快照。
  Cycle 校验动画集、骨架、Idle、Standing 样本与 WalkRun 角色，再消费该快照。

目前时间线仍是已有导出合同中的 authored TimeSeconds/DurationSeconds/权重阈值和
语义负载，不是新增的 UE 有效 Trigger/EndTrigger 偏移、完整过滤条件或 NotifyQueue
生命周期。下一步必须补齐这些合同与执行语义，不能把定义展开直接当作通知提取等价。

## 发现并修复的边界

主编译器最初只核对六套来源的资产集合。两个反例证明它会接受前后方向互换，或者
WalkPose/RunPose 四角角色互换，尽管所有资产和数量仍相同。新增验证按原生
F/B/FL/BL/FR/BR 方向匹配 BlendSpace，并逐个核对 `x + 2*y` 四角的动画、倍率和骨架。
`source-snapshot-roles-red-final.trx` 保留两项先失败证据，最终全套已通过。

旧编译器仍检验原版固定角色、图/绑定摘要和物理通道。只更新公共 API 合同中新增的
源版本入口/只读字段，以及“v3 不存在”的旧假设；没有删除旧摘要断言或放宽容差。

## 验证

新增 13 项 Import 测试：所有来源/Sync 表与映射、资源闭包、每来源事件展开和固定
求值器不发通知、动作通道 handle、不可变视图/零分配、来源/清单变更影响主摘要、
被替换的旧 Sync 成员仍校验、四类缺失/陈旧输入、旧 Prepare 拒绝 v3，以及两个角色
互换反例。Godot 测试新增三类候选身份失配和提交身份与 Context 主快照一致断言。

| 检查 | 最终结果 |
| --- | --- |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1733/1733 |
| Import 全套 Debug | 737/737 |
| 新主快照、旧主绑定和来源布局 Import Release | 81/81 |
| P5 合同及事务 Core Release | 212/212 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

TRX 位于 `artifacts/test-results/source-snapshot/`。本批未重跑大规模 P5aGolden/Schema
矩阵。保留开发阶段失败：旧公共 API/版本断言、测试对旧 Sync 成员顺序的错误假设，
以及方向/四角角色反例；均已修正并全套复跑。构建期间还修正了 xUnit2031、测试类型
命名空间及 BlendSpace 骨架应从样本动画读取的编译错误，没有改动测试容差。

真实资源结果保持前批一致：

```text
STANDING_CYCLE_OK rates=30,60,120 phases=3 hip_transitions=9 wait_frames=371 rollback=9 interrupted_rollback=9 max_active=11 movement_direction=36 pose_bridge=63 alloc=0B active_alloc=0B curves=603 source_timing=15216 timing_authority=1260 movement_frames=5040 sync=asset_runtime
DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=asset_runtime identities=source_graph demo=not_connected
GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks=85680 detail_frames=1000 stop_frames=128 requests=35 pose_evaluations=3251 pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected
```

Cycle single/parallel 各 180 帧通过，result=`C658034A39C5917B`，完整姿势
`076E345A0151A012`，与前批一致。双模式晚期失败注入验证来源历史、移动入口、
Controller、姿势和 P4 banks 一起恢复。日志 `artifacts/source-snapshot-*.log`。

完整 `verify-p4-pose.ps1` 通过：曲线/转身/图、双模式脚部与晚期事务、姿势门禁；
拓扑/活动姿势各 10000 次零分配，活动 6396.736 ms。该短时检查不是 P7 性能预算。
最后两项方向校验和 Cycle 上下文断言随后再次优化构建，并通过上述新 Cycle 回归。

相机/输入 SHA-256 与前批一致，未修改 UE 插件、重复导出资源、commit 或 revert。
`git diff --check` 通过，只有既有 LF/CRLF 提示。本批没有新视觉捕获或滑移改善结论。

## 下一步

主绑定和实际 Cycle 的来源读取已接通，不再重复包装来源视图或布局。继续迁移旧
P5 执行入口：有效通知元数据、同步 Tick 窗口、NotifyMode/过滤、队列状态及来源候选
历史/状态事件，全部纳入同一 Prepare/Finalize 和失败回滚。旧保护只能在新执行合同
具备对应测试后放开，不能先接受 v3 再让旧单组时间或连续 cursor 静默接管。

随后组装真实 Main/Slot 上游、Standing/Detail/Stop、惯性化和 Lean/Sprint Impulse，
替换临时外层权重；继续同输入 UE/Godot 状态、时间、曲线、骨骼与多帧画面对照。
动态上身、P5B 全 Overlay/道具、P5C/P6/P7 和人工验收仍保持原范围，音频继续暂缓。
