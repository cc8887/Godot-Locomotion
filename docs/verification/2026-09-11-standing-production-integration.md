# Standing/Stop 生产接线与脚部姿势历史

日期：2026-09-11。工作区：`ARCHIVED_P5A_WORKTREE_PATH`。
本批为完整性修复第四十批，不是完整 ALS 或基础移动视觉验收。

## 实际改动

1. Controller 的 Standing 路径复用已编译的 Standing/Stop/Detail 缓存更新图，
   按原生表中的过渡栈组合 Idle、Moving、Stop、左右 Rotate 的姿势和曲线。
   生产路径不再用 `MoveToward` 代替外层状态机；独立 Cycle 组件夹具仍保留原入口。
2. Stop 消费实际 Detail/Cycle 输出、上一帧 `Feet_Position` 和已跟踪髋方向。
   Plant 的固定采样器不创建播放时钟。修正其错误释放共享 `Godot.Animation`
   的所有权问题，避免随后 Cycle 访问已释放资源。
3. Standing Rotate 与 Detail/Cycle 使用同一来源批次、时间、epoch 和通知事务。
   Standing 生效时不再叠加旧 P4 Rotate 姿势覆盖；旧 P4 Turn 和非 Standing 路径保留。
   Worker 将旋转相位及 yaw 积分区间回填为实际来源时间。
   这是唯一时钟接线，Rotate 选择、速率和 yaw 积分仍采用现有 P4 模型，
   并非已完成 V4 最终 `RotationAmount` 的角色反馈链。
4. 无 P4 参数的动画库构建入口也补入正式来源资源闭包。
5. 原脚部约束把位置限制产生的旋转同时施加到脚掌，角色后方角度分界会导致翻转。
   现分离位置与朝向约束，并使物理脚掌角度限制参照动画原姿势而不是膝盖 IK 后的继承旋转。
6. 锁定时记录实际动画脚掌朝向，避免 Stop 满权重锁定突然切换到绑定姿势。
   锁定旋转以移动基底局部空间保存，约束后的旋转写回候选历史，避免转身跨半周时
   重新选择相反的角度限制分支。两份四元数追加到 `AlsRuntimeState` 末尾，
   保留既有字段顺序，加入 Worker 回滚比较；释放时清空。

## 失败与边界

- 首次实际 Standing 接线因 Stop 释放共享动画失败；保留 `standing-production-first.log`
  及 `standing-production-worker-first.log`。修复后实际源图测试通过。
- 新测试曾对包含 InlineArray 的结构调用默认 Equals，运行时拒绝；
  现按整个值快照比较候选与提交历史，保留 `standing-production-coverage.log`。
- 步行回放首先在第 68 帧产生约 48.385 度脚旋转，而原动画变化不到 5 度。
  分离约束后，第 427 帧满锁又暴露绑定姿势替代实际脚掌朝向的问题。
  补姿势历史后，转身第 657 帧暴露未持久化旋转限制的半周分支翻转。
  这些失败及诊断截图均保留，没有放宽 30 度回放守卫。
- 阅读本机 `Plugins/ALS/Source/ALS/Private/AlsAnimationInstance.cpp` 的
  `RefreshFootLock` 和 `ConstrainFootLock`，确认原版保存锁定旋转并持久化约束结果。
  但原版的约束参照骨盆和实际大腿轴，当前桥接仍包含以角色前方为参照的位置限制。
  本批的分离修复不是该原生约束的 1:1 实现；捕获时机、足轴、地面偏移顺序、
  曲线满权重重捕获和完整 Control Rig 继续属于 P4 完整性工作。
- Core 初跑三个 P4 派生 oracle 测试失败。使用既有 `AlsPoseTrace.WritePortOracle`
  重建五份 `portExpected`；备份在 `artifacts/standing-foot-port-oracle-before/`。
  结构化比较确认 `nativeActual`、输入、源身份和容差均未改变。
  更新派生预期不是新的 UE 对照证据。之后字段顺序测试提示新增状态应追加，已修正。

## 验证

| 检查 | 本批结果 |
| --- | --- |
| Godot 构建 | 0 警告、0 错误 |
| Core 常规，排除独立 P5A golden/schema 套件 | 1836/1836 |
| Import | 815/815，最终重跑另存日志 |
| FootPlacement 专项 | 76/76，新增后方角度分界反例 |
| Standing 真实 Controller | 30/60/120 Hz，共 5040 帧，实际 Stop、左右 Rotate、回滚与相同重试 |
| 锁定旋转历史 | 360 帧跨半周连续、候选重试一致、释放清空 |
| Detail/Sprint 既有生产测试 | 1890/1260 帧通过 |
| 实际 Worker 单/多线程 | 各 180 帧，姿势/结果摘要一致，10 个来源通知 |
| 来源事件晚期失败 | 两种模式第 25 帧，零回调泄漏 |
| 整帧晚期失败 | 两种模式第 13 帧，状态/姿势/控制器回滚 |
| P4 全脚本 | 图、姿势、地形/平台、两种模式回滚及零分配检查通过 |

Worker 摘要：result `A9DF0647AFC3574C`，full_pose `04D4A5651B87E0E4`，
pose `2DED5435A66BCAEC`，root `309E8D0E0BEEB2CB`。
测试记录：`artifacts/test-results/standing-production/`、`artifacts/standing-production-*.log`。

| 有效连续视觉回放 | 帧/截图 | 最大单帧脚旋转 |
| --- | --- | --- |
| `standing-production-orientation-final`，步行横移/转身 | 720/120 | 10.743 度 |
| `standing-production-run-final`，跑步横移 | 720/120 | 12.546 度 |
| `standing-production-sprint-final`，冲刺/转身 | 720/120 | 18.990 度 |

已查看起步失败图及修复后 426、432、660 帧。非空画面及连续旋转守卫通过，
不等于上身、交错步或脚底不滑的人工验收。
低脚位移代理：步行 7.9046、跑步 9.8983 cm/帧；上一批为 6.1238/9.3969。
此指标没有改善，也不是严格的支撑脚滑移测量；两项 `StartupAccepted` 均为 false。
不得把本批旋转稳定性改善宣称为起步滑步已经修复。录制帧率不是性能预算结果。

## 继续顺序

1. 接入方向状态进入/退出及 Pivot 通知的实际反馈和非重触发延迟，验证换髋节奏；
   当前仅将方向映射为髋枚举，尚不是完整事件分发。
2. 接 Main/Slot、真实上游缓存权重、惯性化上下文，核对 Cycle 重入时的源初始化
   与缓存生命周期。当前 `MainGroundedWeight=1`、`Pivot=false` 等上游边界仍存在。
3. 校验 Idle/Rotate 的完整 ModifyCurve/Slot 引脚合同，统一最终 `Weight_Gait`、
   `Mask_Sprint`、`RotationAmount`、YawOffset 和速率消费；不能用来源时间一致代替最终曲线一致。
4. 补动态 Layering/Add/LS、Lean、上身/髋部、手部 IK，完成 P3/P4 与 P5A 生产通道。
5. 按原计划继续 P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/
   Pose Recovery/完整相机，最后 P7 十分钟 Release 性能预算。音频仍暂缓。

本批未提交、未回退用户改动，未改相机或输入适配器，未构建或修改 UE 插件。
