# 统一 BaseLayer 帧所有者与生产来源绑定

日期：2026-09-12，第八十二批。上一批已完成输出链组件，本批推进完整性路线
第 2 项的生产绑定、来源停用和统一提交边界。没有完成全部 Demo 接线。

## 生产路径的实际变化

新增 `AlsMovementGraphDefinition`，由主线程上的 `AlsP3RuntimeContext` 为 Cycle
配置编译一次完整定义：75 个播放来源、109 个采样、共享 P5 绑定、Grounded/
Crouching/Air/Landing/Main Movement/BaseLayer 正式配置和骨骼依赖。

`AlsP3RuntimeContext.SourceBindings` 现在来自这份完整定义；
`AlsP3WorkerRoot.Configure` 用同一份 Sources 构建角色动画库。普通非 Cycle
配置保持原路径。没有为同一角色启动第二个调度器或迁移旧历史到不同绑定。
已有 Worker 仍用原控制器求值，Standing 消费的是新完整来源表中的地面部分。

这项是生产接线；它不等于完整 Main Movement 已替换 Demo 的旧空中/蹲伏路径。

## 统一运行时

新增 `AlsBaseLayerFrameRuntime`，独占一个角色的 Grounded、Main Movement 和
BaseLayer 惯性化所有者，提供 Prepare/Evaluate/Commit/Discard：

- Prepare 从 BaseLayer Slot 来源权重决定是否遍历主移动图；活动时使用实际
  requester/缓存 handler 98，并保持来源权重、root-motion 权重和活动标志。
- 不活动时使用新 `MainMovement.PrepareHidden`：只执行空的共享同步批次和
  来源事件阶段，清空当帧参与者/notify ticks，保留按 player 索引的时间和 epoch，
  不推进主状态过渡栈、地面缓存、空中/落地输入或惯性化历史。
- Evaluate 将真实原始主移动姿势交给惯性化，再调用显式 BaseLayer Slot 提供者。
  Grounded Slot 与 BaseLayer Slot 分别传入，避免把它们当作同一个动作节点。
- Commit 先检查两个子候选都已求值、身份一致，再统一发布。晚期故障保留旧
  身份/来源；Discard 一并取消两个子候选。后续 LayerBlending/IK 和事件验证
  仍须由外部控制器完成后才调用 Commit。
- 本节点接收来源惯性化请求；缓存被跳过时，向其他祖先接收者转发当前队列的
  最短持续时间，自身重复请求不重复转发。当前正式配置无自定义请求 profile，
  标量最小值等价于逐个转发后取最小值；不扩展成已支持任意 UE 请求类型。

本批没有实现真正 Montage 播放/中断和 BaseLayer Slot 自身向祖先节点请求
惯性化的策略，也没有把这些请求错误送给位于 Slot 下方的节点 98。

## 验证与边界

优化构建零警告、零错误。均在成功构建后运行 Godot；两个分层组件的原生算法
未改动，Core/Import 完整套件未重跑。本批无 UE 启动或新原生数值探针。

1. 生产 Worker single/parallel 各 180 帧通过，明确检查 Sources 为 75/109、
   来自同一 context 绑定。结果摘要 `21E164D829153157`、全身姿势摘要
   `CF9225D4DE9B2C8B` 与前批保持；各有 10 个真实来源事件跨主线程提交，
   generation、亲和性、旧代拒绝和可见性门禁通过。
   日志：`artifacts/movement-binding-worker-single.log`、`movement-binding-worker-parallel.log`。
2. 生产 Worker 两模式的 `late_source_event` 故障注入通过：frame 25 拒绝候选，
   通知回调泄漏 0，来源同步、运行状态、姿势和 P4 banks 回滚保持。
   日志：`artifacts/movement-binding-worker-late-single.log`、`movement-binding-worker-late-parallel.log`。
3. 新 `scenes/tests/base_layer_frame_smoke.tscn` 通过：30/60/120 Hz 两种起跳轨迹，
   总 3360 帧，覆盖五个主姿势状态；271 帧完整覆盖、3 组从覆盖状态启动、15 次
   来源重入、51 来源事件、66 惯性化请求、6 次标量跨接收者转发检查、6 次最终
   Slot 晚期故障、12 次非法提交拒绝。每帧取消/重试的骨骼、曲线、来源时钟和
   事件逐项相同，两个子所有者提交身份一致。
   最终日志：`artifacts/base-layer-frame-runtime-cold.log`。
4. 前批独立 Main Movement + BaseLayer 尾部回归另跑 3360 帧，计数保持，日志
   `artifacts/base-layer-frame-tail-regression.log`。

新联测仍提供受控角色输入及合成 Slot 姿势，不是实际 Demo 的全图多线程证明。
实际片段在覆盖入口没有活动 Notify State，因此 `hidden_end_events=0`；本批
证明不保留/重复来源事件，不能据此声称覆盖了真实 Notify State End 正例。
跨接收者检查直接提供另一个 handler 上下文，验证路由/min/self 去重；不是
完整多祖先缓存图的 UE 原生对照。

首次联测失败来自测试调用含 InlineArray 的状态结构的内建 Equals。改为逐项
比较状态标志、序号、权重、过渡栈及 edge 后通过；同时检查隐藏帧提交之后
状态历史不变。未改变动画规则或放宽容差。失败日志保留在
`artifacts/base-layer-frame-runtime.log`，暖启动通过日志为 `base-layer-frame-runtime-rechecked.log`。

## 下一项

将真实 `AlsFrameInput` 和已提交曲线历史适配到统一所有者，再将它的 BaseLayer
输出接进现有 AnimationTree 的基础输入和后处理，避免旧控制器同时推进另一套
主移动来源。复用现有晚期发布/回滚边界，而不是在姿势修正或事件验证前提交。
随后闭合最终曲线反馈、真实 Slot/ActionPlayer 和动态上身/脚部消费者。

Demo 的滑步、换髋、上身、平台仍未验收；P5A–P7、Overlay/全部道具、Mantle/
Roll/Root Motion、物理恢复、完整 Camera 和十分钟性能预算范围保持，音频暂缓。
