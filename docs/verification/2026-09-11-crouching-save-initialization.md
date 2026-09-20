# Crouch 根状态的 Save 初始化与初始姿势

日期：2026-09-11。完整性恢复第六十三批。

## 本批完成

Main 进入 Crouch 状态并实际初始化其 Save 时，现在立即初始化 Crouch 根状态
及 Idle Slot 的来源，不再把整个动作推迟到首次 Crouch Update。固定时间 Idle
取样器仍是正式来源 43，不获得独立播放时钟。

`AlsCrouchingStateRuntime.Initialize` 返回带身份的初始化快照，消费有序初始状态
记录、清理对应缓存权重并初始化来源，最后通知同一候选的骨骼缓存观察者。
它不推进状态时间、更新来源或提前排空 Cycle 队列。Main 保留该候选，即使当帧
又进入其他状态、Crouch 未得到 Update，也不会遗失已完成的初始化。

首次 Update 只初始化真正新进入的状态；例如 Initialize 已进入 Idle，随后首次
Moving Update 只初始化 Moving 的缓存读取，不再初始化 Idle。更新后的真实
相关性重入仍保留原生重置行为。已初始化但未更新的快照也检查角色、generation
和帧时间，不能跨角色使用或倒退到初始化帧之前。

Crouch 姿势消费者允许合法的已初始化快照参与 Evaluate；此前强制 HasUpdated
会拒绝这种状态。依然拒绝默认/未初始化状态、错误 machine kind 和非法输入，
没有用默认姿势掩盖缺失数据。固定时间取样沿用正式资产数据。

## 验证

- 新增 Import 10 项：独立初始化、同帧/延后首次 Update、未更新身份保护、失败
  重试、Main 顺序与入口别名、Main 当帧跳过 Crouch 后延迟更新等。最终相关 84/84。
- Core 常规 1920/1920，Import 全套 1164/1164；最终优化构建零警告、零错误。
  Core 常规仍按既有范围排除 P5A golden / trace schema，两类未由本结果宣告通过。
- 真实 Crouch Idle 初始化姿势在 30/60/120 Hz 共 210 帧求值，与直接资产取样的
  骨骼/曲线一致，重试一致，状态机 Update 次数零、ElapsedSeconds 保持零。
- 原蹲姿姿势回放 1065 来源帧、1050 过渡帧、186 QuickFeet 帧、61 中断混合帧、
  加上初始姿势共 13962 次曲线检查通过。五个非法候选拒绝、Slot 片尾和活动 0 B 保持。
- Main 六缓存 1680 路线帧通过，每条路线 Crouch Save 根初始化一次，Idle 状态
  初始化分别 4/4/6 次，每帧 epoch 与实际来源初始化次数一致；120 Hz 路线本来
  包含额外初始蹲姿窗口，不把这两个计数混为一谈。
- 三个 Crouch Idle epoch 溢出拒绝后，从已提交来源重试，姿势、事件和骨骼计数
  保持一致。Idle 不出现在共享同步的计时来源列表。前批三个 Standing Cycle
  初始化故障、六个 Slot 姿势故障、六缓存和 2360 次原始 Standing 对照继续通过。
  新增的一次原始 Standing 对照来自 Crouch 初始化故障后的正常重试。
- Main 旧非缓存姿势入口也通过 1680 帧及相同来源初始化故障；Main 活动准备 0 B。
- 单/并行 Worker 各 180 帧、各 10 个来源事件通过，结果摘要 A9DF0647AFC3574C、
  完整姿势摘要 04D4A5651B87E0E4 保持；并行晚期事件回滚通过、回调泄漏零，
  runtime/result/controller/pose/P4 banks 恢复。

TRX：`artifacts/test-results/crouch-save-initialize/`。
Godot 日志：`artifacts/crouch-save-main.log`、`crouch-save-main-legacy.log`、
`crouch-save-initial-pose.log`、`crouch-save-worker-{single,parallel,rollback}.log`。
已跟踪差异及本批涉及的未跟踪代码空白检查通过。

保留新测试第一次 80/81 的失败：测试构造 generation=0 的身份时，身份构造器
先抛出 ArgumentOutOfRangeException，未进入要检查的初始化 API。改用默认身份
验证该 API 的拒绝路径，未放宽运行时校验或更改异常合同。`initialization.trx`
保留失败；之后 `main-initialization.trx` 与全套 Import 通过。

## 剩余工作

本批完成的是 Crouch 根的来源初始化与合法初始姿势，不是完整 Cycle 或 Slot /
Montage 生命周期。Crouching Cycles 的外部八来源初始化与内部单状态机、方向机、
Stride 状态仍需统一，整合中的重复初始化抑制补偿仍待取消；Standing Cycle 的
方向机、滤波、Sprint 混合和内部缓存也仍需纳入整体初始化。

随后继续 Main Movement、真实 Slot / Montage、最终惯性化与统一生产提交，
再接完整 Demo。最终曲线反馈、动态上身、完整脚部及原 P5A-P7 全部保留，音频
仍延后。Main 整合仍使用 Slot 姿势替身；本批没有新增完整原生图对照、移动截图、
平台路线、性能矩阵或十分钟验收。既有平台脚锁失败、短矩阵超预算和视觉问题
继续未关闭。没有修改已确认键鼠输入、UE 资产/插件，也没有提交、回滚或合并。
