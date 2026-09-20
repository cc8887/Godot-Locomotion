# Main Movement 遍历生命周期与 BaseLayer 隐藏初始化

第一百四十批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 完整性缺口与本批修复

这项工作属于原 P3/P4 所依赖的 P5A 动画图运行时补完。资产和公式相同，
仍不足以保证最终动作相同：初始化、Update、CacheBones、Evaluate 的
访问次序与历史也是算法输入。本批没有增加换向等待常数、手臂偏移或脚锁
补偿，也没有将完整 P6 物理玩法变成当前基础动作修复的前置条件。

显式分层生产入口把外层 AlsAnimationGraphFrame 传到 BaseLayer 和 Main
Movement。Main Movement 根据实际 Update 遍历计数判断失去相关性后重入，
不再把 FrameId 间断等同于动画更新间断。保留计数回绕语义；显式初始化
比较 Initialization 计数，不能因其 GlobalFrame 字段变化就清空历史。

普通子图整段未被访问、或被 Slot 完全覆盖时，显式路径仍能传播初始化与
骨骼缓存更新，并通过空普通来源的共享批次提交候选银行；不会伪造一次
零权重 Update，也不会推进未访问播放器。初始化日志、状态机、来源、
通知与缓存一起提交或取消。普通图恢复访问时才处理相关性重置。

BaseLayer 尾部接收同一初始化计数。正式资产的惯性化节点
bResetOnBecomingRelevant=false，因此仅重入不重置惯性化，显式 Initialize
才清空历史。CacheBones 保留历史；当前正式图过滤列表为空。隐藏提交身份
与最后一次姿势身份分开，拒绝隐藏初始化提交后的过期帧、其他角色和代际，
同时保留同帧取消后重试能力。

直接只读核对本机 UE 5.9：

- Engine/Private/Animation/AnimNode_StateMachine.cpp 的 Initialize、
  CacheBones、Update/WasSynchronizedCounter 与 SetState。
- Engine/Private/Animation/AnimNode_Inertialization.cpp 的 Initialize、
  CacheBones 和受 bResetOnBecomingRelevant 控制的重置。
- AnimGraphRuntime/Private/AnimNodes/AnimNode_Slot.cpp 的初始化和来源访问。
- 正式 AlsBaseLayerCompiler 验证惯性化开关；AlsMainGroundedPoseCompiler
  验证内部 Main 的初始状态为没有资产来源的参考姿势。因此冷隐藏初始化
  应进入该状态，不能要求一个无关播放器的 epoch 增加。

本批没有重新导出资源、启动 UE、修改 UE 插件或新增 UE 最终图 oracle。

## 验证

优化 Debug 构建 0 错误、0 警告。真实资源未访问测试覆盖 30/60/120 Hz，
每种生命周期入口各 1,050 帧，包含 651 帧隐藏、6 次恢复、609 帧属性保持、
255 帧隐藏 Montage、651 帧隐藏 Aim，每帧完整取消重试。

显式入口额外检查 6 次初始化、3 次隐藏骨骼刷新、3 次连续 Update 但
FrameId 跳变、short 更新计数回绕、初始化只比较计数。日志：

- `artifacts/traversal-unvisited-verified.log`：显式入口通过。
- `artifacts/traversal-legacy-unvisited-verified.log`：旧入口通过。
- `artifacts/traversal-unvisited-final.log`：身份保护补充后的显式入口最终
  1,050 帧回归通过，初始化/骨骼刷新/更新回绕与逐帧重试断言均通过。
- `artifacts/traversal-tail-regression.log`：尾部冷隐藏初始化、隐藏骨骼刷新、
  初始化计数/GlobalFrame 区别、取消重试、过期/外来身份与恢复访问通过；
  既有 Main Movement/BaseLayer 3,360 帧通过，含 6 次晚期故障。

此前初始专项失败的两处断言已修正：初始参考姿势不初始化无关播放器；
恢复次数单独按 hidden→visited 边沿统计，避免新增 if 改变 else 绑定。
原失败日志保留，不计入通过结果。

生产与既有接线回归：

- `traversal-production-single.log`、`traversal-production-initial.log`：
  single/parallel 各 960 帧，result=B289A6FB5130DBB7、
  fullPose=7B82A91E8A09C723、sampledPose=6804D603D2523040、
  root=DB5B813964D3479C；224/258，37 事件，lag/stale=0。
- `traversal-layered-initial.log`：真实分层输入 1,260 帧，每帧重试，10 次故障。
- `traversal-base-regression.log`：全局拆分/组合 3,360 帧通过。
- `traversal-native-late_transaction.log`、`traversal-native-late_source_event.log`：
  实际来源图与脚部入口晚期失败回滚通过，事件回调泄漏 0。

以上日志均位于 artifacts。晚期失败命令使用 `--als-cycle --foot-ik-frame`，
不使用与失败夹具互斥的 `--full-movement-coverage`；先前参数错误退出不作为
运行时回归失败或通过证据。身份保护补充后的尾部测试与晚期回滚均已通过。

## 尚未闭合与下一步

本批完成的是外层 Main Movement 与 BaseLayer 尾部的传播。内部 Main、
Standing/Stop/Locomotion Detail、Crouching 等仍有以 FrameId 作为更新
serial 的调用；不能据外层测试宣称整图重入语义已完整。下一步继续将实际
Update 遍历计数传到这些独立状态机，并验证缓存多次读取、重入、中断和
同帧重试，然后闭合最终根分支调度。

默认 Demo 仍是 BaseLayer。新分层/手脚链的专项入口不等于默认入口已完成；
P3/P4 起步、左右换髋、上身、平台与支撑脚仍需同输入 UE 多帧和人工对照，
通过后再切默认入口。状态规则造成的换向等待不能替换为固定输入延迟，
起步滑移须同时检查实际速度、播放率、Stride、marker 和真实支撑窗口。

原 P5A 其余通用事件/动作、P5B 全 Overlay/道具玩法、P5C Mantle/Roll/
Root Motion、P6 物理恢复/完整 Camera、P7 十分钟性能验收仍在范围。
既有 Core 23 项失败、Import 分配不稳定、旧 p95=2.559ms 超过 2.5ms
未在本批解决；没有声称全套测试或最终视觉通过。音频暂缓。
未 commit、revert 或 merge，保留现有工作区改动。
