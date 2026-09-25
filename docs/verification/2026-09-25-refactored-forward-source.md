# Refactored 站立前向缓存源更新

## 实现

新增 `AlsRefactoredForwardSource` 与 `AlsRefactoredForwardSourceRuntime`，承接原站立 `Move Forward` 缓存的实际源图：

1. `Move Forward Base` 共享缓存 → 原 Forward BlendSpace；
2. GameplayTagsBlend 以精确 `Als.Gait.Sprinting` 标签选择走跑或冲刺分支；
3. 冲刺分支 TwoWayBlend 在 Sprint 与 Sprint Acceleration 播放器之间混合；
4. 外层 TwoWayBlend 以 SprintBlockAmount 将上述结果混回同一个 Forward Base 缓存。

Profile 验证 runtime 与编辑图链接、共享 cache 身份、原资源、属性绑定和混合政策。使用既有 MovementPlayers 的 host ID、StartPosition、PlayRate/RateBasis，不创建第二套时钟。蹲伏图/外 catalog 被拒绝。

Gait 进入冲刺时间 .2 秒、退出 .3 秒，采用原 Cubic；Core `AlsOverlayPoseWeights.BlendList` 增加对已有 Cubic 算法的准入，旧 Linear/HermiteCubic 行为不变。SprintAcceleration 将 0..0.25 映射为 0..1，保留未钳制的插值历史，增加速度 20、减少速度 4，再钳制最终 alpha；相关子节点重新激活时重置其播放器。

Update 保留原分支遍历顺序、零权重旧分支更新、Inactive 标记、隐藏子树不推进插值、隐藏期间的初始化请求，以及 Prepare/Commit/Cancel 候选事务。两个 BaseCache 读取分别输出原 reader identity、weight、inactive，不能加成一条请求；未来统一缓存调度器需与其余读者一起按 UE 的最大权重上下文规则选择。

SourceInputs 仅包含需要更新的两个冲刺播放器，供共享 SourcePlayerRuntime 批量推进。BaseReads 仍由外层缓存调度器处理。SourceInactive 单独返回给未来图宿主的消息/Notify 路由，当前没有宣称它已被所有下游消费。调用者必须在对应缓存选定更新上下文后每帧调用一次，并协调播放器与本 owner 的事务；本 owner 不执行缓存调度、pose 求值或真实 Parent 更新。

## 原生源码核对

只读本地源码：

- `D:/AdvancedLocomotionSystemV/Plugins/ALS/Source/ALS/Private/Nodes/AlsAnimNode_GameplayTagsBlend.cpp`：GetTags().Find(ActiveTag)，精确相等，无父标签匹配。
- `D:/UnrealEngine/Engine/Source/Runtime/AnimGraphRuntime/Private/AnimNodes/AnimNode_TwoWayBlend.cpp`：AlphaScaleBiasClamp、分支相关性、激活重置与更新次序。
- 同目录 `AnimNode_BlendListBase.cpp`：旧分支零权重更新、Inactive 分支消息与默认 child update mode。
- `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/AlphaBlend.cpp`：Cubic 与 HermiteCubic 使用不同 float 计算路径。

## 验证与失败记录

新增 6 项测试，涵盖 10 次资源变异拒绝、.2/.3 秒切换及中途反转、双缓存读者身份/Inactive、精确标签、加速插值/激活重置/隐藏初始化。30/60/120 Hz 合计 630 提交帧，每帧 source 更新取消重试与独立实例相同，真实冲刺源经共享播放器采样并回滚重试时钟；只提交冲刺请求，未以此替代完整缓存系统验证。

首次 `forward-initial.trx` 的 5 个测试因同一资源编译错误失败：误要求 authored Node 的 BlendTime 数组与 runtime 相同。检查原图发现 Node 保留 .1/.1 默认值，暴露引脚实际覆盖为 .3/.2；修复为核对 runtime 和暴露引脚，不放宽运行时要求。`forward-pins.trx` 当时 5 项通过；补充切换测试及隐藏初始化场景后 `related.trx` 共 27 项通过（新 6、方向姿态图 4、移动播放器 5、武器完整 Overlay 原生对照 12）。首次失败留档。

Core 既有 TransitionStack 原生数学参考回归 `core-stack.trx` 15 项通过；Godot Optimize 构建 0 warning / 0 error。没有新增 UE 连续前向子图 oracle，没有 UE 插件改动/重新导出，没有 Godot 场景/全量/十分钟性能验收，未宣称本批与原生连续姿态逐值相同。

## 后续范围

继续统一方向状态与所有缓存的延迟更新顺序/初始化/一次采样生命周期，接入实际前向与六方向姿态混合，补齐 Parent 的 SetHipsDirection/ActivatePivot 消费；再完成其余 stance 状态机与全角色宿主。普通 Demo 尚未切入完整 Refactored 链，本批不能证明可见滑步、换髋或 Ragdoll 完整修复。

用户未提交文件不纳入本批；Ragdoll/Get-up/Pose Recovery、Mantle、Camera、最终性能等旧任务保持，音频、道具物理及头颈诊断继续暂缓。
