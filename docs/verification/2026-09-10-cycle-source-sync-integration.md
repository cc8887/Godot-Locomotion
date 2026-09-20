# Cycle Source Sync Integration

日期：2026-09-10，第十九批。工作区 `D:/GodotALS-p5a-events-actions`。
本批进入当前 P4 Cycle Demo 的实际采样路径，但不代表完整 P5A 或原版 AnimBP 已接通。

## 改动

`AlsStandingCycleGraph` 原先自行计算混合时长并累加 Phase，再将所有动画以权重 1
提交给旧 Sequence Sync，固定 Walk F 为 Leader。本批移除这段推进/映射，改用已由
原生轨迹验证的 `AlsSyncRuntime.TryEvaluateAssetSyncGroup`。

初始化从正式 `AlsLocomotionSourceProfile` 取得播放器、样本、序列、标记、资产倍率、
起始位置和 length/phase 标志。当前实际姿势图使用的六个 WalkRun BlendSpace 与
Sprint F，共七个播放器、25 个采样身份参与；Idle 仍是外层固定姿势，不伪装为组成员。
选择器明确验证七个播放器、六个 BlendSpace 和 25 个采样的当前图范围。

BlendSpace 以当前图贡献权重参加组同步；其内部样本以现有网格求值顺序、权重和
独立 SourceSampleId 进入共享 Sync。输出使用各样本真实秒数换算姿势/曲线采样时间。
同一个 Pose 动画被不同 BlendSpace 引用时，身份与采样时间不会按 AnimationId 合并。

同时修正 `AlsCyclePoseSampler`：Walk/Run Pose 角样本也是带时间的 BlendSpace 来源，
不能只在初始化时采一次。现在除外层 Idle 外，25 个来源均使用各自的映射时间采样。
先前曲线时间可以变化而这些 Pose 骨骼仍保持初始化姿势的问题不再保留。

## 提交与回滚

播放器保留时间、当前活跃播放器/样本历史、标记和组历史保存在现有
`AlsStandingCycleFrame` 的值类型候选中。`MapTimes` 只调用共享 Core 求值，不直接
修改已提交帧；控制器原有提交、丢弃、回滚负责这份状态。没有新增线程、调度器、
事件队列、外部时钟或全局 occurrence 分配器。

当前 standing 图内变为无活跃来源时，清理组/标记历史，保留播放器时间；再次加入
由共享 Sync 执行标记初始化。此行为不等于已实现完整 AnimBP 的状态相关性重置。
正式 P5 接线时应移动/复用这一份历史，不得同时保留一个独立 Demo 同步时钟。

## 验证

- Godot Debug 构建通过，0 warnings/0 errors。
- StandingCycleSmoke：30/60/120 Hz、三种换向时间偏移、63 次全骨骼载体对照；
  首次、普通、中断回滚各九组；稳定和活动路径均 0 B。
- 新增 15,216 次源采样时间检查：源身份、播放器/样本归属、Leader 切换、Pose 角
  非零时间、Sprint、无活跃组、重新加入和候选重试；采样时间与同步输出一致。
- 换髋转换九次，等待 371 帧，最大活动过渡数 11。没有添加换向冷却或放宽门控。
- 七播放器完整历史已纳入控制器回滚检查；发布前失败注入在 single/parallel 均
  返回 `STANDING_CYCLE_WORKER_ROLLBACK_OK ... source_sync=1`，交换区未泄漏结果。
- 新 Cycle 单/多线程各 180 帧：result `13660E3C1D484C51`，
  full_pose `076E345A0151A012`，root `309E8D0E0BEEB2CB`，lag/stale 均为 0。
- Core 排除 `AlsP5aGoldenTests` 和 `AlsP5aTraceSchemaTests` 后 **1575/1575**。
  本批没有运行这两个较长套件，也没有修改其原生期望值。
- 旧 `verify-p4-pose.ps1` 通过：graph、pose、两模式 foot placement、两模式 late
  transaction 和零分配门禁。其通过仅证明旧路径回归，不替代新 Cycle 的验证。

## 多帧移动

以下回放均为 720 个已提交帧、120 张截图，1280x720；最大脚部旋转沿用原有 30 度
突跳检查，未放宽阈值。已检查横移/快速移动联系图，以及跑步起步连续帧。

| 回放目录（均位于 artifacts） | 最大单帧脚旋转 | 起步低位脚位移峰值 |
| --- | ---: | ---: |
| source-sync-trace-strafe-20260910 | 11.801 度 | 6.1238 cm，左脚 3.2949 cm |
| source-sync-rapid-20260910 | 12.974 度 | 不以此非固定镜头用例评估固定横移起步 |
| source-sync-run-strafe-20260910 | 11.807 度 | 9.5678 cm，左脚 3.2655 cm |

固定横移起步代理未优于第六批约 5.01 cm；跑步横移代理也未优于旧记录约 6.23 cm。
该指标包含低高度摆动脚，尚不是有 UE 接触状态基线的支撑脚滑移测量；不能据此
单独归因，也不能宣称起步滑步已经修复。没有通过调起始相位或修改阈值美化结果。

额外保留了 960 宽预备回放、1280 宽横移与非固定镜头 run 回放。960 宽图不适配
旧分析脚本的固定裁切，因此不作为本报告联系图证据。非固定镜头 run 在第 181 帧
才起步，对其套用 61..72 帧的固定横移指标得到的 0 不代表起步无滑移。

移动回放新增 `SourceSync` 记录：每帧活跃组、播放器、样本时间、前时间和 Tick delta。
最终 trace-strafe 文件验证了 720 帧和 1092 个活跃样本范围，组 Leader 依次覆盖源
ID 8、1、3、0、4、2；当前 Cycle.Phase 与 Group.Ratio 一致。该记录仅覆盖本批
standing 七来源，尚非完整 P5 时间线或完整 UE/Godot AnimBP 对照。

## 明确未完成

- 原生 Cycle 的 Lean 和 Sprint Impulse 尚未接入这条源时间/姿势链；Lean 仍是旧图
  分支。不能把七播放器的结果称为全部九播放器完成。
- 当前贡献权重、共享轴滤波、Idle/Sprint 外层混合仍来自已有部分移植图；完整图的
  更新遍历顺序、同权重提交顺序、独立相关性/滤波重置仍需原生轨迹确认与接线。
- 跨蹲姿/空中/外层转换的全局同步组、Detail、ShouldMove/Stop/Pivot、源状态事件和
  P5 Notify 生命周期/布局/统一事务尚未贯通。当前七来源的候选存储不是最终 P5 ABI。
- Core locomotion model 的 Stride/PlayRate/AnimationPhase 仍与动画图有重复计算。
  trace-strafe 第 66 帧 Result.AnimationPhase 为 0.097222246，而实际 Cycle.Phase
  为 0.2782039；HUD 的旧 Result 相位不能代表源采样时间。全局单一时间权威尚未完成。
- 动态 Layering、完整 Aim、手部 IK、Overlay、Root Motion/动作、恢复/相机和十分钟
  性能预算继续按原 A/B/C/D、P5B/P5C/P6/P7 推进。

相机与输入 SHA256 保持不变：
`6ED2DB72FF9D551C9E2D540BE6EC5889CD0CA9FC8A67425508C9A1D258BED666`、
`BEE5F84E9FA5ABCF6D46C5209F222A51F6B10B5C64D1B10517A81779FA3BC8DB`。
本批未重新运行 UE、未提交 Git、未撤销用户改动，未关闭完整移植目标。
