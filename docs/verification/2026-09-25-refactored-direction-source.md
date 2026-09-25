# Refactored 方向子图共享缓存调度

## 实现范围

新增 `AlsRefactoredDirectionSourceProfile`、`AlsRefactoredDirectionSourceRuntime`，接通方向机器候选更新 → 原 CallFunction 相关性 → 四向 MultiWay 权重 → UseCachedPose 延迟请求 → 原顺序缓存源更新 → 共享 SourcePlayerRuntime 输入。

Profile 从原蓝图的 `orderedSavedPoseNodes` 转换 compiledNodeIndex 为 propertyIndex，检查完整缓存集合后提取方向子图依赖闭包。Standing 顺序为 133、136、138、137、132、134、135（七缓存、26 读者）；Crouching 为 47、51、46、49、50、48（六缓存、24 读者）。Standing Forward 的更新在 Forward Base 之前，允许它在 Drain 期间产生基础缓存的两条新请求。

复用 Core `AlsPoseCacheTraversal`，每缓存只更新一次，最大权重读者的完整上下文胜出；相同权重保留先到者。没有把重叠方向状态的读权重求和，没有把 Forward 内部两个 Base 读者合并。SourceContexts 保留原状态祖先链、RootMotionWeight、更新遍历 counter 和 Inactive 信息；玩家依原缓存顺序加入共享输入批次，一帧重复 player ID 被拒绝。

按外部初始化 counter 对每缓存去重初始化；进入新方向状态时不会重复初始化已在同一初始化遍历处理过的缓存。隐藏缓存保留待重置标记；Forward 初始化递归到其 Base 缓存，并保留冲刺子树自己的待初始化状态。这里不根据“间隔几帧没用”擅自重置 cache。局部候选初始化 counter、pending reset、Forward 历史和 callback 相关性均支持取消重试；源播放器时钟仍归共享 SourcePlayerRuntime 所有。

本地引擎 `AnimNode_SaveCachedPose.cpp` 的 PostGraphUpdate 最大权重/严格大于选择逻辑，与 Initialize 的 counter 逻辑进行了只读核对。其 UpdateCounter 在当前实现中没有同步语句，沿用此前已验证的 Core 缓存生命周期规则，不新增相关性间隔重置。

SetHipsDirection 的候选命令按原状态更新次序输出。本地 ALS `AlsAnimationInstance.h` 方法体仅赋值 GroundedState.HipsDirection；本批没有接全局 Parent 状态提交，也不派发 ActivatePivot。命令不是已经发生的 gameplay 副作用。

## 验证

新增 10 项测试：

- 两 stance 原缓存顺序、完整集合与前向依赖检查，5 次缺失/重复/依赖倒置变异拒绝。
- 两 stance 的前后半权重过渡：同一缓存由两个状态读取时选 .25，不能相加成 .5；同权重保留 Forward 上下文。Standing 内部 Base 再取两条 .125 请求中的第一条。新方向进入不会重置已经初始化的同一 cache player。
- 两 stance × 30/60/120 Hz 共 840 提交帧：每帧取消重试，与独立机器/独立 source runtime 比较全部输入、上下文、cache 更新和回调命令。涵盖多过渡、零权重通道、零 delta、counter 回绕、显式实例重置、隐藏冲刺、嵌套状态上下文。真实所需动画经共享 SourcePlayerRuntime 采样，撤销后时钟历史一致；未包含新的 UE 连续源更新参考。

`artifacts/refactored-direction-source/source-initial.trx`：10 通过，无初始失败。后补充实例初始化必须匹配机器重初始化的检查；最终 `related.trx`：26 通过（新 10、前向源 6、方向原生机器参考 6、stance callback 4）。Core 缓存生命周期、原生缓存参考、精确姿态缓存相关测试 `core-cache.trx`：41 通过。Godot Optimize 构建：0 warning / 0 error。

## 边界与下一步

这是方向机器自身的依赖闭包。外层 Movement/Movement Details 等缓存及其它读者尚未纳入；完整 stance 接入时必须共享同一个全图调度器，不能把本闭包单独推进后再附加迟到读者。带外层 inertialization requester/skipped handler 的调用当前明确拒绝，不能据此宣称那些消息已被消费。

下一步接缓存姿态的逐帧一次求值、Standing Forward 实际姿态混合、六方向状态 ModifyCurve 与有序逐骨过渡混合；然后补其它 stance 机器、真实 Parent 与统一宿主。

本批无 UE 插件/资产修改或新导出，无 Godot 场景、完整角色、全量、性能验收。普通 Demo 尚未切入完整 Refactored 链；Ragdoll/Get-up/Pose Recovery、Mantle、Camera、最终十分钟性能等旧任务未关闭。保留用户修改；音频、道具物理、头颈诊断继续暂缓。
