# Standing Cycle 来源重入与内部状态生命周期

日期：2026-09-11。完整性恢复第六十五批。

## 改动

此前 `AlsStandingCycleGraph` 每次准备帧都将 DirectionInitialized 设为 true，
即使外层 Idle 没有更新 Cycle；真正相关性重入只重置方向和 WalkRun 滤波，
没有统一初始化外部来源。来源收集还保留“epoch 为零就启动”的自动补偿。

新增紧凑的 `AlsStandingCycleLifetime` 及候选生命周期决策。已初始化、已更新、
最近实际更新帧和 epoch 分开保存。外层未访问 Cycle 时不会伪造更新；显式
Save 初始化可先发生，首次 Update 延后发生，仍只有一次来源和初始状态进入。
已经更新后的相关性间断则重新初始化方向、滤波和九个外部动画来源。

实际站姿图消费该结果；Main 的显式 Save 来源初始化不重复执行，整机相关性
重入则由收集阶段在同一个共享来源候选里执行。候选失败不推进已提交来源 epoch。
移除独立路径的启动补偿；从未访问的 Idle Cycle 来源保留 epoch=0，已经建立
Cycle 生命周期却缺少来源初始化时拒绝提交来源。

Sprint 位于内部 Save 后面，有不同的初始化边界。生命周期组件按初始化计数
决定是否重置 Sprint BlendList，而不是按整机相关性间断无条件清空。相同计数
保留混合状态；计数不同重置，只有 global frame 不同不会重置。现有生产路径
仍使用固定初始化计数，完整内部缓存计数传播属于后续接线。

方向初始进入反馈在生命周期初始化时记录，延后首次 Update 不再重复进入。
来源时间和反馈变化会影响事件时序：Main 30 Hz 路线来源事件由此前 16 变为
14，60/120 Hz 仍各 11。本批重试与事务检查通过，但没有用完整 UE 图逐帧轨迹
确认此事件序列，不能只因单元测试通过就宣告它已经与原生全图一致。

## 源码依据

只读核对 `../UnrealEngine` 下的原生实现，无 UE 插件修改或资源重新导出：

- StateMachine 的首次更新和相关性重置条件，以及初始化进入初始状态的行为。
- `AnimNode_LinkedAnimGraph.cpp` 的 InitializeSubGraph 与所有 InputPoses
  初始化，未参与当前更新的输入也沿初始化路径进入。
- `AnimNode_SaveCachedPose.cpp` 的初始化计数门控。该实现不应使用外部方向机
  的相关性间断直接替代内部 Save 的初始化计数。
- `AnimNode_BlendListBase.cpp` 的初始第一子项权重、LastActiveChildIndex 清理
  和所有子项初始化；`AnimNode_TwoWayBlend.cpp` 的相关性/滤波初始化。

## 验证

- Core 生命周期专项 7/7：未访问、同帧/延迟首更、相关性重入、缓存计数与 global
  frame 区别、重试、跨角色和 epoch 溢出拒绝。Core 常规 1927/1927，仍按既有
  范围排除 P5A golden / trace schema；Import 全套 1171/1171。
- Godot 新增 30/60/120 Hz 实际站姿回放：Idle 未访问来源为零，进入 Sprint，
  间断后进入 Walking，九来源 epoch 各增加一次，Sprint 继续混合而非跳变归零。
  三次末来源 epoch 溢出拒绝，重试生命周期、Sprint 和完整同步状态一致。
- 既有 Sprint 1260 帧、Detail 1890 帧、Standing 5040 帧、Pivot 5040 帧，
  17337 个来源时间检查、1260 个时间归属检查和 5040 个移动输入帧通过。
  交错步回放包含九次换髋、371 个等待帧、九次中断回滚；活动分配 0 B。
- Main 六缓存及旧姿势入口各 1680 帧通过，来源初始化/epoch 断言现在分别统计
  Save 回调与整机相关性重入，而非假设来源 epoch 只可能随 Save 回调变化。
  六缓存入口原始 Standing 对照 2360 次，既有来源溢出、Slot 故障及重试保持。
- 有序 Standing 初始化 210 帧，三次来源溢出拒绝、活动零分配通过。
- Worker 单/并行各 180 帧，各十个来源事件；结果摘要 A9DF0647AFC3574C、
  完整姿势摘要 04D4A5651B87E0E4 保持。并行晚期事件失败回滚通过，回调泄漏零。
- 最终优化 Godot 构建零警告、零错误。

TRX：`artifacts/test-results/standing-cycle-lifetime/`。
最终日志：`artifacts/standing-lifetime-final-standing.log`、
`standing-lifetime-final-main.log`、`standing-lifetime-final-initialization.log`、
`standing-lifetime-main-legacy.log`、`standing-lifetime-worker-{single,parallel,rollback}.log`。

保留旧测试失败记录：Idle 和来源时间测试将连续状态更新重复放在同一 frame id，
新增时序校验正确拒绝，现改为递增身份；Main 旧 epoch 断言漏掉整机重入来源。
新增测试还出现 InlineArray 属性直接赋值的编译错误，以及未恢复组件准备的
临时姿势造成后续首帧回滚检查失败；分别改为复制来源候选、测试结束恢复已提交
姿势。没有放宽运行时身份校验、回滚断言、分配预算或关闭失败检查。

## 剩余工作

本批完成来源和状态生命周期的一部分实际接线，并非整个 Standing Cycle 完成。
方向层内部各 Save 的完整初始化/骨骼缓存/分作用域求值，初始化后首次 Update
之前的空 BlendSpace 样本与精确姿势/曲线，以及完整初始化计数传播仍待完成。
现有方向最终姿势采样不能冒充这套完整缓存生命周期。

随后继续 Main Movement、实际 Slot/Montage、原生位置的最终惯性化与统一生产
事务；完整 Demo、最终曲线、动态上身、完整脚部约束及 P5A-P7 范围保持。
本批未运行新的完整原生图逐帧探针、移动截图、平台路线、短性能矩阵或十分钟
验收。平台脚锁、滑步、交错步、上身和性能门禁仍未完成或未通过。已确认键鼠
输入未改；没有提交、回滚或合并现有用户工作区。
