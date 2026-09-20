# BaseLayer 输出链与候选惯性化历史

日期：2026-09-12，第八十一批。承接完整性路线第 2 项；不是 Demo 视觉验收。

## 源图与原生语义

正式 `v4_main_movement_graph.json` 的 BaseLayer 接线是：

`Main Movement (245) -> Inertialization (98) -> BaseLayer Slot (101) -> Root`。

惯性化在 Slot **之前**，它的历史只记录平滑后的移动姿势，不能记录蒙太奇混合
后的输出。BaseLayer 也不是角色最终骨骼图，后面仍有 LayerBlending 与 IK 等。

新增 `AlsBaseLayerCompiler`，沿 Root 的实际连接校验该顺序，并与
`v4_pose_cache_graph.json` 的 compiled inventory 对照节点身份和属性。当前
支持的正式策略：Slot 不强制更新来源；惯性化无默认 BlendProfile、无过滤骨骼/
曲线、不在重新相关时清空历史、开启缓存跳过请求转发、无自定义回调或 tag。
不支持的配置拒绝导入，不静默套用默认值。

本地 UE 对照：

- `../UnrealEngine/Engine/Source/Runtime/AnimGraphRuntime/Private/AnimNodes/AnimNode_Slot.cpp`：
  Update 按真实 SourceWeight 决定来源更新，并在来源淡出时标 inactive；Evaluate
  在完整覆盖时不求值 Source。Slot 自身的惯性化请求寻找的是其**祖先**接收者，
  不能发给位于它下面的节点 98。
- `../UnrealEngine/Engine/Source/Runtime/Engine/Private/Animation/AnimNode_Inertialization.cpp`：
  Update 安装请求/缓存跳过消息后更新来源；只累计实际 Update 的 delta。
  当前正式配置不因失去相关性而清空历史。

本批只读本地源文件，没有启动 UE 或产生新的原生数值探针。

## 实现边界

新增 `AlsBaseLayerPoseRuntime`，拥有独立的 committed/candidate 惯性化历史和
Slot 前一帧来源权重；复用已有经原生对照的惯性化算法，Godot 单位为米。

- Begin 返回带真实 requester 98 和 skipped-handler 98 的来源上下文。该
  上下文的权重、root-motion 权重与活动状态传入 Main Movement。
- 收集来源图发出的惯性化请求；求值时先处理真实 Main Movement 原始骨骼和
  按名称排列的曲线，再调用显式 BaseLayer Slot 姿势提供者。
- 完全覆盖时返回“不更新来源”，要求调用者跳过 Main Movement，并向 Slot
  提供空来源；隐藏期间不推进惯性化时间或历史。
- 必须完成合法输出后才能 Commit。取消、缺失动作提供者、非法最终骨骼和
  晚期 Slot 异常均不发布历史。拒绝错误角色、generation、旧帧或错误 requester。

这仍是输出链组件，**不是通用 Montage/ActionPlayer 的完整实现**。外部所有者
仍负责蒙太奇时钟、Slot 权重/注册、Slot 自身向祖先发送请求，以及跨多个惯性化
接收者的缓存请求转发。当前主移动联测只有一个接收者；其 skipped callback
仍是夹具，不能据此宣布跨接收者转发完成。没有将蒙太奇请求错误路由给节点 98。
外部事务所有者仍须在最终骨骼、后续事件验证全部通过后统一提交移动和尾部状态。

## 验证

`MainMovementRuntimeSmoke --base-layer` 在原 3360 帧真实来源回放中接入尾部：

- 30/60/120 Hz、两种起跳脚/速度，共 3360 帧，204 帧无 Slot 覆盖的姿势实际被
  惯性化改变，168 帧经过部分 BaseLayer Slot 混合。
- 每帧取消并重试，最终骨骼和曲线逐项完全相同。6 次在主移动及惯性化求值
  **之后**注入最终 Slot 异常，提交身份、来源时钟和历史保持，重试结果一致。
- 原始图的五姿势状态、53 来源事件、18 状态事件、51 惯性化请求、210 帧真实
  移动落地和 12 次实际动画时间退出仍通过。

`BaseLayerPoseChecks` 使用只接收来源姿势的独立惯性化实例作顺序对照；故意给
Slot 大幅不同的骨骼/曲线值，验证其不会进入来源历史。三种帧率下覆盖 9 个
完全隐藏帧、21 个平滑输出帧、24 次非法操作拒绝、6 次缺失提供者/NaN 故障。
重入后不计入隐藏期间时间，消失曲线也经过历史对照。

最终日志：`artifacts/base-layer-movement-runtime-final.log`。
不启用尾部的原始图回归另跑 3360 帧通过，计数与前批一致，日志为
`artifacts/base-layer-raw-regression.log`。
测试仍使用合成 Slot 姿势；反馈仍使用原始 BasePose_CLF，以保留原始图回放
基线，尚未测试最终曲线反馈闭环或真实 gameplay 输入。

Import 定向测试 21 项通过（新增 BaseLayer 15 项、原 Standing Slot 6 项）。
Godot 优化构建零警告、零错误。首次构建因当前 .NET 8 没有
`JsonElement.DeepEquals` 失败，改为 `JsonNode.DeepEquals` 后测试/构建通过，
没有在构建失败后运行旧 Godot 程序。本批没有修改 Core 算法或正式 UE 资产。

## 尚未完成

Worker/Demo 接入、真实 Montage/ActionPlayer/缓存转发、最终曲线 presence 与
反馈、YawOffset 到角色旋转、动态上身、Foot IK/Lock/pelvis 均保留未验收。
不把输出链组件测试通过当作起步滑步、换髋、双臂姿态已修好。
原 P5A–P7、全部 Overlay/道具、Mantle/Roll、Root Motion、物理恢复、Camera
和十分钟性能预算不缩减；音频暂缓。
