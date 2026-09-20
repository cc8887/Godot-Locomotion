# Crouching Cycles 整体初始化与延迟姿势求值

日期：2026-09-11。完整性恢复第六十四批。

## 问题与完成内容

此前 Main 与蹲姿来源整合在 Save 初始化时只初始化八个外部动画来源，内部
Cycles/Direction 状态机与 Stride 仍等待首次 Update 建立，再用抑制标记跳过
重复的来源初始化。这没有完整表达原始图的 Initialize / Update / Evaluate
生命周期，尤其不能可靠表示已初始化但延后多帧才首次更新或求值的子图。

`AlsCrouchingCycleRuntime.Initialize` 现在同时建立根机和方向机的初始状态、
重置 Stride 滤波状态，并依正式顺序初始化来源 54/49/50/55/51/52/53/48。
此时状态是 HasInitialized 且未 HasUpdated，不推进状态时间或注册更新贡献。
Main 和蹲姿来源整合改用此入口，移除两处来源初始化抑制补偿。

首次 Update 无论当帧还是延后发生，都不重复初始化八来源或方向机初始状态。
已经更新后的方向层相关性重入仍重置方向状态机，但不重置外部来源；整个
Cycles 的相关性重入则重置两台状态机、Stride 和八来源。

初始化 epoch 随 Cycle 候选状态持久保存。姿势消费者与已提交 epoch 比较，
识别尚未消费的整体初始化；不会依赖仅表示当前调用执行过来源初始化的
InitializedSources 标记。缓存初始化和 epoch 仅随成功的姿势候选提交；失败
后从上次提交状态重试。内层 Save 的既有初始化/骨骼计数规则继续负责去重，
不会凭空增加全局遍历计数来强制重置缓存。

控制节点的最后有效 Diagonal Alpha 在 Update 中保存。冷初始化为零，重新
初始化保留此前值。首次 Update 前，Stride 取 WalkPose，Lean 不读取上一周期
残留的样本时间；首次 Update 后恢复实际 Stride、Diagonal、Lean 完整求值链。

## 原生依据

本批只读本机 `../UnrealEngine` 源码，未修改 UE 插件或重导出资产。

- `AnimGraphRuntime/Private/AnimNodes/AnimNode_TwoWayBlend.cpp`：Initialize
  初始化 A/B 两侧，清理相关性标记与滤波状态；Update 前 Evaluate 选择 A。
- `AnimGraphRuntime/Private/BoneControllers/AnimNode_SkeletalControlBase.cpp`：
  Initialize 不清零此前的 ActualAlpha，控制权重在 Update 中更新。
- `AnimGraphRuntime/Private/AnimNodes/AnimNode_ApplyAdditive.cpp`：Initialize
  初始化 Base/Additive，但不重置此前的 ActualAlpha。
- `AnimGraphRuntime/Private/AnimNodes/AnimNode_BlendSpacePlayer.cpp`：
  Reinitialize 清空 BlendSampleDataCache。
- `Engine/Private/Animation/BlendSpace.cpp`：空样本缓存返回基准姿势；合法加法
  BlendSpace 的基准是 additive identity，因此初始化后、Update/Tick 前没有
  旧 Lean 骨骼偏移或曲线贡献。此规则不能推广为所有动画来源都可忽略初始求值。

上述为源码语义核对，未新增完整原生动画图逐帧探针，不宣称全图 1:1 已验证。

## 验证

- 新增 Import 7 项：同帧/延迟首次 Update、两侧初始状态、完整重初始化、
  保留控制 Alpha、来源初始化故障重试、epoch 溢出及未更新快照身份保护。
  Cycles 专项 33/33，全套 Import 1171/1171。
- Core 常规 1920/1920。范围沿用既有排除 P5A golden / trace schema 的过滤器，
  本批不据此宣告这两类通过。优化 Godot 构建零警告、零错误。
- Godot 30/60/120 Hz 共 210 帧初始姿势与真实 WalkPose 骨骼/曲线一致。
  非法的旧样本时间没有进入未更新 Lean。三个延迟首次求值场景在
  InitializedSources=false 时仍初始化内部缓存；三个失败候选拒绝提交后，
  重试保留缓存初始化和姿势结果。重初始化保留 0.6 控制 Alpha。
- 原 Cycles 1248 帧通过：六次整体初始化，588 帧方向停用，2385 次缓存求值，
  3744 次输出曲线检查，五个来源事件，三个实际取样故障拒绝，活动分配 0 B。
- 蹲姿来源整合 1671 帧通过：五状态、29 个事件、六次缓存初始化、六次相关性
  初始化、104 次共享读取、15 个非法操作拒绝、来源收集活动分配 0 B。
- Main 六缓存与旧入口分别通过 1680 帧；六缓存入口包含 2360 次原始 Standing
  姿势/曲线对照。现有来源初始化溢出、Slot 姿势故障及重试验证保持通过。
- Worker 单线程和并行各 180 帧、十个来源事件通过。结果摘要
  A9DF0647AFC3574C、完整姿势摘要 04D4A5651B87E0E4 保持。并行晚期事件
  故障回滚通过，事件回调泄漏零，运行时和全部被检验的姿势/结果 banks 恢复。

TRX：`artifacts/test-results/crouch-cycle-initialize/`。
Godot：`artifacts/crouch-cycle-initialize-pose.log`、`-sources.log`、`-main.log`、
`-main-legacy.log`、`-worker-single.log`、`-worker-parallel.log`、`-worker-rollback.log`
均使用相同 `crouch-cycle-initialize` 文件名前缀。

## 剩余范围

完成的是可复用 Crouching Cycles 生命周期及整合夹具接线。Standing Cycle
内部方向机、滤波、Sprint 混合和内部缓存的整体初始化仍待推进。Main Movement、
实际 Slot/Montage、原生位置的最终惯性化和统一生产事务随后接入 Demo。

最终播放速率/Stride/Yaw 等曲线反馈、动态 Layering/Add/LS/Lean/IK 分层和完整
Foot IK/Foot Lock/pelvis 继续按 P3/P4 完整性补项实施。P5A 通用通知/同步/动作、
P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/Pose Recovery/
完整 Camera、P7 十角色全质量十分钟性能与人工验收全部保留，音频仍延后。

本批无新移动截图、完整 UE 图对照、平台路线、短性能矩阵或十分钟采样。原有
平台脚锁失败、短矩阵超预算、滑步/交错步/上身视觉问题保持未完成或未通过。
未修改已确认键鼠输入，未提交、回滚或合并用户工作区。
