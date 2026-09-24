# Refactored Transition 动态 Montage 接入

## 实现

在 `D:/GodotALS` 的 `main` 为共享 Montage 系统增加独立 Transition Slot（id 12），原 0–11 编号保持不变，Slot 数量和通知掩码边界扩展为 13。

`AlsRefactoredTransitionMontages` 编译真实骨架 Slot inventory 并与 nativeText 交叉检查：Transition、TurnInPlaceStanding、TurnInPlaceCrouching 共用 Grounded 组，原生组索引为 1。宿主显式传入自己的组编号和两条 Standing Transition Sequence 的动画编号；原生索引不冒充宿主编号，也不复用旧 V4 animation set。

资源来自上一批四武器通知 profile 的源路径，绑定已校验哈希的原始 Sequence、SK_Als、时长、mesh-space additive（2）、禁用 root motion 和单位 RateScale。四种武器 profile 必须完整且来自同一 catalog。

Play 在同一个已 Begin 的共享 `AlsMontageRuntime` 上依原队列顺序创建独立 Sequence Montage 实例。它先验证整个批次的帧、ordinal、原始参数和 bank 资源一致性，再发起播放，因此尾部非法请求不会留下已播放前缀。正常重复请求不会合并；组内替换沿用原共享播放器，当前帧已冻结的 Evaluation 不会被通知播放反向改写。

新增共享 StopSlots，按传入 Slot 顺序遍历实例；只停止 DesiredWeight>0 的活动实例。负时长使用该实例的原始 BlendOutSeconds，非负时长覆盖时长，保留原 BlendOutOption，既有 outgoing fade 不重新设置。导入适配器提供 StopTransitionAndTurnInPlace，按 UE 原顺序停止 Transition、Standing Turn、Crouching Turn。

原始语义依据本机 `AlsAnimationInstance.cpp` 的 PlayQueuedTransitionAnimation/StopQueuedTransitionAndTurnInPlaceAnimations，以及 `AlsMontageUtility.cpp` 的 StopMontagesWithSlot。Slot 名字来自 `AlsConstants.h` 和原骨架导出。

## 验证

- 新增 Import 8 项通过：四武器真实 machine→notify→物理 Montage，三个频率的连续播放，非法批次/错误 bank/组/宿主编号拒绝。
- 每种武器都验证单帧两个通知创建两个不同实例、先前转身被同组替换而 Head 不受影响、起播位置/速率/additive/Slot 正确、停止不改已有 fade、显式与原始淡出、整批 discard/retry 后实例身份一致。
- 30/60/120 Hz 合计 840 帧，重复请求、交叠、自然结束及逐帧晚取消重试后候选/冻结评估一致。
- Core Slot 测试扩展到全部 13 个 Slot，覆盖姿态/曲线隔离、独立组和掩码越界。
- 相关 Core Montage 回归 **160 通过**；相关 Import **91 通过**；均 0 失败/跳过。
- 最后将停止循环顺序明确为 Slot→Instance 后，新 Import 8 项重跑通过；Optimize 再次构建 **0 警告、0 错误**。
- 产物：`artifacts/refactored-transition-montage/` 中的 TRX。本批无编译或测试失败。

未新增 UE 播放/停止连续 oracle，未改 UE 插件或资产、未启动 Godot 场景、未跑全量/十分钟预算/打包验收。

## 后续边界

已接共享逻辑播放器中的真实 Montage 实例，不等于普通 Demo 已播放这些动作。还需实际原生 EventGraph/父函数消费对照，Transition 的原始姿态/曲线采样与 Locomotion Slot 求值，四武器 state 源和整图姿态，以及统一宿主资源绑定。bStopTransitionsQueued 与 worker 直接调用的覆盖队列尚未完整移植；本批 Stop API 是显式执行接口，不声称实现那套排队生命周期。

Overlay 完整姿态仍 9/13；普通 Demo 未切换。实际移动状态机、Ragdoll/Flail/Get-up 整体验收和其他旧缺口继续保留。用户未提交文件未纳入本批，道具物理、音频、头颈诊断继续暂缓。
