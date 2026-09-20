# 蹲姿方向缓存更新接线

日期：2026-09-11。第五十一批。工作区 `../GodotALS-p5a-events-actions`。

## 对完整性问题的判断

已有动画、曲线和混合公式不代表运行图等价。此前真实资源 smoke 将六方向来源
固定按权重 1 提交，并在 Stride 关闭方向分支时仍更新方向机。姿势用了 Stride，
来源时间/同步参与及通知却没有使用相同相关性，属于 P3/P4 基础移动补完中的
运行图接线缺口，不是交给 Mantle/Ragdoll 等后续功能自然解决的问题。

本批只补方向层更新调度并进入真实资源组件回放；不宣称实际 Demo 已改善。

## 实现与依据

- `src/Als.Import/Compilation/AlsCrouchingDirectionPoseCompiler.cs`：在原有严格
  六方向姿势合同上保留 24 个 UseCachedPose 的 compiled node identity、6 个
  SaveCachedPose identity 和方向状态机 identity。读取到缓存归属继续使用原生
  property index，不凭名称配对，不按数组位置猜测。
- `src/Als.Core/Locomotion/AlsCrouchingDirectionGraph.cs`：消费状态机的实际
  `GetUpdate()` 顺序，包括中断栈和零全局权重状态，按归一化 F/B/L/R 的局部相关性
  提交缓存读取。复用既有 `AlsPoseCacheTraversal`，同缓存最大权重更新一次，
  同权重保留首次上下文；保留祖先状态、根运动权重、惯性化与 skipped-update 消息。
- 编译缓存写入顺序为 `[827,824,829,828,826,825]`；对应方向索引
  `[2,5,0,1,3,4]`，六来源 player ID 为 `[49,50,55,51,52,53]`。
- 本机 UE `Engine/Source/Runtime/AnimGraphRuntime/Private/AnimNodes/`
  `AnimNode_MultiWayBlend.cpp` 的更新按局部 CachedAlpha 判断相关性，不按乘上
  上游后的全局权重决定是否更新。故全局权重 0 不等于缺席；空 VelocityBlend
  则不提交来源。原生缓存更新规则继续由已有缓存探针与公共运行时测试覆盖。
- 更新器不拥有动画时间、同步历史或提交状态；调用方提供前帧状态，接收候选。
  发生收集异常后可从同一前帧重试，不残留前次读取队列；不是自动提交 gameplay。

## 实际资源回放

`src/Als.Godot/Animation/CrouchingSourceSmoke.cs` 已在共享 Sync 之前推进 Stride，
只在方向分支相关时更新方向机并收集缓存赢家。退出方向分支时不提交六方向 tick，
重入仍使用已有方向状态机规则。来源时间按正式 player/sample ID 读取，不依赖
同步批次返回顺序；P5 通知活动状态使用赢家携带的方向状态上下文。

为了在相关性窗口内继续覆盖六方向，受控方向输入从每两秒一轮改成两轮；不是修改
原生轨迹或提高容差。Stride、Diagonal 和 Lean 的受控输入不变。结果：

| 检查 | 结果 |
| --- | --- |
| 30/60/120 Hz 总回放 | 420 帧、7140 次真实动画采样 |
| 方向分支 | 更新 218 帧，停用 202 帧 |
| 方向缓存 | 3316 次读取，1254 次来源更新 |
| 状态覆盖 | 6 状态、33 转换、191 中断混合帧 |
| Stride | Walk-only 202、Direction-only 94、混合 124 |
| Diagonal / Lean | 262 有效 / 158 旁路；Lean 420 帧变化、263 次单样本直接对照 |
| 共享来源事件 | 24 个，候选重试相同 |

测试仍单独驱动 Rotate/Lean 的上游权重；所有 17 个源样本继续取样检查，不是完整
缓存求值调度。保留固定 epoch=1 的组件来源约定，尚未完成整体 Cycles 初始化及
SaveCachedPose 初始化/骨骼缓存/求值生命周期与 Main 的统一所有权。不能将这里的
方向重入检查当作全部来源重新初始化正确的证明。

## 验证

日志和 TRX：`artifacts/test-results/crouching-cached-updates/`。

- 新增 `AlsCrouchingDirectionGraphTests` 13/13：节点布局、三组全局权重、零输入及
  阈值、同权重首次优先、30/60/120 Hz 中断赢家、Stride 停用重入、失败重试、实际
  来源绑定，以及专用线程测量零分配。
- Core 常规 1882/1882，沿用排除 `AlsP5aGoldenTests` 与 `AlsP5aTraceSchemaTests` 的
  现有回归命令；并非声称这两个排除组本批已运行。
- Import 全套 1047/1047；蹲姿/Lean 相关 Release 176/176；Godot 构建零警告/错误。
- Standing/Pivot 各 5040 帧、Detail 1890、Sprint 1260；Main 来源 1050、混合 420、
  中断 104，组件活动求值零分配。
- Worker 单/多线程各 180 帧，10 个来源事件、首帧 25；晚期来源事件失败注入两模式
  均回滚，无回调泄漏。注入异常是预期测试结果，不是无故崩溃。
- 生产摘要保持：result `A9DF0647AFC3574C`、full pose `04D4A5651B87E0E4`、
  pose `2DED5435A66BCAEC`、root `309E8D0E0BEEB2CB`。

未改 UE 插件或原始导出，没有本批 UE 编译/完整图原生轨迹、新移动截图、全套 P4
脚本或十分钟 Release 性能采样。保留现有脏工作区；未 commit、revert 或 merge。

## 后续顺序

1. 完成完整 Cycles 的更新/初始化/求值所有权及蹲姿主图，接入 Main 生产路径。
2. 闭合最终曲线消费、播放速率/Stride/RotationScale、动态 Layering/Add/LS/Lean
   与原生脚部约束；Standing 已知 WeightFactor 最小权重及零速度回退差异仍须统一。
3. 继续原 P5A 正式 Notify/State、Sync/Action/Slot 事务，再推进 P5B Overlay/道具、
   P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/Recovery/Camera、P7 性能验收。
4. 用相同输入逐帧比较状态、曲线、来源时间/权重和最终骨骼，再做连续移动截图与
   人工验收。没有这些证据，不关闭起步滑移、交错步和上身问题。音频继续暂缓。
