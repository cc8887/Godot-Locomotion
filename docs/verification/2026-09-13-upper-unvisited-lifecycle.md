# Aim 与 Post Layering 的未访问生命周期

第一百四十二批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 实现

继续最终根普通分支的初始化、隐藏与恢复依赖。本批不是 Ragdoll 玩法或
完整根混合接入，也没有切换默认 Demo。

AimFrame 接受实际 AlsAnimationGraphFrame，三个 Aim 状态机分别保存
UpdateCounter。相关性采用原生 WasSynchronizedCounter，帧编号仅继续
用于事务与求值身份校验。代理记录权重按动画遍历的连续性读取，不再因为
FrameId 跳变丢失上一帧的相机状态权重。专项验证：原先应立即切换的条件
仍读取完整的上帧权重，不会误走带过渡的分支。

显式 Initialization 变化重建 Aim 的初始状态闭包，隐藏时也生效；没有
Update 访问则不更新 Evaluator 输入、不求值。记录权重仍按动画实例帧
清空，其他未访问 Evaluator 历史保持。初始化仅比较计数，不因同计数的
GlobalFrame 变化重复初始化。候选初始化、机器状态、记录权重和操作日志
支持取消/同帧重试，已经提交的运行时拒绝更换遍历所有权模式。

AlsAimLayerFrameStage 支持 updateSource=false 的初始化专用准备：
传播 Post Layering 两个读取节点的 Initialize/CacheBones，通过 Save
缓存抑制重复访问；更新全局 Aiming 属性的提交身份，但不调用 Aim/spine
姿势节点 Update 或 Evaluate。提交身份与内部最后一次姿势访问身份分开。
隐藏期间读取姿势/来源上下文及调用 Evaluate 均被拒绝，不能复用上一帧
姿势冒充当前输出。

AlsLayerBlendingRuntime 和 AlsLayerBlendingFrameStage 同样支持不更新
来源的准备与提交：初始化 linked inputs/Slot，刷新骨骼缓存，保留节点
混合历史，普通输入/Slot 的 Update 和姿势求值次数均为零。BasePoses 的
真实两条 Evaluator 接收初始化和骨骼刷新，时间/Update/Evaluation 计数
保持；其银行与 Post Layering 一起取消或提交。

普通分层生产路径已使用新的 Aim 遍历语义。整个普通分支隐藏的调用目前
由真实阶段组合专项驱动；最终根运行时仍拒绝 Ragdoll 混合，所以不能把
阶段 API 的完成记为根双分支已经启用。

## 来源核对

只读核对本机 UE 5.9：AnimNode_StateMachine::Update 的相关性门控；
AnimTypes.h 的 FGraphTraversalCounter；TwoWayBlend 的 Initialize 与
CacheBones 均访问两条输入；SaveCachedPose 的初始化计数/骨骼缓存访问
抑制和 Evaluate 失效规则。沿用已编译的正式 Aim/Layering 资产定义。

未启动 UE、修改原生插件、重新导出资源或新增 UE 最终图 oracle。固定
骨架下的当前骨骼绑定仍不是任意骨架热更/LOD 验收。

## 验证

优化 Debug 构建 0 错误、0 警告。Import Aim/LayerBlending 专项通过
29 项、跳过 1 项，另一个记录权重专项通过 1 项。跳过的是依赖外部
Normal Editor 重复导出文件的可选核对，未提供新文件，不能记为通过。
结果：`artifacts/test-results/upper-unvisited-import.trx`、
`artifacts/test-results/upper-unvisited-recorded-weights.trx`。

专项覆盖 short 回绕、FrameId 跳变、更新间断、隐藏初始化、同计数不同
GlobalFrame、记录权重、缓存/Slot 初始化但不 Update，以及取消重试。

Godot 真实阶段组合 `layered_frame_input_smoke.tscn -- --unvisited-upper`
在 30/60/120 Hz 共 630 帧通过：228 帧隐藏、6 次初始化、3 次隐藏骨骼
刷新、7 次缓存来源完成后的故障，每帧取消重试。使用真实 Aim 动画和
BasePoses 资源；BaseLayer/Overlay 两个输入是受控姿势。验证初始化 epoch、
隐藏期间 Evaluator Update/Evaluation 不增加、全局 Aim 身份推进、旧银行
保持、恢复后的姿势/曲线一致。日志 `artifacts/upper-unvisited-stages.log`。

原 Post Layering 缓存专项 420 帧通过：140 帧双读取、420 次来源内故障、
420 次填充后故障，每个根作用域只求值一次，日志
`artifacts/upper-unvisited-cache-regression.log`。其直接求值对照现在也
接收同一真实遍历，只关闭 Post 缓存；否则对照会保留旧 FrameId 重置语义，
比较的就不再只是缓存行为。此对照属于组合参考，不是独立 UE oracle。

实际生产单线程/并行各 960 帧通过：
`artifacts/upper-unvisited-production-single.log`、
`artifacts/upper-unvisited-production-parallel.log`。共同摘要保持：
result=B289A6FB5130DBB7、fullPose=7B82A91E8A09C723、
sampledPose=6804D603D2523040、root=DB5B813964D3479C；224/258，
37 事件，lag/stale=0。

真实分层输入 1,260 帧、每帧重试、10 次失败通过，日志
`artifacts/upper-unvisited-layered-input.log`。晚期姿势/来源事件失败回滚
通过，回调泄漏 0：`artifacts/upper-unvisited-late-transaction.log`、
`artifacts/upper-unvisited-late-events.log`。本批修改文件空白检查通过。

## 接下来

继续 Overlay 的初始化/未访问/恢复事务，分离 Foot IK 全局属性更新与
姿势控制节点访问；将 BaseLayer、Overlay、Aim、Post Layering、手脚和
Ragdoll 所需来源按最终根真实访问权重调度，在一次事务内完成。当前
阶段专项没有替代这一步，物理 Ragdoll/快照与恢复玩法仍归 P6。

随后完成 P3/P4 起步、换髋、上身、平台/支撑脚的 UE 同输入多帧与人工
验收，通过后切默认入口。默认仍为 BaseLayer，不等待完整 P6 才处理
基础视觉问题。原 P5A 其余通用事件/动作、P5B 全 Overlay/道具玩法、
P5C Mantle/Roll/Root Motion、P6 完整恢复/Camera、P7 十分钟性能验收
保持范围。既有 Core 23 项失败、Import 分配不稳定、旧 p95=2.559ms
超过 2.5ms 未在本批关闭；没有全套测试或最终视觉通过声明。音频暂缓。
未 commit、revert 或 merge，保留已有工作区改动。
