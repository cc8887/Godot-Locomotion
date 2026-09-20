# Standing Source Time Authority

日期：2026-09-10，第二十批。工作区 `D:/GodotALS-p5a-events-actions`。
本批消除当前 standing Cycle 路径的重复 Stride/PlayRate/Phase 计算，并贯通结果提交；
不代表完整 AnimBP、全局 P5 布局或所有同步组已完成。

## 问题与改动

第十九批实际 Cycle 已由源 Sync 推进，但 `AlsLocomotionModel` 仍先计算旧 Stride、
PlayRate 并累加独立 Phase。发布的 Result 和 RuntimeState 因而不是当前图的时间摘要。
旧横移第 66 帧 Result.AnimationPhase=0.097222246，Cycle.Phase=0.2782039。

- Core 新增显式 `AlsLocomotionTimingPolicy.StandingSourceGraph`。只对动画状态为
  Grounded、姿态为 Standing 的帧跳过旧三项计算；运动、步态、朝向、Lean 不变。
  Model 默认路径、Crouching、Jump/Fall/LandRecovery 仍消费原有实现。
- 被延后的三个结果字段暂为 NaN，运行时保留上一提交相位。这是内部未完成候选，
  不是可发布结果，不用看似有效的默认数值掩盖尚未完成的求值。
- 控制器从同一个 PreparedApply.Cycle 返回真实 Stride/PlayRate/Phase；Core
  completion 验证输入身份、分支、待完成状态和数值范围，再一次写回结果及状态。
  standing 源速率允许零、Stride 不套旧模型的上限 1；没有裁剪源输出以适配旧摘要。
- Worker 在 PrepareFrame 后、姿势与脚部求值前完成此合入，之后沿原有候选状态、
  结果、控制器及姿势提交/回滚路径。没有新增时钟、队列、线程或独立运行时。
- 控制器拒绝未合入源时间的 Apply/Commit/Finalize，以及重复或已丢弃准备帧的合入。
  身份不匹配、非法数值不会部分修改候选；源历史仍只在原有成功提交处发布。
- 原生 Sync 的合法区间包含终点 1，而循环相位摘要为 [0,1)。仅把循环摘要的精确
  终点 1 表示为 0；源播放器时间、样本时间、Marker/组历史不变，不是起步相位补偿。

`AlsFrameResult` 和 `AlsRuntimeState` 的物理布局未改。本次 summary 不是源身份，
不能用于按源派发 Notify，也不能代替旧布局 v2 尚未覆盖的完整 P5 source slots。

## 验证

- Godot 构建通过，0 warnings/0 errors。
- 新增 Core 测试 24 项：延后与原运动结果对照、蹲姿/空中保留原路径、精确一次合入、
  不同帧拒绝、非法数据原子性、非法策略及循环摘要边界。
- Core Debug 排除 `AlsP5aGoldenTests`、`AlsP5aTraceSchemaTests` 后 **1599/1599**。
  没有运行这两个长套件，不称为全部 Core/P5A 认证。
- Core Release 源时间、动画参数、Locomotion 状态及 Locomotion Golden 相关 **126/126**。
- StandingCycleSmoke：30/60/120 Hz，源样本时间检查 15,216 次；新增 1,260 帧
  Core -> Prepare -> Complete -> Discard/retry -> Apply -> Commit 检查，确认无旧时钟
  提前推进、未完成不可 Apply、已丢弃不可合入、同一准备帧不可重复合入。
  暖机后的完整合入/重试路径与原稳定/活动采样路径均 0 B；原换髋九次、等待 371 帧、
  首次/普通/中断回滚、63 次全骨骼载体对照仍通过。
- 新 Cycle single/parallel 各 180 帧，其中每模式检查 120 个 standing 源帧，所有
  已提交帧均检查结果相位等于 runtime 相位。结果摘要共同为 `C658034A39C5917B`，
  full_pose=`076E345A0151A012`，root=`309E8D0E0BEEB2CB`，lag/stale=0。
  结果摘要因真实时间字段更新而改变，骨骼/根摘要与第十九批相同。
- 两模式 late_transaction 均通过：source_sync=1，exchange=0，runtime/result/
  controller/pose/p4_banks 全部恢复。日志中的 frame 13 异常为显式失败注入。
- 旧 `verify-p4-pose.ps1` 全部通过，包含 graph/pose、两模式 Foot Placement、
  两模式晚期回滚及零分配门禁。此门禁不能替代上述新 Cycle 检查。

## 实际移动回放

两组均为 1280x720、720 个提交帧、120 张截图；沿用原有脚旋转突跳与直立检查，
没有修改阈值。检查了横移多帧联系图及跑步起步帧。

| artifacts 下的目录 | 最大单帧脚旋转 | 起步低位脚位移峰值 | 换髋开始帧 / 等待帧 |
| --- | ---: | ---: | --- |
| source-authority-strafe-20260910 | 11.801 度 | 6.1238 cm | 291、487 / 42 |
| source-authority-run-strafe-20260910 | 11.807 度 | 9.5678 cm | 319、496 / 63 |

回放现在记录 RuntimeAnimationPhase，并在每帧校验 Result/Runtime/Cycle 时间一致。
导出 JSON 复核两组共 1,440 帧的 Phase/Stride/PlayRate 一致；FootPose 全部与各自
第十九批回放逐字段相同。横移组的 720 帧 SourceSync 历史也与旧数据逐字段相同。

横移第 66 帧现在 Result.AnimationPhase、RuntimeAnimationPhase、Cycle.Phase
均为 **0.2782039**。不是单独修改 HUD，且没有为了更改数值而改变已验证的源时间。

起步低位脚指标没有改善；它还包含低位摆动脚，不是有 UE 接触状态基线的支撑脚滑移。
因此本批不宣称滑步、交错步或上身完成视觉修复，也没有新的人工验收结论。

## 未完成与下一步

1. 本次统一限于当前 standing Cycle 的时间及结果摘要。跨蹲姿/空中/外层过渡的
   统一贡献权重、相关性/滤波重置、全局同步组和最终 P5 occurrence 布局仍未完成。
2. 当前七来源之外的 Cycle Lean/Sprint Impulse、Detail 的十六来源、ShouldMove/
   Not Moving/Moving/Stop、Feet Position Lock/Plant、Pivot/进入退出事件继续接线。
   复用已提交的源时间历史，不再引入第二套 Demo 时钟。
3. 通知提取到队列/Notify State 生命周期、P5 facade 和最终 Gather/Worker/Commit
   事务，随后动态 Layering/Aim/手部 IK 与 P5B Overlay/道具，仍按原计划推进。
4. P5C/P6/P7 范围不缩减。普通起步问题不能用 Mantle/Root Motion 功能补偿掩盖。

相机和输入 SHA256 仍为
`6ED2DB72FF9D551C9E2D540BE6EC5889CD0CA9FC8A67425508C9A1D258BED666`、
`BEE5F84E9FA5ABCF6D46C5209F222A51F6B10B5C64D1B10517A81779FA3BC8DB`。
本批没有修改 UE/exporter/资产/布局编译器，没有重跑 UE、Import 全量或完整 P5A
长矩阵；没有提交 Git、撤销用户改动或关闭完整移植目标。
