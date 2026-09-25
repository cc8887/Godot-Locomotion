# Standing 状态回调的真实动作消费

新增 `AlsRefactoredStandingActions`，绑定原 Standing 的三个 Blueprint 函数图：左右 `PlayStop…TransitionAnimation` 与 `StopTransitionAndTurnInPlaceAnimations`。校验函数入口、执行连线、GetParent 接收者、无后续调用、图闭包、原资源及参数。

四个 Stop53 状态入口分别请求 `A_Als_Stop_Left/Right`，保持 .2 秒淡入/淡出、1.5 播放速率、.4 秒起点，以及关闭 StandingIdleOnly 门控。原 Stand Transition 不能替代这两个 Stop 序列。资源使用原 Skeleton、mesh additive、无 RootMotion 和 Grounded 组。

离开 Idle 和进入 Movement 都调用停止函数。Idle→Movement 的两个回调均按原顺序写入线程候选队列，最后值覆盖；物理 bank 在后续消费时停止 Transition、TurnInPlaceStanding、TurnInPlaceCrouching。新增 queue-bank 身份校验，防止向另一角色/另一 bank 的队列写入已验证但不属于它的资源。

现有 `AlsRefactoredTransitionPose` 增加经过 StandingActions 资源校验的入口，复用原 mesh additive 采样器。采样读取物理播放时间，不新增时钟。

## 验证

- 只读核对本地 ALS C++ 的 PlayTransitionAnimation/StopTransitionAndTurnInPlaceAnimations 及 PlayQuickStopAnimation，并解析现有 Standing 原图。没有新 UE 启动、导出、插件修改或 DataValidation。
- 首次四项新测试失败：只处理 Movement entry，漏掉 Idle exit。根据原图补齐两个入口，测试改为验证两个原有停止回调，未忽略回调。
- 第二轮新四项失败：测试合并左右资源的曲线布局时未去重，被现有布局门禁拒绝；修正测试布局集合，未放宽门禁。同期37项既有 Transition 回归通过。
- 最终新四项全部通过：四脚部状态、真实候选队列、原播放起点、外来 bank 拒绝、取消重试、停止优先/延后播放、左右动画实际 Slot 混合与结束回基底。共480帧、逐帧重放比较79骨和曲线。
- Core TransitionQueue 六项通过。以上最终相关证据合计47项通过；历史失败 TRX 保留，不将失败轮称为全绿。
- TRX：`artifacts/tests/standing-actions/standing-actions-initial.trx`、`standing-actions-related.trx`、`standing-actions-final.trx`、`standing-action-queue.trx`。
- Godot Optimize 构建0警告0错误，diff whitespace检查通过。

## 未完成

`StopQuick` 是独立的主线程通知路径：根据原 QuickStop 设置、旋转模式和剩余朝向角选择过渡，不能直接复用上述固定 Stop 请求。本批尚未接入。

当前停止入口可向共用 `AlsTransitionQueueRuntime` 写入；Rest Parent 自有 Dynamic 请求尚需与它统一为单一、按原图调用顺序覆盖的 Transition 队列，随后接入完整角色宿主。这批受控测试不等于整图或普通 Demo 已完成。

尚需 UE 连续 Parent/Slot/停止动作原生对照、Crouching、统一宿主、普通 Demo 及 Godot 视觉/十分钟性能验收，所有 Ragdoll/Get-up 等既定目标继续保留。用户改动、暂缓音频/道具物理/头颈问题保持不动。
