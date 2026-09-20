# 蹲姿主状态更新与来源联动验证（第五十四批）

## 结论与边界

本批关闭主蹲姿状态调度及来源收集组件缺口，不关闭 Main/BaseLayer 生产接线或
Demo 视觉验收。当前仍不能称为完整 1:1 移植，也不能把组件测试通过当作滑步、
交错步或上身问题已经修复。

原计划归属不变：基础状态、方向/换髋、起停、最终曲线属于 P3 完整性补项，
动态分层与脚部约束属于 P4；通用事件/同步/动作与 Slot 属于 P5A，Overlay
与道具/手部 IK 属于 P5B，Mantle/Roll/Root Motion 属于 P5C，Ragdoll/Get-up/
Pose Recovery/Camera 属于 P6，最终人工及十分钟预算属于 P7。音频继续暂缓。

## 本批实现

- `AlsGroundedStateMachine` 增加有序初始化记录；保留旧位掩码供既有消费者使用。
  同帧路径 `[0,3,2,3]` 不能简化成三个唯一状态，否则 Rotate Right 少一次 epoch。
- `AlsCrouchingStateCompiler` 严格读取 compiled 110/122 -> Save 31、主机器 103、
  Cycles 机器与 Idle Slot，核对 property/compiled identity、缓存来源及根更新表。
- `AlsCrouchingStateRuntime` 按真实 GetUpdate 顺序分发，不从最终姿势权重反推。
  Stop 初始化为 Base -> 左腿 -> 右腿，更新为左腿 -> 右腿 -> Base；腿部上下文
  root-motion 权重为零，Base 保持原值。缓存排空交给外层 BaseLayer owner。
- `AlsCrouchingCycleRuntime.InitializeSources` 提供显式外部输入初始化入口，
  用于 Save 初始化先于 Stop 腿部初始化；正常重入仍复用同一顺序。
- `AlsCrouchingSourceCollector` 只修改候选：独立 epoch/初始秒数、状态权重清除、
  实际 Lean 样本顺序、同步上下文与来源身份守卫。移动状态只持有缓存读取，
  不能清除外部 Cycles 播放器。没有引入第二套时钟或直接派发玩法事件。

## 验证

日志和 TRX 位于 `artifacts/test-results/crouching-state-runtime/`。

| 检查 | 结果 |
| --- | --- |
| 新 Import 调度专项 | 28 项；初始化顺序、同权重首次赢家、零权重、祖先上下文、身份与故障重试、零分配 |
| Core 常规 | 1891/1891；按既有命令排除 P5A Golden/TraceSchema 两类，不称全部测试 |
| Import 全套 | 1101/1101 |
| Release Core 状态机/转换/缓存 | 63/63 |
| Release 蹲姿与 Lean | 230/230，包含新增权重清除断言 |
| 最终 Godot 构建 | 零警告、零错误 |
| 新主状态来源 owner smoke | 1671 帧，30/60/120 Hz，五状态、29 个来源事件、104 帧共享双读取 |
| 来源生命周期 | 6 次缓存初始化、6 次更新相关性重入；方向重入不重置外部来源 |
| 来源候选安全 | 15 次非法操作拒绝、重试一致、反向初始化、重复 Right epoch=2；活动收集 0 B |
| Cycles 整图组件 | 1248 帧、2385 次缓存求值、3744 次曲线检查、3 次故障拒绝、0 B |
| 蹲姿主状态姿势组件 | 1065 来源帧、1050 过渡帧、12702 次曲线检查、5 次拒绝、0 B |
| Standing/Detail/Pivot | 5040/1890/5040 帧；Sprint 1260 帧；Controller 实际路径通过 |
| Main 姿势组件 | 1050 来源帧、420 次 profile 混合、104 中断帧；缓存仍为夹具 |
| Worker single/parallel | 各 180 帧、10 个来源事件；结果和姿势摘要保持 |
| 并行晚期事件/事务失败 | 来源、事件、随机状态、Controller、姿势及交换回滚通过；事件回调泄漏为零 |

保留摘要：result `A9DF0647AFC3574C`，full pose `04D4A5651B87E0E4`，
pose `2DED5435A66BCAEC`，root `309E8D0E0BEEB2CB`。

新增来源 smoke 使用真实正式来源、共享 Sync 与 P5 事件候选，但其 SourceTestOwner
是受控输入夹具：Save 生命周期只使用 Initialize，不求缓存姿势；无 Montage、
惯性化请求不求姿势。通知活跃性按机器 identity 查找祖先状态，不假设固定栈深。
这些测试不能证明全图 Slot AsInactive、惯性化、曲线元数据或最终骨骼等价。

首次新 smoke 的嵌套类名 Owner 与 Godot Node.Owner 冲突，构建失败；改为
SourceTestOwner 后构建与全部上述检查通过。未放宽生产断言、容差或修改源资产。

## 下一步与未关闭问题

1. 将 Main、Standing、Crouching 统一到 BaseLayer 候选 owner，按原生
   `[100,99,33,32,30,31]` 排空共享缓存；一次来源提交/同步，统一缓存求值及提交。
2. 接真实 Slot/惯性化 owner、主图状态反馈与初始化生命周期。旧 Standing 的
   位掩码消费者仍需逐项迁移，新增有序 API 不等于所有调用方已经使用。
3. 对齐最终 RotationScale、Stride/播放速率、YawOffset 与 Layering/Add/LS/
   Lean/IK mask，再闭合完整 Foot IK/Foot Lock/pelvis/thigh 行为。
4. 逐帧核对 UE 与 Godot 同输入的状态、源时间/权重、曲线及最终骨骼，结合
   起步、停止、左右换向、Alt 视角和多帧截图验收，不以无异常代替动作正确。
5. 完成 P5A 剩余事件/Slot/Action 接线后继续 P5B/P5C/P6/P7，不取消原后续功能。

上一批 Demo 平台路线门禁仍未关闭：平台窗口 85--96 的 IK/FootLock 为
`(1,1,0,0)`，锁定样本零。本批未重跑完整 P4 路线或移动截图，不能声称该问题
已修复；仍需区分最终曲线/源状态与路线起停时机。没有新增 UE 完整图探针或
最终十分钟 Release 采样。未修改 UE 插件、源资产、已确认键鼠，也未提交或回滚。
